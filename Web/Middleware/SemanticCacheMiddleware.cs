using System.Diagnostics;
using System.Text.Json;

namespace Web.Middleware;

public class SemanticCacheMiddleware
{
    private readonly RequestDelegate _next;

    public SemanticCacheMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        context.Response.OnStarting(() =>
        {
            stopwatch.Stop();
            context.Response.Headers["X-Latency-Ms"] = stopwatch.ElapsedMilliseconds.ToString();
            
            // X-Cache-Status is set in the endpoint handler if applicable.
            return Task.CompletedTask;
        });

        await _next(context);
    }
}