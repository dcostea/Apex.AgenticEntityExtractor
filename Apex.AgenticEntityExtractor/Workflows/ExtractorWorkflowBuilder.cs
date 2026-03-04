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
/// Builds extraction workflows using three different composition strategies:
///
/// <b>1. Sequential Pipeline</b> — agents chained in sequence via <see cref="AgentWorkflowBuilder.BuildSequential"/>.
/// <code>
///   [Entity Agent] ──→ [Relationship Agent] ──→ [Mermaid Agent] ──→ Output
/// </code>
///
/// <b>2. Pipeline from Concurrent Workflows</b> — high-level concurrent and group chat workflows
///    wrapped as agents via <see cref="Workflow.AsAIAgent"/> and chained sequentially.
/// <code>
///   [BuildConcurrent ──→ AsAIAgent] ──→ [BuildConcurrent ──→ AsAIAgent] ──→ [GroupChat ──→ AsAIAgent] ──→ Output
/// </code>
///
/// <b>3. Pipeline from Custom Orchestrations</b> — same patterns as #2 but manually wired
///    using <see cref="WorkflowBuilder"/>, custom executors, and explicit edges.
///    This approach gives full control over the execution graph topology.
/// <code>
///   [Fan-Out/Fan-In Orchestration ──→ AsAIAgent] ──→ [...] ──→ [Star-Topology Orchestration ──→ AsAIAgent] ──→ Output
/// </code>
///
/// <b>4. Fully Custom Single Pipeline</b> — a single flat workflow graph where all stages
///    (fan-out/fan-in, star topology, and inter-stage handoff) are wired into one
///    <see cref="WorkflowBuilder"/> with no sub-workflows or <see cref="Workflow.AsAIAgent"/> wrapping.
/// <code>
///   [Entity Fan-Out/Fan-In] ──→ [Relationship Fan-Out/Fan-In] ──→ [Mermaid Star-Topology GroupChat] ──→ Output
/// </code>
/// </summary>
public class ExtractorWorkflowBuilder(IExtractorAgentsBuilder agentsBuilder) : IExtractorWorkflowBuilder
{
  // ════════════════════════════════════════════════════════════════════════
  //  TOP-LEVEL PIPELINES (all three produce equivalent extraction results)
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// <b>Strategy 1:</b> Simple sequential pipeline — each agent runs one after another.
  /// <code>
  ///   [Entity Agent] ──→ [Relationship Agent] ──→ [Mermaid Diagram Agent] ──→ Output
  /// </code>
  /// Each agent receives the full conversation history from all previous agents.
  /// </summary>
  public Workflow BuildSequentialPipeline(string workflowName)
  {
    AIAgent entityAgent = agentsBuilder.BuildEntitiesAgent();
    AIAgent relationshipAgent = agentsBuilder.BuildRelationshipsAgent();
    AIAgent mermaidDiagramAgent = agentsBuilder.BuildMermaidDiagramAgent();

    Workflow workflow = AgentWorkflowBuilder.BuildSequential(
      workflowName,
      [
        entityAgent, 
        relationshipAgent, 
        mermaidDiagramAgent
      ]
    );

    return workflow;
  }

  /// <summary>
  /// <b>Strategy 2:</b> Composes high-level workflows (built with <see cref="AgentWorkflowBuilder"/>)
  /// as agents in a sequential pipeline. Each inner workflow is wrapped via <see cref="Workflow.AsAIAgent"/>
  /// so it behaves like a single agent from the outer pipeline's perspective.
  /// </summary>
  public Workflow BuildPipelineFromConcurrentWorkflows(string workflowName)
  {
    // Each of these uses AgentWorkflowBuilder's high-level helpers internally
    Workflow entityExtractionWorkflow = BuildConcurrentEntityExtraction("ConcurrentEntityExtraction");
    Workflow relationshipExtractionWorkflow = BuildConcurrentRelationshipExtraction("ConcurrentRelationshipExtraction");
    Workflow mermaidDiagramWorkflow = BuildMermaidDiagramGroupChat("MermaidDiagramGroupChat");

    // Wrap each workflow as an AIAgent so the outer sequential pipeline can chain them
    Workflow workflow = AgentWorkflowBuilder.BuildSequential(
        workflowName,
        [
          entityExtractionWorkflow.AsAIAgent("ConcurrentEntityExtraction"), 
          relationshipExtractionWorkflow.AsAIAgent("ConcurrentRelationshipExtraction"), 
          mermaidDiagramWorkflow.AsAIAgent("MermaidDiagramAsGroupChat")
        ]
    );

    return workflow;
  }

