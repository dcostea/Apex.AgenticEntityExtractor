namespace Apex.AgenticEntityExtractor.Enums;

/// <summary>
/// Identifies the available AI chat providers.
/// The enum member names must match the appsettings section keys
/// so that can resolve the correct configuration block.
/// </summary>
public enum ChatProvider
{
  Ollama,
  OpenAI,
  Smaller_OpenAI,
  AzureOpenAI,
  Anthropic
}
