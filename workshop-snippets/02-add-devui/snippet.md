# 02 - Add DevUI

Goal: Give students a visual playground for agents and workflows before the architecture becomes complex.

## New Packages (add to step 01)

```xml
<PackageReference Include="Microsoft.Agents.AI.DevUI" Version="1.5.0-preview.260507.1" />
<PackageReference Include="Microsoft.Agents.AI.Hosting" Version="1.5.0-preview.260507.1" />
```

## New Usings (add to step 01)

```csharp
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
```

## DevUI Registration (add before `builder.Build()`)

```csharp
builder.AddDevUI();
builder.AddAIAgent("MinimalEntityExtractor", (sp, _) => sp.GetRequiredService<AIAgent>());
builder.AddOpenAIResponses();
builder.AddOpenAIConversations();
```

## Map DevUI Routes (add after `builder.Build()`)

```csharp
app.MapOpenAIResponses();
app.MapOpenAIConversations();
```

## Enable DevUI in Development (inside existing `if (app.Environment.IsDevelopment())` block)

```csharp
app.MapDevUI();
```

## Complete Program.cs (for reference)

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IChatClient>(sp =>
{
  IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
  return new OpenAIClient(new ApiKeyCredential(configuration["OpenAI:ApiKey"]!))
    .GetChatClient(configuration["OpenAI:SmallerModelId"] ?? "gpt-4.1-mini")
    .AsIChatClient();
});

builder.Services.AddSingleton<AIAgent>(sp =>
{
  IChatClient chatClient = sp.GetRequiredService<IChatClient>();

  return chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "MinimalEntityExtractor",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "ExtractorSoloAgent.md"))
    }
  });
});

builder.AddDevUI();
builder.AddAIAgent("MinimalEntityExtractor", (sp, _) => sp.GetRequiredService<AIAgent>());
builder.AddOpenAIResponses();
builder.AddOpenAIConversations();

WebApplication app = builder.Build();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
  app.MapDevUI();
}

app.MapPost("/extract", async (string request, AIAgent agent, CancellationToken cancellationToken) =>
{
  AgentResponse response = await agent.RunAsync(new ChatMessage(ChatRole.User, request), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});

app.Run();
```

## Access DevUI

Navigate to `https://localhost:7078/devui` to interact with the agent through a visual interface.

## Teaching Points

- DevUI lets students interact with agents without building a frontend
- Agents and workflows registered with `builder.AddAIAgent` and `builder.AddWorkflow` appear in DevUI
- Especially useful for debugging tools, workflows, and multi-agent orchestration
- Production apps typically remove DevUI or restrict it to development environment
