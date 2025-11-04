using System.Text.Json.Serialization;

namespace Apex.AgenticEntityExtractor.Models;

public class Entity
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public required string EntityType { get; init; }

    [JsonPropertyName("value")]
    public required string EntityValue { get; init; }
}
