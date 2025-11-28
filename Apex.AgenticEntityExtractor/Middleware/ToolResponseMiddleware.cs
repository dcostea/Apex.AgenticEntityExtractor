using Apex.AgenticEntityExtractor.Helpers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Apex.AgenticEntityExtractor.Middleware;

public class ToolResponseMiddleware(IDistributedCache cache, IConfiguration configuration) : IToolResponseMiddleware
{
    public async ValueTask<object?> CacheMiddleware(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
    {
        var cacheKey = context.Function.Name;

        var cachedBytes = await cache.GetAsync(cacheKey, cancellationToken);
        // Check for cached result
        if (cachedBytes != null)
        {
            ConsoleHelper.PrintColoredLine($"[{agent.Name}] Function: {context.Function.Name} - Cache HIT", ConsoleColor.DarkCyan);

            using var doc = JsonDocument.Parse(cachedBytes);
            var cachedResult = doc.RootElement.Clone();

            return cachedResult;
        }

        // Execute function
        var result = await next(context, cancellationToken);

        // Cache the result value
        if (result != null)
        {
            var resultBytes = JsonSerializer.SerializeToUtf8Bytes(result);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = configuration.GetValue<TimeSpan?>("CacheTTL")
            };
            await cache.SetAsync(cacheKey, resultBytes, options, cancellationToken);
            ConsoleHelper.PrintColoredLine($"[{agent.Name}] Function: {context.Function.Name} - Cached result", ConsoleColor.DarkCyan);
        }

        return result;
    }
}
