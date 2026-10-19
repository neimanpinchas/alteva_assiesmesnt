# Code Review: Alteva Crawler

Reviewed 2026-10-19 against `docs/Technical Assessment.pdf`, `docs/comments.md` and `docs/plan_points.md`.
Scope: Worker, API, Shared, SharedHaxe, UI sources, tests, README/TRICKS. `dotnet test`: 6 pass. The stack, Haxe build and npm build were **not** run, so UI behaviour is judged from code only.

Companion docs: [audit-report.md](audit-report.md) (requirement traceability) and [n-plus-one-replacements.md](n-plus-one-replacements.md) (query fixes).

**Verdict: the task was not faithfully finished.** README/TRICKS describe more than the code does. Search, page preview, cancel/pause, DLQ, idempotency and the Haxe pipeline are missing or cannot work.

## 1. Blockers (claimed but broken or absent)

| # | Finding | Evidence |
|---|---|---|
| B1 | **SimHash search cannot work.** Nothing writes `Page.SimHash`, `ContentHash` or `StoragePath`; they are only read. Every page has SimHash 0, `GetPageContent` always 404s, so preview, highlighting and hover-text are dead. | `JobsController.cs`, `ApiEventConsumers.cs` (no assignments) |
| B2 | **Global search is a stub**: `Take(100)` of crawled pages, unordered, then ranked. | `JobsController.GlobalSearch` |
| B3 | **Cancel/pause do not stop the crawl.** API only flips DB status and publishes an event the worker never consumes. `PauseJob/ResumeJob` in the engine are never called. Worker's final "Completed" overwrites `Canceled`. "Pause" sets `Pending`. "Resume" starts a second concurrent crawl with an empty dedupe cache. Consumer passes no `CancellationToken`, so shutdown (your "can't Ctrl+C" comment) is unhandled. | `CrawlJobConsumer`, `CrawlerEngine`, `JobsController` |
| B4 | **No idempotency.** No unique index on `Pages`; `UrlCrc32` is a plain C#-filled column, not a persisted generated column with index/key as you asked (no `HasComputedColumnSql`). Consumers do check-then-insert and swallow `DbUpdateException`. Redelivery double-counts pages and bytes. `JobPageLinks` has no unique key. | `ApiEventConsumers.cs`, `Page.cs` |
| B5 | **No DLQ and no documented retry policy.** No fault consumer or error-queue config. The whole job is retried 3x, and since the dedupe cache is in memory the retry re-crawls and re-publishes everything. README lacks message schema, idempotency, retry/transient and DLQ sections, plus "cut / next / limitations". | `Worker/Program.cs`, `README.md` |
| B6 | **No worker health endpoint** (required). `Worker.cs` is the unregistered template that logs every second. | `Worker/` |
| B7 | **Haxe requirement not met.** UI is plain TS and never imports `haxeGenerated/shared.js`. C# never calls generated Haxe. Implementations diverge (signed vs unsigned CRC32, 32-bit vs 64-bit SimHash, `split(" ")` vs `\w+`). `Crypto.hx` "signs" with CRC32 plus the secret, which is forgeable. `IHaxeUtils.cs` is declared but nothing implements it and no parity test exists. | `SharedHaxe/*.hx`, `Contracts/IHaxeUtils.cs` |
| B8 | **Git state.** No remote; almost everything is untracked; two commits touch only `docs/comments.md` and the consumers. `.gitignore` has `*.html`, which also ignores `Ui/index.html`. No CI workflow (optional). | `git status` |

## 2. Correctness bugs

