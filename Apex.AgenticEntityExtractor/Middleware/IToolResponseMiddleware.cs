using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Middleware;

public interface IToolResponseMiddleware
{
    ValueTask<object?> CacheMiddleware(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken);
}