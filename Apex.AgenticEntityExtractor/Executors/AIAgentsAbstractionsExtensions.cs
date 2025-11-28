using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

public static class AIAgentsAbstractionsExtensions
{
    public static ChatMessage ToChatMessage(this AgentRunResponseUpdate update) =>
        new()
        {
            AuthorName = update.AuthorName,
            Contents = update.Contents,
            Role = update.Role ?? ChatRole.User,
            CreatedAt = update.CreatedAt,
            MessageId = update.MessageId,
            RawRepresentation = update.RawRepresentation ?? update,
        };

    /// <summary>
    /// Iterates through <paramref name="messages"/> looking for <see cref="ChatRole.Assistant"/> messages and swapping
    /// any that have a different <see cref="ChatMessage.AuthorName"/> from <paramref name="targetAgentName"/> to
    /// <see cref="ChatRole.User"/>.
    /// </summary>
    public static List<ChatMessage>? ChangeAssistantToUserForOtherParticipants(this List<ChatMessage> messages, string targetAgentName)
    {
        List<ChatMessage>? changedMessages = null;
        HashSet<string> pendingToolCallIds = [];

        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];

            // Track tool calls that need responses
            if (message.Role == ChatRole.Assistant)
            {
                var toolCalls = message.Contents.OfType<FunctionCallContent>().ToList();
                foreach (var toolCall in toolCalls)
                {
                    if (!string.IsNullOrEmpty(toolCall.CallId))
                    {
                        pendingToolCallIds.Add(toolCall.CallId);
                    }
                }
            }

            // Remove tool call IDs that have been responded to
            if (message.Role == ChatRole.Tool)
            {
                var toolResults = message.Contents.OfType<FunctionResultContent>().ToList();
                foreach (var result in toolResults)
                {
                    if (!string.IsNullOrEmpty(result.CallId))
                    {
                        pendingToolCallIds.Remove(result.CallId);
                    }
                }
            }

            // Skip messages that are not assistant role or are from the current agent
            if (message.Role != ChatRole.Assistant || message.AuthorName == targetAgentName)
            {
                continue;
            }

            // Skip assistant messages that contain tool calls
            if (message.Contents.OfType<FunctionCallContent>().Any())
            {
                continue;
            }

            // Skip assistant messages while there are pending tool calls (we're in a tool call sequence)
            if (pendingToolCallIds.Count > 0)
            {
                continue;
            }

            // Change assistant to user
            changedMessages ??= [];
            changedMessages.Add(message);
            messages[i] = new ChatMessage(ChatRole.User, message.Contents)
            {
                AuthorName = message.AuthorName,
                AdditionalProperties = message.AdditionalProperties,
                MessageId = message.MessageId,
                CreatedAt = message.CreatedAt,
                RawRepresentation = message.RawRepresentation
            };
        }

        return changedMessages;
    }

    /// <summary>
    /// Undoes changes made by <see cref="ChangeAssistantToUserForOtherParticipants"/> when passed the list of changes
    /// made by that method.
    /// </summary>
    public static void ResetUserToAssistantForChangedRoles(this List<ChatMessage>? roleChanged)
    {
        if (roleChanged is not null)
        {
            foreach (var m in roleChanged)
            {
                m.Role = ChatRole.Assistant;
            }
        }
    }
}