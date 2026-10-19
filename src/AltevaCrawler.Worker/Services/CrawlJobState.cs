namespace AltevaCrawler.Worker.Services;

using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Thread-safe counters and the discovered-URL set of one crawl job.
/// Owns the page-limit rules so they can be unit tested without any HTTP or messaging.
/// </summary>
public sealed class CrawlJobState
{
    private readonly int _maxPagesLimit;
    private readonly HashSet<uint> _discovered = new();
    private int _crawledCount;
    private long _totalBytes;
    private string? _startPageError;

    public CrawlJobState(int maxPagesLimit)
    {
        _maxPagesLimit = maxPagesLimit;
    }

    public int CrawledCount => Volatile.Read(ref _crawledCount);

    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    /// <summary>Why the start page failed, or null when it did not fail (yet).</summary>
    public string? StartPageError => Volatile.Read(ref _startPageError);

    public int DiscoveredCount
    {
        get
        {
            lock (_discovered)
            {
                return _discovered.Count;
            }
        }
    }

    /// <summary>True while fewer pages than the limit have been crawled.</summary>
    public bool HasCapacity => CrawledCount < _maxPagesLimit;

    /// <summary>
    /// Atomically claims one page slot and adds its bytes. Returns false (and claims nothing) when the limit is reached.
    /// </summary>
    public bool TryReserveSlot(long byteSize)
    {
        if (Interlocked.Increment(ref _crawledCount) > _maxPagesLimit)
        {
            Interlocked.Decrement(ref _crawledCount);
            return false;
        }

        Interlocked.Add(ref _totalBytes, byteSize);
        return true;
    }

    /// <summary>Gives back a slot claimed by <see cref="TryReserveSlot"/> (the page could not be stored).</summary>
    public void ReleaseSlot(long byteSize)
    {
        Interlocked.Decrement(ref _crawledCount);
        Interlocked.Add(ref _totalBytes, -byteSize);
    }

    /// <summary>Records a URL hash; returns true only the first time it is seen in this job.</summary>
    public bool TryMarkDiscovered(uint urlCrc)
    {
        lock (_discovered)
        {
            return _discovered.Add(urlCrc);
        }
    }

    public void RecordStartPageError(string message) => Volatile.Write(ref _startPageError, message);
}
