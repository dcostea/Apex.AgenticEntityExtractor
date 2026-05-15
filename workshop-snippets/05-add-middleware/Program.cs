using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<ChatClientFactory>();
builder.Services.AddSingleton<ToolResponseMiddleware>();

builder.Services.AddSingleton<IChatClient>(sp =>
{
  IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
  ChatProvider provider = configuration.GetValue<ChatProvider>("Provider");
  ChatClientFactory factory = sp.GetRequiredService<ChatClientFactory>();
  return factory.Create(provider);
});

builder.Services.AddSingleton<AIAgent>(sp =>
{
  IChatClient chatClient = sp.GetRequiredService<IChatClient>();
  ToolResponseMiddleware middleware = sp.GetRequiredService<ToolResponseMiddleware>();

  return chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "CachedToolEntityAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "EntitiesAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntityTypesAsync, "load_entities_ontology")],
      ToolMode = ChatToolMode.RequireAny,
      ResponseFormat = ChatResponseFormat.ForJsonSchema<Entities>()
    }
  })
  .AsBuilder()
  .Use(middleware.CacheToolResponseAsync)
  .Build();
});

builder.AddDevUI();
builder.AddAIAgent("CachedToolEntityAgent", (sp, _) => sp.GetRequiredService<AIAgent>());
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

app.MapPost("/extract", async (string request, AIAgent agent, CancellationToken cancellationToken) =>
{
  AgentResponse response = await agent.RunAsync(new ChatMessage(ChatRole.User, request), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});

app.Run();
