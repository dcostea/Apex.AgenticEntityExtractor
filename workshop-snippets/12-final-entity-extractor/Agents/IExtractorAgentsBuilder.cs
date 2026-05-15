using Apex.AgenticEntityExtractor.Enums;
using Microsoft.Agents.AI;

namespace Apex.AgenticEntityExtractor.Agents
{
  public interface IExtractorAgentsBuilder
  {
    /// <summary>
    /// Builds the single-pass extractor agent.
    /// </summary>
    AIAgent BuildSoloAgent(ChatProvider? provider = null);

    /// <summary>
    /// Builds an entities extraction agent, optionally suffixing the agent name.
    /// </summary>
    AIAgent BuildEntitiesAgent(string suffix = "", ChatProvider? provider = null);

    /// <summary>
    /// Builds a relationships extraction agent, optionally suffixing the agent name.
    /// </summary>
    AIAgent BuildRelationshipsAgent(string suffix = "", ChatProvider? provider = null);

    /// <summary>
    /// Builds the Reporter agent that argues insights from a narrative perspective.
    /// </summary>
    AIAgent BuildReporterAgent(ChatProvider? provider = null);

    /// <summary>
    /// Builds the Analyst agent that argues insights from a business perspective.
    /// </summary>
    AIAgent BuildAnalystAgent(ChatProvider? provider = null);
  }
}