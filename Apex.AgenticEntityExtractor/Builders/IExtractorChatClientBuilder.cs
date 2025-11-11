using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Builders
{
    public interface IExtractorChatClientBuilder
    {
        IChatClient BuildOllamaChatClient();
        IChatClient BuildOpenAIChatClient();
    }
}