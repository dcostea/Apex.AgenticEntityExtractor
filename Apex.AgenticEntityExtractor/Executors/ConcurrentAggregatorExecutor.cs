using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Terminal Fan-In Aggregator</b> — the final executor in a standalone concurrent
/// orchestration graph that yields the merged result as workflow output.
///
/// <b>Role in the graph:</b>
/// <code>
///   [Batcher 1] ──┐
///   [Batcher 2] ──┼──barrier──→ [ConcurrentAggregatorExecutor] ──→ Output
///   [Batcher 3] ──┘
/// </code>
///
/// <b>How it works:</b>
/// After the fan-in barrier releases (all agents completed), this executor receives each
/// agent's result set one at a time via <see cref="HandleMessagesAsync"/>. Each batch is
/// appended to <c>_agentResults</c>. Once the count equals <paramref name="numberOfConcurrentAgents"/>,
/// the injected <paramref name="aggregator"/> function deduplicates and merges all batches,
/// then the combined result is yielded as final workflow output via
/// <see cref="IWorkflowContext.YieldOutputAsync"/>.
///
/// <b>Aggregator signature:</b>
/// <c>Func&lt;IList&lt;List&lt;ChatMessage&gt;&gt;, List&lt;ChatMessage&gt;&gt;</c> — the same shape
/// used by <see cref="AgentWorkflowBuilder.BuildConcurrent"/>, making the aggregation logic
/// reusable across high-level and custom-wired workflows.
///
/// <b>Contrast with <see cref="AggregatorExecutor"/>:</b>
/// <see cref="AggregatorExecutor"/> <i>forwards</i> merged results downstream for further
/// pipeline stages. This executor <i>yields</i> them as final output, making it suitable
/// only for the last fan-in in a workflow (or in a sub-workflow wrapped via
/// <see cref="Workflow.AsAIAgent"/>).
///
/// <b>Cross-run state:</b> Declared as <c>declareCrossRunShareable: true</c> and implements
/// <see cref="IResettableExecutor"/> so that <c>_agentResults</c> is cleared between runs.
/// </summary>
[YieldsOutput(typeof(List<ChatMessage>))]
public partial class ConcurrentAggregatorExecutor(string executorId, int numberOfConcurrentAgents, Func<IList<List<ChatMessage>>, List<ChatMessage>> aggregator)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private readonly List<List<ChatMessage>> _agentResults = [];

  [MessageHandler]
  private async ValueTask HandleMessagesAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
  {
    // Collect each agent's result set (one invocation per agent)
    _agentResults.Add(messages);

    // Once all agents have reported, merge and yield the final output
    if (_agentResults.Count == numberOfConcurrentAgents)
    {
      List<ChatMessage> aggregatedResult = aggregator(_agentResults);
      await context.YieldOutputAsync(aggregatedResult, cancellationToken);
    }
  }

  public ValueTask ResetAsync()
  {
    _agentResults.Clear();
    return default;
  }
}
