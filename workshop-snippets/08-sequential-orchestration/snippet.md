# 08 - Sequential orchestration

Goal: Introduce orchestration as a pipeline of specialized agents.

## New Package (add to step 07)

```xml
<PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.5.0" />
```

## New Files

### OntologyTools.cs

```csharp
using System.ComponentModel;

internal static class OntologyTools
{
  [Description("Load permitted entity types from ENTITIES ONTOLOGY")]
  public static Task<string> LoadEntitiesOntologyAsync() =>
    File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ontology", "entities-ontology.json"));

  [Description("Load permitted relationship types from RELATIONSHIPS ONTOLOGY")]
  public static Task<string> LoadRelationshipsOntologyAsync() =>
    File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ontology", "relationships-ontology.json"));
}
```

## Build Sequential Workflow

```csharp
using Microsoft.Agents.AI.Workflows;

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

  AIAgent summaryAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "SummaryAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "ReporterAgent.md"))
    }
  });

  return AgentWorkflowBuilder.BuildSequential("SequentialEntityExtraction", [entityAgent, relationshipAgent, summaryAgent]);
});
```

## Register Workflow in DevUI

```csharp
builder.AddWorkflow("SequentialEntityExtraction", (sp, name) =>
{
  Workflow workflow = sp.GetRequiredService<Workflow>();
  return workflow;
}).AddAsAIAgent();
```

## Run Workflow (non-streaming)

```csharp
app.MapPost("/extract/sequential", async (string request, Workflow workflow, CancellationToken cancellationToken) =>
{
  AIAgent workflowAgent = workflow.AsAIAgent("SequentialEntityExtraction");
  AgentResponse response = await workflowAgent.RunAsync(new ChatMessage(ChatRole.User, request), cancellationToken: cancellationToken);
  return Results.Ok(response.Text);
});
```

## Teaching Points

- `AgentWorkflowBuilder.BuildSequential` creates a linear pipeline
- Each agent receives the output of the previous agent as context
- `workflow.AsAIAgent()` wraps the workflow behind the standard `AIAgent` interface
- `RunAsync` returns the final output from the last agent in the pipeline
- DevUI shows all three agent executions in sequence
- Sequential workflows are easy to understand but wait for each step to complete
