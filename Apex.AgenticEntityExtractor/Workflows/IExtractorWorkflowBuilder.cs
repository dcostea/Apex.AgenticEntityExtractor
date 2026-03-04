using Microsoft.Agents.AI.Workflows;

namespace Apex.AgenticEntityExtractor.Workflows;

public interface IExtractorWorkflowBuilder
{
  /// <summary>
  /// <b>Strategy 1:</b> Builds a simple sequential pipeline where each extractor agent runs
  /// one after another, with full conversation history flowing through.
  /// </summary>
  Workflow BuildSequentialPipeline(string workflowName);

  /// <summary>
  /// <b>Strategy 2:</b> Builds a pipeline where high-level concurrent and group-chat
  /// sub-workflows (built via <see cref="AgentWorkflowBuilder"/>) are wrapped as agents
  /// via <see cref="Workflow.AsAIAgent"/> and composed sequentially.
  /// </summary>
  Workflow BuildPipelineFromConcurrentWorkflows(string workflowName);

  /// <summary>
  /// <b>Strategy 3:</b> Builds a pipeline where custom low-level sub-workflows (manually
  /// wired with <see cref="WorkflowBuilder"/>, custom executors, and explicit edges) are
  /// wrapped as agents and composed sequentially.
  /// </summary>
  Workflow BuildPipelineFromCustomOrchestrations(string workflowName);

  /// <summary>
  /// <b>Strategy 4:</b> Builds a fully custom single-graph pipeline where all stages
  /// (entity fan-out/fan-in, relationship fan-out/fan-in, and Mermaid star-topology
  /// group chat) are wired into one flat <see cref="WorkflowBuilder"/> graph with no
  /// sub-workflows.
  /// </summary>
  Workflow BuildFullyCustomOrchestratedPipeline(string workflowName);

  /// <summary>
  /// Builds the entity extraction stage as a standalone concurrent fan-out/fan-in workflow
  /// using manual executor and edge wiring.
  /// </summary>
  Workflow BuildEntityExtractionAsConcurrent(string workflowName);

  /// <summary>
  /// Builds the relationship extraction stage as a standalone concurrent fan-out/fan-in
  /// workflow using manual executor and edge wiring.
  /// </summary>
  Workflow BuildRelationshipExtractionAsConcurrent(string workflowName);

  /// <summary>
  /// Builds the Mermaid diagram refinement stage as a standalone group-chat workflow
  /// using manual star-topology wiring with a <see cref="RefinementExecutor"/> hub.
  /// </summary>
  Workflow BuildMermaidDiagramAsGroupChat(string workflowName);
}
