using Apex.AgenticEntityExtractor.Helpers;
using Apex.AgenticEntityExtractor.Models;
using System.ComponentModel;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Tools;

/// <summary>
/// Loads ontology data files used by tool-enabled extraction agents.
/// </summary>
internal class OntologyTools
{
  [Description("Load permitted entity types from ENTITIES ONTOLOGY")]
  /// <summary>
  /// Loads allowed entity types from the entities ontology JSON file.
  /// </summary>
  public static async Task<List<EntityType>> LoadEntitiesOntologyAsync()
  {
    string filePath = Path.Combine("Data", "Ontology", "entities-ontology.json");
    string jsonContent = await File.ReadAllTextAsync(filePath);
    var entityTypes = JsonSerializer.Deserialize<List<EntityType>>(jsonContent);
    WorkflowHelper.RecordExternalToolEvent($"[ONTOLOGY: Load Entities] {entityTypes?.Count ?? 0} entities");

    return entityTypes ?? [];
  }

  [Description("Load permitted relationship types from RELATIONSHIPS ONTOLOGY")]
  /// <summary>
  /// Loads allowed relationship types from the relationships ontology JSON file.
  /// </summary>
  public static async Task<List<RelationshipType>> LoadRelationshipsOntologyAsync()
  {
    string filePath = Path.Combine("Data", "Ontology", "relationships-ontology.json");
    string jsonContent = await File.ReadAllTextAsync(filePath);
    var relationshipTypes = JsonSerializer.Deserialize<List<RelationshipType>>(jsonContent);
    WorkflowHelper.RecordExternalToolEvent($"[ONTOLOGY: Load Relationships] {relationshipTypes?.Count ?? 0} relationships");

    return relationshipTypes ?? [];
  }
}