  /// <summary>
  /// <b>Strategy 3:</b> Composes custom orchestrations (manually wired with executors and edges)
  /// as agents in a sequential pipeline. Functionally equivalent to Strategy 2, but built from scratch
  /// to demonstrate the low-level <see cref="WorkflowBuilder"/> API.
  /// </summary>
  public Workflow BuildPipelineFromCustomOrchestrations(string workflowName)
  {
    // Each of these manually wires executors and edges instead of using AgentWorkflowBuilder
    Workflow concurrentEntityExtraction = BuildEntityExtractionAsConcurrent("EntityExtractionAsConcurrent");
    Workflow concurrentRelationshipExtraction = BuildRelationshipExtractionAsConcurrent("RelationshipExtractionAsConcurrent");
    Workflow mermaidGroupChatWorkflow = BuildMermaidDiagramAsGroupChat("MermaidDiagramAsGroupChat");

    // Same wrapping — the outer pipeline doesn't know (or care) how the inner workflows were built
    Workflow workflow = AgentWorkflowBuilder.BuildSequential(
      workflowName,
      [
        concurrentEntityExtraction.AsAIAgent("ConcurrentEntityExtraction"), 
        concurrentRelationshipExtraction.AsAIAgent("ConcurrentRelationshipExtraction"), 
        mermaidGroupChatWorkflow.AsAIAgent("MermaidDiagramAsGroupChat")
      ]
    );

    return workflow;
  }

