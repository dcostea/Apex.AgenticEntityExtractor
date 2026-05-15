using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Bundles all mutable state tracked during a workflow dashboard rendering session.
/// Extracted from <see cref="WorkflowHelper.RenderWorkflowExecutionEventsAsync"/> to
/// eliminate 27 loose local variables and reduce method parameter counts
/// (e.g. <c>UpdateDashboardPanels</c> from 15 params to 3).
/// </summary>
public sealed class DashboardState
{
  // ── Event tracking ──
  public List<string> EventLines { get; } = [];
  public List<string> FullEventLog { get; } = [];
  public List<string> ToolLines { get; } = [];
  private HashSet<string> ToolDedup { get; } = [];
  public List<string> ReviewLines { get; } = [];

  // ── Dirty flags (per dashboard section) ──
  internal bool EventsDirty { get; set; }
  internal bool ToolsDirty { get; set; }
  internal bool MetricsDirty { get; set; }
  internal bool ReviewDirty { get; set; }

  private const int MaxEventLines = 500;

  // ── Streaming ──
  public int TokenCount { get; set; }
  public int SuperStep { get; set; }
  public string? ActiveAgent { get; set; }
  public string? YieldedOutputText { get; set; }

  // ── Agent tracking ──
  public Dictionary<string, int> AgentTokenCounts { get; } = [];
  public Dictionary<string, string> AgentColorMap { get; } = [];
  public Dictionary<string, StringBuilder> PerAgentOutput { get; } = [];
  public Dictionary<string, int> PerAgentResponseCounts { get; } = [];
  public HashSet<string> PendingNextResponseHeader { get; } = [];
  public HashSet<string> UniqueAgents { get; } = [];

  // ── Executor tracking ──
  public Dictionary<string, int> ExecutorInvocationCounts { get; } = [];
  public Dictionary<string, int> PendingTurnTokenCounts { get; } = [];

  // ── Timing ──
  public Stopwatch Elapsed { get; } = new();
  private Dictionary<string, Stopwatch> ExecutorTimings { get; } = [];
  public Dictionary<string, TimeSpan> ExecutorDurations { get; } = [];
  public Dictionary<int, Stopwatch> SuperStepTimings { get; } = [];
  public Dictionary<int, TimeSpan> SuperStepDurations { get; } = [];

  // ════════════════════════════════════════════════════════════════════════
  //  EVENT LOGGING
  // ════════════════════════════════════════════════════════════════════════

  public void LogEvent(string text)
  {
    EventLines.Add(text);
    FullEventLog.Add(text);
    EventsDirty = true;

    // Cap the live display list; FullEventLog keeps the complete history for post-run output
    if (EventLines.Count > MaxEventLines)
      EventLines.RemoveRange(0, EventLines.Count - MaxEventLines);
  }

  // ════════════════════════════════════════════════════════════════════════
  //  TIMING
  // ════════════════════════════════════════════════════════════════════════

  public void StartTiming(string executorId)
  {
    ExecutorTimings[executorId] = Stopwatch.StartNew();
  }

  public void StopTiming(string executorId)
  {
    if (!ExecutorTimings.Remove(executorId, out var sw))
      return;

    sw.Stop();
    ExecutorDurations[executorId] = ExecutorDurations.TryGetValue(executorId, out var existing)
      ? existing + sw.Elapsed
      : sw.Elapsed;
  }

  public void FlushInProgressTimings()
  {
    foreach (var (id, sw) in ExecutorTimings)
    {
      if (!sw.IsRunning) continue;
      sw.Stop();
      ExecutorDurations[id] = ExecutorDurations.TryGetValue(id, out var existing)
        ? existing + sw.Elapsed
        : sw.Elapsed;
    }
    ExecutorTimings.Clear();
  }

  public void FlushInProgressSuperStepTimings()
  {
    foreach (var (step, sw) in SuperStepTimings)
    {
      if (!sw.IsRunning) continue;
      sw.Stop();
      SuperStepDurations[step] = sw.Elapsed;
    }
    SuperStepTimings.Clear();
  }

  public void FlushPendingTurnTokenCounts()
  {
    if (PendingTurnTokenCounts.Count == 0)
      return;

    foreach (var (executorId, count) in PendingTurnTokenCounts)
    {
      FullEventLog.Add($"🔄 {executorId}: {count}× TurnToken");
    }

    PendingTurnTokenCounts.Clear();
  }

  // ════════════════════════════════════════════════════════════════════════
  //  EVENT DRAINING
  // ════════════════════════════════════════════════════════════════════════

  public void DrainExternalToolEvents()
  {
    while (WorkflowHelper.ExternalToolEvents.TryDequeue(out var line))
    {
      if (ToolDedup.Add(line))
      {
        ToolLines.Add(line);
        ToolsDirty = true;
      }
    }
  }

  public void DrainReviewStatusEvents()
  {
    while (WorkflowHelper.ReviewStatusEvents.TryDequeue(out var line))
    {
      ReviewLines.Add(line);
      ReviewDirty = true;
    }
  }

  // ════════════════════════════════════════════════════════════════════════
  //  TOOL EXTRACTION
  // ════════════════════════════════════════════════════════════════════════

  public void TryExtractTools(object? data)
  {
    if (data is null)
      return;

    if (data is IEnumerable<ChatMessage> messages)
    {
      ExtractToolsFromMessages(messages);
      return;
    }

    if (data is AgentResponse agentResponse)
    {
      ExtractToolsFromMessages(agentResponse.Messages);
      return;
    }

    var messagesProperty = data.GetType().GetProperty("Messages");
    if (messagesProperty?.GetValue(data) is IEnumerable<ChatMessage> propertyMessages)
    {
      ExtractToolsFromMessages(propertyMessages);
    }
  }

  private void ExtractToolsFromMessages(IEnumerable<ChatMessage>? messages)
  {
    if (messages is null)
      return;

    foreach (var message in messages)
    {
      foreach (var content in message.Contents)
      {
        if (content is FunctionCallContent call)
        {
          // Skip raw tool-call events for functions that already have formatted
          // events from the tool response middleware (e.g. [CACHE MISS], [ONTOLOGY]).
          if (ToolLines.Any(l => l.Contains(call.Name, StringComparison.Ordinal)))
            continue;

          var args = call.Arguments is null ? string.Empty : JsonSerializer.Serialize(call.Arguments);
          var line = $"CALL [{call.CallId}] {call.Name}({args})";
          if (ToolDedup.Add(line))
            ToolLines.Add(line);
        }
        else if (content is FunctionResultContent result)
        {
          if (ToolLines.Any(l => l.Contains(result.CallId ?? "", StringComparison.Ordinal)))
            continue;

          var line = $"RESP [{result.CallId}] {result.Result}";
          if (ToolDedup.Add(line))
            ToolLines.Add(line);
        }
      }
    }
  }
}
