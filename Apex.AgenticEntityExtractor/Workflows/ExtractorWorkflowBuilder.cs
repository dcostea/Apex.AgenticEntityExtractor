using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Aggregators;
using Apex.AgenticEntityExtractor.Enums;
using Apex.AgenticEntityExtractor.Executors;
using Apex.AgenticEntityExtractor.GroupChatManagers;
using Apex.AgenticEntityExtractor.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Workflows;

/// <summary>
/// Builds extraction workflows using two different composition strategies:
///
/// <b>1. Pipeline from Concurrent Workflows</b> — a single entity agent followed by high-level
///    concurrent and group-chat sub-workflows (built via <see cref="AgentWorkflowBuilder"/> helpers)
///    wrapped as agents via <see cref="Workflow.AsAIAgent"/> and composed sequentially.
/// <code>
///   [EntityAgent] ──→ [BuildConcurrent ──→ AsAIAgent] ──→ [GroupChat ──→ AsAIAgent] ──→ Output
/// </code>
///
/// <b>2. Fully Custom Single Pipeline</b> — a single flat workflow graph where all stages
///    (single entity agent, relationship fan-out/fan-in, star topology, and inter-stage handoff)
///    are wired into one <see cref="WorkflowBuilder"/> with no sub-workflows or <see cref="Workflow.AsAIAgent"/> wrapping.
/// <code>
///   [Entity Agent] ──→ [Relationship Fan-Out/Fan-In] ──→ [Mermaid Star-Topology GroupChat] ──→ Output
/// </code>
/// </summary>
public class ExtractorWorkflowBuilder(IExtractorAgentsBuilder agentsBuilder, IConfiguration configuration) : IExtractorWorkflowBuilder
{
  // ════════════════════════════════════════════════════════════════════════
  //  STRATEGY 1 — ORCHESTRATION PATTERN (HIGH-LEVEL orchestration patterns)
  //
  //  Sub-workflows are built using high-level AgentWorkflowBuilder helpers;
  //  the framework handles all executor/edge wiring internally.
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// <b>Strategy 1:</b> Composes a single entity agent, a concurrent relationship workflow, and a
  /// group-chat workflow as agents in a sequential workflow. Inner workflows are wrapped via
  /// <see cref="Workflow.AsAIAgent"/> so they behave like single agents from the outer workflow's perspective.
  /// </summary>
  public Workflow BuildHighLevelPatterns(string workflowName)
  {
    AIAgent entityAgent = agentsBuilder.BuildEntitiesAgent(provider: configuration.GetValue<ChatProvider>("Provider"));
    Workflow relationshipExtractionWorkflow = BuildConcurrentRelationshipExtraction("ConcurrentRelationshipExtraction");
    Workflow mermaidDiagramWorkflow = BuildMermaidDiagramGroupChat("MermaidDiagramGroupChat");

    // Chain the single entity agent with the wrapped workflows
    return AgentWorkflowBuilder.BuildSequential(workflowName,
      [
        entityAgent,
        relationshipExtractionWorkflow.AsAIAgent("ConcurrentRelationshipExtraction"),
        mermaidDiagramWorkflow.AsAIAgent("MermaidDiagramAsGroupChat")
      ]);
  }

