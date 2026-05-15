# 05 - Add middleware

Goal: Show where enterprise concerns belong — auditing, caching, retries, redaction, policy checks.

## New Package (add to step 04)

```xml
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.0" />
```

## New Files

### ExtractionRequest.cs

```csharp
public sealed class ExtractionRequest
{
  public required string Text { get; init; }
}
```

### ToolResponseMiddleware.cs

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

public sealed class ToolResponseMiddleware(IDistributedCache cache)
{
  public async ValueTask<object?> CacheToolResponseAsync(
    AIAgent agent,
    FunctionInvocationContext context,
    Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
    CancellationToken cancellationToken)
  {
    string cacheKey = $"tool:{context.Function.Name}";
    byte[]? cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);

    if (cachedBytes is not null)
    {
      Console.WriteLine($"[CACHE HIT: {agent.Name}] {context.Function.Name}");
      using JsonDocument document = JsonDocument.Parse(cachedBytes);
      return document.RootElement.Clone();
    }

    Console.WriteLine($"[CACHE MISS: {agent.Name}] {context.Function.Name}");
    object? result = await next(context, cancellationToken);

    if (result is not null)
    {
      await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result), cancellationToken);
    }

    return result;
  }
}
```

## Register Middleware (add to Program.cs before agent registration)

```csharp
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<ToolResponseMiddleware>();
```

## Apply Middleware to Agent (replace agent registration)

```csharp
builder.Services.AddSingleton<AIAgent>(sp =>
{
  IChatClient chatClient = sp.GetRequiredService<IChatClient>();
  ToolResponseMiddleware middleware = sp.GetRequiredService<ToolResponseMiddleware>();

  return chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "CachedToolEntityAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "EntitiesAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntityTypesAsync, "load_entities_ontology")],
      ToolMode = ChatToolMode.RequireAny,
      ResponseFormat = ChatResponseFormat.ForJsonSchema<Entities>()
    }
  })
  .AsBuilder()
  .Use(middleware.CacheToolResponseAsync)
  .Build();
});
```

## Update Endpoint to Accept JSON (replace existing endpoint)

```csharp
app.MapPost("/extract", async (ExtractionRequest request, AIAgent agent, CancellationToken cancellationToken) =>
{
  AgentResponse response = await agent.RunAsync(new ChatMessage(ChatRole.User, request.Text), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});
```

## Test

First request (cache miss):

```bash
curl -X POST https://localhost:7078/extract \
  -H "Content-Type: application/json" \
  -d '{"text":"Daniel is in London. Andrada works for Daniel."}'
```

Console output:
```
[CACHE MISS: CachedToolEntityAgent] load_entities_ontology
```

Second request (cache hit):

```bash
curl -X POST https://localhost:7078/extract \
  -H "Content-Type: application/json" \
  -d '{"text":"Elena met Dr. Michael at the conference."}'
```

Console output:
```
[CACHE HIT: CachedToolEntityAgent] load_entities_ontology
```

## Teaching Points

- `.AsBuilder().Use(middleware).Build()` creates a middleware pipeline around the agent
- Middleware intercepts tool calls before/after execution
- Caching ontology tool responses avoids repeated file I/O or API calls
- Enterprise patterns: auditing (log every call), retries (on transient failures), policy (block sensitive operations), redaction (strip PII)
- Middleware keeps agent code clean — cross-cutting concerns stay separate
