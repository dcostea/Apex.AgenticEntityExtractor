using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Aggregators;
using Apex.AgenticEntityExtractor.Executors;
using Apex.AgenticEntityExtractor.GroupChatManagers;
using Apex.AgenticEntityExtractor.Helpers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Apex.AgenticEntityExtractor.Workflows;

/// <summary>
/// Builder for creating entity extractor workflows using sub-workflows for concurrent processing.
/// </summary>
public class ExtractorWorkflowBuilder(IExtractorAgentsBuilder agentsBuilder) : IExtractorWorkflowBuilder
{
    /// <summary>
    /// Builds a workflow for entity extraction with optional concurrent processing.
    /// </summary>
    public Workflow BuildEntitiesSubWorkflow(string workflowName)
    {
        var startExecutor = new ConcurrentStartExecutor(nameof(ConcurrentStartExecutor));
        var aggregationExecutor = new ConcurrentAggregationExecutor(nameof(ConcurrentAggregationExecutor), 3);

        AIAgent entitiesAgent1 = agentsBuilder.BuildEntitiesAgent("#1");
        AIAgent entitiesAgent2 = agentsBuilder.BuildEntitiesAgent("#2");
        AIAgent entitiesAgent3 = agentsBuilder.BuildEntitiesAgent("#3");

        WorkflowBuilder workflowBuilder = new(startExecutor);

        // Each agent is wrapped in an executor
        workflowBuilder.AddFanOutEdge(startExecutor, [entitiesAgent1, entitiesAgent2, entitiesAgent3]);

        workflowBuilder.AddEdge(entitiesAgent1, new CollectChatMessagesExecutor($"Batcher/{entitiesAgent1.Id}"));
        workflowBuilder.AddEdge(entitiesAgent2, new CollectChatMessagesExecutor($"Batcher/{entitiesAgent2.Id}"));
        workflowBuilder.AddEdge(entitiesAgent3, new CollectChatMessagesExecutor($"Batcher/{entitiesAgent3.Id}"));

        workflowBuilder.AddFanInEdge([entitiesAgent1, entitiesAgent2, entitiesAgent3], aggregationExecutor);

        workflowBuilder.WithOutputFrom(aggregationExecutor);

        workflowBuilder.WithName(workflowName);

        Workflow entitiesSubWorkflow = workflowBuilder.Build();

        return entitiesSubWorkflow;
    }

    /// <summary>
    /// Builds a workflow for relationship extraction with optional concurrent processing.
    /// </summary>
    public Workflow BuildRelationshipsSubWorkflow(string workflowName)
    {
        var startExecutor = new ConcurrentStartExecutor(nameof(ConcurrentStartExecutor));
        var aggregationExecutor = new ConcurrentAggregationExecutor(nameof(ConcurrentAggregationExecutor), 3);

        AIAgent relationshipsAgent1 = agentsBuilder.BuildRelationshipsAgent("#1");
        AIAgent relationshipsAgent2 = agentsBuilder.BuildRelationshipsAgent("#2");
        AIAgent relationshipsAgent3 = agentsBuilder.BuildRelationshipsAgent("#3");

        Workflow relationshipsSubWorkflow = new WorkflowBuilder(startExecutor)
            .AddFanOutEdge(startExecutor, [relationshipsAgent1, relationshipsAgent2, relationshipsAgent3])
            .AddFanInEdge([relationshipsAgent1, relationshipsAgent2, relationshipsAgent3], aggregationExecutor)
            .WithOutputFrom(aggregationExecutor)
            .Build();

        return relationshipsSubWorkflow;
    }

