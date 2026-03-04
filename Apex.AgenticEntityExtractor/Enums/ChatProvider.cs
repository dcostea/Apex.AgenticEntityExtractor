using Apex.AgenticEntityExtractor.Clients;

namespace Apex.AgenticEntityExtractor.Enums;

/// <summary>
/// Identifies the available AI chat providers.
/// The enum member names must match the appsettings section keys
/// so that <see cref="IExtractorChatClientBuilder.GetChatClient"/> can
/// resolve the correct configuration block.
/// </summary>
public enum ChatProvider
{
  Ollama,
  OpenAI,
  AzureOpenAI,
  Anthropic
}
