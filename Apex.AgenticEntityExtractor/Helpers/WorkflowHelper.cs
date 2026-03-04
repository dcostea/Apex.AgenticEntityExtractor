using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Helpers;

public static class WorkflowHelper
{
  private static readonly System.Buffers.SearchValues<char> s_guidChars = System.Buffers.SearchValues.Create(GuidChars);
  private const string GuidChars = "0123456789abcdefABCDEF";
  private static readonly ConcurrentQueue<string> ExternalToolEvents = new();
  private static readonly ConcurrentQueue<string> ReviewStatusEvents = new();

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
  public static async Task RenderAgentResponseStreamAsync(AIAgent agent, ChatMessage message, string header)
  {
    WorkflowConsoleRenderer.PrintBanner(header);
    WorkflowConsoleRenderer.PrintQuery(message);
    WorkflowConsoleRenderer.PrintInputImage(message);

    string? lastAuthor = null;
    await foreach (var update in agent.RunStreamingAsync(message))
    {
      if (lastAuthor != update.AuthorName)
      {
        lastAuthor = update.AuthorName;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [cyan]🤖 [{Markup.Escape(update.AuthorName ?? "")}][/]");
      }

      AnsiConsole.Markup($"[white]{Markup.Escape(update.Text ?? "")}[/]");
    }
    AnsiConsole.WriteLine();
  }

