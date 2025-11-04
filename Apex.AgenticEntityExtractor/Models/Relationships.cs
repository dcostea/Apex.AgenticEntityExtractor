using System.Text.Json.Serialization;

namespace Apex.AgenticEntityExtractor.Models;

public class Relationships
{
    [JsonPropertyName("relationships")]
    public List<Relationship>? Items { get; init; }
}
