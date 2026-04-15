using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Clients;
using Apex.AgenticEntityExtractor.OutputRenderers;
using Apex.AgenticEntityExtractor.Middleware;
using Apex.AgenticEntityExtractor.Workflows;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Apex.AgenticEntityExtractor.Enums;

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

builder.AddDevUI();

// DEVUI AGENTS REGISTRATION
builder.AddAIAgent("ExtractorSoloAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  var configuration = sp.GetRequiredService<IConfiguration>();
  return agentBuilder.BuildSoloAgent(configuration.GetValue<ChatProvider>("Provider"));
});
builder.AddAIAgent("EntAgent_1", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  var configuration = sp.GetRequiredService<IConfiguration>();
  return agentBuilder.BuildEntitiesAgent("1", configuration.GetValue<ChatProvider>("Provider"));
});
builder.AddAIAgent("RelAgent_1", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  var configuration = sp.GetRequiredService<IConfiguration>();
  return agentBuilder.BuildRelationshipsAgent("1", configuration.GetValue<ChatProvider>("Provider"));
});
builder.AddAIAgent("RelAgent_2", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  var configuration = sp.GetRequiredService<IConfiguration>();
  return agentBuilder.BuildRelationshipsAgent("2", configuration.GetValue<ChatProvider>("Provider"));
});
builder.AddAIAgent("MermaidBuilderAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  var configuration = sp.GetRequiredService<IConfiguration>();
  return agentBuilder.BuildMermaidBuilderAgent(configuration.GetValue<ChatProvider>("Provider"));
});
builder.AddAIAgent("MermaidRefinerAgent", (sp, _) =>
{
  var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
  var configuration = sp.GetRequiredService<IConfiguration>();
  return agentBuilder.BuildMermaidRefinerAgent(configuration.GetValue<ChatProvider>("Provider"));
});

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
