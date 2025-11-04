using Apex.AgenticEntityExtractor.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Helpers;

public static class AgentHelper
{
    public static ChatClientAgent BuildEntitiesAgent(IChatClient chatClient)
    {
        return chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "EntitiesAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "EntitiesAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                Temperature = 0.1F,
                Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadEntitiesOntology), AIFunctionFactory.Create(FilesPlugin.SaveEntities)],
                //ToolMode = ChatToolMode.Auto,
            }
        });
    }

    public static ChatClientAgent BuildRelationshipsAgent(IChatClient chatClient)
    {
        var relationshipsAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "RelationshipsAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "RelationshipsAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                Temperature = 0.1F,
                Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadRelationshipsOntology), AIFunctionFactory.Create(FilesPlugin.SaveRelationships)],
                //ToolMode = ChatToolMode.Auto,
            }
        });
        return relationshipsAgent;
    }

    public static ChatClientAgent BuildMermaidAgent(IChatClient chatClient)
    {
        return chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "MermaidAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "MermaidAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                Temperature = 0.1F,
            }
        });
    }

    public static ChatClientAgent BuildValidationAgent(IChatClient chatClient)
    {
        return chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "ValidationAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "ValidationAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                Temperature = 0.1F,
            }
        });
    }
}
