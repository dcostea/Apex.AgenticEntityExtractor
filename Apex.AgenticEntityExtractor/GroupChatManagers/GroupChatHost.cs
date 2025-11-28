using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace Apex.AgenticEntityExtractor.GroupChatManagers;

public class GroupChatHost(string id, Dictionary<AIAgent, ExecutorBinding> agentMap, ApprovalRoundRobinGroupChatManager manager)
    : Executor(id), IResettableExecutor
{
    private readonly List<ChatMessage> _pendingMessages = [];

    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder)
    {
        return routeBuilder
            .AddHandler<List<ChatMessage>>((messages, _, __) => _pendingMessages.AddRange(messages))
            .AddHandler<TurnToken>(async (token, context, cancellationToken) =>
            {
                List<ChatMessage> messages = [.. _pendingMessages];
                _pendingMessages.Clear();

                if (!await manager.ShouldTerminateAsync(messages, cancellationToken).ConfigureAwait(false))
                {
                    var filtered = await manager.UpdateHistoryAsync(messages, cancellationToken).ConfigureAwait(false);
                    messages = filtered is null || ReferenceEquals(filtered, messages) ? messages : [.. filtered];

                    if (await manager.SelectNextAgentAsync(messages, cancellationToken).ConfigureAwait(false) is AIAgent nextAgent &&
                        agentMap.TryGetValue(nextAgent, out var executor))
                    {
                        manager.CurrentIterationCount++;
                        await context.SendMessageAsync(messages, executor.Id, cancellationToken).ConfigureAwait(false);
                        await context.SendMessageAsync(token, executor.Id, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                manager = null!;
                await context.YieldOutputAsync(messages, cancellationToken).ConfigureAwait(false);
            });
    }
}