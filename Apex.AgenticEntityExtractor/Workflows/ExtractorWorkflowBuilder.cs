using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Aggregators;
using Apex.AgenticEntityExtractor.Enums;
using Apex.AgenticEntityExtractor.Executors;
using Apex.AgenticEntityExtractor.GroupChatManagers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Workflows;

/// <summary>
/// Builds extraction workflows using two different composition strategies:
///
/// <b>1. Pipeline from Concurrent Workflows</b> — high-level concurrent and group-chat
///    sub-workflows (built via <see cref="AgentWorkflowBuilder"/> helpers) wrapped as agents
///    via <see cref="Workflow.AsAIAgent"/> and composed sequentially.
/// <code>
///   [BuildConcurrent ──→ AsAIAgent] ──→ [BuildConcurrent ──→ AsAIAgent] ──→ [GroupChat ──→ AsAIAgent] ──→ Output
/// </code>
///
/// <b>2. Fully Custom Single Pipeline</b> — a single flat workflow graph where all stages
///    (fan-out/fan-in, star topology, and inter-stage handoff) are wired into one
///    <see cref="WorkflowBuilder"/> with no sub-workflows or <see cref="Workflow.AsAIAgent"/> wrapping.
/// <code>
///   [Entity Fan-Out/Fan-In] ──→ [Relationship Fan-Out/Fan-In] ──→ [Mermaid Star-Topology GroupChat] ──→ Output
/// </code>
/// </summary>
public class ExtractorWorkflowBuilder(IExtractorAgentsBuilder agentsBuilder) : IExtractorWorkflowBuilder
{
  // ════════════════════════════════════════════════════════════════════════
  //  STRATEGY 1 — HIGH-LEVEL WORKFLOWS (AgentWorkflowBuilder helpers)
  //
  //  Sub-workflows are built using high-level AgentWorkflowBuilder helpers;
  //  the framework handles all executor/edge wiring internally.
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// <b>Strategy 1:</b> Composes high-level concurrent and group-chat workflows as agents
  /// in a sequential pipeline. Each inner workflow is wrapped via <see cref="Workflow.AsAIAgent"/>
  /// so it behaves like a single agent from the outer pipeline's perspective.
  /// </summary>
  public Workflow BuildHighLevelPatterns(string workflowName)
  {
    Workflow entityExtractionWorkflow = BuildConcurrentEntityExtraction("ConcurrentEntityExtraction");
    Workflow relationshipExtractionWorkflow = BuildConcurrentRelationshipExtraction("ConcurrentRelationshipExtraction");
    Workflow mermaidDiagramWorkflow = BuildMermaidDiagramGroupChat("MermaidDiagramGroupChat");

    // Wrap each workflow as an AIAgent so the outer sequential pipeline can chain them
    return AgentWorkflowBuilder.BuildSequential(workflowName,
      [
        entityExtractionWorkflow.AsAIAgent("ConcurrentEntityExtraction"),
        relationshipExtractionWorkflow.AsAIAgent("ConcurrentRelationshipExtraction"),
        mermaidDiagramWorkflow.AsAIAgent("MermaidDiagramAsGroupChat")
      ]);
  }

