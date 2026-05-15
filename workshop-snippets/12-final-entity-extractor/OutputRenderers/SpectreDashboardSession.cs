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
  private const int RefreshIntervalMs = 500;
  private const int MaxOutputChars = 32_000;

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
    AppendColoredMarkup(_coloredOutput, escaped, color);

    // Sliding window: trim the front when the buffer grows too large to avoid
    // unbounded StringBuilder growth that pressures LOH and gen2 GC.
    if (_coloredOutput.Length > MaxOutputChars)
    {
      int trimTo = _coloredOutput.Length - MaxOutputChars;
      // Find the first newline after the trim point to keep markup tags intact
      int newlineIdx = -1;
      for (int i = trimTo; i < _coloredOutput.Length; i++)
      {
        if (_coloredOutput[i] == '\n') { newlineIdx = i; break; }
      }
      int removeCount = newlineIdx >= 0 ? newlineIdx + 1 : trimTo;
      _coloredOutput.Remove(0, removeCount);
    }

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

  /// <summary>
  /// Appends Spectre markup–colored text to <paramref name="sb"/>, splitting on newlines
  /// without allocating intermediate strings from <see cref="string.Replace"/>.
  /// </summary>
  internal static void AppendColoredMarkup(StringBuilder sb, string escaped, string color)
  {
    ReadOnlySpan<char> span = escaped.AsSpan();
    bool first = true;

    while (!span.IsEmpty)
    {
      int nlIdx = span.IndexOf('\n');

      if (!first)
      {
        sb.Append("[/]\n[");
        sb.Append(color);
        sb.Append(']');
      }
      else
      {
        sb.Append('[');
        sb.Append(color);
        sb.Append(']');
        first = false;
      }

      if (nlIdx < 0)
      {
        sb.Append(span);
        break;
      }

      sb.Append(span[..nlIdx]);
      span = span[(nlIdx + 1)..];
    }

    sb.Append("[/]");
  }
}
