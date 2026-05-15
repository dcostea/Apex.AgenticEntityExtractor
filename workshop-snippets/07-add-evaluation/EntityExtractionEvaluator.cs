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
