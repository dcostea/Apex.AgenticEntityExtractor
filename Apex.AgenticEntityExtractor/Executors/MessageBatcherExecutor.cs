using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Per-Agent Passthrough Batcher</b> — a lightweight relay that sits between each agent
/// and the fan-in barrier, forwarding messages without accumulation.
///
/// <b>Role in the graph:</b>
/// <code>
///   [Agent #N] ──→ [MessageBatcherExecutor] ──→ (fan-in barrier waits for all batchers)
/// </code>
///
/// <b>Why it exists:</b>
/// The fan-in barrier needs a distinct executor per branch to know when each agent has
/// finished. Without a dedicated node per branch, the barrier would receive an
/// undifferentiated stream of messages and could not determine which agent produced which
/// result. This executor gives each branch its own identity in the barrier.
///
/// <b>How it works:</b>
/// <see cref="HandleMessagesAsync"/> simply forwards the incoming <c>List&lt;ChatMessage&gt;</c>
/// unchanged — no buffering, no accumulation. The executor is effectively stateless;
/// <see cref="IResettableExecutor"/> is implemented as a no-op for interface compliance.
///
/// <b>Cross-run state:</b> Declared as <c>declareCrossRunShareable: true</c> because the
/// executor is reused across workflow runs, though it holds no mutable state.
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
internal sealed partial class MessageBatcherExecutor(string executorId)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  [MessageHandler]
  private async ValueTask HandleMessagesAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
  {
    await context.SendMessageAsync(messages, cancellationToken: cancellationToken);
  }

  public ValueTask ResetAsync() => default;
}
