using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// Provides an executor that batches received chat messages that it then releases when
/// receiving a <see cref="TurnToken"/>.
/// </summary>
internal sealed class CollectChatMessagesExecutor(string id) : ChatProtocolExecutor(id, declareCrossRunShareable: true), IResettableExecutor
{
    /// <inheritdoc/>
    protected override ValueTask TakeTurnAsync(List<ChatMessage> messages, IWorkflowContext context, bool? emitEvents, CancellationToken cancellationToken = default)
        => context.SendMessageAsync(messages, cancellationToken: cancellationToken);

    ValueTask IResettableExecutor.ResetAsync() => ResetAsync();
}
