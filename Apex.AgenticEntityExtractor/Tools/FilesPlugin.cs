using Apex.AgenticEntityExtractor.Models;
using Apex.Apex.AgenticEntityExtractor.Models;
using System.ComponentModel;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Tools;

internal class FilesPlugin
{
    [Description("Save the extracted entities")]
    public static void SaveEntities([Description("Entities in JSON format to be saved")] Entities entities)
    {
        string filePath = Path.Combine("Data", "Output", "nodes.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(entities));
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"[FILES PLUGIN: SaveEntities] Entities saved {entities?.Items?.Count} to: {filePath}");
        Console.ResetColor();
    }

    [Description("Save the extracted relationships")]
    public static void SaveRelationships([Description("Relationships in JSON format to be saved")] Relationships relationships)
    {
        string filePath = Path.Combine("Data", "Output", "edges.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(relationships));
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"[FILES PLUGIN: SaveRelationships] Relationships saved {relationships?.Items?.Count} to: {filePath}");
        Console.ResetColor();
    }

    //[Description("Save the Mermaid graph")]
    //public static void SaveMermaidGraph([Description("Mermaid graph to be saved")] string mermaid)
    //{
    //    string filePath = Path.Combine(@"..\..\..", "Data", "Output", "mermaid.md");
    //    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    //    var markdown = $"```mermaid\n{mermaid}\n```";
    //    File.WriteAllText(filePath, markdown);
    //    Console.ForegroundColor = ConsoleColor.Blue;
    //    Console.WriteLine($"[FILES PLUGIN: SaveMermaidGraph] Mermaid graph saved to: {filePath}");
    //    Console.ResetColor();
    //}

    [Description("Load the extracted entities")]
    public static List<Entity> LoadEntities()
    {
        string filePath = Path.Combine("Data", "Output", "nodes.json");

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Entities file not found at: {filePath}");
        }

        string jsonContent = File.ReadAllText(filePath);
        var entities = JsonSerializer.Deserialize<Entities>(jsonContent);
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"[FILES PLUGIN: LoadEntities] Loaded {entities?.Items?.Count} entities from: {filePath}");
        Console.ResetColor();

        return entities?.Items ?? [];
    }

    [Description("Load the extracted relationships")]
    public static List<Relationship> LoadRelationships()
    {
        string filePath = Path.Combine("Data", "Output", "edges.json");

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Relationships file not found at: {filePath}");
        }

        string jsonContent = File.ReadAllText(filePath);
        var relationships = JsonSerializer.Deserialize<Relationships>(jsonContent);
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"[FILES PLUGIN: LoadRelationships] Loaded {relationships?.Items?.Count} relationships from: {filePath}");
        Console.ResetColor();

        return relationships?.Items ?? [];
    }
}
