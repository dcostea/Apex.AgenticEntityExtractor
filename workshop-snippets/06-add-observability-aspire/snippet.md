# 06 - Add observability with OpenTelemetry (Aspire)

Goal: Show traces, logs, metrics, agent activity, tool calls through OpenTelemetry and Aspire Dashboard.

## New Packages (add to step 05)

```xml
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.3" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.2" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.15.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
```

## New Files

### Entity.cs

```csharp
internal sealed class Entity
{
  public string? Id { get; init; }
  public required string Type { get; init; }
  public required string Value { get; init; }
}
```

## Add Usings to Program.cs

```csharp
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Text.Json;
```

## Configure OpenTelemetry (add after Swagger registration)

```csharp
const string SourceName = "EntityExtractor";

builder.Services.AddOpenTelemetry()
  .WithTracing(tracing => tracing
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddSource(SourceName)
    .AddSource("*Microsoft.Agents.AI")
    .AddOtlpExporter())
  .WithMetrics(metrics => metrics
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddRuntimeInstrumentation()
    .AddMeter(SourceName)
    .AddMeter("*Microsoft.Agents.AI")
    .AddOtlpExporter());

builder.Services.AddSingleton(new ActivitySource(SourceName));
```

## Update Agent to Use ForJsonSchema<List<Entity>>

```csharp
builder.Services.AddSingleton<AIAgent>(sp =>
{
  IChatClient chatClient = sp.GetRequiredService<IChatClient>();

  return chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "ObservableEntityAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "EntitiesAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntityTypesAsync, "load_entities_ontology")],
      ToolMode = ChatToolMode.RequireAny,
      ResponseFormat = ChatResponseFormat.ForJsonSchema<List<Entity>>()
    }
  });
});
```

## Add Custom Telemetry to Endpoint

```csharp
app.MapPost("/extract", async (string request, AIAgent agent, ActivitySource activitySource, CancellationToken cancellationToken) =>
{
  using Activity? activity = activitySource.StartActivity("Entity extraction request");
  activity?.SetTag("entities.agent.name", agent.Name);

  AgentResponse response = await agent.RunAsync(new ChatMessage(ChatRole.User, request), cancellationToken: cancellationToken);

  int entityCount = JsonDocument.Parse(response.Text).RootElement
    .GetProperty("entities").GetArrayLength();
  activity?.SetTag("entities.count", entityCount);
  activity?.SetTag("entities.types", string.Join(", ", JsonDocument.Parse(response.Text).RootElement
    .GetProperty("entities").EnumerateArray()
    .Select(e => e.GetProperty("type").GetString())));

  return Results.Ok(response.Text);
});
```

## Run with Aspire Dashboard

The `AddOtlpExporter()` sends telemetry to `http://localhost:4317` by default. When running under Aspire, the dashboard URL is injected automatically via the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.

To run standalone with Aspire Dashboard:

```bash
docker run --rm -it -p 18888:18888 -p 4317:18889 --name aspire-dashboard \
  mcr.microsoft.com/dotnet/aspire-dashboard:10.0
```

Then navigate to `http://localhost:18888` to view traces with custom tags like `entities.count` and `entities.types`.

## Teaching Points

- OpenTelemetry provides automatic instrumentation for HTTP, ASP.NET Core, runtime metrics
- Agent Framework emits traces for agent invocations and tool calls automatically
- Custom `ActivitySource` lets you add business-specific spans and tags
- Custom tags (`entities.count`, `entities.types`) make traces searchable and actionable
- Aspire Dashboard (or any OTLP-compatible backend) visualizes the full request flow
- In production, export to Azure Monitor, Prometheus, Jaeger, or other observability platforms
