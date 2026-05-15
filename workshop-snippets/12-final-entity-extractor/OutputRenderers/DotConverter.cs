using Apex.AgenticEntityExtractor.Models;
using System.Text;

namespace Apex.AgenticEntityExtractor.OutputRenderers;

/// <summary>
/// Converts an <see cref="ExtractionContext"/> (entities + relationships) to a
/// Graphviz DOT string suitable for rendering with Graphviz, the VS Code
/// "Graphviz Interactive Preview" extension, or any online renderer (e.g. viz-js.com).
/// </summary>
internal static class DotConverter
{
  // fillcolor per entity type; unmapped types fall back to white.
  private static readonly Dictionary<string, string> TypeColors = new(StringComparer.OrdinalIgnoreCase)
  {
    ["person"]       = "lightyellow",
    ["organization"] = "lightblue",
    ["event"]        = "lightgreen",
    ["location"]     = "peachpuff",
    ["temporal"]     = "lavender",
  };

  /// <summary>Converts an <see cref="ExtractionContext"/> to a DOT graph string.</summary>
  public static string Convert(ExtractionContext ctx) =>
    Convert(ctx.Entities, ctx.Relationships);

  /// <summary>Converts separate <see cref="Entities"/> and <see cref="Relationships"/> to a DOT graph string.</summary>
  public static string Convert(Entities entities, Relationships relationships)
  {
    StringBuilder sb = new();

    sb.AppendLine("digraph KnowledgeGraph {");
    sb.AppendLine("  rankdir=TB");
    sb.AppendLine("  node [shape=box style=filled fontname=\"Helvetica\" fontsize=11]");
    sb.AppendLine("  edge [fontname=\"Helvetica\" fontsize=10]");
    sb.AppendLine();

    foreach (Entity entity in entities.Items ?? [])
    {
      string label  = Escape($"{entity.EntityType}: {entity.EntityValue}");
      string color  = TypeColors.GetValueOrDefault(entity.EntityType ?? string.Empty, "white");
      sb.AppendLine($"  {entity.Id} [label=\"{label}\" fillcolor={color}]");
    }

    sb.AppendLine();

    foreach (Relationship rel in relationships.Items ?? [])
      sb.AppendLine($"  {rel.Source} -> {rel.Target} [label=\"{Escape(rel.RelationshipType)}\"]");

    sb.AppendLine("}");
    return sb.ToString();
  }

  // Escapes characters that are special inside a DOT quoted string.
  private static string Escape(string? text) =>
    (text ?? string.Empty)
      .Replace("\\", "\\\\")
      .Replace("\"", "\\\"");
}
