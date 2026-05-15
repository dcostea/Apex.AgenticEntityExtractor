# 07 - Add evaluation

Goal: Make quality measurable instead of relying on subjective inspection.

## New Packages (add to step 06)

```xml
<PackageReference Include="Microsoft.Extensions.AI.Evaluation" Version="10.0.0-preview.1.25559.3" />
```

## New Files

### Entities.cs (same as step 03)

```csharp
using System.Text.Json.Serialization;

public sealed class Entity
{
  [JsonPropertyName("id")]
  public string? Id { get; init; }

  [JsonPropertyName("type")]
  public required string Type { get; init; }

  [JsonPropertyName("value")]
  public required string Value { get; init; }
}

public sealed class Entities
{
  [JsonPropertyName("entities")]
  public List<Entity> Items { get; init; } = [];
}
```

### EntityExtractionEvaluator.cs

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using System.Text.Json;

public sealed class EntityExtractionEvaluator : IEvaluator
{
  public IReadOnlyCollection<string> EvaluationMetricNames => ["ValidJson", "EntityCount"];

  public ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse, ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null, CancellationToken cancellationToken = default)
  {
    try
    {
      Entities? entities = JsonSerializer.Deserialize<Entities>(modelResponse.Text ?? string.Empty);
      int entityCount = entities?.Items.Count ?? 0;

      BooleanMetric validJsonMetric = new("ValidJson", true, "The response was valid JSON.");
      NumericMetric entityCountMetric = new("EntityCount", entityCount, $"The response contained {entityCount} entities.")
      {
        Interpretation = entityCount > 0
          ? new EvaluationMetricInterpretation(EvaluationRating.Good)
          : new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, reason: "No entities were extracted.")
      };

      return new ValueTask<EvaluationResult>(new EvaluationResult(validJsonMetric, entityCountMetric));
    }
    catch (JsonException ex)
    {
      BooleanMetric validJsonMetric = new("ValidJson", false, ex.Message)
      {
        Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, reason: "The response was not valid JSON.")
      };

      NumericMetric entityCountMetric = new("EntityCount", 0, "Entity count could not be calculated.");
      return new ValueTask<EvaluationResult>(new EvaluationResult(validJsonMetric, entityCountMetric));
    }
  }
}
```

## Register Evaluator

```csharp
builder.Services.AddSingleton<EntityExtractionEvaluator>();
```

## Add Evaluation Endpoint

```csharp
app.MapPost("/extract/evaluate", async (string request, AIAgent agent, EntityExtractionEvaluator evaluator, CancellationToken cancellationToken) =>
{
  List<ChatMessage> messages = [new(ChatRole.User, request)];
  AgentResponse agentResponse = await agent.RunAsync(messages, cancellationToken: cancellationToken);
  ChatResponse response = new([new ChatMessage(ChatRole.Assistant, agentResponse.Text)]);
  EvaluationResult evaluation = await evaluator.EvaluateAsync(messages, response, cancellationToken: cancellationToken);

  return Results.Ok(new
  {
    response = agentResponse.Text,
    metrics = evaluation.Metrics.Select(metric => new
    {
      Name = metric.Key,
      metric.Value.Interpretation?.Rating,
      metric.Value.Interpretation?.Failed,
      metric.Value.Reason
    })
  });
});
```

## Teaching Points

- Evaluation makes quality measurable and repeatable
- `IEvaluator` provides structured metrics (boolean, numeric, categorical)
- Deterministic evaluators (like this one) are fast and predictable
- LLM-as-judge evaluators (not shown) use another model to score quality
- Evaluation metrics drive regression testing, A/B testing, prompt optimization
