using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using OpenAI;
using System.ClientModel;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<EntityExtractionEvaluator>();

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
    Name = "EvaluatedEntityAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "EntitiesAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntitiesOntologyAsync, "load_entities_ontology")],
      ToolMode = ChatToolMode.RequireAny,
      ResponseFormat = ChatResponseFormat.Json
    }
  });
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
}

app.MapPost("/extract/evaluate", async (string request, AIAgent agent, EntityExtractionEvaluator evaluator, CancellationToken cancellationToken) =>
{
  List<ChatMessage> messages = [new(ChatRole.User, request)];
  AgentResponse agentResponse = await agent.RunAsync(messages, cancellationToken: cancellationToken);
  ChatResponse response = new([new ChatMessage(ChatRole.Assistant, agentResponse.Text)]);
  EvaluationResult evaluation = await evaluator.EvaluateAsync(messages, response, cancellationToken: cancellationToken);

  return Results.Ok(new
  {
    response = agentResponse.Text,
    metrics = evaluation.Metrics.Select(metric => new
    {
      Name = metric.Key,
      metric.Value.Interpretation?.Rating,
      metric.Value.Interpretation?.Failed,
      metric.Value.Reason
    })
  });
});

app.Run();