  /// <summary>
  /// <b>Strategy 4:</b> Fully custom single pipeline — every executor and edge lives in one
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
  public Workflow BuildFullyCustomOrchestratedPipeline(string workflowName)
  {
    // ── Create all agents ──────────────────────────────────────────────
    AIAgent entityAgent1 = agentsBuilder.BuildEntitiesAgent("1", ChatProvider.OpenAI);
    AIAgent entityAgent2 = agentsBuilder.BuildEntitiesAgent("2", ChatProvider.OpenAI);
    AIAgent entityAgent3 = agentsBuilder.BuildEntitiesAgent("3", ChatProvider.OpenAI);

    AIAgent relationshipAgent1 = agentsBuilder.BuildRelationshipsAgent("1", ChatProvider.OpenAI);
    AIAgent relationshipAgent2 = agentsBuilder.BuildRelationshipsAgent("2", ChatProvider.OpenAI);
    AIAgent relationshipAgent3 = agentsBuilder.BuildRelationshipsAgent("3", ChatProvider.OpenAI);

    AIAgent diagramBuilderAgent = agentsBuilder.BuildMermaidDiagramAgent(ChatProvider.OpenAI);
    AIAgent diagramReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent(ChatProvider.OpenAI);

    // ── Stage 1: Entity extraction (fan-out / fan-in) ─────────────────
    var entityFanOut = new FanOutExecutor("EntityFanOut");
    var entityAggregator = new AggregatorExecutor("EntityAggregator", 3, Aggregator.AggregateEntities);

    // ── Stage 2: Relationship extraction (fan-out / fan-in) ───────────
    var relationshipFanOut = new FanOutExecutor("RelationshipFanOut");
    var relationshipAggregator = new AggregatorExecutor("RelationshipAggregator", 3, Aggregator.AggregateRelationships);

    // ── Stage 3: Mermaid diagram refinement (star-topology group chat) ──────
    var builderParticipant = new ParticipantExecutor(diagramBuilderAgent, includeInputInOutput: true);
    var reviewerParticipant = new ParticipantExecutor(diagramReviewerAgent, includeInputInOutput: true);

    // Round-robin manager: alternates between builder and reviewer, terminates on APPROVED or max turns
    ApprovalManager roundRobinManager = new([diagramBuilderAgent, diagramReviewerAgent], terminationFunction: Terminators.TerminationFunction())
    {
      MaximumIterationCount = 10 // Allow up to 10 turns (5 per participant) before forced termination
    };

    var refinementExecutor = new RefinementExecutor(nameof(RefinementExecutor), diagramBuilderAgent, builderParticipant, reviewerParticipant, roundRobinManager);

    // ── Wire the single flat graph (all stages share one WorkflowBuilder) ───
    WorkflowBuilder workflowBuilder = new(entityFanOut);

    // Stage 1 edges: fan-out → entity agents → batchers → fan-in barrier → forwarding aggregator
    workflowBuilder.AddFanOutEdge(entityFanOut, [entityAgent1, entityAgent2, entityAgent3]);
    var entityBatcher1 = new MessageBatcherExecutor($"Batch/{entityAgent1.Name}");
    var entityBatcher2 = new MessageBatcherExecutor($"Batch/{entityAgent2.Name}");
    var entityBatcher3 = new MessageBatcherExecutor($"Batch/{entityAgent3.Name}");
    workflowBuilder.AddEdge((ExecutorBinding)entityAgent1, entityBatcher1);
    workflowBuilder.AddEdge(entityAgent2, entityBatcher2);
    workflowBuilder.AddEdge(entityAgent3, entityBatcher3);
    workflowBuilder.AddFanInBarrierEdge([entityBatcher1, entityBatcher2, entityBatcher3], entityAggregator);

    // Inter-stage handoff: entity aggregator forwards merged entities to relationship fan-out
    workflowBuilder.AddEdge(entityAggregator, relationshipFanOut);

    // Stage 2 edges: fan-out → relationship agents → batchers → fan-in barrier → forwarding aggregator
    workflowBuilder.AddFanOutEdge(relationshipFanOut, [relationshipAgent1, relationshipAgent2, relationshipAgent3]);
    var relationshipBatcher1 = new MessageBatcherExecutor($"Batch/{relationshipAgent1.Name}");
    var relationshipBatcher2 = new MessageBatcherExecutor($"Batch/{relationshipAgent2.Name}");
    var relationshipBatcher3 = new MessageBatcherExecutor($"Batch/{relationshipAgent3.Name}");
    workflowBuilder.AddEdge(relationshipAgent1, relationshipBatcher1);
    workflowBuilder.AddEdge(relationshipAgent2, relationshipBatcher2);
    workflowBuilder.AddEdge(relationshipAgent3, relationshipBatcher3);
    workflowBuilder.AddFanInBarrierEdge([relationshipBatcher1, relationshipBatcher2, relationshipBatcher3], relationshipAggregator);

    // Inter-stage handoff: relationship aggregator forwards merged data to group chat orchestrator
    workflowBuilder.AddEdge(relationshipAggregator, refinementExecutor);

    // Stage 3 edges: star topology — bidirectional edges between orchestrator and each participant
    workflowBuilder.AddEdge(refinementExecutor, builderParticipant);
    workflowBuilder.AddEdge(builderParticipant, refinementExecutor);
    workflowBuilder.AddEdge(refinementExecutor, reviewerParticipant);
    workflowBuilder.AddEdge(reviewerParticipant, refinementExecutor);

    // Output: the refinement orchestrator yields the final Mermaid diagram
    workflowBuilder.WithOutputFrom(refinementExecutor);
    workflowBuilder.WithName(workflowName);

    return workflowBuilder.Build();
  }

