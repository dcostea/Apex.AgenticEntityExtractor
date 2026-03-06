using System.Collections.Concurrent;
using Apex.AgenticEntityExtractor.Clients;
using Apex.AgenticEntityExtractor.Enums;
using Apex.AgenticEntityExtractor.Middleware;
using Apex.AgenticEntityExtractor.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Agents;

/// <summary>
/// Builder for creating AI agents used in entity extraction workflows.
/// Each build method accepts an optional <see cref="ChatProvider"/> to override
/// the default provider read from configuration (<c>appsettings.json → Provider</c>).
/// Chat clients are lazily created and cached inside <see cref="IExtractorChatClientBuilder"/>.
/// </summary>
public class ExtractorAgentsBuilder(IExtractorChatClientBuilder chatClientBuilder, IConfiguration configuration, IToolResponseMiddleware toolResponseMiddleware)
  : IExtractorAgentsBuilder
{
  private readonly ChatProvider _defaultProvider = Enum.Parse<ChatProvider>(configuration["Provider"] ?? "AzureOpenAI");

  private static readonly ConcurrentDictionary<string, string> _instructionsCache = new();

  private IChatClient GetChatClient(ChatProvider? provider) => chatClientBuilder.GetChatClient(provider ?? _defaultProvider);

  private static string LoadInstructions(string fileName) => _instructionsCache.GetOrAdd(fileName, f => File.ReadAllText(Path.Combine("Data", "Instructions", f)));

  /// <summary>
  /// Builds the single-pass extractor agent that runs the full extraction prompt in one call.
  /// </summary>
  public AIAgent BuildExtractorAgent(ChatProvider? provider = null)
  {
    AIAgent extractorAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = "ExtractorSoloAgent",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("ExtractorSoloAgent.md"),
        MaxOutputTokens = 1000,
        //Temperature = 0.1F,
      }
    });

    return extractorAgent;
  }

  /// <summary>
  /// Builds an entities extraction agent configured to require ontology tool usage.
  /// </summary>
  public AIAgent BuildEntitiesAgent(string suffix = "", ChatProvider? provider = null)
  {
    AIAgent entitiesAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = string.IsNullOrEmpty(suffix) ? "EntAgent" : $"EntAgent_{suffix}",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("EntitiesAgent.md"),
        MaxOutputTokens = 3000,
        //Temperature = 0.1F,
        Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntitiesOntologyAsync, "load_entities_ontology")],
        ToolMode = ChatToolMode.Auto,
        Reasoning = new ReasoningOptions
        {
          Effort = ReasoningEffort.None
        },
      }
    })
      .AsBuilder()
      .Use(toolResponseMiddleware.CacheToolResponseAsync)
      .Build();

    return entitiesAgent;
  }

  /// <summary>
  /// Builds a relationships extraction agent configured to require ontology tool usage.
  /// </summary>
  public AIAgent BuildRelationshipsAgent(string suffix = "", ChatProvider? provider = null)
  {
    AIAgent relationshipsAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = string.IsNullOrEmpty(suffix) ? "RelAgent" : $"RelAgent_{suffix}",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("RelationshipsAgent.md"),
        MaxOutputTokens = 3000,
        //Temperature = 0.1F,
        Tools = [AIFunctionFactory.Create(OntologyTools.LoadRelationshipsOntologyAsync, "load_relationships_ontology")],
        ToolMode = ChatToolMode.Auto,
        Reasoning = new ReasoningOptions
        {
          Effort = ReasoningEffort.None
        },
      }
    })
      .AsBuilder()
      .Use(toolResponseMiddleware.CacheToolResponseAsync)
      .Build();

    return relationshipsAgent;
  }

  /// <summary>
  /// Builds the mermaid diagram generation agent.
  /// </summary>
  public AIAgent BuildMermaidDiagramAgent(ChatProvider? provider = null)
  {
    AIAgent mermaidDiagramAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = "MermaidDiagramAgent",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("MermaidDiagramAgent.md"),
        MaxOutputTokens = 3000,
        //Temperature = 0.1F,
        Reasoning = new ReasoningOptions
        {
          Effort = ReasoningEffort.None
        },
      }
    });

    return mermaidDiagramAgent;
  }

  /// <summary>
  /// Builds the mermaid review/approval agent configured with ontology tools
  /// so it can validate that entity and relationship types conform to the defined ontologies.
  /// </summary>
  public AIAgent BuildMermaidReviewerAgent(ChatProvider? provider = null)
  {
    AIAgent mermaidReviewerAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = "MermaidReviewerAgent",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("MermaidReviewerAgent.md"),
        MaxOutputTokens = 3000,
        //Temperature = 0.1F,
        Tools =
        [
          AIFunctionFactory.Create(OntologyTools.LoadEntitiesOntologyAsync, "load_entities_ontology"),
          AIFunctionFactory.Create(OntologyTools.LoadRelationshipsOntologyAsync, "load_relationships_ontology"),
        ],
        ToolMode = ChatToolMode.Auto,
        Reasoning = new ReasoningOptions
        {
          Effort = ReasoningEffort.None
        },
      }
    })
      .AsBuilder()
      .Use(toolResponseMiddleware.CacheToolResponseAsync)
      .Build();

    return mermaidReviewerAgent;
  }
}
