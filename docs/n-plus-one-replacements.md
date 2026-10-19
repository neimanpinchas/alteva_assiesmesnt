# N+1 and In-Memory Aggregation: Replacements

Companion to [code-review.md](code-review.md). Each section names the current code, why it is a problem, and the replacement. EF Core 8 + Pomelo/MySQL. Snippets are proposals, not yet applied or tested.

Common prerequisites:
- Persist `Pages.ByteSize` (needed for real bytes saved, D6).
- Keep `JobPageLinks (JobId, TargetPageId)` indexed (exists). Add a unique key on `(JobId, SourcePageId, TargetPageId)` for idempotency and to make counts exact.
- Page and link IDs stay integer (`long` or `int`); the job ID is the only string key.

## 1. `GetJobHistory`: two queries per job

**Now:** for each of N jobs, load all distinct target page IDs into a list, then `Pages WHERE Id IN (list)` and count in memory. `1 + 2N` queries, unbounded `IN` lists.

**Replace:** one grouped query for the page of job IDs, via a reusable `GetJobDataBatch` (D7). `GetJob` calls it with a single ID.

```csharp
public record JobStats(string JobId, int PageCount, int InternalCount, int LinkCount);

public async Task<Dictionary<string, JobStats>> GetJobDataBatchAsync(
    IReadOnlyCollection<Job> jobs, CancellationToken ct)
{
    var ids = jobs.Select(j => j.Id).ToList();
    var rootHash = jobs.ToDictionary(j => j.Id, j => UrlNormalizer.ComputeDomainHash(UrlNormalizer.ExtractDomain(j.Url)));

    // distinct (job, target page) pairs joined once to Pages
    var rows = await (
        from l in _db.JobPageLinks
        where ids.Contains(l.JobId)
        select new { l.JobId, l.TargetPageId }).Distinct()
        .Join(_db.Pages, x => x.TargetPageId, p => p.Id,
              (x, p) => new { x.JobId, p.DomainHash })
        .GroupBy(x => x.JobId)
        .Select(g => new { JobId = g.Key, Pages = g.Count(),
                           Domains = g.Select(x => x.DomainHash).ToList() })
        .ToListAsync(ct);
    ...
}
```

Simpler and fully in SQL: store `Jobs.RootDomainHash` when the job is created, then compare in SQL:

```sql
SELECT l.JobId,
       COUNT(DISTINCT l.TargetPageId)                                   AS Pages,
       COUNT(DISTINCT CASE WHEN p.DomainHash = j.RootDomainHash
                           THEN l.TargetPageId END)                     AS InternalPages
FROM JobPageLinks l
JOIN Pages p ON p.Id = l.TargetPageId
JOIN Jobs  j ON j.Id = l.JobId
WHERE l.JobId IN (@ids)          -- the 10 jobs on this page
GROUP BY l.JobId;
```

Result: 2 queries per history page (jobs page + stats), independent of page size. Adding `Jobs.RootDomainHash` also removes the repeated normalise-and-hash code in four places.

## 2. `GetJob` and `GetJobMetrics`: all page IDs and rows pulled into memory

**Now:** `SELECT DISTINCT TargetPageId` into a list, then `Pages WHERE Id IN (list)` into a list, then `Count` in C#. Grows with job size; `IN` list can exceed limits.

**Replace:** aggregate in SQL.

```csharp
var counts = await (
    from l in _db.JobPageLinks.Where(l => l.JobId == id)
    join p in _db.Pages on l.TargetPageId equals p.Id
    group p by p.Id into g          // distinct pages
    select new { Internal = g.First().DomainHash == rootHash })
    .GroupBy(x => x.Internal)
    .Select(g => new { g.Key, Count = g.Count() })
    .ToListAsync(ct);
```

**Page sharing and real bytes saved** (D6), one query, no list round trip:

```sql
SELECT
  COUNT(*)                                             AS Pages,
  SUM(CASE WHEN o.PageId IS NULL THEN 1 ELSE 0 END)    AS UniqueToJob,
  SUM(CASE WHEN o.PageId IS NOT NULL THEN 1 ELSE 0 END) AS Shared,
  COALESCE(SUM(CASE WHEN o.PageId IS NOT NULL THEN p.ByteSize END), 0) AS BytesSaved
FROM (SELECT DISTINCT TargetPageId FROM JobPageLinks WHERE JobId = @id) t
JOIN Pages p ON p.Id = t.TargetPageId
LEFT JOIN (SELECT DISTINCT TargetPageId AS PageId
           FROM JobPageLinks WHERE JobId <> @id) o ON o.PageId = t.TargetPageId;
```

Use `EXISTS (SELECT 1 FROM JobPageLinks x WHERE x.TargetPageId = p.Id AND x.JobId <> @id)` if the optimiser prefers it. `BytesSaved` is the true total size of the shared pages, replacing `sharedPagesCount * 15000`. Caveat to document: "shared" means another job also links the page; a page this job re-downloaded anyway is not saved bandwidth, so only count pages the worker reused. If reuse is recorded (e.g. `JobPageLinks.Reused` bit), sum over that instead.

**Forecast:** replace `totalDiscovered` (edge count) with a distinct unfetched page count:

```sql
SELECT COUNT(DISTINCT l.TargetPageId)
FROM JobPageLinks l JOIN Pages p ON p.Id = l.TargetPageId
WHERE l.JobId = @id AND p.IsCrawled = 0;
```
Then `remaining / pagesPerSecond`.

## 3. Tree: one `Include` query per node (recursive)

**Now:** `BuildTreeNodeAsync` recurses; each node issues a query. A 3-level lazy load with fan-out 50 is about 2,500 queries.