    /// <summary>
    /// Builds a workflow for Mermaid diagram generation with optional group chat.
    /// </summary>
    public Workflow BuildMermaidChatGroupWorkflow(string workflowName)
    {
        AIAgent mermaidDiagramAgent = agentsBuilder.BuildMermaidDiagramAgent();
        AIAgent mermaidReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent();

        Workflow groupChatWorkflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents, Terminators.TerminationFunction())
            {
                MaximumIterationCount = 10
            })
            .AddParticipants(mermaidDiagramAgent, mermaidReviewerAgent)
            .Build();

        AIAgent groupChatAgent = groupChatWorkflow.AsAgent("MermaidGroupChat", "Iteratively builds and reviews mermaid diagram until approved");

        return AgentWorkflowBuilder.BuildSequential(workflowName, groupChatAgent);
    }

    /// <summary>
    /// Builds a workflow for Mermaid diagram generation with optional group chat.
    /// </summary>
    public Workflow BuildMermaidSubWorkflow(string workflowName)
    {
        AIAgent mermaidDiagramAgent = agentsBuilder.BuildMermaidDiagramAgent();
        AIAgent mermaidReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent();

        AIAgent[] agents = [mermaidDiagramAgent, mermaidReviewerAgent];
        Dictionary<AIAgent, ExecutorBinding> agentMap = agents.ToDictionary(a => a, a => (ExecutorBinding)new AgentRunStreamingExecutor(a, includeInputInOutput: true));

        ApprovalRoundRobinGroupChatManager chatManager = new(agents, terminationFunction: Terminators.TerminationFunction())
        {
            MaximumIterationCount = 10
        };

        Func<string, string, ValueTask<Executor>> groupChatHostFactory =
            (id, runId) => new(new GroupChatHost(id, agentMap, chatManager));

        ExecutorBinding host = groupChatHostFactory.BindExecutor(nameof(GroupChatHost));
        WorkflowBuilder builder = new(host);

        foreach (var participant in agentMap.Values)
        {
            builder
                .AddEdge(host, participant)
                .AddEdge(participant, host);
        }

        Workflow worgroupChatWorkflow = builder.WithOutputFrom(host).Build();

        return worgroupChatWorkflow;
    }

    /// <summary>
    /// Builds the main extraction workflow that composes the sub-workflows.
    /// </summary>
    public Workflow BuildMainWorkflow()
    {
        AIAgent entitiesAgent = agentsBuilder.BuildEntitiesAgent();
        AIAgent relationshipsAgent = agentsBuilder.BuildRelationshipsAgent();
        AIAgent mermaidAgent = agentsBuilder.BuildMermaidDiagramAgent();

        Workflow mainWorkflow = AgentWorkflowBuilder.BuildSequential("MainWorkflow", entitiesAgent, relationshipsAgent, mermaidAgent);

        return mainWorkflow;
    }

    /// <summary>
    /// Builds the main extraction workflow that composes the sub-workflows.
    /// </summary>
    public Workflow BuildMainWorkflowWithSubWorkflows()
    {
        Workflow entitiesWorkflow = BuildEntitiesSubWorkflow("EntitiesSubWorkflow");
        Workflow relationshipsWorkflow = BuildRelationshipsSubWorkflow("RelationshipsSubWorkflow");
        Workflow mermaidWorkflow = BuildMermaidSubWorkflow("MermaidSubWorkflow");

        Workflow mainWorkflow = AgentWorkflowBuilder.BuildSequential("MainWorkflowWithSubWorkflows", entitiesWorkflow.AsAgent(), relationshipsWorkflow.AsAgent(), mermaidWorkflow.AsAgent());

        return mainWorkflow;
    }

    public Workflow BuildMainWorkflowWithWorkflowsAsAgents()
    {
        Workflow entitiesWorkflow = BuildEntitiesConcurrentWorkflow("EntitiesConcurrentWorkflow");
        Workflow relationshipsWorkflow = BuildRelationshipsConcurrentWorkflow("RelationshipsConcurrentWorkflow");
        Workflow mermaidWorkflow = BuildMermaidChatGroupWorkflow("MermaidChatGroupWorkflow");

        Workflow mainWorkflow = AgentWorkflowBuilder.BuildSequential("MainWorkflowWithSubWorkflows", 
            entitiesWorkflow.AsAgent(), 
            relationshipsWorkflow.AsAgent(), 
            mermaidWorkflow.AsAgent());

        return mainWorkflow;
    }

    private Workflow BuildRelationshipsConcurrentWorkflow(string v)
    {
        AIAgent relationshipsAgent1 = agentsBuilder.BuildRelationshipsAgent("#1");
        AIAgent relationshipsAgent2 = agentsBuilder.BuildRelationshipsAgent("#2");
        AIAgent relationshipsAgent3 = agentsBuilder.BuildRelationshipsAgent("#3");

        Workflow entitiesSubWorkflow = AgentWorkflowBuilder.BuildConcurrent(
            [relationshipsAgent1, relationshipsAgent2, relationshipsAgent3],
            Aggregator.AggregateEntities);

        return entitiesSubWorkflow;
    }

    private Workflow BuildEntitiesConcurrentWorkflow(string v)
    {
        AIAgent entitiesAgent1 = agentsBuilder.BuildEntitiesAgent("#1");
        AIAgent entitiesAgent2 = agentsBuilder.BuildEntitiesAgent("#2");
        AIAgent entitiesAgent3 = agentsBuilder.BuildEntitiesAgent("#3");

        Workflow entitiesSubWorkflow = AgentWorkflowBuilder.BuildConcurrent(
            [entitiesAgent1, entitiesAgent2, entitiesAgent3],
            Aggregator.AggregateEntities);

        return entitiesSubWorkflow;
    }
}
