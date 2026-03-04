using Apex.AgenticEntityExtractor.Enums;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Clients;

public interface IExtractorChatClientBuilder
{
  /// <summary>
  /// Returns a cached <see cref="IChatClient"/> for the given provider.
  /// Clients are created lazily on first access and reused thereafter.
  /// </summary>
  IChatClient GetChatClient(ChatProvider provider);
}
