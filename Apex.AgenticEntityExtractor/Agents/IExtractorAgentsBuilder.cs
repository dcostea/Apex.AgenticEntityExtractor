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
    /// Builds the mermaid diagram generation agent.
    /// </summary>
    AIAgent BuildMermaidBuilderAgent(ChatProvider? provider = null);

    /// <summary>
    /// Builds the mermaid review/approval agent.
    /// </summary>
    AIAgent BuildMermaidRefinerAgent(ChatProvider? provider = null);
  }
}