  // ════════════════════════════════════════════════════════════════════════
  //  STRATEGY 2 — FULLY CUSTOM WORKFLOW (LOW-LEVEL: manual executors + edges)
  //
  //  All stages live in a single flat WorkflowBuilder graph; every executor
  //  and edge is wired explicitly with no sub-workflows or AsAIAgent wrapping.
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// <b>Strategy 2:</b> Fully custom single workflow — every executor and edge lives in one
  /// flat <see cref="WorkflowBuilder"/> graph. No sub-workflows, no <see cref="Workflow.AsAIAgent"/>,
  /// no <see cref="AgentWorkflowBuilder"/> helpers.
  /// <code>
  ///                              ┌──→ [Rel_1] ──→ [Batcher] ──┐
  ///   [Entity Agent] ──→ [Rel Fan-Out] ──┤                         ├──→ [Rel Aggregator]
  ///                              └──→ [Rel_2] ──→ [Batcher] ──┘
  ///                                                                │
  ///                                                                ▼
  ///                     [RefinementExecutor] ←──→ [Participant(Builder) / Participant(Reviewer)] ──→ Output
  /// </code>
  /// <b>Inter-stage handoff:</b> <see cref="AggregatorExecutor"/> merges agent outputs into a
  /// typed <see cref="ExtractionContext"/> and sends it with a <see cref="TurnToken"/> to the
  /// next stage, keeping all stages inside the same graph.
  /// </summary>
  public Workflow BuildLowLevelFullCustomWorkflow(string workflowName)
  {
    AIAgent entityAgent1 = agentsBuilder.BuildEntitiesAgent("1", configuration.GetValue<ChatProvider>("Provider"));

    AIAgent relationshipAgent1 = agentsBuilder.BuildRelationshipsAgent("1", configuration.GetValue<ChatProvider>("Provider"));
    AIAgent relationshipAgent2 = agentsBuilder.BuildRelationshipsAgent("2", configuration.GetValue<ChatProvider>("Provider"));

    AIAgent diagramBuilderAgent = agentsBuilder.BuildMermaidBuilderAgent(configuration.GetValue<ChatProvider>("Provider"));
    AIAgent diagramReviewerAgent = agentsBuilder.BuildMermaidRefinerAgent(configuration.GetValue<ChatProvider>("Provider"));

    // ── Stage 2: Relationship extraction (fan-out / fan-in) 
    NormalizeEntitiesExecutor entityNormalizer = new("EntityNormalizer");
    FanOutExecutor relationshipFanOut = new("RelationshipFanOut");
    AggregatorExecutor relationshipAggregator = new("RelationshipAggregator", 2, Aggregator.AggregateRelationships);

    // ── Stage 3: Mermaid diagram refinement (star-topology group chat) 
    ParticipantExecutor builderParticipant = new(diagramBuilderAgent, includeInputInOutput: false);
    ParticipantExecutor reviewerParticipant = new(diagramReviewerAgent, includeInputInOutput: false);

    // Round-robin manager: alternates between builder and reviewer, terminates on APPROVED or max turns
    ApprovalManager approvalManager = new([diagramBuilderAgent, diagramReviewerAgent], terminationFunction: ApprovalManager.ApprovedTermination())
    {
      MaximumIterationCount = 10
    };
    RefinementExecutor mermaidRefiner = new("MermaidRefiner", diagramBuilderAgent, builderParticipant, reviewerParticipant, approvalManager);

    // ── Wire the single flat graph (all stages share one WorkflowBuilder) 
    WorkflowBuilder workflowBuilder = new(entityAgent1);

    // Stage 1 → Stage 2 handoff: entity agent → normalizer (dedup + re-ID) → relationship fan-out
    workflowBuilder.AddEdge(entityAgent1, entityNormalizer, "EntNormEdge");
    workflowBuilder.AddEdge(entityNormalizer, relationshipFanOut, "NormFanOutEdge");

    // Stage 2 edges: fan-out → relationship agents → batchers → fan-in barrier → forwarding aggregator
    workflowBuilder.AddFanOutEdge(relationshipFanOut, [relationshipAgent1, relationshipAgent2], "RelFanOutEdge");
    FunctionExecutor<List<ChatMessage>> relationshipBatcher1 = new(
      $"Batch/{relationshipAgent1.Name}",
      async (messages, ctx, ct) => await ctx.SendMessageAsync(messages, cancellationToken: ct),
      sentMessageTypes: [typeof(List<ChatMessage>)],
      declareCrossRunShareable: true);
    FunctionExecutor<List<ChatMessage>> relationshipBatcher2 = new(
      $"Batch/{relationshipAgent2.Name}",
      async (messages, ctx, ct) => await ctx.SendMessageAsync(messages, cancellationToken: ct),
      sentMessageTypes: [typeof(List<ChatMessage>)],
      declareCrossRunShareable: true);
    workflowBuilder.AddEdge(relationshipAgent1, relationshipBatcher1);
    workflowBuilder.AddEdge(relationshipAgent2, relationshipBatcher2);
    workflowBuilder.AddFanInBarrierEdge([relationshipBatcher1, relationshipBatcher2], relationshipAggregator, "RelFanInBarrierEdge");

    // Inter-stage handoff: relationship aggregator forwards merged data to group chat orchestrator
    workflowBuilder.AddEdge(relationshipAggregator, mermaidRefiner, "RelStageEdge");

    // Stage 3 edges: star topology — bidirectional edges between orchestrator and each participant
    workflowBuilder.AddEdge(mermaidRefiner, builderParticipant, "Refine2Build");
    workflowBuilder.AddEdge(builderParticipant, mermaidRefiner, "Build2Refine");
    workflowBuilder.AddEdge(mermaidRefiner, reviewerParticipant, "Refine2Review");
    workflowBuilder.AddEdge(reviewerParticipant, mermaidRefiner, "Review2Refine");

    // Output: the refinement orchestrator yields the final Mermaid diagram
    workflowBuilder.WithOutputFrom(mermaidRefiner);
    workflowBuilder.WithName(workflowName);
    workflowBuilder.WithOpenTelemetry();

    return workflowBuilder.Build();
  }

  /// <summary>
  /// Builds a concurrent relationship extraction workflow using <see cref="AgentWorkflowBuilder.BuildConcurrent"/>.
  /// The framework internally creates the fan-out/fan-in topology.
  /// </summary>
  private Workflow BuildConcurrentRelationshipExtraction(string workflowName)
  {
    AIAgent relationshipAgent1 = agentsBuilder.BuildRelationshipsAgent("1", configuration.GetValue<ChatProvider>("Provider"));
    AIAgent relationshipAgent2 = agentsBuilder.BuildRelationshipsAgent("2", configuration.GetValue<ChatProvider>("Provider"));

    return AgentWorkflowBuilder.BuildConcurrent(workflowName, [relationshipAgent1, relationshipAgent2],
      results => Aggregator.ToMessages(Aggregator.AggregateRelationships(results)));
  }

  /// <summary>
  /// Builds a Mermaid diagram group chat workflow using <see cref="AgentWorkflowBuilder.CreateGroupChatBuilderWith"/>.
  /// The framework internally creates the star topology with round-robin speaker selection.
  /// </summary>
  private Workflow BuildMermaidDiagramGroupChat(string workflowName)
  {
    AIAgent diagramBuilderAgent = agentsBuilder.BuildMermaidBuilderAgent(configuration.GetValue<ChatProvider>("Provider"));
    AIAgent diagramReviewerAgent = agentsBuilder.BuildMermaidRefinerAgent(configuration.GetValue<ChatProvider>("Provider"));

    return AgentWorkflowBuilder
      .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents, ApprovalManager.ApprovedTermination())
      {
        MaximumIterationCount = 10
      })
      .AddParticipants(diagramBuilderAgent, diagramReviewerAgent)
      .WithName(workflowName)
      .Build();
  }
}