- **Failed fetches counted as pages.** Timeouts/DNS errors are published as `StatusCode=500` pages; 4xx too. No `Content-Type` check, so PDFs and images are decoded as UTF-8 and "crawled" (assessment: HTML only). No size cap.
- **Retry handling** is a Polly policy on the named client with hardcoded 3 retries and 2/4/8 s backoff. 4xx is skipped (good), but 408/429 are not handled and the numbers are not configurable.
- **Concurrency is batched, not pooled**: `Task.WhenAll` over up to 5 tasks, so one slow page stalls four. `crawledCount < maxPagesLimit` is read while tasks increment it, so the limit can overshoot; the loop can also exit while links are still pending.
- **Page limit not applied to discovery**: `LinkDiscoveredEvent` is published for all links after the limit.
- **No "Running" or periodic progress from the worker.** Counts come from per-event increments, then are overwritten by the final event (your comments e and h).
- **Edge loss race.** `ApiLinkDiscoveredConsumer` can run before the source page row exists; `sourcePage == null` drops the edge silently, the event is still broadcast, and `catch (DbUpdateException) {}` hides the rest.
- **Domain Link Ratio is not per the spec.** Spec: outgoing links within start domain / total outgoing links, per page. Actual: computed from published edges only (depth-limited, so depth-limit pages show 0); job-level ratio counts distinct target pages by CRC32 domain hash (collision-prone). Worker never computes it.
- **`UrlNormalizer`** gaps: see decisions D2/D3 below (scheme allow-list, query strings, `www`, default ports, `<base href>`).
- **Storage paths**: query strings are dropped so `/p?id=1` and `/p?id=2` overwrite each other; invalid path characters and long names unhandled; no traversal sanitising; path never saved; `GetPageContent` reads any DB path without checking it sits under the storage root; compose mounts the volume on the worker only, so the API cannot read it.
- **Stored XSS**: crawled HTML goes to `dangerouslySetInnerHTML` in the same origin as the API token (see D4).
- **Auth**: `AuthFilter` skips any path that merely *contains* `/health` or `/api/auth/token`; SignalR hub is unauthenticated; `POST /api/auth/token` issues tokens to anyone; `HmacAuthService` uses a random per-process key, so restart invalidates sessions and the worker or a second replica cannot validate tokens (breaks plan point 14, see D5).
- CORS policy is named `AllowAll` but restricts to two hardcoded origins.

## 3. Data model and performance

- Job PK and `JobPageLinks.JobId` are `varchar(36)` on the hottest table; you asked for strict ints for page IDs and v1 UUIDs for jobs. Code uses `CreateVersion7`, README says v7, with no explanation of the choice.
- `JobStatusTransitions` stores raw chars and has no status lookup table (plan point 12).
- **legodb YAML vs entities out of sync**: `Pages.yaml` has `Id{36}` but entity is `long`; `JobPageLinks.yaml` `SourcePageId{36}` vs `long`; `Jobs.yaml` lacks `MaxPagesLimit`. `schema.sql` is a malformed T-SQL stub (`nvarchar(max)`, only Jobs table). The app uses `EnsureCreated()`, so the tool is not what builds the DB (violates AGENTS.md).
- **N+1 and in-memory aggregation** in history, job, metrics, tree, delete, search: see [n-plus-one-replacements.md](n-plus-one-replacements.md).
- `EstimatedBytesSaved = sharedPagesCount * 15000` is invented (see D6).
- Forecast uses edge count, not distinct unfetched pages.
- No evidence the endpoints were profiled (your earlier request).
- Search is O(N) in memory; acceptable here but should be listed as a limitation.

## 4. Engineering quality

- **Nullability**: `Shared.csproj` suppresses `CS8600-8605`, `CS8618`, `CS8625`, `CS8765`, hiding what AGENTS.md says to be strict about.
- **Frameworks**: API/Worker/Tests target net10.0, Shared net8.0; the assessment says .NET 8. `Microsoft.OpenApi 2.0.0` raises NU1903 (high severity) on every build.
- **Hardcoded config**: connection fallbacks (`root`/`rootpassword`), RabbitMQ defaults, 10 s timeout, concurrency 5, retries/backoff, user agent, CORS origins, token life. Worker `appsettings.json` has only logging.
- **Layering**: Worker still wires `AddDbContextFactory` and `EnsureCreated` (your comment b). `Shared` mixes entities, DbContext, events, MassTransit and HtmlAgilityPack. `JobsController` is 555 lines with business logic; no evidence lizard was run.
- **Observability**: jobId appears in some messages, but no logging scope or correlation ID.
- **Health**: `messageBroker` is the hardcoded string `"RabbitMQ Active"`; broker is never checked (your "what if MQ is down" comment). The DB check runs inside the response writer rather than as a registered check. API swallows `EnsureCreated` failures and carries on.
- Compose has plaintext DB root password; README says UI on 5173 or 3000.

## 5. Tests

- 6 unit tests; `UnitTest1.cs` is an empty template test.
- **No Domain Link Ratio test** (explicitly required). README claims coverage it does not have.
- **No fixture-based integration test**, no consumer/idempotency/controller/engine tests.
- SimHash test asserts only `0 <= distance <= 64`, which cannot fail.
- No Haxe/C# parity test.
- `Worker/storage/` holds ~60 real-site HTML files from your runs. It is gitignored; do not ship it in a zip.

## 6. Docs vs reality

