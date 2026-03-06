using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

public class WorkflowHelper(IWorkflowRenderer renderer)
{
  private static readonly System.Buffers.SearchValues<char> _guidChars = System.Buffers.SearchValues.Create(GuidChars);
  private const string GuidChars = "0123456789abcdefABCDEF";
  internal static readonly ConcurrentQueue<string> ExternalToolEvents = new();
  internal static readonly ConcurrentQueue<string> ReviewStatusEvents = new();

  private static readonly string[] AgentColorPalette =
    ["cyan1", "green", "yellow", "magenta1", "dodgerblue1", "orange1", "mediumpurple1", "turquoise2", "salmon1", "chartreuse1"];

  internal static string GetOrAssignAgentColor(string agentKey, Dictionary<string, string> agentColors)
  {
    if (!agentColors.TryGetValue(agentKey, out var color))
    {
      color = AgentColorPalette[agentColors.Count % AgentColorPalette.Length];
      agentColors[agentKey] = color;
    }
    return color;
  }

  // ════════════════════════════════════════════════════════════════════════
  //  PUBLIC API
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// Streams a single agent's response to the console with author tracking.
  /// </summary>
  public async Task RenderAgentResponseStreamAsync(AIAgent agent, ChatMessage message, string header)
  {
    renderer.PrintBanner(header);
    renderer.PrintQuery(message);
    renderer.PrintInputImage(message);

    string? lastAuthor = null;
    await foreach (var update in agent.RunStreamingAsync(message))
    {
      if (lastAuthor != update.AuthorName)
      {
        lastAuthor = update.AuthorName;
        renderer.BeginAgentStreamSection(update.AuthorName);
      }

      renderer.WriteStreamingToken(update.Text);
    }
    renderer.EndStreaming();
  }

