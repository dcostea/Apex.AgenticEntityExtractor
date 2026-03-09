using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Clients;
using Apex.AgenticEntityExtractor.OutputRenderers;
using Apex.AgenticEntityExtractor.Middleware;
using Apex.AgenticEntityExtractor.Workflows;
using Microsoft.Agents.AI.DevUI;
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

// CONFIGURE WORKFLOW RENDERER (swap implementation to change the UI layer)
builder.Services.AddSingleton<IWorkflowRenderer, SpectreWorkflowRenderer>();
builder.Services.AddSingleton<WorkflowHelper>();

// DEVUI AGENTS REGISTRATION
////builder.AddAIAgent("ExtractorSoloAgent", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildSoloAgent();
////});
////builder.AddAIAgent("EntAgent_1", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildEntitiesAgent("1");
////});
////builder.AddAIAgent("EntAgent_2", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildEntitiesAgent("2");
////});
////builder.AddAIAgent("EntAgent_3", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildEntitiesAgent("3");
////});
////builder.AddAIAgent("RelAgent_1", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildRelationshipsAgent("1");
////});
////builder.AddAIAgent("RelAgent_2", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildRelationshipsAgent("2");
////});
////builder.AddAIAgent("RelAgent_3", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildRelationshipsAgent("3");
////});
////builder.AddAIAgent("MermaidDiagramAgent", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildMermaidDiagramAgent();
////});
////builder.AddAIAgent("MermaidReviewerAgent", (sp, _) =>
////{
////  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
////  return agentBuilder.BuildMermaidReviewerAgent();
////});

// DEVUI WORKFLOWS REGISTRATION
builder.AddWorkflow("PipelineFromConcurrentWorkflows", (sp, name) =>
{
  var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
  return workflowBuilder.BuildHighLevelPatterns(name);
}).AddAsAIAgent();
builder.AddWorkflow("FullCustomWorkflow", (sp, name) =>
{
  var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
  return workflowBuilder.BuildLowLevelFullCustomWorkflow(name);
}).AddAsAIAgent();

// CONFIGURE CONTROLLERS, SWAGGER, AND DEVUI
builder.Services.AddControllers().AddJsonOptions(options =>
{
  options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.AddOpenAIResponses();
builder.AddOpenAIConversations();

var app = builder.Build();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
  app.MapDevUI();
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
