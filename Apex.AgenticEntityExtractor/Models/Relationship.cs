using System.Text.Json.Serialization;

namespace Apex.AgenticEntityExtractor.Models;

public class Relationship
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("relationship")]
    public required string RelationshipType { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }
}