  /// <summary>
  /// Watches the full workflow event stream and prints a rich, structured execution log
  /// showing every superstep, executor invocation, agent response, and timing summary.
  /// </summary>
  public async Task<string?> RenderWorkflowExecutionEventsAsync(StreamingRun run, string header)
  {
    var state = new DashboardState();
    state.Elapsed.Start();

    await renderer.RunLiveDashboardAsync(header, async session =>
    {
      void FinalizeWorkflow(string eventText, string status)
      {
        state.Elapsed.Stop();
        state.FlushInProgressTimings();
        state.FlushInProgressSuperStepTimings();
        state.FlushPendingTurnTokenCounts();
        state.DrainExternalToolEvents();
        state.LogEvent(eventText);
        session.FinalizeDashboard(state, status);
      }

      await foreach (var evt in run.WatchStreamAsync())
      {
        state.DrainExternalToolEvents();
        state.DrainReviewStatusEvents();

        switch (evt)
        {
          case SuperStepStartedEvent stepStarted:
            state.SuperStep = stepStarted.StepNumber;
            state.FlushPendingTurnTokenCounts();
            var stepStartText = $"⏳ Step #{stepStarted.StepNumber}";
            if (stepStarted.Data is SuperStepStartInfo startInfo && startInfo.SendingExecutors.Count != 0)
            {
              stepStartText += $" ← Senders: {string.Join(", ", startInfo.SendingExecutors.Select(GetShortExecutorId))}";
            }
            state.LogEvent(stepStartText);
            state.SuperStepTimings[stepStarted.StepNumber] = Stopwatch.StartNew();
            break;

          case SuperStepCompletedEvent stepCompleted:
            state.FlushPendingTurnTokenCounts();
            if (state.SuperStepTimings.TryGetValue(stepCompleted.StepNumber, out var ssSw))
            {
              ssSw.Stop();
              state.SuperStepDurations[stepCompleted.StepNumber] = ssSw.Elapsed;
            }
            var stepCompletedText = $"✅ Step #{stepCompleted.StepNumber}";
            if (stepCompleted.Data is SuperStepCompletionInfo completionInfo)
            {
              if (completionInfo.ActivatedExecutors.Count != 0)
              {
                stepCompletedText += $" → Activated: {string.Join(", ", completionInfo.ActivatedExecutors.Select(GetShortExecutorId))}";
              }
              if (completionInfo.InstantiatedExecutors.Count != 0)
              {
                state.LogEvent(stepCompletedText);
                state.LogEvent($"  🆕 Instantiated: {string.Join(", ", completionInfo.InstantiatedExecutors.Select(GetShortExecutorId))}");
                break;
              }
            }
            state.LogEvent(stepCompletedText);
            break;

          case ExecutorInvokedEvent invoked:
            if (invoked.Data is TurnToken)
            {
              var shortId = GetShortExecutorId(invoked.ExecutorId);
              state.PendingTurnTokenCounts[shortId] = state.PendingTurnTokenCounts.TryGetValue(shortId, out var ttc) ? ttc + 1 : 1;
            }
            else
            {
              state.FlushPendingTurnTokenCounts();
              var shortId = GetShortExecutorId(invoked.ExecutorId);
              var preview = GetDataPreview(invoked.Data);
              state.FullEventLog.Add(string.IsNullOrEmpty(preview)
                ? $"⏳ {shortId}"
                : $"⏳ {shortId}: {preview}");
            }
            state.ExecutorInvocationCounts[invoked.ExecutorId] = state.ExecutorInvocationCounts.TryGetValue(invoked.ExecutorId, out var cnt) ? cnt + 1 : 1;
            state.StartTiming(invoked.ExecutorId);
            break;

          case ExecutorCompletedEvent completed:
            if (completed.Data is not TurnToken)
            {
              var completedPreview = GetDataPreview(completed.Data);
              state.FullEventLog.Add(string.IsNullOrEmpty(completedPreview)
                ? $"✅ {GetShortExecutorId(completed.ExecutorId)}"
                : $"✅ {GetShortExecutorId(completed.ExecutorId)}: {completedPreview}");
            }
            state.TryExtractTools(completed.Data);
            state.StopTiming(completed.ExecutorId);
            break;

          case ExecutorFailedEvent failed:
            state.LogEvent($"❌ {GetShortExecutorId(failed.ExecutorId)} FAILED: {FirstLine(failed.Data?.Message)}");
            state.StopTiming(failed.ExecutorId);
            break;

          case AgentResponseUpdateEvent update:
            if (!string.IsNullOrEmpty(update.Update.Text))
            {
              var agentKey = GetShortExecutorId(update.ExecutorId);

              if (!state.PerAgentOutput.TryGetValue(agentKey, out var agentBuffer))
              {
                agentBuffer = new StringBuilder();
                state.PerAgentOutput[agentKey] = agentBuffer;
              }

              // When a new response starts for the same agent, add a separator so code
              // fences from multiple invocations don't get concatenated.
              if (!state.PerAgentResponseCounts.TryAdd(agentKey, 1) && state.PendingNextResponseHeader.Remove(agentKey))
              {
                int responseNumber = ++state.PerAgentResponseCounts[agentKey];
                agentBuffer.AppendLine();
                agentBuffer.AppendLine();
                agentBuffer.AppendLine($"--- Response #{responseNumber} ---");
                agentBuffer.AppendLine();
              }

              agentBuffer.Append(update.Update.Text);

              state.ActiveAgent = agentKey;
              state.UniqueAgents.Add(agentKey);
              state.AgentTokenCounts[agentKey] = state.AgentTokenCounts.TryGetValue(agentKey, out var atc) ? atc + 1 : 1;
              state.TokenCount++;

              session.AppendAgentToken(agentKey, update.Update.Text, state);
            }
            break;

          case WorkflowStartedEvent:
            state.LogEvent("▶ Workflow started");
            break;

          // Subclass events must precede parent class events to avoid unreachable code
          case SubworkflowErrorEvent subError:
            state.LogEvent($"❌ Subworkflow error: {FirstLine(subError.Data)}");
            break;

          case SubworkflowWarningEvent subWarning:
            state.LogEvent($"⚠ Subworkflow warning: {subWarning.Data}");
            break;

          case WorkflowWarningEvent warning:
            state.LogEvent($"⚠ Warning: {warning.Data}");
            break;

          case RequestInfoEvent requestInfo:
            state.LogEvent($"📨 External request: {requestInfo.Data}");
            break;

          // AgentResponseEvent extends WorkflowOutputEvent — must be matched first to avoid premature exit
          case AgentResponseEvent agentResponse:
            state.TryExtractTools(agentResponse.Data);
            state.PendingNextResponseHeader.Add(GetShortExecutorId(agentResponse.ExecutorId));
            break;

          case WorkflowOutputEvent output:
            state.TryExtractTools(output.As<List<ChatMessage>>());
            state.YieldedOutputText = output.As<List<ChatMessage>>()?.LastOrDefault()?.Text;
            FinalizeWorkflow("🏁 Workflow completed", "Completed");
            return;

          case WorkflowErrorEvent error:
            FinalizeWorkflow($"❌ Workflow error: {FirstLine(error.Data)}", "Error");
            return;
        }

        session.TryRefresh(state, "Running");
      }
    });

    state.Elapsed.Stop();
    state.FlushInProgressTimings();
    state.FlushInProgressSuperStepTimings();
    renderer.PrintPostDashboardLog(state);
    return state.YieldedOutputText;
  }

