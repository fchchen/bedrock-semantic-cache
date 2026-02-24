using System.Threading.Channels;

namespace Core.Services;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Func<CancellationToken, Task> workItem);
    ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}

public interface IIngestTaskQueue : IBackgroundTaskQueue { }
public interface ICacheTaskQueue : IBackgroundTaskQueue { }

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _queue;

    public BackgroundTaskQueue(int capacity = 100)
    {
        _queue = Channel.CreateBounded<Func<CancellationToken, Task>>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public async ValueTask EnqueueAsync(Func<CancellationToken, Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        await _queue.Writer.WriteAsync(workItem);
    }

    public async ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}

public class IngestTaskQueue : BackgroundTaskQueue, IIngestTaskQueue
{
    public IngestTaskQueue() : base(100) { }
}

public class CacheTaskQueue : BackgroundTaskQueue, ICacheTaskQueue
{
    public CacheTaskQueue() : base(100) { }
}
