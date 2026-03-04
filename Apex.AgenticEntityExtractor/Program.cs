using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Clients;
using Apex.AgenticEntityExtractor.Middleware;
using Apex.AgenticEntityExtractor.Workflows;
//using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using System.Text;
using System.Text.Json.Serialization;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// CONFIGURE CHAT CLIENT FACTORY
builder.Services.AddSingleton<IExtractorChatClientBuilder, ExtractorChatClientBuilder>();

// CONFIGURE MIDDLEWARE
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<IToolResponseMiddleware, ToolResponseMiddleware>();

// CONFIGURE AGENTS AND WORKFLOWS
builder.Services.AddSingleton<IExtractorAgentsBuilder, ExtractorAgentsBuilder>();
builder.Services.AddSingleton<IExtractorWorkflowBuilder, ExtractorWorkflowBuilder>();

// DEVUI AGENTS REGISTRATION
builder.AddAIAgent("ExtractorSoloAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  return agentBuilder.BuildExtractorAgent();
});
builder.AddAIAgent("EntAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  return agentBuilder.BuildEntitiesAgent();
});
builder.AddAIAgent("RelAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  return agentBuilder.BuildRelationshipsAgent();
});
builder.AddAIAgent("MermaidDiagramAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  return agentBuilder.BuildMermaidDiagramAgent();
});
builder.AddAIAgent("MermaidReviewerAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  return agentBuilder.BuildMermaidReviewerAgent();
});

// DEVUI WORKFLOWS REGISTRATION
builder.AddWorkflow("SequentialPipeline", (sp, name) =>
{
  var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
  return workflowBuilder.BuildSequentialPipeline(name);
}).AddAsAIAgent();
builder.AddWorkflow("PipelineFromConcurrentWorkflows", (sp, name) =>
{
  var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
  return workflowBuilder.BuildPipelineFromConcurrentWorkflows(name);
}).AddAsAIAgent();
builder.AddWorkflow("PipelineFromCustomOrchestrations", (sp, name) =>
{
  var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
  return workflowBuilder.BuildPipelineFromCustomOrchestrations(name);
}).AddAsAIAgent();
builder.AddWorkflow("FullyCustomOrchestratedPipeline", (sp, name) =>
{
  var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
  return workflowBuilder.BuildFullyCustomOrchestratedPipeline(name);
}).AddAsAIAgent();

// CONFIGURE CONTROLLERS, SWAGGER, AND DEVUI
builder.Services.AddControllers().AddJsonOptions(options =>
{
  options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.AddOpenAIResponses();
////builder.AddOpenAIConversations();

var app = builder.Build();

app.MapOpenAIResponses();
////app.MapOpenAIConversations();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
  ////app.MapDevUI();
}

app.UseHttpsRedirection();

app.Lifetime.ApplicationStarted.Register(() =>
{
  foreach (var url in app.Urls)
  {
    Console.WriteLine($"Listening on: {url}/devui");
    Console.WriteLine("Press Ctrl+C to stop the server.");
  }
});

app.Run();
