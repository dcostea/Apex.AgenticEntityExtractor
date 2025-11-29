using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Aggregators;
using Apex.AgenticEntityExtractor.Executors;
using Apex.AgenticEntityExtractor.GroupChatManagers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Apex.AgenticEntityExtractor.Workflows;

/// <summary>
/// Builder for creating entity extractor workflows using sub-workflows or workflows as agents.
/// </summary>
public class ExtractorWorkflowBuilder(IExtractorAgentsBuilder agentsBuilder) : IExtractorWorkflowBuilder
{
    /// <summary>
    /// Builds a sequential extraction workflow that uses simple agents (entities, relationships, and Mermaid diagram agents).
    /// </summary>
    public Workflow BuildWorkflowFromSequentialWorkflow(string workflowName)
    {
        AIAgent entitiesAgent = agentsBuilder.BuildEntitiesAgent();
        AIAgent relationshipsAgent = agentsBuilder.BuildRelationshipsAgent();
        AIAgent mermaidAgent = agentsBuilder.BuildMermaidDiagramAgent();

        Workflow workflow = AgentWorkflowBuilder.BuildSequential(
            workflowName,
            [entitiesAgent, relationshipsAgent, mermaidAgent]
        );

        return workflow;
    }

    /// <summary>
    /// Builds a sequential extraction workflow that composes workflows as agents (entities, relationships, and Mermaid diagram workflows as agents).
    /// </summary>
    public Workflow BuildWorkflowFromWorkflowsAsAgents(string workflowName)
    {
        Workflow entitiesWorkflow = BuildEntitiesConcurrentWorkflow("EntitiesConcurrentWorkflow");
        Workflow relationshipsWorkflow = BuildRelationshipsConcurrentWorkflow("RelationshipsConcurrentWorkflow");
        Workflow mermaidWorkflow = BuildMermaidGroupChatWorkflow("MermaidChatGroupWorkflow");

        Workflow workflow = AgentWorkflowBuilder.BuildSequential(
            workflowName,
            [entitiesWorkflow.AsAgent(), relationshipsWorkflow.AsAgent(), mermaidWorkflow.AsAgent()]
        );

        return workflow;
    }

    /// <summary>
    /// Builds a sequential extraction workflow that composes sub-workflows (entities, relationships, and Mermaid diagram sub-workflows).
    /// </summary>
    public Workflow BuildWorkflowFromSubWorkflows(string workflowName)
    {
        Workflow entitiesSubWorkflow = BuildEntitiesSubWorkflow("EntitiesSubWorkflow");
        Workflow relationshipsSubWorkflow = BuildRelationshipsSubWorkflow("RelationshipsSubWorkflow");
        Workflow mermaidSubWorkflow = BuildMermaidSubWorkflow("MermaidSubWorkflow");

        Workflow workflow = AgentWorkflowBuilder.BuildSequential(
            workflowName,
            [entitiesSubWorkflow.AsAgent(), relationshipsSubWorkflow.AsAgent(), mermaidSubWorkflow.AsAgent()]
        );

        return workflow;
    }

    /// <summary>
    /// Builds a sub-workflow for entities extraction using edges and executors (three similar entity agents).
    /// </summary>
    public Workflow BuildEntitiesSubWorkflow(string workflowName)
    {
        var startExecutor = new ConcurrentStartExecutor(nameof(ConcurrentStartExecutor));
        var aggregationExecutor = new ConcurrentAggregationExecutor(nameof(ConcurrentAggregationExecutor), 3);

        AIAgent entitiesAgent1 = agentsBuilder.BuildEntitiesAgent("#1");
        AIAgent entitiesAgent2 = agentsBuilder.BuildEntitiesAgent("#2");
        AIAgent entitiesAgent3 = agentsBuilder.BuildEntitiesAgent("#3");

        WorkflowBuilder workflowBuilder = new(startExecutor);

        workflowBuilder.AddFanOutEdge(startExecutor, [entitiesAgent1, entitiesAgent2, entitiesAgent3]);
        workflowBuilder.AddEdge(entitiesAgent1, new CollectChatMessagesExecutor($"Batcher/{entitiesAgent1.Id}"));
        workflowBuilder.AddEdge(entitiesAgent2, new CollectChatMessagesExecutor($"Batcher/{entitiesAgent2.Id}"));
        workflowBuilder.AddEdge(entitiesAgent3, new CollectChatMessagesExecutor($"Batcher/{entitiesAgent3.Id}"));
        workflowBuilder.AddFanInEdge([entitiesAgent1, entitiesAgent2, entitiesAgent3], aggregationExecutor);
        workflowBuilder.WithOutputFrom(aggregationExecutor);
        workflowBuilder.WithName(workflowName);

        Workflow subWorkflow = workflowBuilder.Build();

        return subWorkflow;
    }

