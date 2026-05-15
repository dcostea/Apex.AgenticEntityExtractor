using Apex.AgenticEntityExtractor.Aggregators;
using Apex.AgenticEntityExtractor.GroupChatManagers;
using Apex.AgenticEntityExtractor.Models;
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
///   [GroupChatOrchestratorExecutor] ←──→ [Participant(Reporter)]
///   [GroupChatOrchestratorExecutor] ←──→ [Participant(Analyst)]
/// </code>
///
/// <b>Turn lifecycle:</b>
/// <list type="number">
///   <item>Guard: skip spurious empty-buffer turns.</item>
///   <item>Track: append the latest assistant response to the conversation history.</item>
///   <item>Terminate: check <see cref="DebateVerdict.Approved"/> or iteration cap.</item>
///   <item>Route: select next participant, compose base context + conversation history, send.</item>
///   <item>Yield: on termination, yield the final debate output via <see cref="IWorkflowContext.YieldOutputAsync"/>.</item>
/// </list>
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
[YieldsOutput(typeof(List<ChatMessage>))]
public partial class GroupChatOrchestratorExecutor(string executorId, AIAgent firstAgent, ExecutorBinding firstExecutor, ExecutorBinding secondExecutor, DebateTerminationManager manager)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  // ── State ────────────────────────────────────────────────────────────

  /// <summary>Entities/relationships/source-text JSON captured once from the upstream aggregator.</summary>
  private List<ChatMessage>? _baseContext;

  /// <summary>Accumulated debate conversation (assistant responses from each round).</summary>
  private readonly List<ChatMessage> _conversationHistory = [];

  /// <summary>Signals that a message handler buffered new data for the next turn.</summary>
  private bool _hasPending;

  // ── Message handlers ─────────────────────────────────────────────────

  /// <summary>
  /// Phase 0 (initial stage input): receives the typed extraction result from the upstream
  /// <see cref="AggregatorExecutor"/> and converts it to the message format expected by the
  /// group-chat logic. Sets <c>_baseContext</c> once so participants always receive full context.
  /// </summary>
  [MessageHandler]
  private void HandleExtractionContext(ExtractionContext extractionContext, IWorkflowContext workflowContext)
  {
    _baseContext ??= Aggregator.ToMessages(extractionContext);
    _hasPending = true;
  }

  /// <summary>Phase 1: buffer incoming messages from a participant or upstream stage.</summary>
  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _baseContext ??= [.. messages.Where(m => m.Role == ChatRole.User)];

    // Extract the latest assistant response into conversation history immediately.
    ChatMessage? lastAssistant = messages.LastOrDefault(m =>
      m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));
    if (lastAssistant is not null)
      _conversationHistory.Add(lastAssistant);

    _hasPending = true;
  }

  /// <summary>Phase 2: orchestrate a single group-chat turn — route, check termination, or yield output.</summary>
  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    if (!_hasPending)
      return;

    _hasPending = false;

    // Use the latest conversation entry for termination and routing decisions.
    List<ChatMessage> latest = _conversationHistory.Count > 0
      ? [_conversationHistory[^1]]
      : [.. _baseContext ?? []];

    // Terminate on APPROVED or max iterations — yield the latest debate output
    if (await manager.ShouldTerminateAsync(latest, cancellationToken))
    {
      await YieldDebateOutputAsync(context, cancellationToken);
      return;
    }

    // Route to the next participant via round-robin (Reporter → Analyst → Reporter → …)
    AIAgent nextAgent = await manager.SelectNextAgentAsync(latest, cancellationToken);
    ExecutorBinding targetExecutor = nextAgent == firstAgent ? firstExecutor : secondExecutor;

    // Compose: base context + full conversation history as User messages with turn numbers
    List<ChatMessage> prepared = [.. _baseContext ?? []];
    for (int i = 0; i < _conversationHistory.Count; i++)
      prepared.Add(new ChatMessage(ChatRole.User, $"[Turn {i + 1}] Previous debate response:\n{_conversationHistory[i].Text}"));
    prepared.Add(new ChatMessage(ChatRole.User, $"You are now on turn {_conversationHistory.Count + 1}."));

    manager.CurrentIterationCount++;

    await context.SendMessageAsync(prepared, targetExecutor.Id, cancellationToken);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), targetExecutor.Id, cancellationToken);
  }

  // ── Reset ────────────────────────────────────────────────────────────

  public ValueTask ResetAsync()
  {
    _hasPending = false;
    _baseContext = null;
    _conversationHistory.Clear();
    manager.CurrentIterationCount = 0;
    return default;
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  /// <summary>Yields the last debate response (or a fallback message) as final workflow output.</summary>
  private async ValueTask YieldDebateOutputAsync(IWorkflowContext context, CancellationToken cancellationToken)
  {
    ChatMessage output = _conversationHistory.Count > 0
      ? _conversationHistory[^1]
      : new ChatMessage(ChatRole.Assistant, "No consensus reached before termination.");

    try
    {
      await context.YieldOutputAsync(new List<ChatMessage> { output }, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      // Run disposed after consuming the output event — yield already delivered.
    }
  }

  }
