# Audit Report 4: Fix verification after commit 2eb00f9

Date: 2026-10-19. Checks the fixes against [audit-report-3.md](audit-report-3.md). Reviewed commit `2eb00f9` plus the **uncommitted working tree** (14 modified files and 3 untracked: engine rewrite, `SequentialGuid`, Haxe CRC64, README, tests).

How verified: `dotnet test` on the working tree: **builds, 42/42 tests pass** (net8.0). Docker, a live crawl, the UI and the Haxe/npm builds were not run. Runtime claims are reasoned from code unless marked verified.

## 1. Verdict

Good round: **the cancel regression, the build break, the fake byte sizes, the Haxe/C# SimHash mismatch and most of the storage wiring are fixed.** Two items from report 3 were not touched (ContentHash, SimHash input), and the new engine code introduces a few smaller problems. Most of the fixes exist only in the working tree, so **commit them**.

## 2. Report-3 findings: status

| ID | Finding | Status | Evidence |
|---|---|---|---|
| H1 | Cancel faults consumer, retry restarts job | **Fixed** | `OperationCanceledException` caught at semaphore wait, HTTP call, task body and job level; cleanup in `finally`; events published with `CancellationToken.None`; test `CrawlerEngine_Cancellation_CompletesWithoutThrowing` passes. Gap: the test only asserts no exception, not that no events follow or no retry happens |
| H2 | `ContentHash` no longer sent | **Not fixed** | `CrawlerEngine.cs` has no content-hash code; `Pages.ContentHash` stays null |
| H3 | SimHash from raw `InnerText` incl. script/style | **Not fixed** | Still `doc.DocumentNode.InnerText` on the unstripped document. The API preview strips script/style, so search fingerprints and preview text still disagree |
| H4 | Failures stored as pages; storage error kills job | **Mostly fixed** | Non-2xx (including 5xx) and non-HTML now publish failure events and do not count in the engine; storage write errors are caught and logged; `NonHtmlAndHttpError_DoesNotCountAsCrawledPages` passes. Remaining: see N1, N2 below |
| H5 | Max-pages handling | **Fixed (mostly)** | Increment-then-decrement guard, last page now publishes its links, `MaxPagesLimit_EnforcedStrictly` test. Residual: pages beyond the limit are still downloaded before the check |
| H6 | Control messages share crawl queue; no state checks; no cancel-before-start | **Not fixed** | `CancelJob/Pause/Resume` consumers still on `crawl-job-queue`; API still accepts pause/resume/cancel for any status |
| H7 | Worker swallows DB errors | **Fixed** | `EnsureCreated` failure is now logged. Worker still carries DB wiring |
| N7 | Worker `/health` empty | **Fixed (partly)** | `MassTransitBusHealthCheck` added. Compares `health.Status.ToString()` to strings (fragile); no DB check (acceptable since the worker should not need one) |
| H8 | Storage half behind interface; README section lost | **Mostly fixed** | API read and delete now go through `IStorageService` (verified in diff); README "Page Content Storage" re-added with shared volume and S3 swap. Gaps: see N4 |
| H9 | `StoragePathBuilder` gaps | **Not addressed** | No segment length cap; port/scheme still ignored; `.txt` extension on HTML files |
| H10 | Job ID scheme / build | **Fixed in code, docs wrong** | `SequentialGuid` (UUIDv7 layout) used in `CreateJob`, 2 tests, build passes on net8. README line 78 and TRICKS.md still say `Guid.CreateVersion7()`. See N5 |
| H11 | Fake 15000 bytes in history | **Fixed** | `runningByteCount += page.ByteSize` |
| H13 | Haxe SimHash differs from C# | **Fixed** | Haxe now uses CRC64 and `\w+` on lowercase text; `HaxeParity_SimHash64_MatchesCSharpSimHashCalculator` passes |
| H13b | Haxe crypto and UI usage | **Partly** | HMAC-SHA256 in Haxe from before; no parity test against `HmacAuthService` (different token format); no UI call site for Haxe logic found |
| — | Swagger UI | **Done** | `AddSwaggerGen`, `UseSwagger`, `UseSwaggerUI` in `Program.cs`. `docs/openapi.yaml` is still hand-written: two sources of truth |
| H14 | Standing items | **Unchanged** | See section 4 |

## 3. New problems found in this round

