using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Helper functions for parsing and locating structured payloads exchanged in chat messages.
/// </summary>
internal static class PayloadHelper
{
  /// <summary>
  /// Scans messages from latest to earliest and returns the first message that deserializes
  /// successfully as <typeparamref name="T"/>.
  /// Suitable for structured-output agents where the response is guaranteed valid JSON —
  /// no property-name marker is needed to locate the right message.
  /// <para>
  /// <see cref="NormalizeJsonPayload"/> is still applied defensively because some local models
  /// (e.g. Ollama) may wrap structured output in markdown fences despite the response-format
  /// constraint.
  /// </para>
  /// </summary>
  public static T? TryParseLatestStructuredPayload<T>(List<ChatMessage> messages)
  {
    for (int i = messages.Count - 1; i >= 0; i--)
    {
      string? text = messages[i].Text;
      if (string.IsNullOrWhiteSpace(text))
        continue;

      string normalized = NormalizeJsonPayload(text);
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
  private static string NormalizeJsonPayload(string text)
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
  /// Extracts the last fenced ```mermaid``` block from the supplied text.
  /// Returns <c>null</c> if no block is found.
  /// </summary>
  /// <remarks>
  /// Small language models may produce multiple mermaid blocks in a single response.
  /// This method ensures downstream consumers only see the final (most corrected) block.
  /// </remarks>
  public static string? ExtractLastMermaidBlock(string text)
  {
    const string openFence = "```mermaid";
    const string closeFence = "```";

    int lastOpen = text.LastIndexOf(openFence, StringComparison.OrdinalIgnoreCase);
    if (lastOpen < 0)
      return null;

    int contentStart = lastOpen + openFence.Length;
    int closeIndex = text.IndexOf(closeFence, contentStart, StringComparison.OrdinalIgnoreCase);
    if (closeIndex < 0)
      return null;

    string content = text[contentStart..closeIndex].Trim();
    return $"```mermaid\n{content}\n```";
  }
}
