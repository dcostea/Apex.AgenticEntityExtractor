using Apex.AgenticEntityExtractor.OutputRenderers;
using Apex.AgenticEntityExtractor.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Aggregators;

/// <summary>
/// Merges parallel extractor outputs into deduplicated entity and relationship payloads.
/// </summary>
public class Aggregator
{
  private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

  /// <summary>
  /// Aggregates and deduplicates relationship payloads from concurrent agents and returns a typed
  /// <see cref="ExtractionContext"/> carrying both entities and relationships to the next stage.
  /// </summary>
  public static ExtractionContext AggregateRelationships(IList<List<ChatMessage>> aggregateResults)
  {
    var relationships = DeduplicateRelationships(aggregateResults);

    // Entities were already deduplicated and ID-assigned by NormalizeEntitiesExecutor upstream;
    // every relationship branch received the same normalized list, so just pick it from any branch.
    Entities entities = aggregateResults
      .Select(b => PayloadHelper.TryParseLatestStructuredPayload<Entities>(b))
      .FirstOrDefault(e => e?.Items is { Count: > 0 })
      ?? new Entities { Items = [] };

    return new ExtractionContext(entities, new Relationships { Items = relationships });
  }

  /// <summary>Converts an <see cref="ExtractionContext"/> to labeled JSON <see cref="ChatMessage"/>s for downstream agents.</summary>
  public static List<ChatMessage> ToMessages(ExtractionContext ctx)
  {
    List<ChatMessage> messages = [];
    if (ctx.Entities.Items?.Count > 0)
      messages.Add(new ChatMessage(ChatRole.User,
        $"entitiesJson:\n```json\n{JsonSerializer.Serialize(ctx.Entities.Items, IndentedOptions)}\n```"));
    if (ctx.Relationships.Items?.Count > 0)
      messages.Add(new ChatMessage(ChatRole.User,
        $"relationshipsJson:\n```json\n{JsonSerializer.Serialize(ctx.Relationships.Items, IndentedOptions)}\n```"));
    return messages;
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  /// <summary>Deduplicates relationships across branches and assigns deterministic IDs.</summary>
  private static List<Relationship> DeduplicateRelationships(IList<List<ChatMessage>> branches) =>
    [.. branches
      .Select(b => PayloadHelper.TryParseLatestStructuredPayload<Relationships>(b))
      .Where(c => c is not null)
      .SelectMany(c => c!.Items ?? [])
      .GroupBy(r => new { r.Source, r.RelationshipType, r.Target })
      .Select(g => g.First())
      .Select((r, i) => new Relationship { Id = $"r{i + 1}", Source = r.Source, RelationshipType = r.RelationshipType, Target = r.Target })];
}