using Apex.AgenticEntityExtractor.Helpers;
using Apex.AgenticEntityExtractor.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Builders;

public class ExtractorAgentBuilder(IChatClient chatClient, IConfiguration configuration) : IExtractorAgentBuilder
{
    public AIAgent BuildEntitiesAgent()
    {
        bool useConcurrency = configuration.GetValue<bool>("UseConcurrentEntitiesAgents");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"[{(useConcurrency ? "✓" : "✗")}] Use Concurrent Entity Agents");

        if (useConcurrency)
        {
            var entitiesAgent1 = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "EntitiesAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "EntitiesAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadEntitiesOntology), AIFunctionFactory.Create(FilesPlugin.SaveEntities)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
            var entitiesAgent2 = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "EntitiesAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "EntitiesAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadEntitiesOntology), AIFunctionFactory.Create(FilesPlugin.SaveEntities)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
            var entitiesAgent3 = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "EntitiesAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "EntitiesAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadEntitiesOntology), AIFunctionFactory.Create(FilesPlugin.SaveEntities)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
            var entitiesWorkflow = AgentWorkflowBuilder.BuildConcurrent(
                [entitiesAgent1, entitiesAgent2, entitiesAgent3],
                WorkflowHelper.AggregateEntities);
            return entitiesWorkflow.AsAgent(name: "EntitiesAgent");
        }
        else
        {
            return chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "EntitiesAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "EntitiesAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadEntitiesOntology), AIFunctionFactory.Create(FilesPlugin.SaveEntities)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
        }
    }

    public AIAgent BuildRelationshipsAgent()
    {
        bool useConcurrency = configuration.GetValue<bool>("UseConcurrentRelationshipsAgents");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"[{(useConcurrency ? "✓" : "✗")}] Use Concurrent Relationship Agents");

        if (useConcurrency)
        {
            var relationshipsAgent1 = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "RelationshipsAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "RelationshipsAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadRelationshipsOntology), AIFunctionFactory.Create(FilesPlugin.SaveRelationships)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
            var relationshipsAgent2 = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "RelationshipsAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "RelationshipsAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadRelationshipsOntology), AIFunctionFactory.Create(FilesPlugin.SaveRelationships)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
            var relationshipsAgent3 = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "RelationshipsAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "RelationshipsAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadRelationshipsOntology), AIFunctionFactory.Create(FilesPlugin.SaveRelationships)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
            var relationshipsWorkflow = AgentWorkflowBuilder.BuildConcurrent(
                [relationshipsAgent1, relationshipsAgent2, relationshipsAgent3],
                WorkflowHelper.AggregateRelationships);
            return relationshipsWorkflow.AsAgent(name: "RelationshipsAgent");
        }
        else
        {
            return chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "RelationshipsAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "RelationshipsAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                    Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadRelationshipsOntology), AIFunctionFactory.Create(FilesPlugin.SaveRelationships)],
                    //ToolMode = ChatToolMode.Auto,
                }
            });
        }
    }

    public AIAgent BuildMermaidAgent()
    {
        bool useGroupChat = configuration.GetValue<bool>("UseGroupChatAgents");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"[{(useGroupChat ? "✓" : "✗")}] Use GroupChat Mermaid Agents");

        if (useGroupChat)
        {
            var mermaidBuilderAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "MermaidAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "MermaidAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                }
            });
            var validationAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "ValidationAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "ValidationAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                }
            });
            var mermaidWorkflow = AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 7 })
                .AddParticipants(mermaidBuilderAgent, validationAgent)
                .Build();
            return mermaidWorkflow.AsAgent(name: "MermaidAgent");
        }
        else
        {
            return chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "MermaidAgent",
                Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "MermaidAgent.md")),
                ChatOptions = new ChatOptions
                {
                    MaxOutputTokens = 1000,
                    //Temperature = 0.1F,
                }
            });
        }
    }

    public AIAgent BuildValidationAgent()
    {
        return chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "ValidationAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "ValidationAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                //Temperature = 0.1F,
            }
        });
    }
}
