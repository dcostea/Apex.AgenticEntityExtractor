using Apex.AgenticEntityExtractor.Agents;
using Apex.AgenticEntityExtractor.Clients;
using Apex.AgenticEntityExtractor.Middleware;
using Apex.AgenticEntityExtractor.Workflows;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// CONFIGURE CHAT BUILDER
builder.Services.AddSingleton<IExtractorChatClientBuilder, ExtractorChatClientBuilder>();
builder.Services.AddChatClient(sp =>
{
    var extractorChatClientBuilder = sp.GetRequiredService<IExtractorChatClientBuilder>();
    return builder.Configuration["Provider"] switch
    {
        "Ollama" => extractorChatClientBuilder.BuildOllamaChatClient(),
        "OpenAI" => extractorChatClientBuilder.BuildOpenAIChatClient(),
        "AzureOpenAI" => extractorChatClientBuilder.BuildAzureOpenAIChatClient(),
        _ => throw new NotSupportedException($"Chat provider '{builder.Configuration["Provider"]}' is not supported.")
    };
});

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
builder.AddAIAgent("EntitiesAgent", (sp, _) =>
{
    var agentBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
    return agentBuilder.BuildEntitiesAgent();
});
builder.AddAIAgent("RelationshipsAgent", (sp, _) =>
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
builder.AddWorkflow("WorkflowFromSequentialWorkflow", (sp, name) =>
{
    var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
    return workflowBuilder.BuildWorkflowFromSequentialWorkflow("WorkflowFromSequentialWorkflow");
}).AddAsAIAgent();
builder.AddWorkflow("WorkflowFromWorkflowsAsAgents", (sp, name) =>
{
    var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
    return workflowBuilder.BuildWorkflowFromWorkflowsAsAgents("WorkflowFromWorkflowsAsAgents");
}).AddAsAIAgent();
builder.AddWorkflow("WorkflowFromSubWorkflows", (sp, name) =>
{
    var workflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
    return workflowBuilder.BuildWorkflowFromSubWorkflows("WorkflowFromSubWorkflows");
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
