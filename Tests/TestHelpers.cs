namespace Tests;

public static class TestHelpers
{
    /// <summary>
    /// Polls a condition until it returns true, or throws after timeout.
    /// Replaces fragile Task.Delay waits in tests.
    /// </summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var effectiveInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(effectiveInterval);
        }

        throw new TimeoutException($"Condition not met within {effectiveTimeout.TotalMilliseconds}ms");
    }
}
