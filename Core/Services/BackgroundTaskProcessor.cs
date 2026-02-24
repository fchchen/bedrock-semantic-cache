using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.Services;

public class BackgroundTaskProcessor<TQueue> : BackgroundService where TQueue : IBackgroundTaskQueue
{
    private readonly TQueue _taskQueue;
    private readonly ILogger<BackgroundTaskProcessor<TQueue>> _logger;

    public BackgroundTaskProcessor(TQueue taskQueue, ILogger<BackgroundTaskProcessor<TQueue>> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _taskQueue.DequeueAsync(stoppingToken);

            try
            {
                await workItem(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing background work item.");
            }
        }
    }
}