    /// <summary>
    /// Builds a sub-workflow for relationships extraction using edges and executors (three similar relationship agents).
    /// </summary>
    public Workflow BuildRelationshipsSubWorkflow(string workflowName)
    {
        var startExecutor = new ConcurrentStartExecutor(nameof(ConcurrentStartExecutor));
        var aggregationExecutor = new ConcurrentAggregationExecutor(nameof(ConcurrentAggregationExecutor), 3);

        AIAgent relationshipsAgent1 = agentsBuilder.BuildRelationshipsAgent("#1");
        AIAgent relationshipsAgent2 = agentsBuilder.BuildRelationshipsAgent("#2");
        AIAgent relationshipsAgent3 = agentsBuilder.BuildRelationshipsAgent("#3");

        WorkflowBuilder workflowBuilder = new(startExecutor);

        workflowBuilder.AddFanOutEdge(startExecutor, [relationshipsAgent1, relationshipsAgent2, relationshipsAgent3]);
        workflowBuilder.AddEdge(relationshipsAgent1, new CollectChatMessagesExecutor($"Batcher/{relationshipsAgent1.Id}"));
        workflowBuilder.AddEdge(relationshipsAgent2, new CollectChatMessagesExecutor($"Batcher/{relationshipsAgent2.Id}"));
        workflowBuilder.AddEdge(relationshipsAgent3, new CollectChatMessagesExecutor($"Batcher/{relationshipsAgent3.Id}"));
        workflowBuilder.AddFanInEdge([relationshipsAgent1, relationshipsAgent2, relationshipsAgent3], aggregationExecutor);
        workflowBuilder.WithOutputFrom(aggregationExecutor);
        workflowBuilder.WithName(workflowName);

        Workflow subWorkflow = workflowBuilder.Build();

        return subWorkflow;
    }

    /// <summary>
    /// Builds a sub-workflow for Mermaid diagram generation using a round-robin group chat manager, edges, and executors (builder and reviewer agents).
    /// </summary>
    public Workflow BuildMermaidSubWorkflow(string workflowName)
    {
        AIAgent mermaidDiagramAgent = agentsBuilder.BuildMermaidDiagramAgent();
        AIAgent mermaidReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent();

        AIAgent[] agents = [mermaidDiagramAgent, mermaidReviewerAgent];
        Dictionary<AIAgent, ExecutorBinding> agentToExecutorMap = agents.ToDictionary(a => a, a => (ExecutorBinding)new AgentRunStreamingExecutor(a, includeInputInOutput: true));

        ApprovalRoundRobinGroupChatManager chatManager = new(agents, terminationFunction: Terminators.TerminationFunction())
        {
            MaximumIterationCount = 10
        };

        Func<string, string, ValueTask<Executor>> groupChatHostFactory =
            (id, runId) => new(new GroupChatHost(id, agentToExecutorMap, chatManager));

        ExecutorBinding groupChatHost = groupChatHostFactory.BindExecutor(nameof(GroupChatHost));

        WorkflowBuilder workflowBuilder = new(groupChatHost);

        foreach (var participant in agentToExecutorMap.Values)
        {
            workflowBuilder.AddEdge(groupChatHost, participant);
            workflowBuilder.AddEdge(participant, groupChatHost);
        }
        workflowBuilder.WithOutputFrom(groupChatHost);
        workflowBuilder.WithName(workflowName);

        Workflow subWorkflow = workflowBuilder.Build();

        return subWorkflow;
    }

    /// <summary>
    /// Builds a concurrent workflow for entities extraction (three similar entity agents).
    /// </summary>
    private Workflow BuildEntitiesConcurrentWorkflow(string workflowName)
    {
        AIAgent entitiesAgent1 = agentsBuilder.BuildEntitiesAgent("#1");
        AIAgent entitiesAgent2 = agentsBuilder.BuildEntitiesAgent("#2");
        AIAgent entitiesAgent3 = agentsBuilder.BuildEntitiesAgent("#3");

        Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(
            workflowName,
            [entitiesAgent1, entitiesAgent2, entitiesAgent3],
            Aggregator.AggregateEntities);

        return workflow;
    }

    /// <summary>
    /// Builds a concurrent workflow for relationships extraction (three similar relationship agents).
    /// </summary>
    private Workflow BuildRelationshipsConcurrentWorkflow(string workflowName)
    {
        AIAgent relationshipsAgent1 = agentsBuilder.BuildRelationshipsAgent("#1");
        AIAgent relationshipsAgent2 = agentsBuilder.BuildRelationshipsAgent("#2");
        AIAgent relationshipsAgent3 = agentsBuilder.BuildRelationshipsAgent("#3");

        Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(
            workflowName,
            [relationshipsAgent1, relationshipsAgent2, relationshipsAgent3],
            Aggregator.AggregateEntities);

        return workflow;
    }

    /// <summary>
    /// Builds a group chat workflow for Mermaid diagram generation (builder and reviewer agents).
    /// </summary>
    private Workflow BuildMermaidGroupChatWorkflow(string workflowName)
    {
        AIAgent mermaidDiagramAgent = agentsBuilder.BuildMermaidDiagramAgent();
        AIAgent mermaidReviewerAgent = agentsBuilder.BuildMermaidReviewerAgent();

        Workflow workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents, Terminators.TerminationFunction())
            {
                MaximumIterationCount = 10
            })
            .AddParticipants(mermaidDiagramAgent, mermaidReviewerAgent)
            //.WithName(workflowName) // probably this method is missing in official class
            .Build();

        return workflow;
    }
}
