using Microsoft.Extensions.AI;
using Spectre.Console;
using System.Text;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Pure presentation layer — every method writes to the console via Spectre.Console
/// and has no side-effects on workflow state, timing, or event iteration.
/// </summary>
internal static class WorkflowConsoleRenderer
{
  // ════════════════════════════════════════════════════════════════════════
  //  BANNER & QUERY
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintBanner(string header)
  {
    AnsiConsole.WriteLine();
    var panel = new Panel($"[yellow bold]{Markup.Escape(header)}[/]")
      .Border(BoxBorder.Double)
      .BorderColor(Color.Yellow)
      .Padding(2, 0)
      .Expand();
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  internal static void PrintQueryAndInputImagePreviewAndWait(ChatMessage message)
  {
    PrintQuery(message);
    PrintInputImage(message);
  }

  internal static void PrintMermaidFlowPreviewAndWait(string mermaidFlow)
  {
    if (string.IsNullOrWhiteSpace(mermaidFlow))
      return;

    var panel = new Panel($"[grey]{Markup.Escape(mermaidFlow)}[/]")
      .Header("[deepskyblue1 bold]🧭 Workflow Mermaid Graph[/]")
      .BorderColor(Color.DeepSkyBlue1)
      .Padding(1, 0)
      .Expand();

    AnsiConsole.Write(panel);
  }

  internal static void PrintQuery(ChatMessage message)
  {
    var panel = new Panel($"[green dim]{Markup.Escape(message.Text)}[/]")
      .Header("[green]📝 QUERY[/]")
      .BorderColor(Color.Green)
      .Padding(1, 0);
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  // ════════════════════════════════════════════════════════════════════════
  //  INPUT IMAGE
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintInputImage(ChatMessage message)
  {
    var imageContent = message.Contents
      .OfType<DataContent>()
      .FirstOrDefault(content => content.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));

    if (imageContent is null)
      return;

    var tempFile = Path.Combine(Path.GetTempPath(), $"aiextract-{Guid.NewGuid():N}.img");

    try
    {
      File.WriteAllBytes(tempFile, imageContent.Data.ToArray());
      var availableHeight = Math.Max(8, Console.WindowHeight - 8);
      var heightBoundWidth = Math.Max(24, availableHeight * 2);
      var image = new CanvasImage(tempFile)
      {
        MaxWidth = Math.Max(24, Math.Min(Console.WindowWidth - 10, heightBoundWidth))
      };

      var panel = new Panel(image)
        .Header("[blue bold]🖼 Input Image[/]")
        .BorderColor(Color.Blue)
        .Expand();

      AnsiConsole.Write(panel);
      AnsiConsole.WriteLine();
    }
    catch (Exception ex)
    {
      AnsiConsole.MarkupLine($"[grey]Unable to render input image in console: {Markup.Escape(ex.Message)}[/]");
    }
    finally
    {
      if (File.Exists(tempFile))
      {
        File.Delete(tempFile);
      }
    }
  }

  // ════════════════════════════════════════════════════════════════════════
  //  WORKFLOW OUTPUT
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintFinalResponsePanel(string? outputText)
  {
    if (string.IsNullOrWhiteSpace(outputText))
      return;

    AnsiConsole.WriteLine();
    var panel = new Panel($"[white]{Markup.Escape(outputText)}[/]")
      .Header("[green bold]Final Output[/]")
      .BorderColor(Color.Green)
      .Padding(1, 0)
      .Expand();
    AnsiConsole.Write(panel);
  }

  // ════════════════════════════════════════════════════════════════════════
  //  TIMING SUMMARY
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintTimingSummary(Dictionary<string, TimeSpan> executorDurations, TimeSpan totalTime, int tokenCount)
  {
    AnsiConsole.WriteLine();

    if (executorDurations.Count != 0)
    {
      var chart = new BarChart()
        .Width(70)
        .Label("[yellow bold]⏱ Executor/Agent Timing[/]");

      var sorted = executorDurations.OrderByDescending(kvp => kvp.Value);
      foreach (var kvp in sorted)
      {
        chart.AddItem(WorkflowHelper.GetShortExecutorId(kvp.Key), Math.Round(kvp.Value.TotalSeconds, 2), Color.Yellow);
      }

      AnsiConsole.Write(chart);
      AnsiConsole.WriteLine();
    }

    var summaryLines = $"[yellow]⏱ Total Workflow Time:[/] [white]{totalTime.TotalSeconds:F2}s[/]";
    if (tokenCount > 0)
    {
      summaryLines += $"{Environment.NewLine}[yellow]📊 Streamed Tokens:[/]    [white]~{tokenCount}[/]";
    }

    var summaryPanel = new Panel(summaryLines)
      .Header("[yellow bold]Execution Summary[/]")
      .BorderColor(Color.Yellow)
      .Padding(1, 0);
    AnsiConsole.Write(summaryPanel);
    AnsiConsole.WriteLine();
  }

  // ════════════════════════════════════════════════════════════════════════
  //  DASHBOARD LAYOUT
  // ════════════════════════════════════════════════════════════════════════

  internal static Layout BuildDashboardLayout(string header)
  {
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

    root["Left"].Update(BuildDashboardPanel("🤖 Latest Output", "Waiting for model output...", Color.Cyan1));
    root["ReviewStatus"].Update(BuildDashboardPanel("🔄 Terminator Status", "Waiting for terminator review status...", Color.Yellow));
    root["Middle"].Update(BuildDashboardPanel("📋 Events", "Waiting for events...", Color.Grey));
    root["Tools"].Update(BuildDashboardPanel("🔧 Tools", "Waiting for tool calls/responses...", Color.Blue));
    root["Metrics"].Update(BuildDashboardPanel("📊 Metrics", "Elapsed: 0.0s", Color.Gold1));

    return root;
  }

  // ════════════════════════════════════════════════════════════════════════
  //  DASHBOARD HELPERS
  // ════════════════════════════════════════════════════════════════════════

  internal static Panel BuildDashboardPanel(string title, string content, Color borderColor)
  {
    return new Panel($"[white]{Markup.Escape(content)}[/]")
      .Header($"[bold]{Markup.Escape(title)}[/]")
      .Border(BoxBorder.Rounded)
      .BorderColor(borderColor)
      .Expand();
  }

  internal static Panel BuildDashboardPanelMarkup(string title, string markupContent, Color borderColor)
  {
    return new Panel(markupContent)
      .Header($"[bold]{Markup.Escape(title)}[/]")
      .Border(BoxBorder.Rounded)
      .BorderColor(borderColor)
      .Expand();
  }

  internal static void UpdateDashboardPanels(Layout root, DashboardState state, string cachedColoredText, string status)
  {
    int availableHeight = Math.Max(Console.WindowHeight - 3, 10);
    int maxExecutorLines = Math.Max(availableHeight - 3, 5);
    int maxOutputLines = Math.Max(availableHeight - 3, 5);

    var executorsText = state.EventLines.Count == 0
      ? "Waiting for steps..."
      : string.Join('\n', state.EventLines.Count > maxExecutorLines
        ? state.EventLines.Skip(state.EventLines.Count - maxExecutorLines)
        : state.EventLines);

    // outputText contains pre-formatted Spectre markup (per-agent colors)
    string leftPanelMarkup;
    if (string.IsNullOrWhiteSpace(cachedColoredText))
    {
      leftPanelMarkup = "[grey]Waiting for model output...[/]";
    }
    else
    {
      var lines = cachedColoredText.Split('\n');
      leftPanelMarkup = lines.Length > maxOutputLines
        ? string.Join('\n', lines[^maxOutputLines..])
        : cachedColoredText;
    }

    int maxToolLines = Math.Max(availableHeight / 2 - 3, 5);
    var toolsText = state.ToolLines.Count == 0
      ? "Waiting for tool calls/responses..."
      : string.Join('\n', state.ToolLines.Count > maxToolLines
        ? state.ToolLines.Skip(state.ToolLines.Count - maxToolLines)
        : state.ToolLines);

    // ── Metrics ──
    double tokPerSec = state.Elapsed.Elapsed.TotalSeconds > 0 ? state.TokenCount / state.Elapsed.Elapsed.TotalSeconds : 0;

    var metricsLines = new List<string>
    {
      $"Status: {status}",
      $"Elapsed: {state.Elapsed.Elapsed.TotalSeconds:F1}s",
      $"Active: {state.ActiveAgent ?? "-"}",
      $"Agents: {state.UniqueAgents.Count}",
      $"Tokens: ~{state.TokenCount}  |  {tokPerSec:F1} tok/s",
    };

    if (state.ExecutorDurations.Count > 0)
    {
      metricsLines.Add("Executor (duration / invocations / tokens):");
      foreach (var kvp in state.ExecutorDurations.OrderByDescending(kvp => kvp.Value))
      {
        var shortId = WorkflowHelper.GetShortExecutorId(kvp.Key);
        var executorInvocations = state.ExecutorInvocationCounts.TryGetValue(kvp.Key, out var val) ? $"{val}x" : "-";
        var tokens = state.AgentTokenCounts.TryGetValue(shortId, out var t) ? $"~{t} tok" : "-";
        metricsLines.Add($"  {shortId}: {kvp.Value.TotalSeconds:F2}s / {executorInvocations} / {tokens}");
      }
    }

    if (state.SuperStepDurations.Count > 0)
    {
      var stepsDurations = state.SuperStepDurations
        .OrderBy(kvp => kvp.Key)
        .ToList();

      var steps = string.Join(" ", stepsDurations.Select(kvp => $"#{kvp.Key}({kvp.Value.TotalSeconds:F1}s)"));
      metricsLines.Add($"Steps: {steps}");
    }

    // ── Dynamic Output panel header ──
    var outputTitle = state.ActiveAgent is not null
      ? $"🤖 {state.ActiveAgent}"
      : "🤖 Latest Output";

    root["Left"].Update(BuildDashboardPanelMarkup(outputTitle, leftPanelMarkup, Color.Cyan1));
    root["Middle"].Update(BuildDashboardPanel($"📋 SuperSteps ({state.SuperStep}) / Executors ({state.ExecutorInvocationCounts.Count})", executorsText, Color.Grey));
    root["Tools"].Update(BuildDashboardPanel($"🔧 Tools ({state.ToolLines.Count})", toolsText, Color.Blue));
    root["Metrics"].Update(BuildDashboardPanel("📊 Metrics", string.Join('\n', metricsLines), Color.Gold1));

    var reviewText = state.ReviewLines.Count == 0
      ? "Waiting for review loop..."
      : string.Join('\n', state.ReviewLines);
    root["ReviewStatus"].Update(BuildDashboardPanel($"🔄 Terminator Status ({state.ReviewLines.Count})", reviewText, Color.Yellow));
  }

  // ════════════════════════════════════════════════════════════════════════
  //  POST-DASHBOARD FULL LOG
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintPostDashboardLog(DashboardState state)
  {
    // ── Full event history ──
    if (state.FullEventLog.Count > 0)
    {
      var eventLogContent = string.Join('\n', state.FullEventLog.Select(l => $"[grey]{Markup.Escape(l)}[/]"));
      var eventLogPanel = new Panel(eventLogContent)
        .Header("[grey bold]Full Event Log[/]")
        .BorderColor(Color.Grey)
        .Padding(1, 0)
        .Expand();
      AnsiConsole.Write(eventLogPanel);
      AnsiConsole.WriteLine();
    }

    // ── Per-agent output panels ──
    if (state.PerAgentOutput is { Count: > 0 })
    {
      PrintPerAgentOutputPanels(state.PerAgentOutput, state.AgentColorMap);
    }

    // ── Final output (yielded output) ──
    PrintFinalResponsePanel(state.YieldedOutputText);

    // ── Timing summary ──
    PrintTimingSummary(state.ExecutorDurations, state.Elapsed.Elapsed, state.TokenCount);
  }

  private static void PrintPerAgentOutputPanels(
    Dictionary<string, StringBuilder> perAgentOutput,
    Dictionary<string, string>? agentColorMap)
  {
    foreach (var (agentKey, buffer) in perAgentOutput)
    {
      var text = buffer.ToString();
      if (string.IsNullOrWhiteSpace(text))
        continue;

      var color = agentColorMap is not null
        ? WorkflowHelper.GetOrAssignAgentColor(agentKey, agentColorMap)
        : "cyan1";

      var escapedText = Markup.Escape(text);
      var coloredText = escapedText.Replace("\n", $"[/]\n[{color}]");
      var content = $"[{color}]{coloredText}[/]";

      var panel = new Panel(content)
        .Header($"[{color} bold]🤖 {Markup.Escape(agentKey)}[/]")
        .BorderColor(Color.Grey)
        .Padding(1, 0)
        .Expand();
      AnsiConsole.Write(panel);
      AnsiConsole.WriteLine();
    }
  }
}
