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

// DEVUI AGENTS AND WORKFLOWS REGISTRATION
builder.AddAIAgent("ExtractorAgent", (sp, _) =>
{
    var extractorAgentsBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
    return extractorAgentsBuilder.BuildExtractorAgent();
});
builder.AddAIAgent("EntitiesAgent", (sp, _) =>
{
    var extractorAgentsBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
    return extractorAgentsBuilder.BuildEntitiesAgent();
});
builder.AddAIAgent("RelationshipsAgent", (sp, _) =>
{
    var extractorAgentsBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
    return extractorAgentsBuilder.BuildRelationshipsAgent();
});
builder.AddAIAgent("MermaidDiagramAgent", (sp, _) =>
{
    var extractorAgentsBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
    return extractorAgentsBuilder.BuildMermaidDiagramAgent();
});
builder.AddAIAgent("MermaidReviewerAgent", (sp, _) =>
{
    var extractorAgentsBuilder = sp.GetRequiredService<IExtractorAgentsBuilder>();
    return extractorAgentsBuilder.BuildMermaidReviewerAgent();
});
builder.AddWorkflow("MainWorkflow", (sp, name) =>
{
    var extractorWorkflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
    return extractorWorkflowBuilder.BuildMainWorkflow();
})
.AddAsAIAgent();
builder.AddWorkflow("MainWorkflowWithSubWorkflows", (sp, name) =>
{
    var extractorWorkflowBuilder = sp.GetRequiredService<IExtractorWorkflowBuilder>();
    return extractorWorkflowBuilder.BuildMainWorkflowWithSubWorkflows();
})
.AddAsAIAgent();

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
