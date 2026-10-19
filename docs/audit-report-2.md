# Audit Report 2: Re-audit after commit 5fdc37e

Date: 2026-10-19. Scope: everything since the first review (`e017fb4..HEAD`: `33e1d8a` HttpClient UA/decompression, `b93b76d` start/stop scripts, `5fdc37e` "resolve code review findings"). Prior reports: [code-review.md](code-review.md), [audit-report.md](audit-report.md), [n-plus-one-replacements.md](n-plus-one-replacements.md).
Method: static review of the diffs and resulting code. `dotnet test`: **28/28 pass** (was 6). Not run: docker, the live stack, the Haxe/npm builds, the UI. Anything about runtime behaviour is reasoned from code and labelled as such.

## 1. Summary

The commit message says it "resolves code review findings". That is only partly true. Four findings are properly fixed, several are fixed only in part, and the commit **introduces regressions**, one of which breaks the core crawl for any second job.

| Prior finding | Status now |
|---|---|
| B1 SimHash/ContentHash/StoragePath never persisted | **Fixed in code** (worker computes, event carries, API persists); new defects below |
| B3 cancel/pause don't stop the crawl | **Partly fixed**: control events and per-job CTS added; races and resume gap remain |
| B6 no worker health endpoint | **Partly fixed**: `/health` exists but checks nothing |
| B7 Haxe not consumed | **Partly fixed**: C# wrappers exist; UI still doesn't use the JS; parity tests are weak |
| D1 `www` normalised as option | **Done in code**, not configurable from appsettings, no data migration |
| D2 http/https allow-list, relative links | **Done** (protocol-relative links added) |
| D3 query strings and safe path helper | **Not done** |
| D4 text-only preview (XSS) | **Partly done**: API side only; UI unchanged |
| D5 HMAC key from config | **Not done** |
| D6 real bytes saved | **Done**, still loads everything in memory |
| D7 `GetJobData` extraction | **Not done** |
| Domain Link Ratio tests | **Added** but the function is only used by tests (not by the product) |
| B4 idempotency, B5 DLQ docs, nullable suppression, hardcoded config, N+1, Haxe-in-UI, README sections, CI | **Unchanged** |

## 2. New problems (introduced by these commits)

### High

**N1. Singleton engine shares the dedupe cache across all jobs: a second crawl of the same site only fetches one page.**
`Program.cs` changed `CrawlerEngine` from scoped to `AddSingleton`, and `_inMemoryUrlCache` is an instance field keyed only by `uint` CRC, never cleared. In `ProcessJobAsync` the start URL is enqueued unconditionally, but every discovered link goes through `_inMemoryUrlCache.TryAdd(...)`, which is already false for URLs a previous job saw. Failure scenario: crawl `example.com`, then crawl it again (or crawl `a.com` then a site linking to the same pages): job 2 fetches the start page, finds all links "already seen", enqueues none, and reports Completed with 1 page. The same happens to a MassTransit retry or a resume of a crashed job (the cache survives retries). Also: the cache grows without bound for the life of the process (the "LRU" claim remains false), and CRC32 collisions across all jobs ever crawled now silently drop distinct URLs (birthday risk at ~77k URLs in the whole process). Fix: per-job set (a local `HashSet<uint>` plus the exact URL compare, disposed with the job), and `TryAdd` the start URL too.

**N2. `EnsureCreated()` plus a new column: existing databases break.**
`Page.ByteSize` was added with no migration. `EnsureCreated` does nothing when the schema already exists, so on any DB created before this commit (your local MySQL, which you've been crawling into) every `Pages` insert/select includes a `ByteSize` column that does not exist. Expected result (inferred, not executed): `ApiPageCrawledConsumer`, tree, search and metrics fail with "Unknown column". Because the consumers swallow `DbUpdateException` but not `MySqlException` on select, events will fault and land in the `_error` queue. Fix: introduce real EF migrations (or at minimum drop/recreate the dev DB and say so in the README).

**N3. Docker build is broken: SDK/runtime images are 8.0 but projects target net10.0.**
`src/AltevaCrawler.Api/Dockerfile` and the Worker's use `dotnet/sdk:8.0` / `aspnet:8.0`; API, Worker and Tests are `net10.0`. `docker-compose up --build` (the assessment's delivery requirement) should fail at `dotnet publish`. This predates the commit but is now a delivery blocker, since the Worker changed to the Web SDK and port 5001 and no `EXPOSE`/healthcheck/ports were added in compose. Local SDK is 10.0.103, so it only works outside Docker. (Not executed; derived from the files.)

**N4. TLS certificate validation is disabled for the crawler.**
`33e1d8a` sets `ServerCertificateCustomValidationCallback = (…) => true` unconditionally. This presumably works around NetFree's MITM proxy, but it ships as the default for every environment. Make it a config flag (`Crawler:AllowInvalidCertificates`, default false) and document why.

### Medium

