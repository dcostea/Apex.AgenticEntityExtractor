using System.Text.Json.Serialization;

namespace Apex.AgenticEntityExtractor.Models;

public class RelationshipType
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}