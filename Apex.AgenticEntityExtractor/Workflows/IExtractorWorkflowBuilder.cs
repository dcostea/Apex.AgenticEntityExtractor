using Microsoft.Agents.AI.Workflows;

namespace Apex.AgenticEntityExtractor.Workflows;

public interface IExtractorWorkflowBuilder
{
  /// <summary>
  /// <b>Strategy 1:</b> Builds a sequential pipeline where each stage is a concurrent
  /// sub-workflow (built via <see cref="AgentWorkflowBuilder.BuildConcurrent"/> or
  /// <see cref="AgentWorkflowBuilder.CreateGroupChatBuilderWith"/>) wrapped as an agent
  /// via <see cref="Workflow.AsAIAgent"/> and composed sequentially.
  /// </summary>
  Workflow BuildHighLevelPatterns(string workflowName);

  /// <summary>
  /// <b>Strategy 2:</b> Builds a fully custom single-graph pipeline where all stages
  /// (entity fan-out/fan-in, relationship fan-out/fan-in, and Mermaid star-topology
  /// group chat) are wired into one flat <see cref="WorkflowBuilder"/> graph with no
  /// sub-workflows.
  /// </summary>
  Workflow BuildLowLevelFullCustomWorkflow(string workflowName);
}
