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
  private DistributedCacheEntryOptions? _cacheEntryOptions;

  private DistributedCacheEntryOptions CacheEntryOptions => _cacheEntryOptions ??= new DistributedCacheEntryOptions
  {
    AbsoluteExpirationRelativeToNow = configuration.GetValue<TimeSpan?>("ToolResponseCacheTTL")
  };

  /// <summary>
  /// Returns cached tool output when available; otherwise executes the tool and stores the result.
  /// </summary>
  public async ValueTask<object?> CacheToolResponseAsync(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
  {
    string cacheKey = context.Function.Name;

    byte[]? cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);
    if (cachedBytes != null)
    {
      WorkflowHelper.EnqueueExternalToolEvent($"[CACHE HIT: {agent.Name}] {context.Function.Name}");
      using JsonDocument doc = JsonDocument.Parse(cachedBytes);
      return doc.RootElement.Clone();
    }

    WorkflowHelper.EnqueueExternalToolEvent($"[CACHE MISS: {agent.Name}] {context.Function.Name}");
    object? result = await next(context, cancellationToken);

    if (result != null)
    {
      await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result), CacheEntryOptions, cancellationToken);
      WorkflowHelper.EnqueueExternalToolEvent($"[CACHE STORE: {agent.Name}] {context.Function.Name}");
    }

    return result;
  }
}
