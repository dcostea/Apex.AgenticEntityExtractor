namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Represents an active live dashboard rendering session. The workflow event loop
/// calls these methods to update the display as events stream in. Each UI backend
/// (Spectre.Console, System.Console, web, etc.) provides its own implementation.
/// </summary>
public interface IDashboardSession
{
  /// <summary>
  /// Appends a streaming agent token to the presentation buffer.
  /// The session manages its own formatted output (e.g., colored markup).
  /// </summary>
  void AppendAgentToken(string agentKey, string? text, DashboardState state);

  /// <summary>
  /// Forces a full dashboard update and refresh (used at workflow end).
  /// </summary>
  void FinalizeDashboard(DashboardState state, string status);

  /// <summary>
  /// Conditionally refreshes the dashboard (respects implementation-specific throttling).
  /// </summary>
  void TryRefresh(DashboardState state, string status);
}
