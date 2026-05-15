using System.Text.Json.Serialization;

public class Entity
{
  [JsonPropertyName("id")]
  public string? Id { get; init; }

  [JsonPropertyName("type")]
  public required string Type { get; init; }

  [JsonPropertyName("value")]
  public required string Value { get; init; }
}

public class Entities
{
  [JsonPropertyName("entities")]
  public List<Entity> Items { get; init; } = [];
}
