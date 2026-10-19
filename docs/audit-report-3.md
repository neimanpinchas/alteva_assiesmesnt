# Audit Report 3: Commits after 5fdc37e

Date: 2026-10-19. Reviewed: `b8a369b` (EDGE_CASES.md), `4dcb07f` (TRICKS.md), `44fd0ba` (docker/node fixes), `7303fb2` ("complete section 5 and 6": IStorageService, HAXE.md, OpenAPI, speed metrics, sidebar). Earlier reports: [audit-report.md](audit-report.md), [audit-report-2.md](audit-report-2.md).

How verified:
- **Committed state (`7303fb2`)**: built and tested in a clean worktree: **builds, 31/31 tests pass** (net8.0). Docker is not installed here, so compose was not run.
- **Working tree**: has uncommitted edits in 7 files (a new Swashbuckle package, a rewritten `JobsController`, `Program.cs`, `JobSummaryDto`, `History.tsx`, `CrawlerEngine`). **It does not build right now**: `JobsController.cs:46` calls `Guid.CreateVersion7()`, which does not exist on net8.0. This looks like work in progress; I audit it separately in section 4.
- Runtime behaviour below is reasoned from code unless marked "verified".

## 1. Verdict

Real progress: most report-2 blockers are fixed or reduced. But the cancel path has regressed, a hash field was silently dropped, the new storage code is only half wired, and several docs and decisions are now wrong or lost.

## 2. Report-2 findings: status now

| ID | Finding | Status | Notes |
|---|---|---|---|
| N1 | Shared dedupe cache across jobs | **Fixed (verified in code)** | Per-job `HashSet<uint>` under a lock; still CRC-only (collisions drop URLs), no test for crawling the same site twice |
| N2 | `ByteSize` with `EnsureCreated`, no migration | **Open** | No migrations; `schema.sql` regenerated but still a T-SQL stub (`nvarchar(max)`, `IDENTITY`, `NONCLUSTERED`) that the app does not use |
| N3 | Dockerfiles 8.0 vs net10 projects | **Fixed on paper** | API, Worker, Tests, Shared now all net8.0 (tests use `RollForward=LatestMajor`). Compose not executed |
| N4 | TLS validation disabled | **Fixed** | `Crawler:AllowInvalidCertificates` (default false) in both `appsettings.json` |
| N5 | Failure events overwrite stored data | **Partly fixed** | API guards SimHash/hash/path/size when the new event is a failure. Worker still records errors as `StatusCode=500` pages (see H4) |
| N6 | Cancel/pause/resume gaps | **Partly fixed, one regression** | `Paused='S'` status and transitions added (good). See H1, H6 |
| N7 | Worker `/health` is a stub | **Open** | `AddHealthChecks()` with no checks; the added `using ...HealthChecks` is unused |
| N8 | Absolute storage path / shared storage | **Partly fixed** | Relative path via `IStorageService`, API compose volume added, read checks `IsUnderRoot`. See H8 |
| N9 | Haxe parity | **Partly fixed** | Haxe crypto is now HMAC-SHA256 (good). See H13 |
| N10 | Preview XSS | **Fixed** | API returns plain text (`TextContent`); UI renders it in a `<pre>` with React nodes for highlighting; `dangerouslySetInnerHTML` is gone. Matches decision D4 |
| N11 | `www` side effects | **Open** | Still only a method parameter; no config switch, no data migration |
| N12 | `CalculateDomainLinkRatio` unused in product | **Open** | Still only called by tests |
| D3 | Query-string-safe storage helper | **Done** | `StoragePathBuilder` + 3 tests (see H9 for gaps) |
| D5 | HMAC key from config | **Not done** | `HmacAuthService` and `Program.cs` key handling unchanged |
| D7 | `GetJobData` extraction | **In progress** (working tree only) | |

## 3. New findings in the committed code

### High

**H1. Cancel now faults the consumer, which triggers a retry that restarts the canceled job.**
Report 2's engine had `catch (OperationCanceledException)` around the task body and the job; the rewrite removed all of them. After `CancelJob` fires the job token:
- `HttpClient.GetAsync` throws `OperationCanceledException`, which the broad `catch (Exception)` swallows and then tries to **publish a `StatusCode=500` page event with the canceled token**, which throws again.
- `Task.Run(..., token)` tasks end canceled, so `Task.WhenAll` throws `TaskCanceledException` out of `ProcessJobAsync`.
- The exception reaches `CrawlJobConsumer`. The consumer's own token (`context.CancellationToken`) is not the job token, so MassTransit treats it as a failure and applies `UseMessageRetry` (2 retries, 2 s). The retry calls `ProcessJobAsync` again with a fresh CTS, so **the job the user canceled starts crawling again** while the DB says `Canceled`; after retries are exhausted the message goes to `_error`.
Also `_jobCtsMap.TryRemove` and the paused flag cleanup are not in a `finally`, so they leak on that path. Not executed; strongly implied by the code. Fix: catch `OperationCanceledException` around the job body, log, return; do cleanup in `finally`.

