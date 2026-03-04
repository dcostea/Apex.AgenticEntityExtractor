using Apex.AgenticEntityExtractor.Executors;
using Apex.AgenticEntityExtractor.Helpers;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.GroupChatManagers;

/// <summary>
/// Provides termination functions for group chat orchestrations.
///
/// A termination function is called by the <see cref="RoundRobinGroupChatManager"/> (or its
/// <see cref="ApprovalManager"/> subclass) after each agent turn to decide
/// whether the conversation should stop. It receives the manager instance and the current
/// conversation history.
/// </summary>
public class Terminators
{
  /// <summary>
  /// Creates a termination function that ends the group chat when:
  /// <list type="bullet">
  ///   <item>The last message contains <c>"APPROVED"</c> (without <c>"ERRORS"</c>) — the reviewer
  ///         accepted the diagram.</item>
  ///   <item>The iteration count reaches the maximum — forces termination as a safety net.</item>
  /// </list>
  ///
  /// This function works with both orchestration paths:
  /// <list type="bullet">
  ///   <item><b>Custom orchestration:</b> reads <see cref="ApprovalManager.CurrentIterationCount"/>
  ///         (incremented by our <see cref="RefinementExecutor"/>).</item>
  ///   <item><b>High-level workflow:</b> reads the base <c>IterationCount</c> property
  ///         (incremented by the framework's internal host).</item>
  /// </list>
  /// Status messages are recorded via <see cref="WorkflowHelper.RecordReviewStatusEvent(string)"/>
  /// so they appear in the dashboard review-status panel.
  /// </summary>
  public static Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>> TerminationFunction()
  {
    return (RoundRobinGroupChatManager chatManager, IEnumerable<ChatMessage> messages, CancellationToken _) =>
    {
      var lastText = messages.LastOrDefault()?.Text ?? "";

      int currentIteration = chatManager is ApprovalManager approvalManager
        ? approvalManager.CurrentIterationCount
        : chatManager.IterationCount;
      int maxIteration = chatManager.MaximumIterationCount;

      if (currentIteration >= maxIteration)
      {
        WorkflowHelper.RecordReviewStatusEvent($"⚠ Max round-robin turns reached - Stopping review loop without approval (turn {currentIteration}/{maxIteration})");
        return ValueTask.FromResult(true);
      }

      bool isApproved = lastText.Contains("APPROVED", StringComparison.OrdinalIgnoreCase) &&
        !lastText.Contains("ERRORS", StringComparison.OrdinalIgnoreCase);

      if (isApproved)
      {
        WorkflowHelper.RecordReviewStatusEvent($"✅ Diagram APPROVED - Exiting review loop (turn {currentIteration}/{maxIteration})");
        return ValueTask.FromResult(true);
      }

      if (lastText.Contains("ERRORS FOUND", StringComparison.OrdinalIgnoreCase))
      {
        WorkflowHelper.RecordReviewStatusEvent($"✓ Reviewer requested changes - Retrying (turn {currentIteration}/{maxIteration})");
      }

      return ValueTask.FromResult(false);
    };
  }
}
