using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Forwarding Fan-In Aggregator</b> — an intermediate aggregation node for use inside
/// a flat, single-graph pipeline where the aggregated result must flow to the next stage
/// rather than exiting the workflow.
///
/// <b>Role in the graph:</b>
/// <code>
///   [Batcher 1] ──┐
///   [Batcher 2] ──┼──barrier──→ [AggregatorExecutor] ──→ [Next Stage Fan-Out / Orchestrator]
///   [Batcher 3] ──┘
/// </code>
///
/// <b>How it works:</b>
/// Each time <see cref="HandleMessagesAsync"/> is invoked, it appends the incoming batch
/// to <c>_agentResults</c>. When the count equals <paramref name="numberOfConcurrentAgents"/>,
/// the injected <paramref name="aggregator"/> function merges all batches into a single
/// <c>List&lt;ChatMessage&gt;</c>, which is <b>forwarded</b> downstream together with a
/// <see cref="TurnToken"/> to trigger the next stage.
///
/// <b>Cross-run state:</b>
/// <see cref="IResettableExecutor"/> so that <c>_agentResults</c> is cleared between runs,
/// preventing stale data from leaking across invocations.
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
public partial class AggregatorExecutor(string executorId, int numberOfConcurrentAgents, Func<IList<List<ChatMessage>>, List<ChatMessage>> aggregator)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private readonly List<List<ChatMessage>> _agentResults = [];

  [MessageHandler]
  private async ValueTask HandleMessagesAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
  {
    // Collect each agent's result set (one invocation per agent)
    _agentResults.Add(messages);

    // Once all agents have reported, merge and forward to the next stage
    if (_agentResults.Count == numberOfConcurrentAgents)
    {
      List<ChatMessage> aggregatedResult = aggregator(_agentResults);
      await context.SendMessageAsync(aggregatedResult, cancellationToken: cancellationToken);
      await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
    }
  }

  public ValueTask ResetAsync()
  {
    _agentResults.Clear();
    return default;
  }
}