**H2. `ContentHash` is no longer computed or sent.**
The SHA-256 step and the `ContentHash` field were dropped from the success event. `Pages.ContentHash` therefore stays null for every page; the API's new `string.IsNullOrEmpty(page.ContentHash)` guard is moot. Plan point 20 ("hash in the DB so we can always cross-verify the file") and the file-store drift mitigation in my README section both depend on this field.

**H3. SimHash is computed over raw `InnerText` including `<script>` and `<style>` content (and undecoded entities).**
Report-2 code removed script/style/comments and decoded entities first. Now the stored fingerprint is dominated by JS/CSS tokens on many sites and no longer matches what the preview shows (the preview does strip them). Search ranking quality drops accordingly. The same HTML is also parsed twice.

**H4. Fetch/storage failures still become "crawled pages" or kill the whole job.**
- Network exceptions and timeouts publish a `PageCrawledEvent` with `StatusCode=500`; 5xx after retries is read as a successful page (only 4xx is excluded). Non-HTML content types are still decoded and stored (assessment: HTML only).
- `SavePageAsync` has no error handling. A path segment longer than the filesystem limit (255 bytes), or an IO error, throws inside the task; the exception escapes `Task.WhenAll`, faults the consumer, and the retry restarts the job from scratch (same mechanism as H1).
- `crawledCount` is incremented before the save and parse, so a failed save still counts against the page limit.

### Medium

**H5. Max-pages fix is only partly right.**
Strict `Interlocked`/`Volatile` checks now stop the overrun (verified in code). But: pages downloaded just past the limit are fetched and then thrown away (wasted bandwidth); the last allowed page publishes **no outgoing links** (`currentCount < maxPagesLimit` guard), so its edges are missing from the tree and its Domain Link Ratio is 0; `Completed` clamps the count with `Math.Min` instead of fixing the cause.

**H6. Control messages share the long-running crawl queue.**
Report 2 suggested a per-instance control queue; instead the cancel/pause/resume consumers were **moved into `crawl-job-queue`** with the crawl consumer and its retry policy. A cancel for a job can sit behind long crawls (prefetch/concurrency limits) and, with multiple workers, still goes to one competing consumer. Also unchanged: cancel before the crawl starts is a no-op; resume has no fallback if the worker restarted (DB says `Running`, nothing runs); pause, resume and cancel accept any current status, so pausing or resuming a `Completed` or `Canceled` job overwrites its final state and writes a bogus transition.

**H7. Worker startup swallows DB errors.** `try { EnsureCreated(); } catch { }` hides a missing DB entirely, and the Worker still wires `AddDbContextFactory` although the engine has no DB access (comment b from the original review).

**H8. Storage is only half behind the interface.**
- `IStorageService` is registered in the API but **not used there**: `GetPageContent` re-implements path resolution with a hardcoded `Directory.GetCurrentDirectory()/storage` and `File.ReadAllTextAsync`; `DeleteJob` still calls `File.Delete(page.StoragePath)` directly. Since paths are now **relative**, that delete resolves against the process working directory, so **pruned pages' files are never deleted** (orphans).
- Root is derived from the working directory in three places; no `Storage:RootPath` setting.
- `LocalStorageService` does not check `IsUnderRoot` on **write**.
- `MockS3StorageService` is not selectable by config (acceptable: you said reference only), but there is nothing showing how it is swapped in. Its keys use a random GUID, so unlike the local store the same URL gets a new object each time.
- **My README "Page Content Storage" section (shared-root requirement, DB/file drift trade-off, S3 path) is gone**: `7303fb2` rewrote the README and it is no longer present (no match for "Page Content Storage" or `Storage:RootPath`). The README only briefly mentions `IStorageService`. Your instruction to list it as a requirement is not met.

**H9. `StoragePathBuilder` edge cases.**
Query strings are handled (verified by tests) and invalid characters replaced. Gaps: no segment length cap (feeds H4); `http://a.com:8080/x` and `https://a.com/x` map to the same file (port/scheme ignored); case-insensitive root comparison on a case-sensitive Linux filesystem; files are written as `.txt` but contain **raw HTML**, and pages stored before this change (`.html`, absolute paths) are only readable because the API still accepts rooted paths.

**H10. Job ID version: wrong in both directions.**
Committed code uses `Guid.NewGuid()` (random v4) while README and TRICKS.md still say "UUID v7 via `Guid.CreateVersion7()` to prevent B-tree fragmentation"; your plan point 11 asked for time-ordered (v1). The working tree switches back to `CreateVersion7()`, which does not compile on net8.0. Needs a decision: a small own v7/v1 generator or a library on net8, and README/TRICKS aligned to it. The key is still a 36-char string.