**N5. Worker `PageCrawledEvent` for failures overwrites good data.** For HTTP errors and 4xx the event carries `SimHash=0`, `ContentHash=""`, `StoragePath=""`, `ByteSize=0`. The API's update branch assigns all of them unconditionally, so a failed re-crawl of a page that was already stored by another job erases its SimHash, hash and path (the file stays on disk, orphaned) and zeroes `ByteSize`, which also corrupts the new "bytes saved" figure. It also keeps the earlier bug of recording timeouts as `StatusCode = 500` "pages" that count towards `TotalPagesCrawled`.

**N6. Cancel/pause/resume races and gaps.**
- `CancelJob` only cancels an already-registered CTS. If the cancel arrives before the crawl consumer starts (or while it is queued), it is a no-op and the job later runs to completion, then the API's DB says `Canceled` while the worker still crawls and publishes `PageCrawledEvent`s.
- Resume only clears the in-memory pause flag. If the worker restarted, the message was consumed by another instance, or the crawl had ended, resume does nothing but the API already set the DB status to `Running` (stuck "Running" forever). The previous behaviour (re-publish the crawl) was replaced without a fallback.
- Pause is only checked between 5-task batches, so up to 5 slow requests (15 s timeout x retries) continue after pausing.
- With several worker instances the control message goes to one competing consumer, not the one running the job. Use a per-instance (fan-out) control queue or route by job.
- Pause still writes DB status `Pending` (and `Paused` is mapped to `Pending` in the progress consumer), so the UI cannot tell "paused" from "not started". No transition rows are written for pause/resume.
- `Task.Run(..., jobToken)`: if the token cancels before a queued task starts, that task never runs its `finally`, so the semaphore is not released for it. Harmless today (the semaphore is disposed with the job) but fragile.
- Verified fixed: the worker no longer publishes "Completed" over a canceled job; the consumer now passes `context.CancellationToken`.

**N7. Worker `/health` is a stub.** `AddHealthChecks()` registers no checks, so it returns `Healthy` even if RabbitMQ or MySQL is down. The assessment wants health endpoints; the earlier review also asked for real broker/DB state. Port 5001 is hardcoded via `UseUrls`, and the Worker still wires `AddDbContextFactory` and runs `EnsureCreated` although the engine has no DB access (comment b, unchanged).

**N8. `StoragePath` is an absolute worker-side path sent over the bus.** The API reads that exact path. This only works if both run on the same machine with the same directory; in compose the API has no storage volume, so preview and the new SimHash text search stay broken in the deployed topology. It is also a trust issue: whoever can publish a `PageCrawledEvent` chooses which file the API reads (`GetPageContent` does not check the path is under the storage root). The column is `MaxLength(512)`, but nothing limits the event's value, so long URLs will truncate or fail on insert. Send a relative path and resolve it under a configured root. **Decision (2026-10-19):** keep the file store (avoids DB bloat); the shared-root requirement, the DB/file drift trade-off and the S3 path for multi-machine are now documented in the README section "Page Content Storage". The code changes (relative path, `Storage:RootPath`, shared compose volume, root check) are still open.

**N9. Haxe "parity" tests do not prove parity.**
- `HaxeParity_SimHash32_AndHammingDistance` compares Haxe's own distance with `PopCount` of Haxe's own outputs. The C# product uses a 64-bit SimHash (`SimHashCalculator.Compute64`, CRC64, `\w+` tokens); Haxe is still 32-bit with `split(" ")`. They remain different algorithms; the test cannot detect it. The `ISimHash` contract (`float` hash arguments, `int` results) cannot even represent the 64-bit hash.
- The Crypto test round-trips Haxe against itself. `Crypto.hx` still "signs" with `CRC32(payload + secret)`: not a MAC. An attacker can forge by brute-forcing 2^32 or exploiting CRC linearity. Nothing in the product uses it, but the interface is advertised as the shared auth primitive. Either replace with HMAC-SHA256 (Haxe has `haxe.crypto.Hmac`) or delete it.
- CRC32 parity is only tested on ASCII. Haxe hashes `charCodeAt` (UTF-16 code units) while C# hashes UTF-8 bytes, so they diverge for Hebrew/non-ASCII text, which your crawl targets include.
- The generated C# under `HaxeGenerated/src` was modified (`FieldLookup.cs` diff). Hand edits to generated code are lost on the next Haxe build.
- The UI still does not import `haxeGenerated/shared.js` (no TS file references it), and the Haxe code in the UI is unused; plan point 22 is still unmet.

**N10. Text preview is only half implemented (D4).** The API now returns `HtmlEncode(plainText)` but keeps the DTO property named `Html`, and the UI still injects it with `dangerouslySetInnerHTML`, then highlights by string-replacing `<mark>` into the encoded text. This is safe against script injection (encoded text never contains a raw `<`), but: searching `amp` or `lt` highlights inside entities (`&<mark>amp</mark>;`) and corrupts the display; the text has no whitespace collapsing; the whole file is read into memory; there is no CSP header. The agreed design was React text nodes for highlighting. Also the DTO name is misleading (`Html` holds text).

