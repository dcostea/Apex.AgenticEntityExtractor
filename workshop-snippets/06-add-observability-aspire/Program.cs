using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;

const string SourceName = "Workshop.Step06.EntityExtractor";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOpenTelemetry()
  .WithTracing(tracing => tracing
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddSource(SourceName)
    .AddSource("*Microsoft.Agents.AI")
    //.AddOtlpExporter()
    .AddConsoleExporter())
  .WithMetrics(metrics => metrics
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddRuntimeInstrumentation()
    .AddMeter(SourceName)
    .AddMeter("*Microsoft.Agents.AI")
    //.AddOtlpExporter()
    .AddConsoleExporter());

builder.Services.AddSingleton(new ActivitySource(SourceName));

builder.Services.AddSingleton<IChatClient>(sp =>
{
  IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
  return new OpenAIClient(new ApiKeyCredential(configuration["OpenAI:ApiKey"]!))
    .GetChatClient(configuration["OpenAI:SmallerModelId"] ?? "gpt-4.1-mini")
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry(sourceName: SourceName)
    .Build();
});

builder.Services.AddSingleton<AIAgent>(sp =>
{
  IChatClient chatClient = sp.GetRequiredService<IChatClient>();
  ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

  return chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "ObservableEntityAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "EntitiesAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntitiesOntologyAsync, "load_entities_ontology")],
      ToolMode = ChatToolMode.RequireAny,
      ResponseFormat = ChatResponseFormat.ForJsonSchema<Entities>()
    }
  })
  .AsBuilder()
  .UseLogging(loggerFactory)
  .UseOpenTelemetry(SourceName)
  .Build();
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
}

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

app.Run();