**Replace:** fetch each level in one query, then assemble in memory (3 queries for 3 levels, any fan-out).

```csharp
var level1 = parentIds;                       // e.g. [rootPageId]
var nodes = new Dictionary<long, TreeNodeDto>();
for (int d = 0; d < depth && level1.Count > 0; d++)
{
    var edges = await _db.JobPageLinks
        .Where(l => l.JobId == jobId && level1.Contains(l.SourcePageId))
        .Select(l => new { l.SourcePageId, Target = l.TargetPage! })
        .ToListAsync(ct);
    ...                                        // build TreeNodeDto, attach to parent
    level1 = edges.Select(e => e.Target.Id).Distinct().ToList();
}
```

Per-node link ratio (spec: own outgoing links in start domain / total) in the same query via `GROUP BY SourcePageId`:

```sql
SELECT l.SourcePageId,
       COUNT(*) AS Total,
       SUM(p.DomainHash = @rootHash) AS Internal
FROM JobPageLinks l JOIN Pages p ON p.Id = l.TargetPageId
WHERE l.JobId = @id AND l.SourcePageId IN (@levelIds)
GROUP BY l.SourcePageId;
```

Cycles: track visited IDs so a page linked from two parents is not expanded twice in one response.

## 4. `DeleteJob`: loop of `AnyAsync` and `FirstOrDefault` per page

**Now:** per page, check other-job links, load the page, delete the file, remove the entity. `2P + 1` queries and racy with in-flight events.

**Replace:** compute exclusive pages in one query *before* deleting, then delete in bulk, in one transaction.

```csharp
await using var tx = await _db.Database.BeginTransactionAsync(ct);

var exclusive = await _db.Pages
    .Where(p => _db.JobPageLinks.Any(l => l.JobId == id &&
                    (l.TargetPageId == p.Id || l.SourcePageId == p.Id)))   // includes root page
    .Where(p => !_db.JobPageLinks.Any(l => l.JobId != id &&
                    (l.TargetPageId == p.Id || l.SourcePageId == p.Id)))
    .Select(p => new { p.Id, p.StoragePath })
    .ToListAsync(ct);

await _db.Jobs.Where(j => j.Id == id).ExecuteDeleteAsync(ct);     // links cascade
var ids = exclusive.Select(e => e.Id).ToList();
await _db.Pages.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync(ct);
await tx.CommitAsync(ct);

foreach (var e in exclusive) StoragePathBuilder.TryDelete(e.StoragePath);   // after commit
```

This also fixes the review finding that the root page (source only) was never pruned. Tell the worker to stop the job first (cancel event) so late events cannot recreate rows. For large jobs, chunk `ids` (e.g. 1,000) to avoid huge `IN` lists.

## 5. Consumers: repeated lookups per event

**Now:** `ApiLinkDiscoveredConsumer` runs two `FirstOrDefault` page lookups, an `AnyAsync` for the link, and up to two `SaveChanges` per event; `ApiPageCrawledConsumer` loads the job on every event. Event rate equals link rate.

**Replace:**
- Upsert pages with the unique CRC/URL key: `INSERT ... ON DUPLICATE KEY UPDATE Id = LAST_INSERT_ID(Id)` via `ExecuteSqlInterpolatedAsync`, no prior `SELECT`.
- Upsert the link with the unique `(JobId, SourcePageId, TargetPageId)` key: `INSERT IGNORE`.
- Job counters: `UPDATE Jobs SET TotalPagesCrawled = TotalPagesCrawled + 1, TotalBytesDownloaded = TotalBytesDownloaded + @b WHERE Id = @id` instead of load-modify-save, which also loses updates under concurrency.
- Batch links: have the worker send one `LinksDiscoveredEvent` with a list per page (one message and one multi-row insert instead of one per link).
- Cache job existence in memory for a few seconds (`IMemoryCache`) rather than a query per event.

## 6. Search (`global-search`, `{id}/search`)

**Now:** loads up to 100 (global) or all job pages into memory, computes Hamming in C#. Global search only looks at an arbitrary 100 rows.

**Replace:** compute distance in SQL and let the DB sort and limit. MySQL has `BIT_COUNT`:

```sql
SELECT p.Id, p.Url, p.Domain, BIT_COUNT(p.SimHash ^ @query) AS Distance
FROM Pages p
WHERE p.IsCrawled = 1
ORDER BY Distance
LIMIT 25;
```
Job-scoped: join the distinct `(JobId, TargetPageId)` set instead of an `IN` list. `ulong` parameters need care with Pomelo (`bigint unsigned`); cover it with a test. It is still a full scan, O(N); document that as a limitation and, if needed later, add a bucketed index on SimHash prefix bands.

## 7. Cheap wins

- `GetJob`: `Include(StatusTransitions)` is fine (single join). Replace the separate `CountAsync` for links with the batch stats query.
- History `COUNT(*)` plus page query: keep, but add `AsNoTracking()` to all read endpoints.
- `Skip((page-1)*pageSize)`: clamp `page >= 1` and `pageSize` to e.g. 1-100 (currently unbounded and unvalidated).

## 8. Verification plan (not yet done)

1. Seed 5,000 pages, 20 jobs, shared pages across jobs.
2. Enable EF command logging (`Microsoft.EntityFrameworkCore.Database.Command=Information`) and record **query count per endpoint** before and after.
3. Run `EXPLAIN` on the SQL above; confirm index use on `JobPageLinks (JobId, TargetPageId)` and `(JobId, SourcePageId)`.
4. Add integration tests asserting the counts match the old implementation on the same data.