**N11. `www` stripping side effects (D1).**
- The option exists as a method parameter defaulting to true; there is no `appsettings.json` switch, so "or at least an option" is satisfied only for callers who pass the flag, and nobody does.
- The crawler now fetches the www-stripped host. Sites that only serve `www.` (no apex DNS or certificate) will fail where they used to work. Consider fetching the original URL and using the stripped form only as the dedupe key.
- Existing rows keep old `DomainHash`/`Url` values, so history and ratios for old jobs change meaning silently. No data migration.
- `Normalize("example.com:8080")` (no scheme, with port) parses as scheme `example.com` and is rejected as non-http; the lenient URL box (comment d) fails for host:port input. Not run: inferred from `Uri.TryCreate` behaviour; add a test.

### Low

- **N12. `CalculateDomainLinkRatio` is dead code in production.** It is only called from tests. The product still derives the ratio from edge tables (tree/job/history), so the new tests prove a function nobody calls and the original Domain Link Ratio correctness gap (depth-limited edges, no per-page ratio from the worker) stands.
- **N13. Browser User-Agent spoof** (`Chrome/122` string) to get past Cloudflare, with no robots.txt check and no per-host rate limit. Defensible for a local assessment, but it should be a configurable, documented trade-off.
- **N14. `start-local.sh` / `stop.sh`:** `pkill -f AltevaCrawler.Worker` matches any process whose command line contains it (including editors and this shell). `fuser -k 5173/tcp` kills whatever holds the port. Output is sent to `/dev/null`, so startup failures are invisible. Not wired into the README.
- **N15. Docs now out of date.** `docs/audit-report.md` and `docs/code-review.md` were committed with pre-fix statuses; the README/TRICKS are unchanged by the commit and still contain the earlier Misreported claims (LRU, download-reuse shortcuts, Haxe consumed by both sides, "all unit tests pass" list). Section 5 comments added to `docs/comments.md` (no forecast after completion; link tree stopped showing) have no code change in the commit.
- **N16. Regression-test gaps.** The 22 new tests are all pure unit tests of utilities. Nothing covers `CrawlerEngine`, the consumers, cancel/pause, idempotency, or the fixture-based crawl the assessment recommends, which is why N1 and N5 pass CI.

## 3. Previously reported items that remain open (unchanged)

- B4 idempotency: no unique keys, no generated `url_crc32` column, check-then-insert, double counting on redelivery.
- B5 retry/DLQ documentation (note: MassTransit creates `<queue>_error` by default, so poison messages do go somewhere; my first report overstated "no DLQ". It is still unconfigured, untested and undocumented, and the whole-job `UseMessageRetry` re-crawls everything).
- Nullable suppression in `Shared.csproj`; hardcoded config; net8/net10 mix; `Microsoft.OpenApi` NU1903.
- All N+1 and in-memory aggregation endpoints; auth bypass by path and open SignalR hub; random HMAC key per process (D5).
- Query-string collisions in storage paths and no path sanitising (D3). The worker path code is unchanged.
- No fixtures integration test, no CI, README still lacks required sections, `.gitignore` still excludes `*.html` (and so `Ui/index.html`), no git remote.
- Batched (`WhenAll`) concurrency, page-limit overshoot, non-HTML content crawled, failed fetches stored as 500.

## 4. Positive changes verified

- SimHash is computed from script/style-stripped text and persisted end to end; `StoragePath`, `ContentHash` and `ByteSize` now flow through the event.
- `ByteSize`-based bytes saved replaces the invented `* 15000`.
- Cancel no longer publishes a final "Completed"; cancellation tokens thread through HTTP, file I/O and publish calls.
- `UrlNormalizer` has an http/https allow-list, protocol-relative support, default-port removal, www option; Domain Link Ratio function and tests exist.
- 28 tests pass (up from 6), including normalizer cases and Haxe wrappers.

## 5. Priority fixes before submission

1. **N1** per-job dedupe set (and add a test that crawls the same fixture site twice). This is the most damaging regression.
2. **N2/N3** create migrations (or document dropping the DB) and move the Dockerfiles to the .NET version actually used; run `docker compose up --build` once.
3. **N5** make failure events not overwrite stored content fields; record fetch failures as failed, not as crawled 500s.
4. **N6/N7** cancel-before-start handling, resume fallback, per-instance control queue, real health checks.
5. **N8** relative storage path plus a shared volume for the API.
6. **N10** render text with React nodes (D4), and rename the DTO property.
7. **N4/N9** TLS flag; delete or properly implement the Haxe crypto and 64-bit SimHash parity, or remove Haxe from the claims.
8. Then the open items in section 3, starting with idempotency and the README required sections.

## 6. Evidence gaps

Not executed: docker build/compose, live crawl, EF against an existing MySQL schema (N2), the Haxe/npm builds, UI behaviour. N2, N3 and N6 describe likely runtime outcomes inferred from the code; each is cheap to confirm with one run.
