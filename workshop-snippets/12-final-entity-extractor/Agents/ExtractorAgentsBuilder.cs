using System.Collections.Concurrent;
using Apex.AgenticEntityExtractor.Clients;
using Apex.AgenticEntityExtractor.Enums;
using Apex.AgenticEntityExtractor.Middleware;
using Apex.AgenticEntityExtractor.Models;
using Apex.AgenticEntityExtractor.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Agents;

/// <summary>
/// Builder for creating AI agents used in entity extraction workflows.
/// Each build method accepts an optional <see cref="ChatProvider"/> to override
/// the default provider read from configuration (<c>appsettings.json → Provider</c>).
/// </summary>
public class ExtractorAgentsBuilder(IExtractorChatClientBuilder chatClientBuilder, IConfiguration configuration, IToolResponseMiddleware toolResponseMiddleware)
  : IExtractorAgentsBuilder
{
  private readonly ChatProvider _defaultProvider = Enum.Parse<ChatProvider>(configuration["Provider"] ?? "AzureOpenAI");
  private readonly bool _reasoningEnabled = configuration.GetValue<bool>("Reasoning");

  private static readonly ConcurrentDictionary<string, string> _instructionsCache = new();

  private IChatClient GetChatClient(ChatProvider? provider, string ollamaModelKey = "Ollama:Model")
  {
    var effectiveProvider = provider ?? _defaultProvider;
    return effectiveProvider == ChatProvider.Ollama
      ? chatClientBuilder.BuildOllamaChatClient(configuration[ollamaModelKey]!)
      : chatClientBuilder.BuildChatClient(effectiveProvider);
  }

  private static string LoadInstructions(string fileName) => _instructionsCache.GetOrAdd(fileName, f => File.ReadAllText(Path.Combine("Data", "Instructions", f)));

  /// <summary>Returns low-effort <see cref="ReasoningOptions"/> when <c>Reasoning</c> is enabled in configuration, otherwise null.</summary>
  private ReasoningOptions? GetReasoningOptions() =>
    _reasoningEnabled 
      ? new ReasoningOptions 
      {
        Effort = ReasoningEffort.Low 
      } 
      : null;

  /// <summary>
  /// Builds the solo-agent extractor that runs the full extraction prompt in one call.
  /// </summary>
  public AIAgent BuildSoloAgent(ChatProvider? provider = null)
  {
    AIAgent extractorAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = "ExtractorSoloAgent",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("ExtractorSoloAgent.md"),
        //Reasoning = new ReasoningOptions
        //{
        //  Effort = ReasoningEffort.Low
        //},
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
        ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<Entities>(),
        Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntitiesOntologyAsync, "load_entities_ontology")],
        ToolMode = ChatToolMode.RequireAny,
        Reasoning = GetReasoningOptions(),
      }
    })
      .AsBuilder()
      .Use(toolResponseMiddleware.CacheToolResponseAsync)
      .Build();

    return entitiesAgent;
  }

  /// <summary>
  /// Builds a relationships extraction agent with structured output, optionally suffixing the agent name.
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
        ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<Relationships>(),
        Tools = [AIFunctionFactory.Create(OntologyTools.LoadRelationshipsOntologyAsync, "load_relationships_ontology")],
        ToolMode = ChatToolMode.RequireAny,
        Reasoning = GetReasoningOptions(),
      }
    })
      .AsBuilder()
      .Use(toolResponseMiddleware.CacheToolResponseAsync)
      .Build();

    return relationshipsAgent;
  }

  /// <summary>
  /// Builds the Reporter agent that proposes key insights from a narrative/news perspective.
  /// </summary>
  public AIAgent BuildReporterAgent(ChatProvider? provider = null)
  {
    AIAgent reporterAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = "ReporterAgent",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("ReporterAgent.md"),
        MaxOutputTokens = 3000,
        ResponseFormat = ChatResponseFormat.ForJsonSchema<DebateResponse>(),
        Reasoning = GetReasoningOptions(),
      }
    });

    return reporterAgent;
  }

  /// <summary>
  /// Builds the Analyst agent that challenges and refines insights from a business perspective.
  /// </summary>
  public AIAgent BuildAnalystAgent(ChatProvider? provider = null)
  {
    AIAgent analystAgent = GetChatClient(provider).AsAIAgent(new ChatClientAgentOptions
    {
      Name = "AnalystAgent",
      ChatOptions = new ChatOptions
      {
        Instructions = LoadInstructions("AnalystAgent.md"),
        MaxOutputTokens = 3000,
        ResponseFormat = ChatResponseFormat.ForJsonSchema<DebateResponse>(),
        Reasoning = GetReasoningOptions(),
      }
    });

    return analystAgent;
  }
}
