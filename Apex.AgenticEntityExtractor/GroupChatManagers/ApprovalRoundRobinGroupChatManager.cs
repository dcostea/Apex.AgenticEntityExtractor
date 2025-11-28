using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.GroupChatManagers;

public class ApprovalRoundRobinGroupChatManager(IReadOnlyList<AIAgent> agents, Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>> terminationFunction)
    : RoundRobinGroupChatManager(agents, shouldTerminateFunc: terminationFunction)
{
    // Property that explicitly accesses the base implementation
    public int CurrentIterationCount { get; set; }

    public new ValueTask<bool> ShouldTerminateAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        return base.ShouldTerminateAsync(messages, cancellationToken);
    }

    public new ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        return base.SelectNextAgentAsync(history, cancellationToken);
    }

    public new ValueTask<IEnumerable<ChatMessage>> UpdateHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        return base.UpdateHistoryAsync(history, cancellationToken);
    }
}
