namespace Apex.AgenticEntityExtractor.Models;

/// <summary>
/// Carries the combined entity and relationship extraction output from the concurrent relationship
/// stage to the Mermaid refinement stage as a strongly typed inter-stage context.
/// </summary>
public record ExtractionContext(Entities Entities, Relationships Relationships);
