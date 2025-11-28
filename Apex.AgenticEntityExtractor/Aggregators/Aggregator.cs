using Apex.AgenticEntityExtractor.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Aggregators;

public class Aggregator
{
    public static List<ChatMessage> AggregateRelationships(IList<List<ChatMessage>> aggregateResults)
    {
        var allRelationships = new List<Relationship>();

        foreach (var result in aggregateResults)
        {
            try
            {
                // Remove markdown code fences
                var text = result.Last().Text.Trim();
                if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                    text = text[7..];
                else if (text.StartsWith("```"))
                    text = text[3..];
                if (text.EndsWith("```"))
                    text = text[..^3];

                var relationships = JsonSerializer.Deserialize<Relationships>(text.Trim());
                if (relationships?.Items != null)
                    allRelationships.AddRange(relationships.Items);
            }
            catch (JsonException) { }
        }

        var uniqueRelationships = allRelationships
            .GroupBy(r => new { r.Source, r.RelationshipType, r.Target })
            .Select(g => g.First())
            .ToList();

        var unifiedRelationships = new Relationships { Items = uniqueRelationships };
        var jsonOutput = JsonSerializer.Serialize(unifiedRelationships, new JsonSerializerOptions { WriteIndented = true });

        return [new ChatMessage(ChatRole.Assistant, $"```json{jsonOutput}```")];
    }

    public static List<ChatMessage> AggregateEntities(IList<List<ChatMessage>> aggregateResults)
    {
        var allEntities = new List<Entity>();

        foreach (var result in aggregateResults)
        {
            try
            {
                // Remove markdown code fences
                var text = result.Last().Text.Trim();
                if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                    text = text[7..];
                else if (text.StartsWith("```"))
                    text = text[3..];
                if (text.EndsWith("```"))
                    text = text[..^3];

                var entities = JsonSerializer.Deserialize<Entities>(text.Trim());
                if (entities?.Items != null)
                    allEntities.AddRange(entities.Items);
            }
            catch (JsonException) { }
        }

        var uniqueEntities = allEntities
            .GroupBy(e => new { e.EntityType, e.EntityValue })
            .Select(g => g.First())
            .ToList();

        var unifiedEntities = new Entities { Items = uniqueEntities };
        var jsonOutput = JsonSerializer.Serialize(unifiedEntities, new JsonSerializerOptions { WriteIndented = true });

        return [new ChatMessage(ChatRole.Assistant, $"```json{jsonOutput}```")];
    }
}