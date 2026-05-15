using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
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

builder.Services.AddSingleton<Workflow>(sp =>
{
  IChatClient chatClient = sp.GetRequiredService<IChatClient>();

  AIAgent entityAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "EntityAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "EntitiesAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntitiesOntologyAsync, "load_entities_ontology")],
      ToolMode = ChatToolMode.RequireAny
    }
  });

  AIAgent firstRelationshipAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "FirstRelationshipAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "RelationshipsAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadRelationshipsOntologyAsync, "load_relationships_ontology")],
      ToolMode = ChatToolMode.RequireAny
    }
  });

  AIAgent secondRelationshipAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "SecondRelationshipAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "RelationshipsAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadRelationshipsOntologyAsync, "load_relationships_ontology")],
      ToolMode = ChatToolMode.RequireAny
    }
  });

  FanOutExecutor fanOut = new("RelationshipFanOut");
  AggregatorExecutor aggregator = new("RelationshipAggregator", expectedResults: 2);

  WorkflowBuilder workflowBuilder = new(entityAgent);
  workflowBuilder.AddEdge(entityAgent, fanOut, "EntityToFanOut");
  workflowBuilder.AddFanOutEdge(fanOut, [firstRelationshipAgent, secondRelationshipAgent], "RelationshipFanOutEdge");
  workflowBuilder.AddFanInBarrierEdge([firstRelationshipAgent, secondRelationshipAgent], aggregator, "RelationshipFanInBarrierEdge");
  workflowBuilder.WithOutputFrom(aggregator);
  workflowBuilder.WithName("CustomExecutorWorkflow");
  workflowBuilder.WithOpenTelemetry();

  return workflowBuilder.Build();
});

builder.AddDevUI();
builder.AddWorkflow("CustomExecutorWorkflow", (sp, name) =>
{
  Workflow workflow = sp.GetRequiredService<Workflow>();
  return workflow;
}).AddAsAIAgent();
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

app.MapPost("/extract/custom-workflow", async (ExtractionRequest request, Workflow workflow, CancellationToken cancellationToken) =>
{
  AIAgent workflowAgent = workflow.AsAIAgent("CustomExecutorWorkflow");
  AgentResponse response = await workflowAgent.RunAsync(new ChatMessage(ChatRole.User, request.Text), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});

app.Run();
