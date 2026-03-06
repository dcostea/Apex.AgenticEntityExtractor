using Apex.AgenticEntityExtractor.GroupChatManagers;
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

  // ── State ────────────────────────────────────────────────────────────

  private List<ChatMessage> _messages = [];

  /// <summary>Entities/relationships JSON captured once from the upstream aggregator.</summary>
  private List<ChatMessage>? _baseContext;

  /// <summary>Best valid Mermaid diagram seen so far — fallback when termination fires before approval.</summary>
  private ChatMessage? _bestMermaidOutput;

  // ── Message handlers ─────────────────────────────────────────────────

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
    // Guard: no messages buffered — spurious TurnToken
    if (_messages.Count == 0)
      return;

    // Swap-and-clear to avoid reprocessing on subsequent TurnTokens
    List<ChatMessage> messages = _messages;
    _messages = [];

    // Capture: snapshot the latest valid Mermaid candidate
    if (TryGetLatestValidMermaid(messages) is ChatMessage currentMermaid)
      _bestMermaidOutput = currentMermaid;

    // Terminate: check APPROVED keyword or iteration cap
    if (await manager.ShouldTerminateAsync(messages, cancellationToken))
    {
      await YieldBestDiagramAsync(context, cancellationToken);
      return;
    }

    // Route: select next participant via round-robin
    AIAgent nextAgent = await manager.SelectNextAgentAsync(messages, cancellationToken);

    // Resolve target executor and compose a clean conversation
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

    // Targeted send: route to a specific executor (not all connected edges)
    await context.SendMessageAsync(messages, targetExecutor.Id, cancellationToken);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), targetExecutor.Id, cancellationToken);
  }

  // ── Reset ────────────────────────────────────────────────────────────

  public ValueTask ResetAsync()
  {
    _messages = [];
    _baseContext = null;
    _bestMermaidOutput = null;
    manager.CurrentIterationCount = 0;
    return default;
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  /// <summary>Yields the best captured Mermaid diagram (or a fallback message) as final workflow output.</summary>
  private async ValueTask YieldBestDiagramAsync(IWorkflowContext context, CancellationToken cancellationToken)
  {
    List<ChatMessage> output = _bestMermaidOutput is not null
      ? [_bestMermaidOutput]
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

  /// <summary>Returns the latest assistant message with a valid Mermaid block (excluding error feedback).</summary>
  private static ChatMessage? TryGetLatestValidMermaid(List<ChatMessage> messages)
  {
    return messages.LastOrDefault(m =>
      m.Role == ChatRole.Assistant
      && !string.IsNullOrWhiteSpace(m.Text)
      && PayloadHelper.ContainsMermaidBlock(m.Text)
      && !m.Text.Contains(ErrorsFound, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Composes conversation for the builder: base context + reviewer feedback re-roled as User.
  /// Strips prior diagrams to prevent parroting.
  /// </summary>
  private List<ChatMessage> PrepareMessagesForBuilder(List<ChatMessage> latestResponse)
  {
    List<ChatMessage> prepared = [.. _baseContext ?? []];

    foreach (var m in latestResponse)
    {
      if (m.Role == ChatRole.Assistant
        && m.Text is { } text
        && text.Contains(ErrorsFound, StringComparison.OrdinalIgnoreCase))
      {
        prepared.Add(new ChatMessage(ChatRole.User, $"REVIEWER FEEDBACK:\n{text}"));
      }
    }

    return prepared;
  }

  /// <summary>
  /// Composes conversation for the reviewer: base context + latest diagram presented as User input.
  /// Strips prior reviews to prevent parroting.
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