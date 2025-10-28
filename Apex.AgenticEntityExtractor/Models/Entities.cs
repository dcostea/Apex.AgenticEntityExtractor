using System.Text.Json.Serialization;

namespace AgenticEntityExtractor.Models;

public class Entities
{
    [JsonPropertyName("entities")]
    public List<Entity>? Items { get; init; }
}
