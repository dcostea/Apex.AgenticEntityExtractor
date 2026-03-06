using Apex.AgenticEntityExtractor.OutputRenderers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Middleware;

/// <summary>
/// Caches tool invocation results to reduce repeated ontology/file tool calls during workflows.
/// </summary>
public class ToolResponseMiddleware(IDistributedCache cache, IConfiguration configuration) : IToolResponseMiddleware
{
  /// <summary>
  /// Returns cached tool output when available; otherwise executes the tool and stores the result.
  /// </summary>
  public async ValueTask<object?> CacheToolResponseAsync(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
  {
    var cacheKey = context.Function.Name;

    var cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);
    // Return cached result when present.
    if (cachedBytes != null)
    {
      WorkflowHelper.EnqueueExternalToolEvent($"[CACHE HIT: {agent.Name}] {context.Function.Name}");

      using var doc = JsonDocument.Parse(cachedBytes);
      var cachedResult = doc.RootElement.Clone();

      return cachedResult;
    }

    // Execute tool when cache does not contain a value.
    var result = await next(context, cancellationToken);

    // Persist tool result for future calls.
    if (result != null)
    {
      var resultBytes = JsonSerializer.SerializeToUtf8Bytes(result);
      var options = new DistributedCacheEntryOptions
      {
        AbsoluteExpirationRelativeToNow = configuration.GetValue<TimeSpan?>("ToolResponseCacheTTL")
      };
      await cache.SetAsync(cacheKey, resultBytes, options, cancellationToken);
      WorkflowHelper.EnqueueExternalToolEvent($"[CACHE STORE: {agent.Name}] {context.Function.Name}");
    }

    return result;
  }
}
