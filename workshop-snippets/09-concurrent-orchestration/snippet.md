# 09 - Concurrent orchestration

Goal: Show fan-out/fan-in when independent extraction tasks can run in parallel.

## Build Concurrent Workflow

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

  AIAgent relationshipAgent1 = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "RelationshipAgent_1",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "RelationshipsAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadRelationshipsOntologyAsync, "load_relationships_ontology")],
      ToolMode = ChatToolMode.RequireAny
    }
  });

  AIAgent relationshipAgent2 = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "RelationshipAgent_2",
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
    "ConcurrentRelationshipExtraction",
    [relationshipAgent1, relationshipAgent2],
    results => [new ChatMessage(ChatRole.Assistant, string.Join("\n\n", results.SelectMany(result => result).Select(message => message.Text)))]);

  return AgentWorkflowBuilder.BuildSequential(
    "PipelineWithConcurrentRelationships",
    [entityAgent, concurrentRelationships.AsAIAgent("ConcurrentRelationships"), summaryAgent]);
});
```

## Register Workflow

```csharp
builder.AddWorkflow("PipelineWithConcurrentRelationships", (sp, name) =>
{
  Workflow workflow = sp.GetRequiredService<Workflow>();
  return workflow;
}).AddAsAIAgent();
```

## Run Workflow

```csharp
app.MapPost("/extract/concurrent", async (ExtractionRequest request, Workflow workflow, CancellationToken cancellationToken) =>
{
  AIAgent workflowAgent = workflow.AsAIAgent("PipelineWithConcurrentRelationships");
  AgentResponse response = await workflowAgent.RunAsync(new ChatMessage(ChatRole.User, request.Text), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});
```

## Teaching Points

- `AgentWorkflowBuilder.BuildConcurrent` runs agents in parallel
- Both relationship agents run simultaneously with the same input
- Aggregation function merges results before passing to summary agent
- Concurrent workflows reduce latency when tasks are independent
- Use two identical agents to demonstrate concurrency (real scenarios would use different specialized agents)
- Nested workflows: outer sequential pipeline contains inner concurrent step
