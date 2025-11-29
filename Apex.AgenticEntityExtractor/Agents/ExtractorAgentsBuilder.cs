using Apex.AgenticEntityExtractor.Middleware;
using Apex.AgenticEntityExtractor.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Apex.AgenticEntityExtractor.Agents;

/// <summary>
/// Builder for creating AI agents used in entity extraction workflows.
/// </summary>
public class ExtractorAgentsBuilder(IChatClient chatClient, IToolResponseMiddleware toolResponseMiddleware) : IExtractorAgentsBuilder
{
    /// <summary>
    /// Builds extractor agent.
    /// </summary>
    public AIAgent BuildExtractorAgent()
    {
        AIAgent extractorAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "ExtractorSoloAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "ExtractorSoloAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                //Temperature = 0.1F,
            }
        })
            .AsBuilder()
            .Use(toolResponseMiddleware.CacheMiddleware)
            .Build();

        return extractorAgent;
    }

    /// <summary>
    /// Builds extraction entities agent.
    /// </summary>
    public AIAgent BuildEntitiesAgent(string suffix = "")
    {
        AIAgent entitiesAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = $"EntitiesAgent{suffix}",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "EntitiesAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                //Temperature = 0.1F,
                Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadEntitiesOntologyAsync, "load_entities_ontology")],
                ToolMode = ChatToolMode.RequireAny,
            }
        })
            .AsBuilder()
            .Use(toolResponseMiddleware.CacheMiddleware)
            .Build();

        return entitiesAgent;
    }

    /// <summary>
    /// Builds a single relationship extraction agent. 
    /// </summary>
    public AIAgent BuildRelationshipsAgent(string suffix = "")
    {
        AIAgent relationshipsAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = $"RelationshipsAgent{suffix}",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "RelationshipsAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1000,
                //Temperature = 0.1F,
                Tools = [AIFunctionFactory.Create(OntologyPlugin.LoadRelationshipsOntologyAsync, "load_relationships_ontology")],
                ToolMode = ChatToolMode.RequireAny,
            }
        })
            .AsBuilder()
            .Use(toolResponseMiddleware.CacheMiddleware)
            .Build();

        return relationshipsAgent;
    }

    /// <summary>
    /// Builds mermaid diagram builder agent.
    /// </summary>
    public AIAgent BuildMermaidDiagramAgent()
    {
        AIAgent mermaidDiagramAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "MermaidDiagramAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "MermaidDiagramAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1500,
                //Temperature = 0.1F,
            }
        });

        return mermaidDiagramAgent;
    }

    /// <summary>
    /// Builds mermaid reviewer agent.
    /// </summary>
    public AIAgent BuildMermaidReviewerAgent()
    {
        AIAgent mermaidReviewerAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
        {
            Name = "MermaidReviewerAgent",
            Instructions = File.ReadAllText(Path.Combine("Data", "Instructions", "MermaidReviewerAgent.md")),
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 1500,
                //Temperature = 0.1F,
            }
        });

        return mermaidReviewerAgent;
    }
}
