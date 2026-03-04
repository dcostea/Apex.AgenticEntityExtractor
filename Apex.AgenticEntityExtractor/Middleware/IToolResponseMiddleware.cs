using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Apex.AgenticEntityExtractor.Middleware;

public interface IToolResponseMiddleware
{
  /// <summary>
  /// Wraps tool invocation with cache lookup/store behavior for tool responses.
  /// </summary>
  ValueTask<object?> CacheToolResponseAsync(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken);
}