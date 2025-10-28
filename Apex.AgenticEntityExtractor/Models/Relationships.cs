using System.Text.Json.Serialization;

namespace AgenticEntityExtractor.Models;

public class Relationships
{
    [JsonPropertyName("relationships")]
    public List<Relationship>? Items { get; init; }
}
