using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Fan-Out Entry Point</b> — the first executor in a concurrent orchestration stage.
/// Buffers incoming messages and, on receiving a <see cref="TurnToken"/>, broadcasts them
/// to all connected downstream agents.
///
/// <b>Role in the graph:</b>
/// <code>
///   ──→ [FanOutExecutor] ──fan-out──→ [Agent 1], [Agent 2], [Agent 3]
/// </code>
///
/// <b>Two-phase message handling:</b>
/// <list type="number">
///   <item><see cref="HandleMessage"/> / <see cref="HandleMessages"/> — buffer incoming
///         messages. Two overloads are provided so the executor can accept either a single
///         <see cref="ChatMessage"/> (e.g. from an upstream agent sending one message) or a
///         full <c>List&lt;ChatMessage&gt;</c> (e.g. from an <see cref="AggregatorExecutor"/>
///         forwarding a merged batch). Both overloads <b>replace</b> the buffer rather than
///         appending, since only the latest input is relevant.</item>
///   <item><see cref="HandleTurnAsync"/> — triggered by a <see cref="TurnToken"/>; sends the
///         buffered messages followed by a new <see cref="TurnToken"/> to all connected agents.
///         If no messages were buffered (spurious token), returns silently.</item>
/// </list>
///
/// <b>Cross-run state:</b> Declared as <c>declareCrossRunShareable: true</c> and implements
/// <see cref="IResettableExecutor"/> so the message buffer is cleared between runs.
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
public partial class FanOutExecutor(string executorId)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private List<ChatMessage> _messages = [];

  /// <summary>
  /// Accepts a single <see cref="ChatMessage"/> and stores it as the sole message in the buffer.
  /// Handles the case where the upstream agent sends one message (e.g. a structured-output response).
  /// </summary>
  [MessageHandler]
  private void HandleMessage(ChatMessage message, IWorkflowContext context)
  {
    _messages = [message];
  }

  /// <summary>
  /// Accepts a full message list and replaces the buffer.
  /// Preserves the original User message (text + image) at the front so downstream agents
  /// receive both the source context and the upstream agent's output — critical for agents
  /// that need the original input (e.g. relationship agents need the source text to reason about relationships).
  /// </summary>
  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    // Keep the original User message(s) at the front so the full context flows downstream.
    // Assistant messages (structured output) follow so agents see both source and prior stage output.
    List<ChatMessage> userMessages = messages.Where(m => m.Role == ChatRole.User).ToList();
    List<ChatMessage> otherMessages = messages.Where(m => m.Role != ChatRole.User).ToList();
    _messages = [.. userMessages, .. otherMessages];
  }

  /// <summary>Broadcasts the buffered messages and a <see cref="TurnToken"/> to all downstream agents.</summary>
  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    // No messages buffered — nothing to broadcast
    if (_messages.Count == 0)
      return;

    // Capture the current buffer and clear it to prepare for the next turn
    List<ChatMessage> messages = _messages;
    _messages = [];

    // Both sends go to ALL connected edges (fan-out topology)
    await context.SendMessageAsync(messages, cancellationToken: cancellationToken);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), cancellationToken: cancellationToken);
  }

  /// <summary>Clears the message buffer between runs.</summary>
  public ValueTask ResetAsync()
  {
    _messages = [];
    return default;
  }
}