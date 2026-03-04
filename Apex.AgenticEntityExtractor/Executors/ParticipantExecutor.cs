using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Participant Executor</b> — wraps an <see cref="AIAgent"/> as a workflow executor,
/// bridging the agent's <see cref="AIAgent.RunStreamingAsync"/> interface with the
/// executor-to-executor message protocol used by <see cref="WorkflowBuilder"/> graphs.
///
/// <b>Typical role in a star-topology group chat:</b>
/// <code>
///   [RefinementExecutor] ──→ [Participant(Builder)] ──→ [RefinementExecutor]
///   [RefinementExecutor] ──→ [Participant(Reviewer)] ──→ [RefinementExecutor]
/// </code>
/// However, the executor is general-purpose and can be placed in any graph that needs
/// to invoke an <see cref="AIAgent"/> and forward its response downstream.
///
/// <b>Two-phase message handling:</b>
/// <list type="number">
///   <item><see cref="HandleMessages"/> — buffers incoming <c>List&lt;ChatMessage&gt;</c>.</item>
///   <item><see cref="HandleTurnAsync"/> — triggered by a <see cref="TurnToken"/>; runs
///         the agent in streaming mode, collects <see cref="AgentResponseUpdate"/>s
///         (optionally emitting them as live events), assembles the final response, and
///         sends the result plus a new <see cref="TurnToken"/> downstream. If no messages
///         were buffered (spurious token), returns silently.</item>
/// </list>
///
/// <b>Output composition (controlled by <paramref name="includeInputInOutput"/>):</b>
/// When <c>true</c>, the forwarded message list starts with the original input messages
/// followed by the agent's response — preserving full conversation context for the next
/// executor. When <c>false</c>, only the agent's response is forwarded.
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
public sealed partial class ParticipantExecutor(AIAgent agent, bool includeInputInOutput)
  : Executor(agent.Name ?? agent.Id, declareCrossRunShareable: true), IResettableExecutor
{
  private List<ChatMessage> _messages = [];

  /// <summary>Phase 1: buffer incoming messages until a <see cref="TurnToken"/> triggers the agent.</summary>
  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _messages = messages;
  }

  /// <summary>Phase 2: invoke the agent in streaming mode and forward the response downstream.</summary>
  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    // No messages buffered — spurious TurnToken, nothing to do
    if (_messages.Count == 0)
      return;

    // Swap-and-clear to avoid reprocessing on subsequent TurnTokens
    List<ChatMessage> messages = _messages;
    _messages = [];

    // Stream the agent's response, collecting updates (and optionally emitting live events)
    List<AgentResponseUpdate> updates = [];
    await foreach (var update in agent.RunStreamingAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false))
    {
      updates.Add(update);
      if (token.EmitEvents is true)
      {
        await context.AddEventAsync(new AgentResponseUpdateEvent(Id, update), cancellationToken).ConfigureAwait(false);
      }
    }

    // Assemble output: optionally prepend input messages for full conversation context
    List<ChatMessage> result = includeInputInOutput ? [.. messages] : [];
    result.AddRange(updates.ToAgentResponse().Messages);

    // Forward the result and a fresh TurnToken to the next executor in the graph
    await context.SendMessageAsync(result, cancellationToken: cancellationToken).ConfigureAwait(false);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), cancellationToken: cancellationToken).ConfigureAwait(false);
  }

  /// <summary>Asynchronously clears all messages from the collection, resetting it to an empty state.</summary>
  public ValueTask ResetAsync()
  {
    _messages = [];
    return default;
  }
}
