using Microsoft.Agents.AI;

namespace Apex.AgenticEntityExtractor.Builders
{
    public interface IExtractorAgentBuilder
    {
        AIAgent BuildEntitiesAgent();
        AIAgent BuildMermaidAgent();
        AIAgent BuildRelationshipsAgent();
        AIAgent BuildValidationAgent();
    }
}