  /// <summary>
  /// Watches the full workflow event stream and prints a rich, structured execution log
  /// showing every superstep, executor invocation, agent response, and timing summary.
  /// </summary>
  public static async Task<string?> RenderWorkflowExecutionEventsAsync(StreamingRun run, string header)
  {
    var started = Stopwatch.StartNew();
    var executorTimings = new Dictionary<string, Stopwatch>();
    var executorDurations = new Dictionary<string, TimeSpan>();

    var eventLines = new List<string>();
    var fullEventLog = new List<string>();
    var coloredOutput = new StringBuilder();
    var toolLines = new List<string>();
    var toolDedup = new HashSet<string>();

    int tokenCount = 0;
    int superStep = 0;
    string? activeAgent = null;
    string? lastOutputAgent = null;
    int lastConsoleWidth = Console.WindowWidth;
    int lastConsoleHeight = Console.WindowHeight;

    var agentTokenCounts = new Dictionary<string, int>();
    var agentColorMap = new Dictionary<string, string>();
    var perAgentOutput = new Dictionary<string, StringBuilder>();
    var perAgentResponseCounts = new Dictionary<string, int>();
    var pendingNextResponseHeader = new HashSet<string>();
    var executorInvocationCounts = new Dictionary<string, int>();
    var superStepTimings = new Dictionary<int, Stopwatch>();
    var superStepDurations = new Dictionary<int, TimeSpan>();
    var uniqueAgents = new HashSet<string>();
    var pendingTurnTokenCounts = new Dictionary<string, int>();

    // Throttle: only refresh the live dashboard at most every 150 ms to avoid
    // extreme GC pressure from hundreds of per-token refreshes per second.
    var refreshThrottle = Stopwatch.StartNew();
    const int RefreshIntervalMs = 150;
    bool dashboardDirty = false;
    string cachedColoredText = string.Empty;

    string? yieldedOutputText = null;

    var reviewLines = new List<string>();

    void LogEvent(string text)
    {
      eventLines.Add(text);
      fullEventLog.Add(text);
    }

    var root = new Layout("Root")
      .SplitRows(
        new Layout("Top").Size(3),
        new Layout("Main")
          .SplitColumns(
            new Layout("LeftColumn").Ratio(1)
              .SplitRows(
                new Layout("Left").Ratio(3),
                new Layout("ReviewStatus").Ratio(1)),
            new Layout("Middle").Ratio(1),
            new Layout("Right").Ratio(1)
              .SplitRows(
                new Layout("Tools").Ratio(1),
                new Layout("Metrics").Ratio(2))));

    root["Top"].Update(new Panel($"[yellow bold]{Markup.Escape(header)}[/]")
      .Border(BoxBorder.Double)
      .BorderColor(Color.Yellow)
      .Expand());

    root["Left"].Update(WorkflowConsoleRenderer.BuildDashboardPanel("🤖 Latest Output", "Waiting for model output...", Color.Cyan1));
    root["ReviewStatus"].Update(WorkflowConsoleRenderer.BuildDashboardPanel("🔄 Terminator Status", "Waiting for terminator review status...", Color.Yellow));
    root["Middle"].Update(WorkflowConsoleRenderer.BuildDashboardPanel("📋 Events", "Waiting for events...", Color.Grey));
    root["Tools"].Update(WorkflowConsoleRenderer.BuildDashboardPanel("🔧 Tools", "Waiting for tool calls/responses...", Color.Blue));
    root["Metrics"].Update(WorkflowConsoleRenderer.BuildDashboardPanel("📊 Metrics", "Elapsed: 0.0s", Color.Gold1));

    await AnsiConsole.Live(root)
      .AutoClear(false)
      .Overflow(VerticalOverflow.Ellipsis)
      .Cropping(VerticalOverflowCropping.Bottom)
      .StartAsync(async ctx =>
      {
        void FinalizeWorkflow(string eventText, string status)
        {
          started.Stop();
          FlushInProgressTimings(executorTimings, executorDurations);
          FlushInProgressSuperStepTimings(superStepTimings, superStepDurations);
          FlushPendingTurnTokenCounts(pendingTurnTokenCounts, fullEventLog);
          DrainExternalToolEvents(toolLines, toolDedup);
          LogEvent(eventText);
          cachedColoredText = coloredOutput.ToString();
          WorkflowConsoleRenderer.UpdateDashboardPanels(root, eventLines, cachedColoredText, toolLines, reviewLines, executorDurations, started.Elapsed, tokenCount, superStep, null, status, agentTokenCounts, executorInvocationCounts, superStepDurations, uniqueAgents);
          ctx.Refresh();
        }

        await foreach (var evt in run.WatchStreamAsync())
        {
          DrainExternalToolEvents(toolLines, toolDedup);
          DrainReviewStatusEvents(reviewLines);

          switch (evt)
          {
            case SuperStepStartedEvent stepStarted:
              superStep = stepStarted.StepNumber;
              FlushPendingTurnTokenCounts(pendingTurnTokenCounts, fullEventLog);
              var stepStartText = $"⏳ Step #{stepStarted.StepNumber}";
              if (stepStarted.Data is SuperStepStartInfo startInfo && startInfo.SendingExecutors.Count != 0)
              {
                stepStartText += $" ← Senders: {string.Join(", ", startInfo.SendingExecutors.Select(GetShortExecutorId))}";
              }
              LogEvent(stepStartText);
              superStepTimings[stepStarted.StepNumber] = Stopwatch.StartNew();
              break;

            case SuperStepCompletedEvent stepCompleted:
              FlushPendingTurnTokenCounts(pendingTurnTokenCounts, fullEventLog);
              if (superStepTimings.TryGetValue(stepCompleted.StepNumber, out var ssSw))
              {
                ssSw.Stop();
                superStepDurations[stepCompleted.StepNumber] = ssSw.Elapsed;
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
                  LogEvent(stepCompletedText);
                  LogEvent($"  🆕 Instantiated: {string.Join(", ", completionInfo.InstantiatedExecutors.Select(GetShortExecutorId))}");
                  break;
                }
              }
              LogEvent(stepCompletedText);
              break;

            case ExecutorInvokedEvent invoked:
              if (invoked.Data is TurnToken)
              {
                var shortId = GetShortExecutorId(invoked.ExecutorId);
                pendingTurnTokenCounts[shortId] = pendingTurnTokenCounts.TryGetValue(shortId, out var ttc) ? ttc + 1 : 1;
              }
              else
              {
                FlushPendingTurnTokenCounts(pendingTurnTokenCounts, fullEventLog);
                var shortId = GetShortExecutorId(invoked.ExecutorId);
                var preview = GetDataPreview(invoked.Data);
                fullEventLog.Add(string.IsNullOrEmpty(preview)
                  ? $"⏳ {shortId}"
                  : $"⏳ {shortId}: {preview}");
              }
              executorInvocationCounts[invoked.ExecutorId] = executorInvocationCounts.TryGetValue(invoked.ExecutorId, out var cnt) ? cnt + 1 : 1;
              StartTiming(invoked.ExecutorId, executorTimings);
              break;

            case ExecutorCompletedEvent completed:
              if (completed.Data is not TurnToken)
              {
                var completedPreview = GetDataPreview(completed.Data);
                fullEventLog.Add(string.IsNullOrEmpty(completedPreview)
                  ? $"✅ {GetShortExecutorId(completed.ExecutorId)}"
                  : $"✅ {GetShortExecutorId(completed.ExecutorId)}: {completedPreview}");
              }
              TryExtractToolsFromObject(completed.Data, toolLines, toolDedup);
              StopTiming(completed.ExecutorId, executorTimings, executorDurations);
              break;

            case ExecutorFailedEvent failed:
              var failLine = $"❌ {GetShortExecutorId(failed.ExecutorId)} FAILED: {FirstLine(failed.Data?.Message)}";
              LogEvent(failLine);
              StopTiming(failed.ExecutorId, executorTimings, executorDurations);
              break;

            case AgentResponseUpdateEvent update:
              if (!string.IsNullOrEmpty(update.Update.Text))
              {
                var agentKey = GetShortExecutorId(update.ExecutorId);
                var color = GetOrAssignAgentColor(agentKey, agentColorMap);

                if (agentKey != lastOutputAgent)
                {
                  lastOutputAgent = agentKey;
                  coloredOutput.AppendLine();
                  coloredOutput.AppendLine($"[{color} bold]🤖 {Markup.Escape(agentKey)}:[/]");
                }

                var escaped = Markup.Escape(update.Update.Text);
                coloredOutput.Append($"[{color}]{escaped.Replace("\n", $"[/]\n[{color}]")}[/]");

                if (!perAgentOutput.TryGetValue(agentKey, out var agentBuffer))
                {
                  agentBuffer = new StringBuilder();
                  perAgentOutput[agentKey] = agentBuffer;
                }

                // When a new response starts for the same agent, add a separator so code
                // fences from multiple invocations don't get concatenated.
                if (!perAgentResponseCounts.TryAdd(agentKey, 1) && pendingNextResponseHeader.Remove(agentKey))
                {
                  int responseNumber = ++perAgentResponseCounts[agentKey];
                  agentBuffer.AppendLine();
                  agentBuffer.AppendLine();
                  agentBuffer.AppendLine($"--- Response #{responseNumber} ---");
                  agentBuffer.AppendLine();
                }

                agentBuffer.Append(update.Update.Text);

                activeAgent = agentKey;
                uniqueAgents.Add(agentKey);
                agentTokenCounts[agentKey] = agentTokenCounts.TryGetValue(agentKey, out var atc) ? atc + 1 : 1;
                dashboardDirty = true;
                tokenCount++;
              }
              break;

            case WorkflowStartedEvent:
              LogEvent("▶ Workflow started");
              break;

            // Subclass events must precede parent class events to avoid unreachable code
            case SubworkflowErrorEvent subError:
              var subErrText = $"❌ Subworkflow error: {FirstLine(subError.Data)}";
              LogEvent(subErrText);
              break;

            case SubworkflowWarningEvent subWarning:
              var subWarnText = $"⚠ Subworkflow warning: {subWarning.Data}";
              LogEvent(subWarnText);
              break;

            case WorkflowWarningEvent warning:
              var warnText = $"⚠ Warning: {warning.Data}";
              LogEvent(warnText);
              break;

            case RequestInfoEvent requestInfo:
              var reqText = $"📨 External request: {requestInfo.Data}";
              LogEvent(reqText);
              break;

            // AgentResponseEvent extends WorkflowOutputEvent — must be matched first to avoid premature exit
            case AgentResponseEvent agentResponse:
              TryExtractToolsFromObject(agentResponse.Data, toolLines, toolDedup);
              pendingNextResponseHeader.Add(GetShortExecutorId(agentResponse.ExecutorId));
              break;

            case WorkflowOutputEvent output:
              TryExtractToolsFromObject(output.As<List<ChatMessage>>(), toolLines, toolDedup);
              yieldedOutputText = output.As<List<ChatMessage>>()?.LastOrDefault()?.Text;
              FinalizeWorkflow("🏁 Workflow completed", "Completed");
              return;

            case WorkflowErrorEvent error:
              FinalizeWorkflow($"❌ Workflow error: {FirstLine(error.Data)}", "Error");
              return;
          }

          // Throttle refresh: only update dashboard at most every 150 ms
          if (refreshThrottle.ElapsedMilliseconds >= RefreshIntervalMs)
          {
            if (dashboardDirty)
            {
              cachedColoredText = coloredOutput.ToString();
              dashboardDirty = false;
            }

            WorkflowConsoleRenderer.UpdateDashboardPanels(root, eventLines, cachedColoredText, toolLines, reviewLines, executorDurations, started.Elapsed, tokenCount, superStep, activeAgent, "Running", agentTokenCounts, executorInvocationCounts, superStepDurations, uniqueAgents);

            int currentWidth = Console.WindowWidth;
            int currentHeight = Console.WindowHeight;
            if (currentWidth != lastConsoleWidth || currentHeight != lastConsoleHeight)
            {
              lastConsoleWidth = currentWidth;
              lastConsoleHeight = currentHeight;
              AnsiConsole.Clear();
            }

            ctx.Refresh();
            refreshThrottle.Restart();
          }
        }
      });

