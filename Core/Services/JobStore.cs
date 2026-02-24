using Core.Entities;
using System.Collections.Concurrent;

namespace Core.Services;

public class JobStore
{
    private readonly ConcurrentDictionary<string, IngestJob> _jobs = new();
    private readonly ConcurrentQueue<string> _jobOrder = new();
    private const int MaxJobs = 1000;

    public void AddOrUpdate(IngestJob job)
    {
        if (!_jobs.ContainsKey(job.Id))
        {
            _jobOrder.Enqueue(job.Id);
            
            // Basic eviction to prevent memory leak
            while (_jobOrder.Count > MaxJobs)
            {
                if (_jobOrder.TryDequeue(out var oldJobId))
                {
                    _jobs.TryRemove(oldJobId, out _);
                }
            }
        }
        _jobs[job.Id] = job;
    }

    public IngestJob? GetJob(string id)
    {
        _jobs.TryGetValue(id, out var job);
        return job;
    }
}