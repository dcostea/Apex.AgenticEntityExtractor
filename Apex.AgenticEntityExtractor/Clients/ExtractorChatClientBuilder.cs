using Anthropic;
using Apex.AgenticEntityExtractor.Enums;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using Spectre.Console;
using System.ClientModel;
using System.Collections.Concurrent;

namespace Apex.AgenticEntityExtractor.Clients;

/// <summary>
/// Builds and caches provider-specific <see cref="IChatClient"/> instances used by extractor agents.
/// </summary>
public class ExtractorChatClientBuilder(IConfiguration configuration) : IExtractorChatClientBuilder
{
  private readonly ConcurrentDictionary<ChatProvider, Lazy<IChatClient>> _clients = new();

  /// <inheritdoc/>
  public IChatClient GetChatClient(ChatProvider provider) =>
    _clients.GetOrAdd(provider, p => new Lazy<IChatClient>(() => BuildChatClient(p))).Value;

  private IChatClient BuildChatClient(ChatProvider provider) => provider switch
  {
    ChatProvider.Ollama => BuildOllamaChatClient(),
    ChatProvider.OpenAI => BuildOpenAIChatClient(),
    ChatProvider.Nano_OpenAI => BuildNanoOpenAIChatClient(),
    ChatProvider.AzureOpenAI => BuildAzureOpenAIChatClient(),
    ChatProvider.Anthropic => BuildAnthropicChatClient(),
    _ => throw new NotSupportedException($"Chat provider '{provider}' is not supported.")
  };

  /// <summary>
  /// Builds an Azure OpenAI chat client with the configured endpoint, API key, deployment, and network timeout.
  /// </summary>
  private IChatClient BuildAzureOpenAIChatClient()
  {
    var endpoint = configuration["AzureOpenAI:Endpoint"]!;
    var apiKey = configuration["AzureOpenAI:ApiKey"]!;
    var deploymentName = configuration["AzureOpenAI:DeploymentName"]!;

    AnsiConsole.MarkupLine($"\n[yellow]MODEL: {Markup.Escape(deploymentName)}[/]");

    IChatClient chatClient = new AzureOpenAIClient(
        new Uri(endpoint),
        new ApiKeyCredential(apiKey),
        new AzureOpenAIClientOptions
        {
          NetworkTimeout = TimeSpan.FromMinutes(5)
        })
      .GetChatClient(deploymentName)
      .AsIChatClient();

    return chatClient;
  }

  /// <summary>
  /// Builds an OpenAI chat client for the configured model.
  /// </summary>
  private IChatClient BuildOpenAIChatClient()
  {
    var model = configuration["OpenAI:ModelId"]!;
    var apiKey = configuration["OpenAI:ApiKey"]!;

    AnsiConsole.MarkupLine($"\n[yellow]MODEL: {Markup.Escape(model)}[/]");

    var chatClient = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions
        {
          NetworkTimeout = TimeSpan.FromMinutes(5)
        })
      .GetChatClient(model)
      .AsIChatClient();

    return chatClient;
  }


  /// <summary>
  /// Builds an OpenAI chat client for the configured model.
  /// </summary>
  private IChatClient BuildNanoOpenAIChatClient()
  {
    var model = configuration["OpenAI:NanoModelId"]!;
    var apiKey = configuration["OpenAI:ApiKey"]!;

    AnsiConsole.MarkupLine($"\n[yellow]MODEL: {Markup.Escape(model)}[/]");

    var chatClient = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions
        {
          NetworkTimeout = TimeSpan.FromMinutes(5)
        })
      .GetChatClient(model)
      .AsIChatClient();

    return chatClient;
  }

  /// <summary>
  /// Builds an Ollama chat client and prints detected model capabilities.
  /// </summary>
  private IChatClient BuildOllamaChatClient()
  {
    string model = configuration["Ollama:Model"]!;
    string ollamaServer = configuration["Ollama:Server"]!;

    var chatClient = new OllamaApiClient(ollamaServer, model);

    var modelInfo = chatClient.ShowModelAsync(model).Result;
    AnsiConsole.MarkupLine($"\n[yellow]MODEL: {Markup.Escape(model)}[/]");

    string[] capabilities = ["completion", "tools", "vision"];
    foreach (var capability in capabilities)
    {
      bool hasCapability = modelInfo.Capabilities!.Contains(capability);
      AnsiConsole.MarkupLine(hasCapability
        ? $"[white]✓ {Markup.Escape(capability)}[/]"
        : $"[red]✗ {Markup.Escape(capability)}[/]");
    }

    return chatClient;
  }

  /// <summary>
  /// Builds an Anthropic chat client for the configured default model.
  /// </summary>
  private IChatClient BuildAnthropicChatClient()
  {
    var model = configuration["Anthropic:ModelId"];
    var apiKey = configuration["Anthropic:ApiKey"];

    IChatClient chatClient = new AnthropicClient() { ApiKey = apiKey }
      .AsIChatClient(defaultModelId: model);

    return chatClient;
  }
}
