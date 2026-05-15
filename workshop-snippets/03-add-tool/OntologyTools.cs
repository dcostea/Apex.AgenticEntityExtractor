using System.ComponentModel;

internal static class OntologyTools
{
  [Description("Load permitted entity types from ENTITIES ONTOLOGY")]
  public static Task<string> LoadEntityTypesAsync() =>
    File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ontology", "entities-ontology.json"));
}