  // ════════════════════════════════════════════════════════════════════════
  //  STRATEGY 2 — FULLY CUSTOM PIPELINE (manual executors + edges)
  //
  //  All stages live in a single flat WorkflowBuilder graph; every executor
  //  and edge is wired explicitly with no sub-workflows or AsAIAgent wrapping.
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// <b>Strategy 2:</b> Fully custom single pipeline — every executor and edge lives in one
  /// flat <see cref="WorkflowBuilder"/> graph. No sub-workflows, no <see cref="Workflow.AsAIAgent"/>,
  /// no <see cref="AgentWorkflowBuilder"/> helpers.
  /// <code>
  ///                        ┌──→ [Ent_1] ──→ [Batcher] ──┐
  ///   [Entity Fan-Out] ────┼──→ [Ent_2] ──→ [Batcher] ──┼──→ [Entity Aggregator]
  ///                        └──→ [Ent_3] ──→ [Batcher] ──┘            │
  ///                        ┌──→ [Rel_1] ──→ [Batcher] ──┐            │
  ///   [Rel Fan-Out] ◄──────┼──→ [Rel_2] ──→ [Batcher] ──┼──→ [Rel Aggregator]
  ///                        └──→ [Rel_3] ──→ [Batcher] ──┘            │
  ///                                                                   ▼
  ///                     [RefinementExecutor] ←──→ [Participant(Builder) / Participant(Reviewer)] ──→ Output
  /// </code>
  /// <b>Inter-stage handoff:</b> <see cref="AggregatorExecutor"/> sends merged messages
  /// and a <see cref="TurnToken"/> to the next stage's fan-out (or refinement orchestrator) instead of
  /// yielding workflow output, keeping all stages inside the same graph.
  /// </summary>
  public Workflow BuildLowLevelFullCustomWorkflow(string workflowName)
  {
    // ── Create entity agents ──────────────────────────────────────────────
    AIAgent entityAgent1 = agentsBuilder.BuildEntitiesAgent("1", ChatProvider.Smaller_OpenAI);
    AIAgent entityAgent2 = agentsBuilder.BuildEntitiesAgent("2", ChatProvider.Smaller_OpenAI);
    AIAgent entityAgent3 = agentsBuilder.BuildEntitiesAgent("3", ChatProvider.Smaller_OpenAI);

    // ── Create relationship agents ───────────────────────────────────────
    AIAgent relationshipAgent1 = agentsBuilder.BuildRelationshipsAgent("1", ChatProvider.Smaller_OpenAI);
    AIAgent relationshipAgent2 = agentsBuilder.BuildRelationshipsAgent("2", ChatProvider.Smaller_OpenAI);
    AIAgent relationshipAgent3 = agentsBuilder.BuildRelationshipsAgent("3", ChatProvider.Smaller_OpenAI);

    // ── Create diagram builder/reviewer agents ─────────────────────────
    AIAgent diagramBuilderAgent = agentsBuilder.BuildMermaidDiagramAgent(ChatProvider.OpenAI);
    AIAgent diagramReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent(ChatProvider.OpenAI);

    // ── Stage 1: Entity extraction (fan-out / fan-in) ─────────────────
    FanOutExecutor entityFanOut = new("EntityFanOut");
    AggregatorExecutor entityAggregator = new("EntityAggregator", 3, Aggregator.AggregateEntities);

    // ── Stage 2: Relationship extraction (fan-out / fan-in) ───────────
    FanOutExecutor relationshipFanOut = new("RelationshipFanOut");
    AggregatorExecutor relationshipAggregator = new("RelationshipAggregator", 3, Aggregator.AggregateRelationships);

    // ── Stage 3: Mermaid diagram refinement (star-topology group chat) ──────
    ParticipantExecutor builderParticipant = new(diagramBuilderAgent, includeInputInOutput: false);
    ParticipantExecutor reviewerParticipant = new(diagramReviewerAgent, includeInputInOutput: false);

    // Round-robin manager: alternates between builder and reviewer, terminates on APPROVED or max turns
    ApprovalManager approvalManager = new([diagramBuilderAgent, diagramReviewerAgent], terminationFunction: ApprovalManager.ApprovedTermination())
    {
      MaximumIterationCount = 20
    };
    RefinementExecutor mermaidRefiner = new("MermaidRefiner", diagramBuilderAgent, builderParticipant, reviewerParticipant, approvalManager);

    // ── Wire the single flat graph (all stages share one WorkflowBuilder) ───
    WorkflowBuilder workflowBuilder = new(entityFanOut);

    // Stage 1 edges: fan-out → entity agents → batchers → fan-in barrier → forwarding aggregator
    workflowBuilder.AddFanOutEdge(entityFanOut, [entityAgent1, entityAgent2, entityAgent3], "EntFanOutEdge");
    BatcherExecutor entityBatcher1 = new($"Batch/{entityAgent1.Name}");
    BatcherExecutor entityBatcher2 = new($"Batch/{entityAgent2.Name}");
    BatcherExecutor entityBatcher3 = new($"Batch/{entityAgent3.Name}");
    workflowBuilder.AddEdge(entityAgent1, entityBatcher1);
    workflowBuilder.AddEdge(entityAgent2, entityBatcher2);
    workflowBuilder.AddEdge(entityAgent3, entityBatcher3);
    workflowBuilder.AddFanInBarrierEdge([entityBatcher1, entityBatcher2, entityBatcher3], entityAggregator, "EntFanInBarrierEdge");

    // Inter-stage handoff: entity aggregator forwards merged entities to relationship fan-out
    workflowBuilder.AddEdge(entityAggregator, relationshipFanOut, "EntHandoffEdge");

    // Stage 2 edges: fan-out → relationship agents → batchers → fan-in barrier → forwarding aggregator
    workflowBuilder.AddFanOutEdge(relationshipFanOut, [relationshipAgent1, relationshipAgent2, relationshipAgent3], "RelFanOutEdge");
    BatcherExecutor relationshipBatcher1 = new($"Batch/{relationshipAgent1.Name}");
    BatcherExecutor relationshipBatcher2 = new($"Batch/{relationshipAgent2.Name}");
    BatcherExecutor relationshipBatcher3 = new($"Batch/{relationshipAgent3.Name}");
    workflowBuilder.AddEdge(relationshipAgent1, relationshipBatcher1);
    workflowBuilder.AddEdge(relationshipAgent2, relationshipBatcher2);
    workflowBuilder.AddEdge(relationshipAgent3, relationshipBatcher3);
    workflowBuilder.AddFanInBarrierEdge([relationshipBatcher1, relationshipBatcher2, relationshipBatcher3], relationshipAggregator, "RelFanInBarrierEdge");

    // Inter-stage handoff: relationship aggregator forwards merged data to group chat orchestrator
    workflowBuilder.AddEdge(relationshipAggregator, mermaidRefiner, "RelHandoffEdge");

    // Stage 3 edges: star topology — bidirectional edges between orchestrator and each participant
    workflowBuilder.AddEdge(mermaidRefiner, builderParticipant, "Refine2Build");
    workflowBuilder.AddEdge(builderParticipant, mermaidRefiner, "Build2Refine");
    workflowBuilder.AddEdge(mermaidRefiner, reviewerParticipant, "Refine2Review");
    workflowBuilder.AddEdge(reviewerParticipant, mermaidRefiner, "Review2Refine");

    // Output: the refinement orchestrator yields the final Mermaid diagram
    workflowBuilder.WithOutputFrom(mermaidRefiner);
    workflowBuilder.WithName(workflowName);

    return workflowBuilder.Build();
  }