**H13. Haxe "shared logic" is still not shared.**
- Good: `Crypto.hx` now uses `haxe.crypto.Hmac` with SHA-256.
- But the Haxe 64-bit SimHash uses SHA-256 bytes and `split(" ")`, while C# uses CRC64 and `\w+` tokens: different fingerprints for the same text. The 31 tests include no 64-bit or HMAC parity test (the 3 added tests cover storage only); the earlier 32-bit "parity" tests remain tautological.
- No product code calls the Haxe output: the UI only does a side-effect `import './haxeGenerated/shared.js'` with `@ts-ignore`, and C# `HmacAuthService` does not use the Haxe crypto. HAXE.md and TRICKS describe contract enforcement that the tests do not actually enforce.
- Hand edits to generated C# (`FieldLookup.cs`) are still in the tree; the generated Haxe runtime (Md5, Sha1, Hmac, Bytes…) adds ~1,700 lines to `Shared` and new CS8597 warnings.

### Low

- **H11. EDGE_CASES.md, TRICKS.md and README claim features the code does not have** (e.g. UUID v7, per-job in-memory LRU, download reuse, domain ratio per page). I did not re-verify every claim; the ones above are checked against code.
- **H12. Duplicate/old reports.** `docs/audit-report.md` and `-2` are stale for items fixed since; keep them but mark them superseded or add status columns when you next update.
- **H14. Open items from earlier reports that did not move:** idempotency (unique keys, generated `url_crc32`), explicit DLQ/retry documentation and tests, nullable `NoWarn` list in `Shared.csproj`, hardcoded settings (retry counts, concurrency 5, timeouts, connection fallbacks), N+1 queries, auth path-contains bypass and unauthenticated SignalR hub, no integration/fixture test, no CI, `.gitignore` excludes `*.html` (and `Ui/index.html`), no git remote. Your latest comments ("remove job details", "prove paging works", "no cards, use separators") have no evidence of being done; I found no paging test.

## 4. Uncommitted working tree (in progress, not yet a verdict)

- **Build is broken** (`Guid.CreateVersion7` on net8.0). Tests cannot run until fixed.
- **Fabricated byte sizes are back**: the new crawl-evolution history adds `runningByteCount += 15000 // estimated/actual page size` per page. `Pages.ByteSize` exists and is populated; use it. The chart's blue line will show invented data, the same defect fixed earlier for "bytes saved" (D6).
- History is built by loading **all crawled pages of the job into memory** ordered by `CrawledAt`, inside `GetJob` (also called for history rows), formatting times with the server's local timezone (`ToLocalTime`). For big jobs this adds to the N+1/in-memory issues in [n-plus-one-replacements.md](n-plus-one-replacements.md). `GetJob`'s own `pages` query now also filters `CrawledAt.HasValue`, which changes the Domain Link Ratio denominator silently (uncrawled targets are dropped).
- Speed metrics (`avgPagesPerSec`, `avgKbPerSec`) use `Math.Max(1.0, duration)`: fine, but are computed from `TotalPagesCrawled`, which the final progress event overwrites.
- Swashbuckle 6.5.0 added for interactive docs: check it pulls the vulnerable `Microsoft.OpenApi` that the project already warns about (NU1903), and that `docs/openapi.yaml` (hand-written) matches what Swagger generates (otherwise two sources of truth; you asked for NSwag-style generation from the DTOs).
- `CrawlerEngine` uncommitted edits are the max-pages changes from H5.

## 5. What is good now (verified)

- Per-job dedupe (N1), TLS flag (N4), text-only preview with React-node highlighting (N10), query-string-safe storage paths with tests (D3), HMAC-SHA256 in Haxe, Paused status with transition rows, relative storage paths plus `IsUnderRoot` on read and delete helper, API compose volume, aligned target frameworks, 31 passing tests at HEAD.

## 6. Recommended next steps (in order)

1. **H1** catch cancellation in the engine, clean up in `finally`, and add a test that cancels a running fixture crawl and asserts no retry and no further events.
2. **Fix the build** (H10): decide the job-ID scheme for net8 and make README/TRICKS match.
3. **H2/H3** restore `ContentHash` and compute SimHash from cleaned text (reuse one helper for worker and API).
4. **H4** treat exceptions, 5xx and non-HTML as failed pages (not crawled), wrap storage writes, cap segment length.
5. **H8** route reads, deletes and writes through `IStorageService` with `Storage:RootPath`; re-add the README storage requirement/trade-off/S3 section.
6. **H6/H7** separate control queue (per instance), state checks on pause/resume/cancel, real worker health checks, stop swallowing DB errors.
7. Replace the `15000` in the history with `Pages.ByteSize` and move history/stat queries to SQL aggregates.
8. Then the standing items in H14 (idempotency, DLQ docs, nullable, config, auth, CI, git remote).

## 7. Evidence gaps

Not executed: Docker/compose, a live crawl or cancel (H1 and H6 are reasoned from the code), the UI, the Haxe and npm builds, EF against an existing database. H1 is the first thing worth confirming with one run: start a crawl, cancel it, and watch the worker log for a second "Starting parallel crawl job".
