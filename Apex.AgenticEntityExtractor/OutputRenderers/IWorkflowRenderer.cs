using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Abstraction over the workflow presentation layer. Implementations render workflow
/// execution events, agent responses, and dashboards using a specific UI technology
/// (e.g., Spectre.Console, plain System.Console, or a web-based DevUI).
/// </summary>
public interface IWorkflowRenderer
{
  // ── Simple rendering ──

  void PrintBanner(string header);
  void PrintQuery(ChatMessage message);
  void PrintInputImage(ChatMessage message);
  void PrintQueryAndInputImagePreviewAndWait(ChatMessage message);
  void PrintMermaidFlowPreviewAndWait(string mermaidFlow);
  void PrintPostDashboardLog(DashboardState state);

  // ── Agent response streaming (non-dashboard) ──

  void BeginAgentStreamSection(string? authorName);
  void WriteStreamingToken(string? text);
  void EndStreaming();

  // ── Live dashboard ──

  Task RunLiveDashboardAsync(string header, Func<IDashboardSession, Task> body);
}
