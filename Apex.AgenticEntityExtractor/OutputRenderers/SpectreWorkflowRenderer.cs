using Microsoft.Extensions.AI;
using Spectre.Console;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Spectre.Console implementation of <see cref="IWorkflowRenderer"/>.
/// Delegates to <see cref="WorkflowConsoleRenderer"/> for panel building and layout helpers.
/// </summary>
internal sealed class SpectreWorkflowRenderer : IWorkflowRenderer
{
  public void PrintBanner(string header)
    => WorkflowConsoleRenderer.PrintBanner(header);

  public void PrintQuery(ChatMessage message)
    => WorkflowConsoleRenderer.PrintQuery(message);

  public void PrintInputImage(ChatMessage message)
    => WorkflowConsoleRenderer.PrintInputImage(message);

  public void PrintQueryAndInputImagePreviewAndWait(ChatMessage message)
    => WorkflowConsoleRenderer.PrintQueryAndInputImagePreviewAndWait(message);

  public void PrintMermaidFlowPreviewAndWait(string mermaidFlow)
    => WorkflowConsoleRenderer.PrintMermaidFlowPreviewAndWait(mermaidFlow);

  public void PrintPostDashboardLog(DashboardState state)
  {
    AnsiConsole.WriteLine();
    WorkflowConsoleRenderer.PrintPostDashboardLog(state);
  }

  public void BeginAgentStreamSection(string? authorName)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"  [cyan]🤖 [{Markup.Escape(authorName ?? "")}][/]");
  }

  public void WriteStreamingToken(string? text)
  {
    AnsiConsole.Markup($"[white]{Markup.Escape(text ?? "")}[/]");
  }

  public void EndStreaming()
  {
    AnsiConsole.WriteLine();
  }

  public async Task RunLiveDashboardAsync(string header, Func<IDashboardSession, Task> body)
  {
    var root = WorkflowConsoleRenderer.BuildDashboardLayout(header);

    await AnsiConsole.Live(root)
      .AutoClear(false)
      .Overflow(VerticalOverflow.Ellipsis)
      .Cropping(VerticalOverflowCropping.Bottom)
      .StartAsync(async ctx =>
      {
        var session = new SpectreDashboardSession(root, ctx);
        await body(session);
      });
  }
}
