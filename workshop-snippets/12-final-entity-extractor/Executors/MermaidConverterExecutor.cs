using Apex.AgenticEntityExtractor.Models;
using Apex.AgenticEntityExtractor.OutputRenderers;
using Microsoft.Agents.AI.Workflows;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// <b>Deterministic Code Executor</b> — converts the aggregated <see cref="ExtractionContext"/>
/// into a Mermaid JS knowledge graph using pure C# (no LLM call).
///
/// Demonstrates that workflow graphs can mix <b>non-deterministic agent nodes</b> (LLM-powered)
/// with <b>deterministic code nodes</b> (conventional functions) in the same topology.
///
/// <code>
///                              ┌──→ [DebateOrchestrator] (LLM — non-deterministic)
///   [RelationshipAggregator] ──┤
///                              └──→ [MermaidConverterExecutor] (code — deterministic)
/// </code>
///
/// The Mermaid output is written to <c>Data/Output/knowledge-graph.mmd</c> and also enqueued
/// as a tool event so it appears in the dashboard's tool panel during execution.
/// Students can open the file or copy the content and paste it into mermaid.live.
/// </summary>
[YieldsOutput(typeof(string))]
public partial class MermaidConverterExecutor(string executorId)
  : Executor(executorId, declareCrossRunShareable: true)
{
  private static readonly string OutputPath = Path.Combine("Data", "Output", "knowledge-graph.mmd");

  /// <summary>
  /// Receives the aggregated extraction result, converts entities + relationships to
  /// Mermaid JS format, writes it to a file, and yields the Mermaid string as output.
  /// </summary>
  [MessageHandler]
  private async ValueTask HandleExtractionContextAsync(ExtractionContext extractionContext, IWorkflowContext context, CancellationToken cancellationToken)
  {
    string mermaid = MermaidConverter.Convert(extractionContext);

    Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
    await File.WriteAllTextAsync(OutputPath, mermaid, cancellationToken);
    WorkflowHelper.EnqueueExternalToolEvent($"[Mermaid] Knowledge graph written to {OutputPath}");

    try
    {
      await context.YieldOutputAsync(mermaid, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      // Run disposed after consuming the output event — yield already delivered.
    }
  }
}
