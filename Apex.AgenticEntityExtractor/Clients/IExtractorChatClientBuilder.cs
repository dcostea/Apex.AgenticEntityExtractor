using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Clients
{
    public interface IExtractorChatClientBuilder
    {
        IChatClient BuildOllamaChatClient();
        IChatClient BuildOpenAIChatClient();
        IChatClient BuildAzureOpenAIChatClient();
    }
}