using Spectre.Console;
using System.Diagnostics;
using System.Text;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Spectre.Console implementation of <see cref="IDashboardSession"/> wrapping
/// a <see cref="LiveDisplayContext"/> for throttled live-dashboard updates.
/// Owns all presentation-specific state (colored markup buffer, throttle timer,
/// console resize tracking) that was previously held on <see cref="DashboardState"/>.
/// </summary>
internal sealed class SpectreDashboardSession(Layout root, LiveDisplayContext ctx) : IDashboardSession
{
  private readonly Stopwatch _refreshThrottle = Stopwatch.StartNew();
  private readonly StringBuilder _coloredOutput = new();
  private string _cachedColoredText = "";
  private bool _dirty;
  private string? _lastOutputAgent;
  private int _lastConsoleWidth = Console.WindowWidth;
  private int _lastConsoleHeight = Console.WindowHeight;
  private const int RefreshIntervalMs = 150;

  public void AppendAgentToken(string agentKey, string? text, DashboardState state)
  {
    if (string.IsNullOrEmpty(text))
      return;

    var color = WorkflowHelper.GetOrAssignAgentColor(agentKey, state.AgentColorMap);

    if (agentKey != _lastOutputAgent)
    {
      _lastOutputAgent = agentKey;
      _coloredOutput.AppendLine();
      _coloredOutput.AppendLine($"[{color} bold]🤖 {Markup.Escape(agentKey)}:[/]");
    }

    var escaped = Markup.Escape(text);
    _coloredOutput.Append($"[{color}]{escaped.Replace("\n", $"[/]\n[{color}]")}[/]");
    _dirty = true;
  }

  public void FinalizeDashboard(DashboardState state, string status)
  {
    _cachedColoredText = _coloredOutput.ToString();
    _dirty = false;
    WorkflowConsoleRenderer.UpdateDashboardPanels(root, state, _cachedColoredText, status);
    ctx.Refresh();
  }

  public void TryRefresh(DashboardState state, string status)
  {
    if (_refreshThrottle.ElapsedMilliseconds < RefreshIntervalMs)
      return;

    if (_dirty)
    {
      _cachedColoredText = _coloredOutput.ToString();
      _dirty = false;
    }

    WorkflowConsoleRenderer.UpdateDashboardPanels(root, state, _cachedColoredText, status);

    int currentWidth = Console.WindowWidth;
    int currentHeight = Console.WindowHeight;
    if (currentWidth != _lastConsoleWidth || currentHeight != _lastConsoleHeight)
    {
      _lastConsoleWidth = currentWidth;
      _lastConsoleHeight = currentHeight;
      AnsiConsole.Clear();
    }

    ctx.Refresh();
    _refreshThrottle.Restart();
  }
}
