using Apex.AgenticEntityExtractor.Models;
using System.Text.Json.Serialization;

namespace Apex.Apex.AgenticEntityExtractor.Models;

public class Entities
{
    [JsonPropertyName("entities")]
    public List<Entity>? Items { get; init; }
}
