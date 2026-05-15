# 01 - Minimal agent in Web API

Goal: Show the smallest useful agent-backed HTTP endpoint using Microsoft Agent Framework.

## Packages

```xml
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.5.0" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="10.1.7" />
```

## Program.cs

```csharp
using Microsoft.Agents.AI;
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

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
}

app.MapPost("/extract", async (string request, AIAgent agent, CancellationToken cancellationToken) =>
{
  AgentResponse response = await agent.RunAsync(new ChatMessage(ChatRole.User, request), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});

app.Run();
```

## Test

```bash
curl -X POST https://localhost:7078/extract \
  -H "Content-Type: application/json" \
  -d "Daniel is in London today."
```

## Teaching Points

- This is an **agent**, not yet an **agentic system**
- No tools, no workflows, no evaluation, no recovery loop
- Instructions loaded from external markdown file
- `RunAsync` returns complete `AgentResponse`
- Endpoint accepts plain string (not complex DTO) for simplicity
