using Apex.AgenticEntityExtractor.Builders;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// CONFIGURE CHAT BUILDER
builder.Services.AddSingleton<IExtractorChatClientBuilder, ExtractorChatClientBuilder>();
builder.Services.AddChatClient(sp =>
{
    var extractorChatClientBuilder = sp.GetRequiredService<IExtractorChatClientBuilder>();
    if (builder.Configuration.GetValue<bool>("UseSelfHostedOllama")) 
    {
        return extractorChatClientBuilder.BuildOllamaChatClient();
    }
    else 
    {
        return extractorChatClientBuilder.BuildOpenAIChatClient();   
    }
});

// CONFIGURE AGENTS AND WORKFLOWS
builder.Services.AddSingleton<IExtractorAgentBuilder, ExtractorAgentBuilder>();
builder.AddAIAgent("EntitiesAgent", (sp, _) => 
{
    //_logger.LogInformation("Using Single Entities Agent");
    var extractorAgentBuilder = sp.GetRequiredService<IExtractorAgentBuilder>();
    return extractorAgentBuilder.BuildEntitiesAgent();
});
builder.AddAIAgent("RelationshipsAgent", (sp, _) =>
{
    //_logger.LogInformation("Using Single Relationships Agent");
    var extractorAgentBuilder = sp.GetRequiredService<IExtractorAgentBuilder>();
    return extractorAgentBuilder.BuildRelationshipsAgent();
});
builder.AddAIAgent("MermaidAgent", (sp, _) =>
{
    //_logger.LogInformation("Using Single Mermaid Agent");
    var extractorAgentBuilder = sp.GetRequiredService<IExtractorAgentBuilder>();
    return extractorAgentBuilder.BuildMermaidAgent();
});
builder.AddWorkflow("ExtractionWorkflow", (sp, name) =>
{
    List<AIAgent> agents = [
        sp.GetRequiredKeyedService<AIAgent>("EntitiesAgent"),
        sp.GetRequiredKeyedService<AIAgent>("RelationshipsAgent"),
        sp.GetRequiredKeyedService<AIAgent>("MermaidAgent")
    ];
    return AgentWorkflowBuilder.BuildSequential(name, agents);
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
