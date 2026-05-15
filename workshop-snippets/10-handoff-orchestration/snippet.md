# 10 - Handoff orchestration

Goal: Show agent-to-agent delegation using the framework's handoff pattern.

## Build Handoff Workflow

```csharp
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

  AIAgent relationshipAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "RelationshipAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "RelationshipsAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadRelationshipsOntologyAsync, "load_relationships_ontology")],
      ToolMode = ChatToolMode.RequireAny
    }
  });

  #pragma warning disable MAAIW001
    return AgentWorkflowBuilder.CreateHandoffBuilderWith(entityAgent)
      .WithHandoffs(entityAgent, [relationshipAgent])
      .Build();
  #pragma warning restore MAAIW001
});
```

## Update OntologyTools.cs (add relationship method)

```csharp
[Description("Load permitted relationship types from RELATIONSHIPS ONTOLOGY")]
public static Task<string> LoadRelationshipsOntologyAsync() =>
  File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ontology", "relationships-ontology.json"));
```

## Update csproj (add relationships file copy)

```xml
<None Update="Data\Instructions\RelationshipsAgent.md">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
<None Update="Data\Ontology\relationships-ontology.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

## Register Workflow

```csharp
builder.AddWorkflow("HandoffEntityExtraction", (sp, name) =>
{
  Workflow workflow = sp.GetRequiredService<Workflow>();
  return workflow;
}).AddAsAIAgent();
```

## Run Workflow

```csharp
app.MapPost("/extract/handoff", async (ExtractionRequest request, Workflow workflow, CancellationToken cancellationToken) =>
{
  AIAgent workflowAgent = workflow.AsAIAgent("HandoffEntityExtraction");
  AgentResponse response = await workflowAgent.RunAsync(new ChatMessage(ChatRole.User, request.Text), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});
```

## Teaching Points

- `AgentWorkflowBuilder.CreateHandoffBuilderWith` creates mesh topology where agents can transfer control
- Framework automatically injects handoff tools into each agent
- Entity agent starts first, then can hand off to relationship agent via tool call
- Context synchronized across all agents automatically
- Handoff pattern useful for routing to specialists or escalation paths
- `#pragma warning disable MAAIW001` suppresses experimental API warning
