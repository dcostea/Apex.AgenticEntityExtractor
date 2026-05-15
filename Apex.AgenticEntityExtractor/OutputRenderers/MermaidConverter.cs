using Apex.AgenticEntityExtractor.Models;
using System.Text;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Converts an <see cref="ExtractionContext"/> (entities + relationships) to a
/// Mermaid JS diagram string suitable for rendering in Markdown, the VS Code
/// "Mermaid Preview" extension, or any online renderer (e.g. mermaid.live).
/// </summary>
internal static class MermaidConverter
{
  /// <summary>Converts an <see cref="ExtractionContext"/> to a Mermaid diagram string.</summary>
  public static string Convert(ExtractionContext ctx) =>
    Convert(ctx.Entities, ctx.Relationships);

  /// <summary>Converts separate <see cref="Entities"/> and <see cref="Relationships"/> to a Mermaid diagram string.</summary>
  public static string Convert(Entities entities, Relationships relationships)
  {
    StringBuilder sb = new();

    sb.AppendLine("graph TD");

    foreach (Entity entity in entities.Items ?? [])
    {
      string label = Escape($"{entity.EntityType}: {entity.EntityValue}");
      sb.AppendLine($"  {entity.Id}[\"{label}\"]");
    }

    sb.AppendLine();

    foreach (Relationship rel in relationships.Items ?? [])
      sb.AppendLine($"  {rel.Source} -->|\"{Escape(rel.RelationshipType)}\"| {rel.Target}");

    return sb.ToString();
  }

  // Escapes characters that are special inside a Mermaid quoted label.
  private static string Escape(string? text) =>
    (text ?? string.Empty)
      .Replace("\"", "&quot;");
}
