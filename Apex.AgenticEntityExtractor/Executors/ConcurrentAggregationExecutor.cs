using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// Executor that aggregates the results from the concurrent agents.
/// </summary>
public class ConcurrentAggregationExecutor(string executorId, int numberOfConcurrentAgents) : Executor<List<ChatMessage>>(executorId)
{
    private readonly List<ChatMessage> _messages = [];

    public override async ValueTask HandleAsync(List<ChatMessage> message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _messages.AddRange(message);

        // Wait for all "n" agents to respond
        if (_messages.Count == numberOfConcurrentAgents)
        {
            // Aggregate and deduplicate the results
            var aggregatedText = AggregateAndDeduplicateResults(_messages);

            // Create a clean message with aggregated results
            var aggregatedMessage = new ChatMessage(ChatRole.Assistant, aggregatedText);

            // Yield the aggregated result
            ////await context.YieldOutputAsync(new List<ChatMessage> { aggregatedMessage }, cancellationToken);
            await context.YieldOutputAsync(aggregatedText, cancellationToken);
        }
    }

    private static string AggregateAndDeduplicateResults(List<ChatMessage> messages)
    {
        var allItems = new HashSet<string>();
        var jsonObjects = new List<string>();

        foreach (var msg in messages)
        {
            var text = msg.Text.Trim();

            // Remove markdown code fences
            if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                text = text[7..];
            else if (text.StartsWith("```"))
                text = text[3..];
            if (text.EndsWith("```"))
                text = text[..^3];

            text = text.Trim();

            // Parse and collect unique items based on content
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(text);
                if (jsonDoc.RootElement.TryGetProperty("entities", out var entitiesArray))
                {
                    foreach (var entity in entitiesArray.EnumerateArray())
                    {
                        // Create a unique key from type and value
                        var type = entity.GetProperty("type").GetString();
                        var value = entity.GetProperty("value").GetString();
                        var entityKey = $"{type}:{value}";

                        if (allItems.Add(entityKey))
                        {
                            jsonObjects.Add(entity.GetRawText());
                        }
                    }
                }
                else if (jsonDoc.RootElement.TryGetProperty("relationships", out var relationshipsArray))
                {
                    foreach (var rel in relationshipsArray.EnumerateArray())
                    {
                        // Create a unique key from source, relationship, target
                        var source = rel.GetProperty("source").GetString();
                        var relType = rel.GetProperty("relationship").GetString();
                        var target = rel.GetProperty("target").GetString();
                        var relKey = $"{source}:{relType}:{target}";

                        if (allItems.Add(relKey))
                        {
                            jsonObjects.Add(rel.GetRawText());
                        }
                    }
                }
            }
            catch
            {
                // If parsing fails, skip this message
                continue;
            }
        }

        // Determine if we're dealing with entities or relationships
        var isEntities = messages.Any(m => m.Text.Contains("\"entities\"", StringComparison.OrdinalIgnoreCase));
        var key = isEntities ? "entities" : "relationships";

        // Reconstruct the JSON with unique items and sequential ID
        var items = jsonObjects.Select((item, index) =>
        {
            var doc = System.Text.Json.JsonDocument.Parse(item);
            var root = doc.RootElement;
            var newId = isEntities ? $"e{index + 1}" : $"r{index + 1}";

            // Rebuild the JSON object with the new ID
            var props = new List<string>
            {
                    $"\"id\": \"{newId}\""
            };

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name != "id")
                {
                    var value = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? $"\"{prop.Value.GetString()}\""
                        : prop.Value.GetRawText();
                    props.Add($"\"{prop.Name}\": {value}");
                }
            }

            return "    {\n      " + string.Join(",\n      ", props) + "\n    }";
        });

        return $"{{\n  \"{key}\": [\n{string.Join(",\n", items)}\n  ]\n}}";
    }
}
