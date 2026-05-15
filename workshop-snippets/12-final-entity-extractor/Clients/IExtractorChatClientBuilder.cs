using Apex.AgenticEntityExtractor.Enums;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Clients;

public interface IExtractorChatClientBuilder
{
  IChatClient BuildChatClient(ChatProvider provider);
  IChatClient BuildOllamaChatClient(string modelName);
}
