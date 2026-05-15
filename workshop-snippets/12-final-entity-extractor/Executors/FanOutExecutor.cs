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
///   <item><see cref="HandleMessage"/> / <see cref="HandleMessages"/> — accumulate incoming
///         messages. Two overloads are provided so the executor can accept either a single
///         <see cref="ChatMessage"/> (e.g. from an upstream agent sending one message) or a
///         full <c>List&lt;ChatMessage&gt;</c> (e.g. from an upstream agent whose output
///         arrives in multiple batches). Both overloads <b>append</b> to the buffer because
///         the upstream agent may deliver its output across several handler calls (e.g. the
///         original User message first, then the agent's response messages).</item>
///   <item><see cref="HandleTurnAsync"/> — triggered by a <see cref="TurnToken"/>; filters
///         the accumulated messages to keep only User messages and text-bearing Assistant
///         messages (stripping tool-call artifacts from the upstream agent), then broadcasts
///         them followed by a new <see cref="TurnToken"/> to all connected agents.
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
  /// Accumulates a single incoming <see cref="ChatMessage"/> into the buffer.
  /// </summary>
  [MessageHandler]
  private void HandleMessage(ChatMessage message, IWorkflowContext context)
  {
    _messages.Add(message);
  }

  /// <summary>
  /// Accumulates an incoming batch of messages into the buffer.
  /// The upstream agent's output may arrive in multiple batches (e.g. the original User
  /// message first, then the agent's response messages), so the buffer appends rather
  /// than replaces to preserve the full conversation context.
  /// </summary>
  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _messages.AddRange(messages);
  }

  /// <summary>Broadcasts the buffered messages and a <see cref="TurnToken"/> to all downstream agents.</summary>
  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    if (_messages.Count == 0)
      return;

    List<ChatMessage> accumulated = _messages;
    _messages = [];

    // Keep User messages (source text + image) and text-bearing Assistant messages (structured output).
    // Strip tool-call / tool-result messages so downstream agents start clean and invoke their own tools.
    List<ChatMessage> messages = 
      [.. accumulated.Where(m => m.Role == ChatRole.User || (m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text)))];

    if (messages.Count == 0)
      return;

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