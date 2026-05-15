using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

public sealed class ToolResponseMiddleware(IDistributedCache cache)
{
  public async ValueTask<object?> CacheToolResponseAsync(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
  {
    string cacheKey = $"tool:{context.Function.Name}";
    byte[]? cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);

    if (cachedBytes is not null)
    {
      Console.WriteLine($"[CACHE HIT: {agent.Name}] {context.Function.Name}");
      using JsonDocument document = JsonDocument.Parse(cachedBytes);
      return document.RootElement.Clone();
    }

    Console.WriteLine($"[CACHE MISS: {agent.Name}] {context.Function.Name}");
    object? result = await next(context, cancellationToken);

    if (result is not null)
    {
      await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(result), cancellationToken);
    }

    return result;
  }
}
