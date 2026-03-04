using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Spectre.Console;
using System.Text;

namespace Apex.AgenticEntityExtractor.Helpers;

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
    //AnsiConsole.MarkupLine("[yellow]Press Enter to continue...[/]");
    PrintInputImage(message);
    //AnsiConsole.MarkupLine("[yellow]Press Enter to continue...[/]");
    //Console.ReadLine();
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
    //AnsiConsole.MarkupLine("[yellow]Press Enter to continue and start workflow execution...[/]");
    //Console.ReadLine();
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
  //  SUPERSTEP EVENTS
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintSuperstepStart(SuperStepStartedEvent stepStarted)
  {
    AnsiConsole.WriteLine();
    var rule = new Rule($"[olive bold]⏳ #{stepStarted.StepNumber}[/]")
      .RuleStyle(new Style(Color.Olive))
      .LeftJustified();
    AnsiConsole.Write(rule);

    if (stepStarted.Data is SuperStepStartInfo startInfo && startInfo.SendingExecutors.Count != 0)
    {
      var executorNames = startInfo.SendingExecutors.Select(Helpers.WorkflowHelper.GetShortExecutorId);
      AnsiConsole.MarkupLine($"  [olive]📤 Sending: {Markup.Escape(string.Join(", ", executorNames))}[/]");
    }
  }

  internal static void PrintSuperstepCompleted(SuperStepCompletedEvent stepCompleted)
  {
    if (stepCompleted.Data is SuperStepCompletionInfo completionInfo)
    {
      if (completionInfo.ActivatedExecutors.Count != 0)
      {
        var executorNames = completionInfo.ActivatedExecutors.Select(Helpers.WorkflowHelper.GetShortExecutorId);
        AnsiConsole.MarkupLine($"  [olive]📥 Activated: {Markup.Escape(string.Join(", ", executorNames))}[/]");
      }

      if (completionInfo.InstantiatedExecutors.Count != 0)
      {
        var executorNames = completionInfo.InstantiatedExecutors.Select(Helpers.WorkflowHelper.GetShortExecutorId);
        AnsiConsole.MarkupLine($"  [olive]🆕 Instantiated: {Markup.Escape(string.Join(", ", executorNames))}[/]");
      }
    }

    AnsiConsole.Write(new Rule().RuleStyle(new Style(Color.Olive)));
  }

  // ════════════════════════════════════════════════════════════════════════
  //  EXECUTOR EVENTS
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintExecutorInvoked(string executorId, string preview = "")
  {
    var name = Markup.Escape(Helpers.WorkflowHelper.GetShortExecutorId(executorId));
    if (string.IsNullOrEmpty(preview))
      AnsiConsole.MarkupLine($"  [grey]⏳ {name}[/]");
    else
      AnsiConsole.MarkupLine($"  [grey]⏳ {name}: {Markup.Escape(preview)}[/]");
  }

  internal static void PrintExecutorCompleted(string executorId, string preview = "")
  {
    var name = Markup.Escape(Helpers.WorkflowHelper.GetShortExecutorId(executorId));
    if (string.IsNullOrEmpty(preview))
      AnsiConsole.MarkupLine($"  [grey]✓ {name}[/]");
    else
      AnsiConsole.MarkupLine($"  [grey]✓ {name}: {Markup.Escape(preview)}[/]");
  }

  internal static void PrintExecutorFailed(string executorId, string? errorMessage)
  {
    var shortError = Helpers.WorkflowHelper.FirstLine(errorMessage);
    AnsiConsole.MarkupLine($"  [red]❌ {Markup.Escape(Helpers.WorkflowHelper.GetShortExecutorId(executorId))} FAILED[/]");
    if (!string.IsNullOrWhiteSpace(shortError))
    {
      AnsiConsole.MarkupLine($"    [red]{Markup.Escape(shortError)}[/]");
    }
  }

  // ════════════════════════════════════════════════════════════════════════
  //  AGENT STREAMING
  // ════════════════════════════════════════════════════════════════════════

  internal static int PrintAgentStreamToken(AgentResponseUpdateEvent e, ref string? lastAgentId, Dictionary<string, string> agentColorMap)
  {
    if (string.IsNullOrEmpty(e.Update.Text))
      return 0;

    string agentName = Helpers.WorkflowHelper.GetShortExecutorId(e.ExecutorId);
    var color = Helpers.WorkflowHelper.GetOrAssignAgentColor(agentName, agentColorMap);

    if (agentName != lastAgentId)
    {
      lastAgentId = agentName;
      AnsiConsole.WriteLine();
      AnsiConsole.MarkupLine($"  [{color} bold]🤖 [{Markup.Escape(agentName)}][/]");
    }

    AnsiConsole.Markup($"[{color}]{Markup.Escape(e.Update.Text)}[/]");
    return 1;
  }

  // ════════════════════════════════════════════════════════════════════════
  //  WORKFLOW OUTPUT & ERRORS
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintFinalOutput(WorkflowOutputEvent output)
  {
    AnsiConsole.WriteLine();

    var messages = output.As<List<ChatMessage>>();
    var finalText = messages?.LastOrDefault()?.Text;
    if (!string.IsNullOrWhiteSpace(finalText))
    {
      var panel = new Panel($"[white]{Markup.Escape(finalText)}[/]")
        .Header("[green bold]Final Output[/]")
        .BorderColor(Color.Green)
        .Padding(1, 0)
        .Expand();
      AnsiConsole.Write(panel);
    }
    else
    {
      var panel = new Panel("[red]⚠ No final output text found[/]")
        .Header("[green bold]Final Output[/]")
        .BorderColor(Color.Red)
        .Padding(1, 0)
        .Expand();
      AnsiConsole.Write(panel);
    }
  }

  internal static void PrintWorkflowError(WorkflowErrorEvent error)
  {
    AnsiConsole.WriteLine();

    var errorMessage = error.Data switch
    {
      Exception ex => ex.Message,
      _ => error.Data?.ToString() ?? "Unknown error"
    };

    var panel = new Panel($"[red]✘ {Markup.Escape(errorMessage)}[/]")
      .Header("[red bold]Workflow Error[/]")
      .BorderColor(Color.Red)
      .Padding(1, 0)
      .Expand();
    AnsiConsole.Write(panel);
  }

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
        chart.AddItem(Helpers.WorkflowHelper.GetShortExecutorId(kvp.Key), Math.Round(kvp.Value.TotalSeconds, 2), Color.Yellow);
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

  internal static void UpdateDashboardPanels(
    Layout root,
    List<string> executorLines,
    string outputText,
    List<string> toolLines,
    List<string> reviewLines,
    Dictionary<string, TimeSpan> executorDurations,
    TimeSpan elapsed,
    int tokenCount,
    int superStep,
    string? activeAgent,
    string status,
    Dictionary<string, int> agentTokenCounts,
    Dictionary<string, int> executorInvocationCounts,
    Dictionary<int, TimeSpan> superStepDurations,
    HashSet<string> uniqueAgents)
  {
    int availableHeight = Math.Max(Console.WindowHeight - 3, 10);
    int maxExecutorLines = Math.Max(availableHeight - 3, 5);
    int maxOutputLines = Math.Max(availableHeight - 3, 5);

    var executorsText = executorLines.Count == 0
      ? "Waiting for steps..."
      : string.Join('\n', executorLines.Count > maxExecutorLines
        ? executorLines.Skip(executorLines.Count - maxExecutorLines)
        : executorLines);

    // outputText contains pre-formatted Spectre markup (per-agent colors)
    string leftPanelMarkup;
    if (string.IsNullOrWhiteSpace(outputText))
    {
      leftPanelMarkup = "[grey]Waiting for model output...[/]";
    }
    else
    {
      var lines = outputText.Split('\n');
      leftPanelMarkup = lines.Length > maxOutputLines
        ? string.Join('\n', lines[^maxOutputLines..])
        : outputText;
    }

    int maxToolLines = Math.Max(availableHeight / 2 - 3, 5);
    var toolsText = toolLines.Count == 0
      ? "Waiting for tool calls/responses..."
      : string.Join('\n', toolLines.Count > maxToolLines
        ? toolLines.Skip(toolLines.Count - maxToolLines)
        : toolLines);

    // ── Metrics ──
    double tokPerSec = elapsed.TotalSeconds > 0 ? tokenCount / elapsed.TotalSeconds : 0;

    var metricsLines = new List<string>
    {
      $"Status: {status}",
      $"Elapsed: {elapsed.TotalSeconds:F1}s",
      $"Active: {activeAgent ?? "-"}",
      $"Agents: {uniqueAgents.Count}",
      $"Tokens: ~{tokenCount}  |  {tokPerSec:F1} tok/s",
    };

    if (executorDurations.Count > 0)
    {
      metricsLines.Add("Executor (duration / invocations / tokens):");
      foreach (var kvp in executorDurations.OrderByDescending(kvp => kvp.Value))
      {
        var shortId = Helpers.WorkflowHelper.GetShortExecutorId(kvp.Key);
        var executorInvocations = executorInvocationCounts.TryGetValue(kvp.Key, out var val) ? $"{val}x" : "-";
        var tokens = agentTokenCounts.TryGetValue(shortId, out var t) ? $"~{t} tok" : "-";
        metricsLines.Add($"  {shortId}: {kvp.Value.TotalSeconds:F2}s / {executorInvocations} / {tokens}");
      }
    }

    if (superStepDurations.Count > 0)
    {
      var stepsDurations = superStepDurations
        .OrderBy(kvp => kvp.Key)
        .ToList();

      var steps = string.Join(" ", stepsDurations.Select(kvp => $"#{kvp.Key}({kvp.Value.TotalSeconds:F1}s)"));
      metricsLines.Add($"Steps: {steps}");
    }

    // ── Dynamic Output panel header ──
    var outputTitle = activeAgent is not null
      ? $"🤖 {activeAgent}"
      : "🤖 Latest Output";

    root["Left"].Update(BuildDashboardPanelMarkup(outputTitle, leftPanelMarkup, Color.Cyan1));
    root["Middle"].Update(BuildDashboardPanel($"📋 SuperSteps ({superStep}) / Executors ({executorInvocationCounts.Count})", executorsText, Color.Grey));
    root["Tools"].Update(BuildDashboardPanel($"🔧 Tools ({toolLines.Count})", toolsText, Color.Blue));
    root["Metrics"].Update(BuildDashboardPanel("📊 Metrics", string.Join('\n', metricsLines), Color.Gold1));

    var reviewText = reviewLines.Count == 0
      ? "Waiting for review loop..."
      : string.Join('\n', reviewLines);
    root["ReviewStatus"].Update(BuildDashboardPanel($"🔄 Terminator Status ({reviewLines.Count})", reviewText, Color.Yellow));
  }

  // ════════════════════════════════════════════════════════════════════════
  //  POST-DASHBOARD FULL LOG
  // ════════════════════════════════════════════════════════════════════════

  internal static void PrintPostDashboardLog(
    List<string> eventLines,
    string outputText,
    string? yieldedOutputText,
    Dictionary<string, TimeSpan> executorDurations,
    TimeSpan totalTime,
    int tokenCount,
    Dictionary<string, StringBuilder>? perAgentOutput = null,
    Dictionary<string, string>? agentColorMap = null)
  {
    // ── Full event history ──
    if (eventLines.Count > 0)
    {
      var eventLogContent = string.Join('\n', eventLines.Select(l => $"[grey]{Markup.Escape(l)}[/]"));
      var eventLogPanel = new Panel(eventLogContent)
        .Header("[grey bold]Full Event Log[/]")
        .BorderColor(Color.Grey)
        .Padding(1, 0)
        .Expand();
      AnsiConsole.Write(eventLogPanel);
      AnsiConsole.WriteLine();
    }

    // ── Per-agent output panels ──
    if (perAgentOutput is { Count: > 0 })
    {
      PrintPerAgentOutputPanels(perAgentOutput, agentColorMap);
    }

    // ── Final output (yielded output) ──
    PrintFinalResponsePanel(yieldedOutputText);

    // ── Timing summary ──
    PrintTimingSummary(executorDurations, totalTime, tokenCount);
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
