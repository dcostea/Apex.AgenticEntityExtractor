using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Helpers;

/// <summary>
/// Helper functions for parsing and locating structured payloads exchanged in chat messages.
/// </summary>
internal static class PayloadHelper
{
  /// <summary>
  /// Scans messages from latest to earliest and returns the first successfully parsed payload matching <paramref name="payloadKind"/>.
  /// </summary>
  public static T? TryParseLatestStructuredPayload<T>(List<ChatMessage> messages, string payloadKind)
  {
    string marker = $"\"{payloadKind}\"";

    for (int i = messages.Count - 1; i >= 0; i--)
    {
      var text = messages[i].Text;
      if (string.IsNullOrWhiteSpace(text))
        continue;

      string normalized = NormalizeJsonPayload(text);
      if (!normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
        continue;

      try
      {
        if (JsonSerializer.Deserialize<T>(normalized) is { } parsed)
          return parsed;
      }
      catch (JsonException) { }
    }

    return default;
  }

  /// <summary>
  /// Removes markdown code-fence wrappers from a JSON payload string.
  /// </summary>
  public static string NormalizeJsonPayload(string text)
  {
    var span = text.AsSpan().Trim();

    if (span.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
      span = span[7..];
    else if (span.StartsWith("```", StringComparison.OrdinalIgnoreCase))
      span = span[3..];

    if (span.EndsWith("```", StringComparison.OrdinalIgnoreCase))
      span = span[..^3];

    return span.Trim().ToString();
  }

  /// <summary>
  /// Checks whether the supplied text includes a fenced mermaid block.
  /// </summary>
  public static bool ContainsMermaidBlock(string text)
    => text.Contains("```mermaid", StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Finds the latest user-role JSON payload message containing the requested payload kind marker.
  /// </summary>
  public static ChatMessage? FindLatestJsonPayloadMessage(IList<List<ChatMessage>> aggregateResults, string payloadKind)
  {
    string marker = $"\"{payloadKind}\"";
    return aggregateResults
      .SelectMany(r => r)
      .FirstOrDefault(m => m.Role == ChatRole.User &&
                           m.Text is { } text &&
                           text.Contains("```json", StringComparison.OrdinalIgnoreCase) &&
                           text.Contains(marker, StringComparison.OrdinalIgnoreCase));
  }
}
