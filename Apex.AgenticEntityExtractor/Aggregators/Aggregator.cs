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
  /// Aggregates and deduplicates entity payloads from concurrent agent branches.
  /// </summary>
  public static List<ChatMessage> AggregateEntities(IList<List<ChatMessage>> aggregateResults)
  {
    var uniqueEntities = CollectAndDeduplicate<Entities, Entity>(aggregateResults, payloadKind: "entities", container => container.Items,
      e => new { e.EntityType, e.EntityValue });

    // Re-assign sequential IDs — different agents assign conflicting IDs to the same entity.
    var reindexedEntities = uniqueEntities
      .Select((e, i) => new Entity { Id = $"e{i + 1}", EntityType = e.EntityType, EntityValue = e.EntityValue })
      .ToList();

    ChatMessage? originalContext = aggregateResults
      .SelectMany(r => r)
      .FirstOrDefault(m => m.Role == ChatRole.User);

    return BuildOutput(originalContext, new Entities { Items = reindexedEntities });
  }

  /// <summary>
  /// Aggregates and deduplicates relationship payloads from concurrent agent branches.
  /// </summary>
  public static List<ChatMessage> AggregateRelationships(IList<List<ChatMessage>> aggregateResults)
  {
    var uniqueRelationships = CollectAndDeduplicate<Relationships, Relationship>(aggregateResults, payloadKind: "relationships", container => container.Items,
      r => new { r.Source, r.RelationshipType, r.Target });

    // Re-assign sequential IDs — different agents assign conflicting IDs to the same relationship.
    var reindexedRelationships = uniqueRelationships
      .Select((r, i) => new Relationship { Id = $"r{i + 1}", Source = r.Source, RelationshipType = r.RelationshipType, Target = r.Target })
      .ToList();

    ChatMessage? entitiesContext = PayloadHelper.FindLatestJsonPayloadMessage(aggregateResults, "entities");

    return BuildOutput(entitiesContext, new Relationships { Items = reindexedRelationships });
  }

  private static List<TItem> CollectAndDeduplicate<TContainer, TItem>(
    IList<List<ChatMessage>> aggregateResults,
    string payloadKind,
    Func<TContainer, List<TItem>?> itemsSelector,
    Func<TItem, object> deduplicationKeySelector)
  {
    return [.. aggregateResults
      .Select(r => PayloadHelper.TryParseLatestStructuredPayload<TContainer>(r, payloadKind))
      .Where(c => c is not null)
      .SelectMany(c => itemsSelector(c!) ?? [])
      .GroupBy(deduplicationKeySelector)
      .Select(g => g.First())];
  }

  private static List<ChatMessage> BuildOutput<T>(ChatMessage? contextMessage, T payload)
  {
    var jsonOutput = JsonSerializer.Serialize(payload, IndentedOptions);

    var output = new List<ChatMessage>();
    if (contextMessage is not null)
      output.Add(contextMessage);

    output.Add(new ChatMessage(ChatRole.User, $$"""
      ```json
      {{jsonOutput}}
      ```
      """));

    return output;
  }
}