**N1. API still counts failure events as crawled pages (live count wrong).**
`ApiPageCrawledConsumer` does `TotalPagesCrawled++` and `TotalBytesDownloaded += ByteSize` for **every** `PageCrawledEvent`, including the failure and non-HTML events the worker now sends. The page row is also marked `IsCrawled = true`. The engine and its test treat failures as not crawled, but the job counter and the "pages" shown during a crawl include dead links until the final "Completed" event overwrites the total. Fix: only count events with a success status and `ByteSize > 0` (or add an explicit `Success` flag to the event).

**N2. Job-level `catch (Exception)` swallows errors and publishes nothing.**
If anything unexpected escapes the loop, the engine logs and returns without a "Failed" event: the job stays `Running` in the DB forever and MassTransit sees a normal completion (no retry, no `_error` queue). Use that catch to publish `Failed` with a reason (`FailureReason` exists on `Job`). The per-task `catch (Exception)` has the same effect for one page: it is logged and the page silently disappears (no event, no retry).

**N3. Storage failures are invisible downstream.** A failed save sets `StoragePath` to empty and the page is still published as crawled with a `SimHash`. Preview later returns "file missing" with no indication why. Publish a failure marker or retry the write once.

**N4. README overstates the storage safety.** It says path traversal protection is enforced "on read, write, and delete". `LocalStorageService` checks `IsUnderRoot` only in read (line 36) and delete (line 49), **not in `SavePageAsync`**. Also still missing from the README: the `Storage:RootPath` setting (there is none; the root is `cwd/storage`) and the DB/file drift trade-off you asked to list. `MockS3StorageService` mints a random GUID key per save, so re-crawling a URL creates a new object each time.

**N5. `SequentialGuid` details.** It reads the shared `_sequence` after releasing the lock (race between threads in the same millisecond); the 12-bit sequence wraps after 4096 ids per millisecond and then breaks ordering; timestamps going backwards are clamped but the sequence is never re-checked. Fine for this load, but use a lock around the whole read, and note it is a v7 layout so README/TRICKS should say `SequentialGuid`, not `Guid.CreateVersion7()`. `Job.Id` still defaults to `Guid.NewGuid()` in the entity (overridden by the controller, but a trap for other creation paths) and remains a 36-char string.

**N6. Stray file:** an empty `dotnet-install.sh` is untracked in the repo root. Delete it or add it to `.gitignore`.

**N7. Test gaps in the new engine tests.** The three engine tests use a fake handler and a fake publisher (good start, and the fixture-style test the assessment recommends). Missing: same-site-twice (per-job dedupe, N1 of report 2), failure events not overwriting stored data, idempotent redelivery in the API consumers, and a cancel test that asserts no events after cancel.

## 4. Still open from earlier reports (unchanged by this round)

- Idempotency: no unique keys, no generated `url_crc32`, check-then-insert consumers, counters double-count on redelivery.
- No migrations (`EnsureCreated` plus new `ByteSize` column), `schema.sql` still T-SQL stub, legodb YAML minimal.
- DLQ/retry policy not documented or tested; whole-job `UseMessageRetry` remains.
- D5 HMAC key from config (still `RandomNumberGenerator.GetBytes(32)` per process); auth path-contains bypass; SignalR hub unauthenticated.
- Nullable `NoWarn` list in `Shared.csproj`; hardcoded concurrency (5), retries, timeouts, connection fallbacks.
- N+1 and in-memory aggregation, now also in the new crawl-history (loads all job pages in memory, per `GetJob`).
- `www` stripping not configurable; domain ratio per spec still unused in product; no content-hash; docs claiming v7, LRU, download reuse.
- No CI, no git remote, `.gitignore` excludes `*.html`.
- Cross-job download reuse is not implemented (each job re-downloads pages other jobs already stored).

## 5. What now looks right (verified by reading and by tests)

Cancel without retry loop; strict page limit with last-page links; non-HTML and non-2xx excluded from engine counts; storage via `IStorageService` for read/delete; real `ByteSize` in history; Haxe/C# 64-bit SimHash parity test; sequential job IDs with tests; Swagger UI; worker bus health check; DB startup error logged; 42 passing tests including three engine tests.

## 6. Suggested next steps

1. Commit the working tree (so reviews and the repo agree), delete `dotnet-install.sh`.
2. **N1** fix API counting; **N2** publish `Failed` and set `FailureReason` on unexpected errors.
3. **H2/H3** restore `ContentHash`; compute SimHash from the same cleaned text the preview uses (one shared helper).
4. **H6** state checks for pause/resume/cancel; separate control queue.
5. Fix README/TRICKS claims (UUID, storage safety, `Storage:RootPath`, drift trade-off) and add `Storage:RootPath` plus write-path check.
6. Then the standing items in section 4, starting with idempotency and migrations.
