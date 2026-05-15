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
  /// Aggregates and deduplicates entity and relationship payloads from concurrent agents and
  /// returns a typed <see cref="ExtractionContext"/> carrying both to the next stage.
  ///
  /// Entities are grouped by <c>type|value</c> (case-insensitive) in a single pass that
  /// simultaneously builds the normalized entity list and an ID remapping used to rewrite
  /// relationship source/target references after duplicates are collapsed.
  /// </summary>
  public static ExtractionContext AggregateRelationships(IList<List<ChatMessage>> aggregateResults)
  {
    List<Entity> rawItems = aggregateResults
      .Select(b => PayloadHelper.TryParseLatestStructuredPayload<Entities>(b))
      .FirstOrDefault(e => e?.Items is { Count: > 0 })
      ?.Items ?? [];

    // Pre-allocate with upper-bound capacity (rawItems.Count before dedup).
    List<Entity> normalizedEntities = new(rawItems.Count);
    Dictionary<string, string> idRemap = new(rawItems.Count, StringComparer.OrdinalIgnoreCase);
    int entityIndex = 0;

    // Iterate GroupBy directly (no intermediate List<IGrouping<...>>).
    // Enumerate each group exactly once to build both collections.
    foreach (IGrouping<string, Entity> group in
      rawItems.GroupBy(e => $"{e.EntityType}|{e.EntityValue}", StringComparer.OrdinalIgnoreCase))
    {
      string newId = $"e{++entityIndex}";
      bool isFirst = true;
      foreach (Entity entity in group)
      {
        if (isFirst)
        {
          normalizedEntities.Add(new Entity { Id = newId, EntityType = entity.EntityType, EntityValue = entity.EntityValue });
          isFirst = false;
        }
        if (entity.Id is not null)
          idRemap[entity.Id] = newId;
      }
    }

    // Capture the original source text from the first User message for downstream debate agents.
    string? sourceText = aggregateResults
      .SelectMany(b => b)
      .FirstOrDefault(m => m.Role == ChatRole.User)
      ?.Text;

    return new ExtractionContext(
      new Entities { Items = normalizedEntities },
      new Relationships { Items = DeduplicateRelationships(aggregateResults, idRemap) },
      sourceText);
  }

  /// <summary>Converts an <see cref="ExtractionContext"/> to labeled JSON <see cref="ChatMessage"/>s for downstream agents.</summary>
  public static List<ChatMessage> ToMessages(ExtractionContext ctx)
  {
    List<ChatMessage> messages = [];
    if (ctx.SourceText is { Length: > 0 })
      messages.Add(new ChatMessage(ChatRole.User, $"sourceText:\n```\n{ctx.SourceText}\n```"));
    if (ctx.Entities.Items?.Count > 0)
      messages.Add(new ChatMessage(ChatRole.User,
        $"entitiesJson:\n```json\n{JsonSerializer.Serialize(ctx.Entities.Items, IndentedOptions)}\n```"));
    if (ctx.Relationships.Items?.Count > 0)
      messages.Add(new ChatMessage(ChatRole.User,
        $"relationshipsJson:\n```json\n{JsonSerializer.Serialize(ctx.Relationships.Items, IndentedOptions)}\n```"));
    return messages;
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  /// <summary>
  /// Deduplicates relationships across branches, remaps source/target IDs using the entity
  /// deduplication map, drops self-referential edges that arise from collapsing duplicates,
  /// and assigns deterministic IDs.
  /// </summary>
  private static List<Relationship> DeduplicateRelationships(
    IList<List<ChatMessage>> branches,
    Dictionary<string, string> idRemap) =>
    [.. branches
      .SelectMany(b => PayloadHelper.TryParseLatestStructuredPayload<Relationships>(b)?.Items ?? [])
      .Select(r => (
        Source: idRemap.GetValueOrDefault(r.Source, r.Source),
        Type:   r.RelationshipType,
        Target: idRemap.GetValueOrDefault(r.Target, r.Target)))
      .Where(r => r.Source != r.Target)
      .GroupBy(r => r)   // ValueTuple has built-in structural equality — no anonymous-type heap allocation
      .Select((g, i) => new Relationship
      {
        Id = $"r{i + 1}",
        Source = g.Key.Source,
        RelationshipType = g.Key.Type,
        Target = g.Key.Target,
      })];
}