    started.Stop();
    FlushInProgressTimings(executorTimings, executorDurations);
    FlushInProgressSuperStepTimings(superStepTimings, superStepDurations);
    AnsiConsole.WriteLine();
    WorkflowConsoleRenderer.PrintPostDashboardLog(fullEventLog, coloredOutput.ToString(), yieldedOutputText, executorDurations, started.Elapsed, tokenCount, perAgentOutput, agentColorMap);
    return yieldedOutputText;
  }

  /// <summary>
  /// Watches the workflow stream but only prints the final output message (quiet mode).
  /// </summary>
  public static async Task RenderWorkflowFinalMessageAsync(StreamingRun run, string header)
  {
    WorkflowConsoleRenderer.PrintBanner(header);

    await foreach (var evt in run.WatchStreamAsync())
    {
      if (evt is WorkflowOutputEvent outputEvent)
      {
        var text = outputEvent.As<List<ChatMessage>>()?.LastOrDefault()?.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
          WorkflowConsoleRenderer.PrintFinalResponsePanel(text);
        }
      }
    }
  }

  // Presentation methods are handled by WorkflowConsoleRenderer.

  /// <summary>
  /// Emits a single collapsed summary line per executor for accumulated TurnToken events,
  /// then clears the pending counts.
  /// </summary>
  private static void FlushPendingTurnTokenCounts(Dictionary<string, int> pendingCounts, List<string> fullEventLog)
  {
    if (pendingCounts.Count == 0)
      return;

    foreach (var (executorId, count) in pendingCounts)
    {
      fullEventLog.Add($"🔄 {executorId}: {count}× TurnToken");
    }

    pendingCounts.Clear();
  }

  private static void TryExtractToolsFromMessages(IEnumerable<ChatMessage>? messages, List<string> toolLines, HashSet<string> dedup)
  {
    if (messages is null)
      return;

    foreach (var message in messages)
    {
      foreach (var content in message.Contents)
      {
        if (content is FunctionCallContent call)
        {
          var args = call.Arguments is null ? string.Empty : JsonSerializer.Serialize(call.Arguments);
          var line = $"CALL [{call.CallId}] {call.Name}({args})";
          if (dedup.Add(line))
          {
            toolLines.Add(line);
          }
        }
        else if (content is FunctionResultContent result)
        {
          var line = $"RESP [{result.CallId}] {result.Result}";
          if (dedup.Add(line))
          {
            toolLines.Add(line);
          }
        }
      }
    }
  }

  private static void TryExtractToolsFromObject(object? data, List<string> toolLines, HashSet<string> dedup)
  {
    if (data is null)
      return;

    if (data is IEnumerable<ChatMessage> messages)
    {
      TryExtractToolsFromMessages(messages, toolLines, dedup);
      return;
    }

    if (data is AgentResponse agentResponse)
    {
      TryExtractToolsFromMessages(agentResponse.Messages, toolLines, dedup);
      return;
    }

    var messagesProperty = data.GetType().GetProperty("Messages");
    if (messagesProperty?.GetValue(data) is IEnumerable<ChatMessage> propertyMessages)
    {
      TryExtractToolsFromMessages(propertyMessages, toolLines, dedup);
    }
  }

  public static void RecordExternalToolEvent(string line)
  {
    if (!string.IsNullOrWhiteSpace(line))
      ExternalToolEvents.Enqueue(line);
  }

  public static void RecordReviewStatusEvent(string line)
  {
    if (!string.IsNullOrWhiteSpace(line))
      ReviewStatusEvents.Enqueue(line);
  }

  private static void DrainReviewStatusEvents(List<string> reviewLines)
  {
    while (ReviewStatusEvents.TryDequeue(out var line))
    {
      reviewLines.Add(line);
    }
  }

  private static void DrainExternalToolEvents(List<string> toolLines, HashSet<string> dedup)
  {
    while (ExternalToolEvents.TryDequeue(out var line))
    {
      if (dedup.Add(line))
      {
        toolLines.Add(line);
      }
    }
  }

  // ════════════════════════════════════════════════════════════════════════
  //  TIMING HELPERS
  // ════════════════════════════════════════════════════════════════════════

  private static void StartTiming(string executorId, Dictionary<string, Stopwatch> timings)
  {
    timings[executorId] = Stopwatch.StartNew();
  }

  private static void StopTiming(string executorId, Dictionary<string, Stopwatch> timings, Dictionary<string, TimeSpan> durations)
  {
    if (!timings.Remove(executorId, out var sw))
      return;

    sw.Stop();
    durations[executorId] = durations.TryGetValue(executorId, out var existing)
      ? existing + sw.Elapsed
      : sw.Elapsed;
  }

  private static void FlushInProgressTimings(Dictionary<string, Stopwatch> timings, Dictionary<string, TimeSpan> durations)
  {
    foreach (var (id, sw) in timings)
    {
      if (!sw.IsRunning) continue;
      sw.Stop();
      durations[id] = durations.TryGetValue(id, out var existing)
        ? existing + sw.Elapsed
        : sw.Elapsed;
    }
    timings.Clear();
  }

  private static void FlushInProgressSuperStepTimings(Dictionary<int, Stopwatch> timings, Dictionary<int, TimeSpan> durations)
  {
    foreach (var (step, sw) in timings)
    {
      if (!sw.IsRunning) continue;
      sw.Stop();
      durations[step] = sw.Elapsed;
    }
    timings.Clear();
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
      if (suffix.Length > 6 && suffix.AsSpan().IndexOfAnyExcept(s_guidChars) < 0)
        return executorId[..lastUnderscoreIdx];

      return executorId;
    }

    // Pure GUID fallback (no underscore, all hex, 32+ chars)
    if (executorId.Length >= 32 && executorId.AsSpan().IndexOfAnyExcept(s_guidChars) < 0)
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
