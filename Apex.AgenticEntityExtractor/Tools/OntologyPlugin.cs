using AgenticEntityExtractor.Models;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;

namespace AgenticEntityExtractor.Tools;

internal class OntologyPlugin
{
    [Description("Load permitted entity types from ENTITIES ONTOLOGY")]
    public static List<EntityType> LoadEntitiesOntology()
    {
        string filePath = Path.Combine("Data", "Ontology", "entities-ontology.json");
        string jsonContent = File.ReadAllText(filePath);
        var entityTypes = JsonSerializer.Deserialize<List<EntityType>>(jsonContent);
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"[ONTOLOGY PLUGIN: LoadEntitiesOntology] Loaded {entityTypes?.Count ?? 0} entity types from: {filePath}");
        Console.ResetColor();

        return entityTypes ?? [];
    }

    [Description("Load permitted relationship types from RELATIONSHIPS ONTOLOGY")]
    public static List<RelationshipType> LoadRelationshipsOntology()
    {
        string filePath = Path.Combine("Data", "Ontology", "relationships-ontology.json");
        string jsonContent = File.ReadAllText(filePath);
        var relationshipTypes = JsonSerializer.Deserialize<List<RelationshipType>>(jsonContent);
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"[ONTOLOGY PLUGIN: LoadRelationshipsOntology] Loaded {relationshipTypes?.Count ?? 0} relationship types from: {filePath}");
        Console.ResetColor();

        return relationshipTypes ?? [];
    }
}
