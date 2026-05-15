using System.Text.Json.Serialization;
using Apex.AgenticEntityExtractor.Enums;

namespace Apex.AgenticEntityExtractor.Models;

/// <summary>
/// Structured output model for debate agent responses.
/// Only the <see cref="Verdict"/> field is machine-parsed — the <see cref="Insights"/>
/// field contains free-text prose that is forwarded directly to the user.
/// This hybrid approach gives reliable termination detection without
/// forcing the creative output into rigid JSON structures.
/// </summary>
public class DebateResponse
{
  /// <summary>Free-text ranked insights (numbered list, natural language).</summary>
  [JsonPropertyName("insights")]
  public required string Insights { get; init; }

  /// <summary>Machine-readable verdict for termination logic.</summary>
  [JsonPropertyName("verdict")]
  [JsonConverter(typeof(JsonStringEnumConverter<DebateVerdict>))]
  public required DebateVerdict Verdict { get; init; }

  /// <summary>One-sentence reason (required when Rejected, optional when Approved).</summary>
  [JsonPropertyName("reason")]
  public string? Reason { get; init; }
}
