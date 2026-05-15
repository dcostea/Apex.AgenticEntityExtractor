namespace Apex.AgenticEntityExtractor.Models;

/// <summary>
/// Carries the combined extraction output between workflow stages:
/// entities, relationships, and the original source text for cross-checking.
/// </summary>
public record ExtractionContext(Entities Entities, Relationships Relationships, string? SourceText = null);