  // ════════════════════════════════════════════════════════════════════════
  //  LOW-LEVEL ORCHESTRATIONS (manual executors + edges)
  //
  //  These methods build workflows by explicitly defining:
  //    • Executors — units of work (nodes in the graph)
  //    • Edges — message routes between executors (directed connections)
  //    • Fan-out/Fan-in — parallel branching and barrier-based merging
  //    • Star topology — central hub routing messages to/from participants
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// Simulates the <b>concurrent pattern</b> for entity extraction using manual fan-out/fan-in wiring.
  /// <code>
  ///                      ┌──→ [EntAgent_1] ──→ [Batcher 1] ──┐
  ///   [FanOutExecutor] ──┼──→ [EntAgent_2] ──→ [Batcher 2] ──┼──→ [ConcurrentAggregatorExecutor] ──→ Output
  ///                      └──→ [EntAgent_3] ──→ [Batcher 3] ──┘
  /// </code>
  /// <b>Flow:</b>
  /// <list type="number">
  ///   <item>FanOutExecutor receives input messages and broadcasts them to all three agents simultaneously.</item>
  ///   <item>Each agent processes independently and sends results to its dedicated Batcher.</item>
  ///   <item>The fan-in barrier waits for all three batchers to complete before proceeding.</item>
  ///   <item>ConcurrentAggregatorExecutor deduplicates and merges the three result sets into a single output.</item>
  /// </list>
  /// </summary>
  public Workflow BuildEntityExtractionAsConcurrent(string workflowName)
  {
    // 1. Create the boundary executors: fan-out (entry) and fan-in (exit)
    var fanOutExecutor = new FanOutExecutor(nameof(FanOutExecutor));
    var fanInAggregator = new ConcurrentAggregatorExecutor(nameof(ConcurrentAggregatorExecutor), 3, Aggregator.AggregateEntities);

    // 2. Create three identical agents that will process the same input in parallel
    AIAgent entityAgent1 = agentsBuilder.BuildEntitiesAgent("1");
    AIAgent entityAgent2 = agentsBuilder.BuildEntitiesAgent("2");
    AIAgent entityAgent3 = agentsBuilder.BuildEntitiesAgent("3");

    // 3. Start building the workflow graph with fan-out as the entry point
    WorkflowBuilder workflowBuilder = new(fanOutExecutor);

    // 4. Fan-out edge: broadcasts input from one source to multiple targets
    workflowBuilder.AddFanOutEdge(fanOutExecutor, [entityAgent1, entityAgent2, entityAgent3]);

    // 5. Each agent's output flows to a dedicated batcher that collects messages per turn
    var batcher1 = new MessageBatcherExecutor($"Batch/{entityAgent1.Name}");
    var batcher2 = new MessageBatcherExecutor($"Batch/{entityAgent2.Name}");
    var batcher3 = new MessageBatcherExecutor($"Batch/{entityAgent3.Name}");
    workflowBuilder.AddEdge(entityAgent1, batcher1);
    workflowBuilder.AddEdge(entityAgent2, batcher2);
    workflowBuilder.AddEdge(entityAgent3, batcher3);

    // 6. Fan-in barrier: waits for ALL batchers to complete, then sends each result to the aggregator
    workflowBuilder.AddFanInBarrierEdge([batcher1, batcher2, batcher3], fanInAggregator);

    // 7. Mark the aggregator as the workflow's output node
    workflowBuilder.WithOutputFrom(fanInAggregator);
    workflowBuilder.WithName(workflowName);

    return workflowBuilder.Build();
  }

