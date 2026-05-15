# 03 - Add a tool

Goal: Show that tools are the bridge from language reasoning to deterministic business logic.

## New Files

### Entities.cs

```csharp
using System.Text.Json.Serialization;

public sealed class Entity
{
  [JsonPropertyName("id")]
  public string? Id { get; init; }

  [JsonPropertyName("type")]
  public required string Type { get; init; }

  [JsonPropertyName("value")]
  public required string Value { get; init; }
}

public sealed class Entities
{
  [JsonPropertyName("entities")]
  public List<Entity> Items { get; init; } = [];
}
```

### OntologyTools.cs

```csharp
using System.ComponentModel;

internal static class OntologyTools
{
  [Description("Load permitted entity types from ENTITIES ONTOLOGY")]
  public static Task<string> LoadEntityTypesAsync() =>
    File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ontology", "entities-ontology.json"));
}
```

## Update Agent Registration (replace existing agent in Program.cs)

```csharp
builder.Services.AddSingleton<AIAgent>(sp =>
{
  IChatClient chatClient = sp.GetRequiredService<IChatClient>();

  return chatClient.AsAIAgent(new ChatClientAgentOptions
  {
    Name = "ToolEnabledEntityAgent",
    ChatOptions = new ChatOptions
    {
      Instructions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "Instructions", "EntitiesAgent.md")),
      Tools = [AIFunctionFactory.Create(OntologyTools.LoadEntityTypesAsync, "load_entities_ontology")],
      ToolMode = ChatToolMode.RequireAny,
      ResponseFormat = ChatResponseFormat.ForJsonSchema<Entities>()
    }
  });
});

builder.AddDevUI();
builder.AddAIAgent("ToolEnabledEntityAgent", (sp, _) => sp.GetRequiredService<AIAgent>());
builder.AddOpenAIResponses();
builder.AddOpenAIConversations();
```

## Data/Ontology/entities-ontology.json

```json
{
  "entity_types": [
    "person",
    "organization",
    "location",
    "event",
    "temporal",
    "product",
    "technology"
  ]
}
```

## Test

```bash
curl -X POST https://localhost:7078/extract \
  -H "Content-Type: application/json" \
  -d "Elena met Dr. Michael Anders at the Amsterdam Tech Conference 2025 on 1 Oct 2025."
```

Expected response format:

```json
{
  "entities": [
    { "id": "e1", "type": "person", "value": "Elena" },
    { "id": "e2", "type": "person", "value": "Dr. Michael Anders" },
    { "id": "e3", "type": "location", "value": "Amsterdam" },
    { "id": "e4", "type": "event", "value": "Amsterdam Tech Conference 2025" },
    { "id": "e5", "type": "temporal", "value": "1 Oct 2025" }
  ]
}
```

## Teaching Points

- `AIFunctionFactory.Create` converts a C# method into a tool the model can call
- `ToolMode.RequireAny` forces the agent to call at least one tool before responding
- `ResponseFormat.ForJsonSchema<T>()` enforces structured output with schema validation
- The model decides **when** to call the tool; the tool itself is predictable C# code
- Tools bridge non-deterministic reasoning with deterministic business logic
- Instructions loaded from external markdown file keep prompts maintainable
