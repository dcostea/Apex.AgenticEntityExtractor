using Microsoft.Agents.AI;

namespace Apex.AgenticEntityExtractor.Agents
{
    public interface IExtractorAgentsBuilder
    {
        AIAgent BuildExtractorAgent();
        AIAgent BuildEntitiesAgent(string suffix = "");
        AIAgent BuildRelationshipsAgent(string suffix = "");
        AIAgent BuildMermaidDiagramAgent();
        AIAgent BuildMermaidReviewerAgent();
    }
}