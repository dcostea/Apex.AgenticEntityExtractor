using Apex.AgenticEntityExtractor.Models;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Helpers;

/// <summary>
/// Helper for building chat messages from extraction requests.
/// </summary>
internal static class MessageHelper
{
  /// <summary>
  /// Builds the user message from request text and optional image input.
  /// </summary>
  internal static async Task<ChatMessage> BuildUserMessageAsync(ExtractionRequest request)
  {
    var input = request.InputText ?? await System.IO.File.ReadAllTextAsync(Path.Combine("Data", "Input", "input.txt"));
    var query = $"""
      ## CONTEXT
      Input:
      ```
      {input}
      ```
      """;

    byte[] imageBytes;
    string contentType;
    if (request.InputImage is { Length: > 0 })
    {
      int length = (int)request.InputImage.Length;
      imageBytes = new byte[length];
      using var stream = request.InputImage.OpenReadStream();
      await stream.ReadExactlyAsync(imageBytes);
      contentType = request.InputImage.ContentType;
    }
    else
    {
      imageBytes = await System.IO.File.ReadAllBytesAsync(Path.Combine("Data", "Input", "input.png"));
      contentType = "image/png";
    }

    return new ChatMessage(ChatRole.User,
    [
      new TextContent(query),
      new DataContent(imageBytes, contentType)
    ]);
  }
}