  /// <summary>
  /// Simulates the <b>concurrent pattern</b> for relationship extraction using manual fan-out/fan-in wiring.
  /// Same topology as <see cref="BuildEntityExtractionAsConcurrent"/> but with relationship agents
  /// and a relationship-specific aggregation function.
  /// <code>
  ///                      ┌──→ [RelAgent_1] ──→ [Batcher 1] ──┐
  ///   [FanOutExecutor] ──┼──→ [RelAgent_2] ──→ [Batcher 2] ──┼──→ [ConcurrentAggregatorExecutor] ──→ Output
  ///                      └──→ [RelAgent_3] ──→ [Batcher 3] ──┘
  /// </code>
  /// </summary>
  public Workflow BuildRelationshipExtractionAsConcurrent(string workflowName)
  {
    var fanOutExecutor = new FanOutExecutor(nameof(FanOutExecutor));
    var fanInAggregator = new ConcurrentAggregatorExecutor(nameof(ConcurrentAggregatorExecutor), 3, Aggregator.AggregateRelationships);

    AIAgent relationshipAgent1 = agentsBuilder.BuildRelationshipsAgent("1");
    AIAgent relationshipAgent2 = agentsBuilder.BuildRelationshipsAgent("2");
    AIAgent relationshipAgent3 = agentsBuilder.BuildRelationshipsAgent("3");

    WorkflowBuilder workflowBuilder = new(fanOutExecutor);

    workflowBuilder.AddFanOutEdge(fanOutExecutor, [relationshipAgent1, relationshipAgent2, relationshipAgent3]);
    var batcher1 = new MessageBatcherExecutor($"Batch/{relationshipAgent1.Name}");
    var batcher2 = new MessageBatcherExecutor($"Batch/{relationshipAgent2.Name}");
    var batcher3 = new MessageBatcherExecutor($"Batch/{relationshipAgent3.Name}");
    workflowBuilder.AddEdge(relationshipAgent1, batcher1);
    workflowBuilder.AddEdge(relationshipAgent2, batcher2);
    workflowBuilder.AddEdge(relationshipAgent3, batcher3);
    workflowBuilder.AddFanInBarrierEdge([batcher1, batcher2, batcher3], fanInAggregator);
    workflowBuilder.WithOutputFrom(fanInAggregator);
    workflowBuilder.WithName(workflowName);

    return workflowBuilder.Build();
  }

  /// <summary>
  /// Simulates the <b>group chat pattern</b> for Mermaid diagram generation using manual star-topology wiring.
  /// <code>
  ///         ┌──────────────────────────────────┐
  ///         │  [RefinementExecutor]            │
  ///         │  selects next ──→ sends messages │
  ///         │  checks termination condition    │
  ///         └──────┬──────────────┬────────────┘
  ///                │              │
  ///                ▼              ▼
  ///          ┌─────────────┐  ┌──────────────┐
  ///          │ Participant │  │ Participant  │
  ///          │ (Builder)   │  │ (Reviewer)   │
  ///          └─────┬───────┘  └─────┬────────┘
  ///                │                │
  ///                └──────┬─────────┘
  ///                       │
  ///            back to RefinementExecutor
  ///         (until APPROVED or max iterations)
  /// </code>
  /// <b>Flow:</b>
  /// <list type="number">
  ///   <item>RefinementExecutor receives input and selects the first participant (round-robin).</item>
  ///   <item>The selected participant processes messages and sends results back to the orchestrator.</item>
  ///   <item>RefinementExecutor checks the termination condition (APPROVED keyword or max iterations).</item>
  ///   <item>If not terminated, the orchestrator selects the next participant and repeats from step 2.</item>
  ///   <item>On termination, the orchestrator yields the best mermaid diagram as output.</item>
  /// </list>
  /// </summary>
  public Workflow BuildMermaidDiagramAsGroupChat(string workflowName)
  {
    AIAgent diagramBuilderAgent = agentsBuilder.BuildMermaidDiagramAgent();
    AIAgent diagramReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent();

    // 1. Create explicit participant executors — bridges the AIAgent interface
    //    to the workflow executor protocol, handling role reassignment and message forwarding
    var builderParticipant = new ParticipantExecutor(diagramBuilderAgent, includeInputInOutput: true);
    var reviewerParticipant = new ParticipantExecutor(diagramReviewerAgent, includeInputInOutput: true);

    // 2. Create the group chat manager that controls speaker selection and termination
    ApprovalManager roundRobinManager = new([diagramBuilderAgent, diagramReviewerAgent], terminationFunction: Terminators.TerminationFunction())
    {
      MaximumIterationCount = 10 // Allow up to 10 turns (5 per participant) before forced termination
    };

    // 3. Create the central Refinement Orchestrator — the hub of the star topology.
    //    Direct instantiation ensures the [YieldsOutput] attribute is visible to
    //    the WorkflowBuilder when registering output types.
    var refinementExecutor = new RefinementExecutor(nameof(RefinementExecutor), diagramBuilderAgent, builderParticipant, reviewerParticipant, roundRobinManager);

    // 4. Build the star topology: Chat Host ↔ each participant (bidirectional edges)
    WorkflowBuilder workflowBuilder = new(refinementExecutor);

    workflowBuilder.AddEdge(refinementExecutor, builderParticipant);
    workflowBuilder.AddEdge(builderParticipant, refinementExecutor);
    workflowBuilder.AddEdge(refinementExecutor, reviewerParticipant);
    workflowBuilder.AddEdge(reviewerParticipant, refinementExecutor);

    // 5. The RefinementExecutor is both the entry point and the output node
    workflowBuilder.WithOutputFrom(refinementExecutor);
    workflowBuilder.WithName(workflowName);

    return workflowBuilder.Build();
  }