  /// <summary>
  /// Builds a concurrent entity extraction workflow using <see cref="AgentWorkflowBuilder.BuildConcurrent"/>.
  /// The framework internally creates the fan-out/fan-in topology.
  /// </summary>
  private Workflow BuildConcurrentEntityExtraction(string workflowName)
  {
    AIAgent entityAgent1 = agentsBuilder.BuildEntitiesAgent("1", ChatProvider.Smaller_OpenAI);
    AIAgent entityAgent2 = agentsBuilder.BuildEntitiesAgent("2", ChatProvider.Smaller_OpenAI);
    AIAgent entityAgent3 = agentsBuilder.BuildEntitiesAgent("3", ChatProvider.Smaller_OpenAI);

    return AgentWorkflowBuilder.BuildConcurrent(workflowName, [entityAgent1, entityAgent2, entityAgent3], Aggregator.AggregateEntities);
  }

  /// <summary>
  /// Builds a concurrent relationship extraction workflow using <see cref="AgentWorkflowBuilder.BuildConcurrent"/>.
  /// The framework internally creates the fan-out/fan-in topology.
  /// </summary>
  private Workflow BuildConcurrentRelationshipExtraction(string workflowName)
  {
    AIAgent relationshipAgent1 = agentsBuilder.BuildRelationshipsAgent("1", ChatProvider.Smaller_OpenAI);
    AIAgent relationshipAgent2 = agentsBuilder.BuildRelationshipsAgent("2", ChatProvider.Smaller_OpenAI);
    AIAgent relationshipAgent3 = agentsBuilder.BuildRelationshipsAgent("3", ChatProvider.Smaller_OpenAI);

    return AgentWorkflowBuilder.BuildConcurrent(workflowName, [relationshipAgent1, relationshipAgent2, relationshipAgent3], Aggregator.AggregateRelationships);
  }

  /// <summary>
  /// Builds a Mermaid diagram group chat workflow using <see cref="AgentWorkflowBuilder.CreateGroupChatBuilderWith"/>.
  /// The framework internally creates the star topology with round-robin speaker selection.
  /// </summary>
  private Workflow BuildMermaidDiagramGroupChat(/*Bug? CreateGroupChatBuilderWith does not use a workflow name*/string workflowName)
  {
    AIAgent diagramBuilderAgent = agentsBuilder.BuildMermaidDiagramAgent(ChatProvider.OpenAI);
    AIAgent diagramReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent(ChatProvider.OpenAI);

    return AgentWorkflowBuilder
      .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents, ApprovalManager.ApprovedTermination())
      {
        MaximumIterationCount = 20
      })
      .AddParticipants(diagramBuilderAgent, diagramReviewerAgent)
      .Build();
  }
}
