using Apex.AgenticEntityExtractor.Executors;
using Apex.AgenticEntityExtractor.OutputRenderers;
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

  private const string Approved = "APPROVED";
  private const string Rejected = "REJECTED";

  /// <summary>
  /// Creates a termination function that ends the group chat when:
  /// <list type="bullet">
  ///   <item>The last message contains <c>"APPROVED"</c> (without <c>"REJECTED"</c>) — the reviewer
  ///         accepted the diagram.</item>
  ///   <item>The iteration count reaches the maximum — forces termination as a safety net.</item>
  /// </list>
  ///
  /// This function works with both orchestration paths:
  /// <list type="bullet">
  ///   <item><b>Custom orchestration:</b> reads <see cref="CurrentIterationCount"/>
  ///         (incremented by our <see cref="RefinementExecutor"/>).</item>
  ///   <item><b>High-level workflow:</b> reads the base <c>IterationCount</c> property
  ///         (incremented by the framework's internal host).</item>
  /// </list>
  /// Status messages are recorded via <see cref="WorkflowHelper.EnqueueReviewStatusEvent(string)"/>
  /// so they appear in the dashboard review-status panel.
  /// </summary>
  public static Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>> ApprovedTermination()
  {
    return (chatManager, messages, _) =>
    {
      string lastText = messages.LastOrDefault()?.Text ?? string.Empty;
      int currentIterationCount = GetCurrentIterationCount(chatManager);
      int maxIteration = chatManager.MaximumIterationCount;

      if (currentIterationCount >= maxIteration)
      {
        WorkflowHelper.EnqueueReviewStatusEvent($"⚠ Max turns reached - Stopping review loop without approval (turn {currentIterationCount}/{maxIteration})");
        return ValueTask.FromResult(true);
      }

      bool isApproved = IsApproved(lastText);

      if (isApproved)
      {
        WorkflowHelper.EnqueueReviewStatusEvent($"✅ Diagram APPROVED - Exiting review loop (turn {currentIterationCount}/{maxIteration})");
        return ValueTask.FromResult(true);
      }

      if (lastText.Contains(Rejected, StringComparison.OrdinalIgnoreCase))
      {
        WorkflowHelper.EnqueueReviewStatusEvent($"🔄 Reviewer requested changes - Retrying (turn {currentIterationCount}/{maxIteration})");
      }

      return ValueTask.FromResult(false);
    };
  }

  private static int GetCurrentIterationCount(RoundRobinGroupChatManager chatManager)
  {
    if (chatManager is ApprovalManager approvalManager)
      return approvalManager.CurrentIterationCount;

    return chatManager.IterationCount;
  }

  private static bool IsApproved(string text)
  {
    return text.Contains(Approved, StringComparison.OrdinalIgnoreCase)
      && !text.Contains(Rejected, StringComparison.OrdinalIgnoreCase);
  }
}
