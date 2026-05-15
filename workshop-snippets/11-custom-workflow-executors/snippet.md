# 11 - Custom workflow executors

Goal: Show low-level control with custom executors, explicit edges, and event-driven execution.

## New Package (add to step 10)

```xml
<PackageReference Include="Microsoft.Agents.AI.Workflows.Generators" Version="1.5.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

## New Files

### FanOutExecutor.cs

```csharp
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

[SendsMessage(typeof(List<ChatMessage>))]
[SendsMessage(typeof(TurnToken))]
public partial class FanOutExecutor(string executorId)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private List<ChatMessage> _messages = [];

  [MessageHandler]
  private void HandleMessage(ChatMessage message, IWorkflowContext context)
  {
    _messages.Add(message);
  }

  [MessageHandler]
  private void HandleMessages(List<ChatMessage> messages, IWorkflowContext context)
  {
    _messages.AddRange(messages);
  }

  [MessageHandler]
  private async ValueTask HandleTurnAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
  {
    if (_messages.Count == 0)
    {
      return;
    }

    List<ChatMessage> messages = _messages;
    _messages = [];

    await context.SendMessageAsync(messages, cancellationToken: cancellationToken);
    await context.SendMessageAsync(new TurnToken(emitEvents: token.EmitEvents is true), cancellationToken: cancellationToken);
  }

  public ValueTask ResetAsync()
  {
    _messages = [];
    return default;
  }
}
```

### AggregatorExecutor.cs

```csharp
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

[SendsMessage(typeof(List<ChatMessage>))]
[YieldsOutput(typeof(ChatMessage))]
public partial class AggregatorExecutor(string executorId, int expectedResults)
  : Executor(executorId, declareCrossRunShareable: true), IResettableExecutor
{
  private readonly List<ChatMessage> _messages = [];

  [MessageHandler]
  private async ValueTask HandleMessagesAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
  {
    _messages.AddRange(messages.Where(message => message.Role == ChatRole.Assistant));

    if (_messages.Count < expectedResults)
    {
      return;
    }

    string mergedText = string.Join("\n\n", _messages.Select(message => message.Text));
    ChatMessage output = new(ChatRole.Assistant, mergedText);

    await context.SendMessageAsync(new List<ChatMessage> { output }, cancellationToken: cancellationToken);
    await context.YieldOutputAsync(output, cancellationToken);
  }

  public ValueTask ResetAsync()
  {
    _messages.Clear();
    return default;
  }
}
```

## Build Custom Workflow

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

  FanOutExecutor fanOut = new("RelationshipFanOut");
  AggregatorExecutor aggregator = new("RelationshipAggregator", expectedResults: 2);

  WorkflowBuilder workflowBuilder = new(entityAgent);
  workflowBuilder.AddEdge(entityAgent, fanOut, "EntityToFanOut");
  workflowBuilder.AddFanOutEdge(fanOut, [relationshipAgent1, relationshipAgent2], "RelationshipFanOutEdge");
  workflowBuilder.AddFanInBarrierEdge([relationshipAgent1, relationshipAgent2], aggregator, "RelationshipFanInBarrierEdge");
  workflowBuilder.WithOutputFrom(aggregator);
  workflowBuilder.WithName("CustomExecutorWorkflow");
  workflowBuilder.WithOpenTelemetry();

  return workflowBuilder.Build();
});
```

## Teaching Points

- Custom executors provide full control over message routing and state management
- `[SendsMessage]` and `[YieldsOutput]` attributes declare message contracts
- Source generators create type-safe edge connections
- `FanOutExecutor` duplicates input to multiple downstream executors
- `AggregatorExecutor` waits for N results before proceeding
- Low-level workflow builder for complex orchestration patterns
- Explicit edges create a directed graph that's fully observable and debuggable
