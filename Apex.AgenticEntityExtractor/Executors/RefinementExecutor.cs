using Apex.AgenticEntityExtractor.GroupChatManagers;
using Apex.AgenticEntityExtractor.Helpers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Star-Topology Hub (Group Chat Orchestrator)</b> — the central controller in a
/// manual group-chat orchestration, routing messages between participants and enforcing
/// termination rules.
///
/// This is the custom equivalent of the framework's internal <c>GroupChatHost</c> (created
/// automatically by <see cref="AgentWorkflowBuilder.CreateGroupChatBuilderWith"/>), built
/// from scratch to demonstrate low-level orchestration with explicit executor-to-executor
/// message routing.
///
/// <b>Role in the graph:</b>
/// <code>
///   [RefinementExecutor] ←──→ [Participant(Builder)]
///   [RefinementExecutor] ←──→ [Participant(Reviewer)]
/// </code>
///
/// <b>Turn lifecycle (each invocation of <see cref="HandleTurnAsync"/>):</b>
/// <list type="number">
///   <item>Guard: skip spurious empty-buffer turns produced by token-only dispatches.</item>
///   <item>Capture: if the incoming messages contain a valid Mermaid diagram (without
///         <c>"ERRORS FOUND"</c>), store it in <c>_bestMermaidOutput</c> as a fallback for
///         termination.</item>
///   <item>Terminate check: ask <see cref="ApprovalManager.ShouldTerminateAsync"/>
///         (which evaluates the <c>APPROVED</c> keyword and the iteration cap).</item>
///   <item>Continue: if not terminated, let the manager filter/update history via
///         <see cref="ApprovalManager.UpdateHistoryAsync"/> and select
///         the next participant via <see cref="ApprovalManager.SelectNextAgentAsync"/>.
///         Send messages + <see cref="TurnToken"/> to the selected participant's executor
///         using a <b>targeted</b> <see cref="IWorkflowContext.SendMessageAsync"/> overload
///         (providing the executor ID).</item>
///   <item>Yield: on termination, yield the best captured Mermaid diagram (or a fallback
///         message) as final workflow output via <see cref="IWorkflowContext.YieldOutputAsync"/>.</item>
/// </list>
///
/// <b>Cross-run state:</b> Declared as <c>declareCrossRunShareable: true</c> and implements
/// <see cref="IResettableExecutor"/>. On reset, clears the message buffer, the best Mermaid
/// snapshot, and the manager's iteration counter.
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
[YieldsOutput(typeof(List<ChatMessage>))]
public partial class RefinementExecutor(string executorId, AIAgent builderAgent, ExecutorBinding builderExecutor, ExecutorBinding reviewerExecutor, ApprovalManager manager)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private List<ChatMessage> _messages = [];

  /// <summary>Best valid Mermaid diagram seen so far — used as fallback output when termination fires before approval.</summary>
  private ChatMessage? _bestMermaidOutput;

  /// <summary>Phase 1: buffer incoming messages from a participant or upstream stage.</summary>
  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _messages = messages;
  }

  /// <summary>Phase 2: orchestrate a single group-chat turn — route, check termination, or yield output.</summary>
  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    // No messages buffered — spurious TurnToken, nothing to orchestrate
    if (_messages.Count == 0)
      return;

    // Swap-and-clear to avoid reprocessing on subsequent TurnTokens
    List<ChatMessage> messages = _messages;
    _messages = [];

    // Snapshot the best Mermaid candidate so far (ignoring error feedback)
    if (TryGetLatestValidMermaid(messages) is ChatMessage currentMermaid)
    {
      _bestMermaidOutput = currentMermaid;
    }

    if (!await manager.ShouldTerminateAsync(messages, cancellationToken).ConfigureAwait(false))
    {
      // Let the manager filter/rewrite history before the next participant sees it
      var filtered = (await manager.UpdateHistoryAsync(messages, cancellationToken).ConfigureAwait(false))?.ToList();
      if (filtered is { Count: > 0 })
        messages = filtered;

      // Round-robin: select the next participant and route messages to their executor
      if (await manager.SelectNextAgentAsync(messages, cancellationToken).ConfigureAwait(false) is AIAgent nextAgent)
      {
        var targetExecutor = nextAgent == builderAgent ? builderExecutor : reviewerExecutor;

        // Manual increment — the manager doesn't auto-advance because the executor owns the turn lifecycle
        manager.CurrentIterationCount++;

        // Targeted send: route to a specific executor (not all connected edges)
        await context.SendMessageAsync(messages, targetExecutor.Id, cancellationToken).ConfigureAwait(false);
        await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), targetExecutor.Id, cancellationToken).ConfigureAwait(false);
        return;
      }
    }

    // Terminated (or no next agent selected) — yield the best Mermaid diagram captured so far
    List<ChatMessage> output = _bestMermaidOutput is not null
      ? [_bestMermaidOutput]
      : [new ChatMessage(ChatRole.Assistant, "No valid mermaid diagram produced before termination.")];

    try
    {
      await context.YieldOutputAsync(output, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // Run disposed after consuming the output event — yield already delivered.
    }
  }

  /// <summary>Asynchronously clears all messages from the collection, resetting it to an empty state.</summary>
  public ValueTask ResetAsync()
  {
    _messages = [];
    _bestMermaidOutput = null;
    manager.CurrentIterationCount = 0;
    return default;
  }

  /// <summary>
  /// Scans messages in reverse for the latest assistant message containing a valid Mermaid
  /// code block that is not an error-feedback message (i.e. does not contain "ERRORS FOUND").
  /// </summary>
  private static ChatMessage? TryGetLatestValidMermaid(List<ChatMessage> messages)
  {
    return messages.LastOrDefault(m =>
      !string.IsNullOrWhiteSpace(m.Text)
      && PayloadHelper.ContainsMermaidBlock(m.Text)
      && !m.Text.Contains("ERRORS FOUND", StringComparison.OrdinalIgnoreCase));
  }
}