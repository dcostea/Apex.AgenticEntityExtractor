# 04 - Switch provider

Goal: Keep application code stable while switching between OpenAI, Azure OpenAI, and Ollama (local SLMs).

## New Packages (add to step 03)

```xml
<PackageReference Include="Azure.AI.OpenAI" Version="2.1.0" />
<PackageReference Include="OllamaSharp" Version="5.4.25" />
```

## New Files

### ChatProvider.cs

```csharp
public enum ChatProvider
{
  OpenAI,
  AzureOpenAI,
  Ollama
}
```

### ChatClientFactory.cs

```csharp
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

public sealed class ChatClientFactory(IConfiguration configuration)
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
```

## appsettings.json

```json
{
  "Provider": "OpenAI",
  "OpenAI": {
    "ModelId": "gpt-4.1-mini"
  },
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "DeploymentName": "YOUR-DEPLOYMENT"
  },
  "Ollama": {
    "Server": "http://localhost:11434",
    "Model": "gemma4:e4b"
  }
}
```

## Update Program.cs (replace IChatClient registration)

```csharp
builder.Services.AddSingleton<ChatClientFactory>();

builder.Services.AddSingleton<IChatClient>(sp =>
{
  IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
  ChatProvider provider = configuration.GetValue<ChatProvider>("Provider");
  ChatClientFactory factory = sp.GetRequiredService<ChatClientFactory>();
  return factory.Create(provider);
});
```

## Test

1. Set `"Provider": "OpenAI"` in `appsettings.json` and verify extraction works
2. Change to `"Provider": "Ollama"` (ensure Ollama is running locally with the specified model)
3. Change to `"Provider": "AzureOpenAI"` (configure endpoint and deployment)

Application code remains unchanged — only configuration switches.

## Teaching Points

- Factory pattern isolates provider-specific details
- Application code depends on `IChatClient` abstraction, not concrete implementations
- Configuration-driven provider selection enables A/B testing, cost optimization, and compliance requirements
- Local models (Ollama) useful for development, testing, and privacy-sensitive scenarios
- Azure OpenAI provides enterprise features (private endpoints, compliance, SLA)
