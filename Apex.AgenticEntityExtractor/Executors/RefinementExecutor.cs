using Apex.AgenticEntityExtractor.Aggregators;
using Apex.AgenticEntityExtractor.GroupChatManagers;
using Apex.AgenticEntityExtractor.Models;
using Apex.AgenticEntityExtractor.OutputRenderers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Star-Topology Hub (Group Chat Orchestrator)</b> — the central controller in a
/// manual group-chat orchestration, routing messages between participants and enforcing
/// termination rules.
///
/// Custom equivalent of the framework's internal <c>GroupChatHost</c>, built from scratch
/// to demonstrate low-level orchestration with explicit executor-to-executor message routing.
///
/// <code>
///   [RefinementExecutor] ←──→ [Participant(Builder)]
///   [RefinementExecutor] ←──→ [Participant(Reviewer)]
/// </code>
///
/// <b>Turn lifecycle:</b>
/// <list type="number">
///   <item>Guard: skip spurious empty-buffer turns.</item>
///   <item>Capture: snapshot the latest valid Mermaid diagram as fallback output.</item>
///   <item>Terminate: check <c>APPROVED</c> keyword or iteration cap.</item>
///   <item>Route: select next participant, compose a clean conversation, send targeted messages.</item>
///   <item>Yield: on termination, yield the best diagram via <see cref="IWorkflowContext.YieldOutputAsync"/>.</item>
/// </list>
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
[YieldsOutput(typeof(List<ChatMessage>))]
public partial class RefinementExecutor(string executorId, AIAgent builderAgent, ExecutorBinding builderExecutor, ExecutorBinding reviewerExecutor, ApprovalManager manager)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private const string ErrorsFound = "ERRORS FOUND";
  private const string Rejected = "REJECTED";

  // ── State ────────────────────────────────────────────────────────────

  private List<ChatMessage> _messages = [];

  /// <summary>Entities/relationships JSON captured once from the upstream aggregator.</summary>
  private List<ChatMessage>? _baseContext;

  /// <summary>Latest valid Mermaid diagram produced by the builder.</summary>
  private ChatMessage? _latestMermaidOutput;

  // ── Message handlers ─────────────────────────────────────────────────

  /// <summary>
  /// Phase 0 (initial stage input): receives the typed extraction result from the upstream
  /// <see cref="AggregatorExecutor"/> and converts it to the message format expected by the
  /// group-chat logic. Sets <c>_baseContext</c> once so participants always receive full context.
  /// </summary>
  [MessageHandler]
  private void HandleExtractionContext(ExtractionContext extractionContext, IWorkflowContext workflowContext)
  {
    List<ChatMessage> messages = ToContextMessages(extractionContext);
    _baseContext ??= messages;
    _messages = messages;
  }

  /// <summary>Phase 1: buffer incoming messages from a participant or upstream stage.</summary>
  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _baseContext ??= [.. messages.Where(m => m.Role == ChatRole.User)];
    _messages = messages;
  }

  /// <summary>Phase 2: orchestrate a single group-chat turn — route, check termination, or yield output.</summary>
  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    if (_messages.Count == 0)
      return;

    List<ChatMessage> messages = _messages;
    _messages = [];

    // Capture the latest valid diagram from the builder
    if (TryGetLatestValidMermaid(messages) is ChatMessage currentMermaid)
      _latestMermaidOutput = currentMermaid;

    // Terminate on APPROVED or max iterations — yield the latest diagram we have
    if (await manager.ShouldTerminateAsync(messages, cancellationToken))
    {
      await YieldLatestDiagramAsync(context, cancellationToken);
      return;
    }

    // Route to the next participant via round-robin (builder → reviewer → builder → …)
    AIAgent nextAgent = await manager.SelectNextAgentAsync(messages, cancellationToken);

    ExecutorBinding targetExecutor;
    if (nextAgent == builderAgent)
    {
      targetExecutor = builderExecutor;
      messages = PrepareMessagesForBuilder(messages);
    }
    else
    {
      targetExecutor = reviewerExecutor;
      messages = PrepareMessagesForReviewer(messages);
    }

    manager.CurrentIterationCount++;

    await context.SendMessageAsync(messages, targetExecutor.Id, cancellationToken);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), targetExecutor.Id, cancellationToken);
  }

  // ── Reset ────────────────────────────────────────────────────────────

  public ValueTask ResetAsync()
  {
    _messages = [];
    _baseContext = null;
    _latestMermaidOutput = null;
    manager.CurrentIterationCount = 0;
    return default;
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  /// <summary>Yields the latest captured Mermaid diagram (or a fallback message) as final workflow output.</summary>
  private async ValueTask YieldLatestDiagramAsync(IWorkflowContext context, CancellationToken cancellationToken)
  {
    List<ChatMessage> output = _latestMermaidOutput is not null
      ? [_latestMermaidOutput]
      : [new ChatMessage(ChatRole.Assistant, "No valid mermaid diagram produced before termination.")];

    try
    {
      await context.YieldOutputAsync(output, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      // Run disposed after consuming the output event — yield already delivered.
    }
  }

  /// <summary>
  /// Converts the extraction context to labeled JSON messages using the shared <see cref="Aggregator.ToMessages"/> method.
  /// </summary>
  private static List<ChatMessage> ToContextMessages(ExtractionContext ctx) => Aggregator.ToMessages(ctx);

  /// <summary>Returns the latest assistant message with a valid Mermaid block (excluding error feedback), extracting only the last block if multiple exist.</summary>
  private static ChatMessage? TryGetLatestValidMermaid(List<ChatMessage> messages)
  {
    var candidate = messages.LastOrDefault(m =>
      m.Role == ChatRole.Assistant
      && !string.IsNullOrWhiteSpace(m.Text)
      && PayloadHelper.ContainsMermaidBlock(m.Text)
      && !m.Text.Contains(ErrorsFound, StringComparison.OrdinalIgnoreCase)
      && !m.Text.Contains(Rejected, StringComparison.OrdinalIgnoreCase));

    if (candidate is null)
      return null;

    // Small LLMs may produce multiple mermaid blocks in one response — keep only the last one.
    string? lastBlock = PayloadHelper.ExtractLastMermaidBlock(candidate.Text!);
    return lastBlock is not null
      ? new ChatMessage(ChatRole.Assistant, lastBlock)
      : candidate;
  }

  /// <summary>
  /// Composes conversation for the builder: base context + previous diagram + reviewer feedback.
  /// Including the previous diagram lets the builder make targeted fixes instead of regenerating
  /// from scratch each turn.
  /// </summary>
  private List<ChatMessage> PrepareMessagesForBuilder(List<ChatMessage> latestResponse)
  {
    List<ChatMessage> prepared = [.. _baseContext ?? []];

    if (_latestMermaidOutput is not null)
    {
      prepared.Add(new ChatMessage(ChatRole.User, $"YOUR PREVIOUS DIAGRAM:\n{_latestMermaidOutput.Text}"));
    }

    foreach (var m in latestResponse)
    {
      if (m.Role == ChatRole.Assistant
        && m.Text is { } text
        && (text.Contains(ErrorsFound, StringComparison.OrdinalIgnoreCase)
          || text.Contains(Rejected, StringComparison.OrdinalIgnoreCase)))
      {
        prepared.Add(new ChatMessage(ChatRole.User, $"REVIEWER FEEDBACK:\n{text}"));
      }
    }

    return prepared;
  }

  /// <summary>
  /// Composes conversation for the reviewer: base context + latest diagram presented as User input.
  /// Strips prior reviews to prevent parroting. Extracts only the last mermaid block if the
  /// builder agent produced multiple blocks in one response.
  /// </summary>
  private List<ChatMessage> PrepareMessagesForReviewer(List<ChatMessage> latestResponse)
  {
    List<ChatMessage> prepared = [.. _baseContext ?? []];

    if (TryGetLatestValidMermaid(latestResponse) is ChatMessage latestDiagram)
    {
      prepared.Add(new ChatMessage(ChatRole.User, $"DIAGRAM TO REVIEW:\n{latestDiagram.Text}"));
    }

    return prepared;
  }
}