  // ════════════════════════════════════════════════════════════════════════
  //  HIGH-LEVEL WORKFLOWS (AgentWorkflowBuilder helpers)
  //
  //  These produce the same topologies as the orchestrations above,
  //  but the framework handles all the executor/edge wiring internally.
  // ════════════════════════════════════════════════════════════════════════

  /// <summary>
  /// Builds a concurrent entity extraction workflow using <see cref="AgentWorkflowBuilder.BuildConcurrent"/>.
  /// The framework internally creates the fan-out/fan-in topology.
  /// </summary>
  private Workflow BuildConcurrentEntityExtraction(string workflowName)
  {
    AIAgent entityAgent1 = agentsBuilder.BuildEntitiesAgent("1");
    AIAgent entityAgent2 = agentsBuilder.BuildEntitiesAgent("2");
    AIAgent entityAgent3 = agentsBuilder.BuildEntitiesAgent("3");

    Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(
      workflowName,
      [entityAgent1, entityAgent2, entityAgent3],
      Aggregator.AggregateEntities);

    return workflow;
  }

  /// <summary>
  /// Builds a concurrent relationship extraction workflow using <see cref="AgentWorkflowBuilder.BuildConcurrent"/>.
  /// The framework internally creates the fan-out/fan-in topology.
  /// </summary>
  private Workflow BuildConcurrentRelationshipExtraction(string workflowName)
  {
    AIAgent relationshipAgent1 = agentsBuilder.BuildRelationshipsAgent("1");
    AIAgent relationshipAgent2 = agentsBuilder.BuildRelationshipsAgent("2");
    AIAgent relationshipAgent3 = agentsBuilder.BuildRelationshipsAgent("3");

    Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(
      workflowName,
      [relationshipAgent1, relationshipAgent2, relationshipAgent3],
      Aggregator.AggregateRelationships);

    return workflow;
  }

  /// <summary>
  /// Builds a Mermaid diagram group chat workflow using <see cref="AgentWorkflowBuilder.CreateGroupChatBuilderWith"/>.
  /// The framework internally creates the star topology with round-robin speaker selection.
  /// </summary>
  private Workflow BuildMermaidDiagramGroupChat(/*Bug? CreateGroupChatBuilderWith does not use a workflow name*/string workflowName)
  {
    AIAgent diagramBuilderAgent = agentsBuilder.BuildMermaidDiagramAgent();
    AIAgent diagramReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent();

    Workflow workflow = AgentWorkflowBuilder
      .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents, Terminators.TerminationFunction())
      {
        MaximumIterationCount = 10 // Allow up to 10 turns (5 per participant) before forced termination
      })
      .AddParticipants(diagramBuilderAgent, diagramReviewerAgent)
      .Build();

    return workflow;
  }
}
