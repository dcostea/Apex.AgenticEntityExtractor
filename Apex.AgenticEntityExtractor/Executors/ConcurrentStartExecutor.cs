using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace Apex.AgenticEntityExtractor.Executors;

/// <summary>
/// Executor that starts the concurrent processing by sending messages to the agents.
/// </summary>
public class ConcurrentStartExecutor(string executorId) : Executor(executorId)
{
    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder)
    {
        return routeBuilder
            .AddHandler<List<ChatMessage>>(RouteMessages)
            .AddHandler<TurnToken>(RouteTurnTokenAsync);
    }

    private ValueTask RouteMessages(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
    {
        return context.SendMessageAsync(messages, cancellationToken: cancellationToken);
    }

    private ValueTask RouteTurnTokenAsync(TurnToken token, IWorkflowContext context, CancellationToken cancellationToken)
    {
        return context.SendMessageAsync(token, cancellationToken: cancellationToken);
    }
}