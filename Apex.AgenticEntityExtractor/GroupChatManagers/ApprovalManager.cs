using Apex.AgenticEntityExtractor.Executors;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.GroupChatManagers;

/// <summary>
/// <b>Public API Adapter</b> for the framework's <see cref="RoundRobinGroupChatManager"/>.
///
/// The base class declares its orchestration methods (<c>ShouldTerminateAsync</c>,
/// <c>SelectNextAgentAsync</c>, <c>UpdateHistoryAsync</c>) as <c>protected internal</c>,
/// which means they can only be called from within the framework assembly.
///
/// Since our custom <see cref="RefinementExecutor"/> lives outside the framework, this adapter
/// exposes those methods publicly via the <c>new</c> keyword (method hiding), delegating
/// each call to the <c>base</c> implementation.
///
/// <b>Why <see cref="CurrentIterationCount"/>?</b>
/// The base <c>IterationCount</c> property has an <c>internal set</c> accessor — only the
/// framework's own <c>GroupChatHost</c> can increment it. Since <see cref="RefinementExecutor"/>
/// is a custom host, we maintain our own counter that the host increments and the termination
/// function reads.
/// </summary>
public class ApprovalManager(IReadOnlyList<AIAgent> agents, Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>> terminationFunction)
  : RoundRobinGroupChatManager(agents, shouldTerminateFunc: terminationFunction)
{
  /// <summary>Tracks iterations independently since base <c>IterationCount</c> has <c>internal set</c>.</summary>
  public int CurrentIterationCount { get; set; }

  /// <summary>
  /// Exposes the base termination check publicly for custom host/orchestrator usage.
  /// </summary>
  public new ValueTask<bool> ShouldTerminateAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
  {
    return base.ShouldTerminateAsync(messages, cancellationToken);
  }

  /// <summary>
  /// Exposes round-robin participant selection publicly for custom host/orchestrator usage.
  /// </summary>
  public new ValueTask<AIAgent> SelectNextAgentAsync(IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
  {
    return base.SelectNextAgentAsync(history, cancellationToken);
  }

  /// <summary>
  /// Exposes history update/filtering publicly for custom host/orchestrator usage.
  /// </summary>
  public new ValueTask<IEnumerable<ChatMessage>> UpdateHistoryAsync(
    IReadOnlyList<ChatMessage> history,
    CancellationToken cancellationToken = default)
  {
    return base.UpdateHistoryAsync(history, cancellationToken);
  }
}
