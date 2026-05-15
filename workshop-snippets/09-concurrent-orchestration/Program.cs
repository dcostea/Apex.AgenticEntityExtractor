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

  AIAgent summaryAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "SummaryAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "ReporterAgent.md"))
    }
  });

  Workflow concurrentRelationships = AgentWorkflowBuilder.BuildConcurrent(
    "ConcurrentRelationshipExtractionWorkflow",
    [firstRelationshipAgent, secondRelationshipAgent],
    results => [new ChatMessage(ChatRole.Assistant, string.Join("\n\n", results.SelectMany(result => result).Select(message => message.Text)))]);

  return AgentWorkflowBuilder.BuildSequential(
    "SequentialConcurrentRelationshipsWorkflow",
    [entityAgent, concurrentRelationships.AsAIAgent("ConcurrentRelationshipWorkflowAsAgent"), summaryAgent]);
});

builder.AddDevUI();
builder.AddWorkflow("SequentialConcurrentRelationshipsWorkflow", (sp, name) =>
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

app.MapPost("/extract/concurrent", async (string request, Workflow workflow, CancellationToken cancellationToken) =>
{
  AIAgent workflowAgent = workflow.AsAIAgent("SequentialConcurrentRelationshipsWorkflowAsAgent");
  AgentResponse response = await workflowAgent.RunAsync(new ChatMessage(ChatRole.User, request), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});

app.Run();
