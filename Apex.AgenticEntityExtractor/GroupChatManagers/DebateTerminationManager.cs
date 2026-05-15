using Apex.AgenticEntityExtractor.Enums;
using Apex.AgenticEntityExtractor.Executors;
using Apex.AgenticEntityExtractor.Models;
using Apex.AgenticEntityExtractor.OutputRenderers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.GroupChatManagers;

/// <summary>
/// <b>Public API Adapter</b> for the framework's <see cref="RoundRobinGroupChatManager"/>.
///
/// The base class declares its orchestration methods (<c>ShouldTerminateAsync</c>,
/// <c>SelectNextAgentAsync</c>, <c>UpdateHistoryAsync</c>) as <c>protected internal</c>,
/// which means they can only be called from within the framework assembly.
///
/// Since our custom <see cref="GroupChatOrchestratorExecutor"/> lives outside the framework,
/// this adapter exposes those methods publicly via the <c>new</c> keyword (method hiding),
/// delegating each call to the <c>base</c> implementation.
///
/// <b>Why <see cref="CurrentIterationCount"/>?</b>
/// The base <c>IterationCount</c> property has an <c>internal set</c> accessor — only the
/// framework's own <c>GroupChatHost</c> can increment it. Since <see cref="GroupChatOrchestratorExecutor"/>
/// is a custom host, we maintain our own counter that the host increments and the termination
/// function reads.
/// </summary>
public class DebateTerminationManager(IReadOnlyList<AIAgent> agents, Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>> terminationFunction)
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
  /// Parses the structured JSON response and extracts the <see cref="DebateVerdict"/>.
  /// With <c>ResponseFormat = ForJsonSchema&lt;DebateResponse&gt;()</c>, the LLM output is
  /// guaranteed valid JSON — no text scanning or keyword detection needed.
  /// Falls back to <see cref="DebateVerdict.None"/> if parsing fails (defensive).
  /// </summary>
  public static DebateVerdict ClassifyVerdict(string text)
  {
    try
    {
      DebateResponse? response = JsonSerializer.Deserialize<DebateResponse>(
        PayloadHelper.NormalizeJsonPayload(text));
      return response?.Verdict ?? DebateVerdict.None;
    }
    catch (JsonException)
    {
      return DebateVerdict.None;
    }
  }

  /// <summary>
  /// Creates a termination function that ends the group chat when:
  /// <list type="bullet">
  ///   <item>The last message is classified as <see cref="DebateVerdict.Approved"/>.</item>
  ///   <item>The iteration count reaches the maximum — forces termination as a safety net.</item>
  /// </list>
  ///
  /// This function works with both orchestration paths:
  /// <list type="bullet">
  ///   <item><b>Custom orchestration:</b> reads <see cref="CurrentIterationCount"/>
  ///         (incremented by our <see cref="GroupChatOrchestratorExecutor"/>).</item>
  ///   <item><b>High-level workflow:</b> reads the base <c>IterationCount</c> property
  ///         (incremented by the framework's internal host).</item>
  /// </list>
  /// Status messages are recorded via <see cref="WorkflowHelper.EnqueueReviewStatusEvent(string)"/>
  /// so they appear in the dashboard review-status panel.
  /// </summary>
  public static Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>> VerdictTermination()
  {
    return (chatManager, messages, _) =>
    {
      string lastText = messages.LastOrDefault()?.Text ?? string.Empty;
      int currentIterationCount = GetCurrentIterationCount(chatManager);
      int maxIteration = chatManager.MaximumIterationCount;

      if (currentIterationCount >= maxIteration)
      {
        WorkflowHelper.EnqueueReviewStatusEvent($"⚠ Max turns reached - Stopping debate loop without approval (turn {currentIterationCount}/{maxIteration})");
        return ValueTask.FromResult(true);
      }

      DebateVerdict verdict = ClassifyVerdict(lastText);

      if (verdict is DebateVerdict.Approved)
      {
        WorkflowHelper.EnqueueReviewStatusEvent($"✅ {DebateVerdict.Approved} - Exiting debate loop (turn {currentIterationCount}/{maxIteration})");
        return ValueTask.FromResult(true);
      }

      if (verdict is DebateVerdict.Rejected)
      {
        WorkflowHelper.EnqueueReviewStatusEvent($"🔄 {DebateVerdict.Rejected} - Continuing debate (turn {currentIterationCount}/{maxIteration})");
      }

      return ValueTask.FromResult(false);
    };
  }

  private static int GetCurrentIterationCount(RoundRobinGroupChatManager chatManager)
  {
    if (chatManager is DebateTerminationManager manager)
      return manager.CurrentIterationCount;

    return chatManager.IterationCount;
  }
}