| Claim (README/TRICKS) | Reality |
|---|---|
| In-memory LRU cache | Unbounded `ConcurrentDictionary`, no eviction, lives on a scoped engine so not shared across jobs |
| Download reuse shortcuts (TRICKS #5) | No code skips a download already stored by another job; worker has no DB access |
| SimHash search, preview, highlight | Not functional (B1) |
| Haxe consumed by both sides | Not consumed (B7) |
| Pause/resume/cancel | DB flag only (B3) |
| How to disable the many-to-many index | Not documented (plan point 8) |

## 7. Decisions recorded from the review discussion (2026-10-19)

These are agreed direction, not yet implemented.

| ID | Decision | Implementation notes |
|---|---|---|
| D1 | **`www.` is normalised, or at least an option.** | Add `StripWww` option (default on, from `appsettings.json`) in the normalizer; apply to host, `ExtractDomain` and domain hash so `www.a.com` and `a.com` are one domain. Document in README. |
| D2 | **Only `http://` and `https://` are handled; relative links resolved.** | Replace the deny-list (`mailto:`, `tel:`, `javascript:`, `data:`) with an allow-list: after resolving against the base, accept only `Uri.UriSchemeHttp/Https`. Keep scheme-less input as lenient auto-prepend for the UI box only (`auto.com`). Honour `<base href>` and protocol-relative `//host/path`. |
| D3 | **Query strings must work; invalid characters go through a helper.** | Keep the query in the normalised URL (sort or keep order, drop empty `?`, drop default ports). Storage: add one helper, e.g. `StoragePathBuilder.ToRelativePath(Uri)`, that maps `path?query` to a safe file name (replace invalid chars, cap length, append a short hash of the query when present, reject `..`), and assert the result stays under the storage root. Unit-test with `?id=1` vs `?id=2`, unicode (Hebrew hosts are in your data), and over-long names. |
| D4 | **XSS: show text only, not HTML.** | Do it in two layers. (a) API: return extracted visible text (HtmlAgilityPack, drop `script`/`style`, decode entities) plus the stored file for download. (b) UI: render as plain text in a `<pre>`; build `<mark>` highlighting by splitting the string into React nodes, never by `innerHTML`. Escaping `<`/`>` to `&lt;`/`&gt;` is an acceptable fallback if raw source must be shown. Also add a `Content-Security-Policy` header. |
| D5 | **`HmacAuthService` key comes from env var or `appsettings.json`.** | Bind `Auth:SigningKey` (env `Auth__SigningKey`); fail fast if missing or under 32 bytes. Comment in `appsettings.json` and code: in production this comes from Azure Key Vault or GitHub secrets. Same key lets the worker or a second replica verify tokens. Do not call it ed25519 (you already agreed on HMAC). |
| D6 | **Bytes saved is the real total of shared pages.** | Persist `Pages.ByteSize`. Saved bytes = `SUM(Pages.ByteSize)` over pages in this job that also have a link from another job (or that this job reused without download). Remove `* 15000`. Query in the N+1 doc. |
| D7 | **Job history stays per-job, but extract a reusable function.** | Introduce `GetJobData(jobId)` / `GetJobDataBatch(ids)` returning the per-job stats (ratio, discovered, sizes) and use it from `GetJob` and `GetJobHistory`. The batch form replaces the loop with grouped queries (see N+1 doc). |
| D8 | Produce a document with replacements for all N+1 issues. | Done: [n-plus-one-replacements.md](n-plus-one-replacements.md). |
| D9 | Produce an audit report. | Done: [audit-report.md](audit-report.md). |

## 8. Suggested fix order

1. Persist SimHash/content hash/path/size from the worker via events; share the storage volume with the API; text-only preview (B1, D3, D4, D6).
2. Generated `url_crc32` column with unique key; unique keys on links; upserts with an idempotency key (B4).
3. Worker consumers for cancel/pause/resume with per-job `CancellationTokenSource`; host stopping token; bounded worker pool (B3).
4. MassTransit retry with transient filter, error queue and fault consumer; document (B5).
5. Remove nullable suppressions; drop DB wiring from the Worker; options classes and `appsettings.json` for all config; HMAC key from config (D5).
6. Worker `/health`; real broker health check (B6).
7. Replace N+1 queries (D7, D8).
8. Tests: Domain Link Ratio, fixture crawl, idempotency, normalizer cases from D1-D3, Haxe parity.
9. Decide Haxe: wire it in as specified, or remove it and fix the docs (B7).
10. README: required sections; git history and remote (B8).
