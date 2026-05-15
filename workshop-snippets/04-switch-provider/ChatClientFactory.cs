using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

public class ChatClientFactory(IConfiguration configuration)
{
  public IChatClient Create(ChatProvider provider) => provider switch
  {
    ChatProvider.OpenAI => CreateOpenAI(),
    ChatProvider.AzureOpenAI => CreateAzureOpenAI(),
    ChatProvider.Ollama => CreateOllama(),
    _ => throw new NotSupportedException($"Provider '{provider}' is not supported.")
  };

  private IChatClient CreateOpenAI()
  {
    return new OpenAIClient(new ApiKeyCredential(configuration["OpenAI:ApiKey"]!))
      .GetChatClient(configuration["OpenAI:SmallerModelId"] ?? "gpt-4.1-mini")
      .AsIChatClient();
  }

  private IChatClient CreateAzureOpenAI()
  {
    return new AzureOpenAIClient(
        new Uri(configuration["AzureOpenAI:Endpoint"]!),
        new ApiKeyCredential(configuration["AzureOpenAI:ApiKey"]!))
      .GetChatClient(configuration["AzureOpenAI:DeploymentName"]!)
      .AsIChatClient();
  }

  private IChatClient CreateOllama()
  {
    return new OllamaApiClient(
      new Uri(configuration["Ollama:Server"] ?? "http://localhost:11434"),
      configuration["Ollama:Model"] ?? "gemma4:e4b");
  }
}
