using Gallery.Application.Interfaces;
using Gallery.Domain.Enums;

namespace Gallery.Infrastructure.Services;

/// <summary>
/// Background worker that processes thumbnail generation jobs.
/// </summary>
public sealed class ThumbWorker : IDisposable
{
    private readonly IThumbJobStore _jobStore;
    private readonly IMediaItemStore _itemStore;
    private readonly IThumbCache _cache;
    private readonly IThumbGenerator _generator;
    private readonly int _maxConcurrency;

    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    public event EventHandler<ThumbGeneratedEventArgs>? ThumbGenerated;

    public ThumbWorker(
        IThumbJobStore jobStore,
        IMediaItemStore itemStore,
        IThumbCache cache,
        IThumbGenerator generator,
        int maxConcurrency = 2)
    {
        _jobStore = jobStore;
        _itemStore = itemStore;
        _cache = cache;
        _generator = generator;
        _maxConcurrency = maxConcurrency;
    }

    public void Start()
    {
        if (_workerTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _workerTask = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null || _workerTask is null)
        {
            return;
        }

        await _cts.CancelAsync();
        try
        {
            await _workerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _cts.Dispose();
        _cts = null;
        _workerTask = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var job = await _jobStore.DequeueAsync(ct);

                if (job is null)
                {
                    // No work, wait a bit
                    await Task.Delay(500, ct);
                    continue;
                }

                await semaphore.WaitAsync(ct);

                var task = ProcessJobAsync(job, ct)
                    .ContinueWith(_ => semaphore.Release(), CancellationToken.None);

                tasks.Add(task);

                // Clean up completed tasks
                tasks.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ThumbWorker error: {ex.Message}");
                await Task.Delay(1000, ct);
            }
        }

        // Wait for remaining tasks
        await Task.WhenAll(tasks);
    }

    private async Task ProcessJobAsync(Domain.Models.ThumbJob job, CancellationToken ct)
    {
        try
        {
            var item = await _itemStore.GetByIdAsync(job.ItemId, ct);
            if (item is null)
            {
                await _jobStore.MarkCompletedAsync(job.Id, ct);
                return;
            }

            // Check if thumb already exists
            var thumbPath = _cache.GetThumbPath(item.CacheKey, job.Size);
            if (_cache.Exists(thumbPath))
            {
                // Already generated, just update DB
                await _itemStore.UpdateThumbPathAsync(item.Id, job.Size, thumbPath, ct);
                await _jobStore.MarkCompletedAsync(job.Id, ct);
                return;
            }

            // Generate thumbnail
            var maxPixels = (int)job.Size;
            var jpegBytes = await _generator.GenerateImageThumbAsync(item.Path, maxPixels, ct);

            // Write to cache
            await _cache.WriteAsync(thumbPath, jpegBytes, ct);

            // Update item with thumb path
            await _itemStore.UpdateThumbPathAsync(item.Id, job.Size, thumbPath, ct);

            // Mark job complete
            await _jobStore.MarkCompletedAsync(job.Id, ct);

            // Notify listeners
            ThumbGenerated?.Invoke(this, new ThumbGeneratedEventArgs(item.Id, job.Size, thumbPath));
        }
        catch (Exception ex)
        {
            await _jobStore.MarkFailedAsync(job.Id, ex.Message, ct);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public sealed class ThumbGeneratedEventArgs : EventArgs
{
    public long ItemId { get; }
    public ThumbSize Size { get; }
    public string ThumbPath { get; }

    public ThumbGeneratedEventArgs(long itemId, ThumbSize size, string thumbPath)
    {
        ItemId = itemId;
        Size = size;
        ThumbPath = thumbPath;
    }
}