  public static void EnqueueExternalToolEvent(string line)
  {
    if (!string.IsNullOrWhiteSpace(line))
      ExternalToolEvents.Enqueue(line);
  }

  public static void EnqueueReviewStatusEvent(string line)
  {
    if (!string.IsNullOrWhiteSpace(line))
      ReviewStatusEvents.Enqueue(line);
  }

  // ════════════════════════════════════════════════════════════════════════
  //  UTILITIES
  // ════════════════════════════════════════════════════════════════════════

  internal static string FirstLine(object? data)
  {
    var text = data is Exception ex ? ex.Message : data?.ToString();
    return text?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
  }

  /// <summary>
  /// Produces a human-readable short form of an executor ID by stripping
  /// framework-appended GUID suffixes while preserving meaningful name parts.
  /// <list type="bullet">
  ///   <item><c>EntitiesAgent_1_abc123…789abc</c> → <c>EntitiesAgent_1</c> (GUID stripped, name suffix preserved)</item>
  ///   <item><c>Batch/EntitiesAgent_1</c> → kept as-is (no GUID)</item>
  ///   <item><c>MermaidDiagramAgent</c> → kept as-is (no GUID)</item>
  ///   <item><c>ede7d7f050d54183bd34169b3e26e265</c> → <c>26e265</c> (pure GUID fallback)</item>
  ///   <item><c>RelationshipAggregator</c> → kept as-is</item>
  /// </list>
  /// </summary>
  internal static string GetShortExecutorId(string executorId)
  {
    // Find the last underscore — framework IDs follow the pattern <AgentName>_<GUID>
    int lastUnderscoreIdx = executorId.LastIndexOf('_');
    if (lastUnderscoreIdx > 0)
    {
      var suffix = executorId[(lastUnderscoreIdx + 1)..];

      // Only strip if the trailing segment looks like a GUID (long hex string)
      if (suffix.Length > 6 && suffix.AsSpan().IndexOfAnyExcept(_guidChars) < 0)
        return executorId[..lastUnderscoreIdx];

      return executorId;
    }

    // Pure GUID fallback (no underscore, all hex, 32+ chars)
    if (executorId.Length >= 32 && executorId.AsSpan().IndexOfAnyExcept(_guidChars) < 0)
      return executorId[^6..];

    return executorId;
  }

  /// <summary>
  /// Extracts a short, single-line text preview from executor event data
  /// (the input message for <see cref="ExecutorInvokedEvent"/> or the result
  /// for <see cref="ExecutorCompletedEvent"/>). Returns empty string for null data.
  /// </summary>
  internal static string GetDataPreview(object? data, int maxLen = 160)
  {
    if (data is null)
      return "";

    if (data is TurnToken)
      return "TurnToken";

    string? preview = null;

    if (data is ChatMessage message)
    {
      var text = message.Text;
      preview = string.IsNullOrWhiteSpace(text)
        ? $"ChatMessage(role={message.Role})"
        : $"ChatMessage(role={message.Role}) {text}";
    }
    else if (data is IEnumerable<ChatMessage> messages)
    {
      var list = messages as IList<ChatMessage> ?? messages.ToList();
      if (list.Count == 0)
        return "empty";

      var lastText = list[^1].Text;
      preview = lastText is not null
        ? $"({list.Count} msg) {lastText}"
        : $"({list.Count} msg)";
    }
    else if (data is AgentResponse response)
    {
      var lastText = response.Messages?.LastOrDefault()?.Text;
      if (lastText is not null)
        preview = lastText;
    }

    preview ??= data.GetType().Name;

    // Collapse to a single line
    preview = preview.ReplaceLineEndings(" ");

    if (preview.Length > maxLen)
      preview = string.Concat(preview.AsSpan(0, maxLen - 1), "…");

    return preview;
  }
}
