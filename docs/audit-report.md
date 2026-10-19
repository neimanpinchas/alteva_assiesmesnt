# Audit Report: Alteva Crawler

Date: 2026-10-19. Repo state: branch `main`, working tree mostly uncommitted.
Method: static review of source against (1) `Technical Assessment.pdf`, (2) `docs/plan_points.md` (numbered requirements), (3) `docs/comments.md` (change requests), (4) `AGENTS.md` rules. `dotnet test` run (6/6 pass). Not run: docker compose, the Haxe and npm builds, the live stack. Status labels reflect what the code does, not what the README says.

Legend: **Met** = implemented and consistent with the requirement. **Partial** = present but incomplete or wrong in places. **Missing** = not implemented. **Misreported** = docs claim it, code does not.

## 1. Summary

| Area | Met | Partial | Missing/Misreported |
|---|---|---|---|
| Assessment: architecture and delivery | 4 | 2 | 1 |
| Assessment: job lifecycle and API | 3 | 2 | 1 |
| Assessment: crawling rules | 2 | 4 | 1 |
| Assessment: event-driven | 1 | 1 | 3 |
| Assessment: tests, observability | 0 | 2 | 3 |
| plan_points.md (23 items) | 8 | 8 | 7 |
| AGENTS.md rules (6) | 1 | 3 | 2 |

Rubric view (assessment weights): Correctness 30% (weak: ratio, failed-page handling, XSS); Architecture 25% (adequate shape, leaks: Worker DB wiring, hardcoded config); Event robustness 15% (weakest: no idempotency, DLQ or documented retries); SQL model 15% (indexes exist, generated column and unique keys missing, N+1); Quality/docs 15% (tests thin, README inaccurate).

## 2. Assessment requirements traceability

| Requirement | Status | Evidence / gap |
|---|---|---|
| Two+ processes (API, Worker) | Met | `src/AltevaCrawler.Api`, `src/AltevaCrawler.Worker`, separate Dockerfiles |
| RabbitMQ with Docker Compose | Met | `docker-compose.yml` with health-gated `depends_on` |
| SQL DB persisting jobs, pages, edges | Met | MySQL; `Jobs`, `Pages`, `JobPageLinks` |
| Compose runs API, worker, DB, broker | Met (unverified) | File reviewed; not executed. Volume for storage mounted on worker only |
| Job fields (GUID, URL, status, timestamps, failure reason) | Met | `Job.cs` |
| Create job `{url, maxDepth?}` to `{jobId}` | Met | `POST /api/jobs`; default 2 |
| Read status/summary, tree, paginated history (newest first) | Partial | Present; history pagination unvalidated; N+1 queries |
| Cancel (optional) | Partial | DB flag only; worker never stops (B3) |
| HTML pages only | Missing | No `Content-Type` check; binaries decoded as text |
| Default depth 2 | Met | |
| Collect absolute URL, outgoing links, Domain Link Ratio | Partial | Ratio derived from depth-limited edges, not per-page outgoing links; worker does not compute |
| Relative links, fragments | Partial | Handled; scheme allow-list and `<base href>` pending (D2) |
| Duplicate URLs not reprocessed within a job | Partial | In-memory CRC dictionary, per engine instance; lost on retry/resume; collisions (CRC32) can wrongly skip distinct URLs |
| Max pages safety limit | Partial | Applied to fetches, can overshoot under concurrency; not applied to link publishing |
| HTTP timeouts and retries | Partial | Exist but hardcoded; 408/429 unhandled |
| Domain Link Ratio per spec (zero links = 0, ignore non-http) | Partial | Zero-link case returns 0; start-domain definition fine; `www` handling undefined (D1) |
| Idempotent worker (same task twice, no duplicate rows) | Missing | No unique keys; check-then-insert; counters double-count |
| Retries for transient failures | Partial | Whole-job retry via MassTransit; no transient filter |
| Dead-letter queue for poison messages | Missing | No configuration or handler |
| README: message schema, idempotency, retry, DLQ | Missing | Sections absent |
| React: Start, Job Details, History screens | Partial | Components exist; behaviour not run |
| Progress indicator (polling or SignalR) | Partial | SignalR present; consumers broadcast; not exercised; bytes not reactive per your comments |
| Loading and error states | Not verified | UI not run |
| Unit tests: URL normalization | Met | 2 tests |
| Unit tests: Domain Link Ratio | Missing | None; README claims otherwise |
| Integration test with HTML fixtures | Missing | None |
| Structured logs with jobId/correlationId | Partial | jobId in some messages; no scope/correlation |
| Health endpoint for API | Partial | Exists; broker status hardcoded |
| Health endpoint for worker | Missing | None |
| Separation of concerns | Partial | Worker has DB wiring; fat controller |
| README: run, architecture, limitations | Partial | Run and architecture present; limitations missing |
| README: implemented first / cut / next | Missing | |
| CI (optional) | Missing | No workflow |
| Submission: repo with history | Partial | No remote, mostly untracked |

## 3. plan_points.md traceability

| # | Requirement | Status | Note |
|---|---|---|---|
| 1 | Backend C# .NET | Met | Mixed net8/net10 (assessment says .NET 8) |
| 2 | MySQL | Met | |
| 3 | Dedup via persisted CRC32 expression column, indexed; DB source of truth; in-memory dict/LRU | Partial | CRC is a plain column computed in C#; no generated column; no unique key; in-memory dict has no LRU or eviction |
| 4 | Nullability strict | Misreported | Eight nullable warnings suppressed in Shared |
| 5 | Tree view as ready component | Met | `rc-tree` in package.json |
| 6 | Build locally first, then docker | Met | |
| 7 | RabbitMQ | Met | |
| 8 | Many-to-many parent/child table, indexed, README on disabling index | Partial | `JobPageLinks` indexed; disable-index note missing |
| 9 | Clickable tree opens page representation | Missing in effect | Content endpoint always 404 (StoragePath never set) |
| 10 | SimHash of every page, quick keyword search with Hamming | Missing in effect | SimHash never computed or stored |
| 11 | GUID v1, explain security not needed | Partial | v7 used; explanation absent |
| 12 | Status enum stored as int, separate transitions table with nav props | Partial | Stored as char (per later item 13); transitions table exists with raw chars, no status lookup |
| 13 | char status `p/r/c`, enum in C# | Met | `JobStatus` char-backed |
| 14 | POST API, static signed session so services validate without DB; skip roles, explain | Partial | HMAC token works in API; random per-process key; worker cannot validate; roles skipped but not explained in README |
| 15 | Tree loads 3 levels, extends reactively | Partial | Endpoint supports depth/parent; query pattern is N+1 |
| 16 | Local search in tree via SimHash | Missing in effect | Depends on item 10 |
| 17 | Found-link and resolved-link events; blank-text page rows; indexed generated bit for quick counts | Partial | Events exist; indexed `IsCrawled` flag exists (not a generated bit); counts are not derived from it in the progress path |
| 18 | M2M table carries job ID | Met | |
| 19 | M2M may contain duplicates across jobs | Met | |
| 20 | Text on disk mirroring site structure, hash in DB | Partial | Folder structure mirrored; hash never stored; query strings collide |
| 21 | 2D progress graph: yellow mountain (count), blue (bytes), lighter forecast | Not verified | UI not run; forecast logic per comment d unverified |
| 22 | Haxe for JS/React plus shared C# utils | Missing | Generated JS not imported; C# does not use Haxe output |
| 23 | SignalR | Met | Hub plus group broadcasts; hub unauthenticated |

## 4. comments.md traceability (selected)

| Comment | Status |
|---|---|
| a. DTOs in Shared/model folder as JS source | Partial: DTOs moved and split; TS types are hand-written, not generated from them |
| b. Crawler should not access DB; use protocol | Partial: engine does not use DB, but Worker still wires DbContext and `EnsureCreated` |
| c. Haxe also consumed by C# via interface | Missing (B7) |
| d. Lenient URL box | Met (normalizer); UI behaviour unverified |
| e/f/j. Chart reactivity, yellow axis, forecast | Not verified; bytes reactivity open per own comments |
| g/h. Hamming display, tabs, sidebar | Not verified; hover/text depends on B1 |
| i. History link-ratio stats | Partial: ratio shown, computed per N+1 and not per spec |
| new a. Global search | Misreported: stub (B2) |
| new b. URL rewrite to reload page | Not verified (UI routing not reviewed) |
| new c. Delete and prune site data, careful with shared | Partial: implemented, racy and incomplete (root page, N+1) |
| new g. Cancel/exit crawler | Missing (B3) |
| new h. Counts match, SignalR flowing, parallelism, isolated retry client, no retry on 4xx | Partial: 4xx not retried (good); parallel batching; counts/reactivity unresolved |
| new i. Duplicate HTTP requests; block via URL hash | Partial: in-memory hash blocks within a run; Polly logs retries as repeated "Sending" lines, so duplicates in log may be retries, not duplicates; unverified |
| review: CRC in generated column | Missing |
| review: auth filter | Partial: filter exists, path-contains bypass, hub unprotected |
| review: normalizer owns null check | Partial |
| review: attributes for nav properties | Partial: attributes used; `OnModelCreating` retains FK config |
| review: GetJobMetrics recursion? | Answered: not recursive; but loads everything in memory |
| review: profile endpoints | Missing |
| review: replace per-page loops | Missing (see N+1 doc) |
| review: page IDs strictly int | Partial: `long`; YAML says 36-char |
| review: one DTO per file, with migration script | Partial: mostly done; `JobMetricsDto.cs` holds four classes; no migration script found |
| review: HMAC not named ed25519 | Met |
| review: colors out of markup | Partial: `types.ts` helper exists; `History.tsx` and `StartCrawl.tsx` still inline Tailwind color classes |
| review: no hardcoded config | Missing |
| review: UI max-pages setting | Partial: `api.ts` sends `maxPagesLimit`; UI field not verified |
| review: pause/resume | Missing in effect (B3) |
| review: pass hash in message; DTO method to compute or reuse | Partial: hashes passed; `SourceUrl` CRC recomputed in consumer |
| review: file name mirrors folder structure | Partial (see item 20) |
| review: what is Haxe simple auth token | Answered: CRC32-based, insecure; remove or replace |
| newer a. mermaid not ASCII | Met |
| newer b. TRICKS.md linked from README | Met, but contains Misreported claims (LRU, download reuse, Haxe) |
| newer: DB or MQ down reporting | Partial: DB checked, broker hardcoded |
| section 4: gitignore and commit to GitHub | Partial: gitignore present (over-broad `*.html`); no remote, mostly uncommitted |
| section 4: delete list does not update | Not verified |

## 5. AGENTS.md compliance

| Rule | Status |
|---|---|
| C# strict nullability | Not met (suppressions) |
| lizard for low CCN | No evidence of use; 555-line controller |
| legodb for DB models | Not met: YAML out of sync; `EnsureCreated` builds the DB |
| Documented and indexed | Partial: docs exist and drifted; no `.codegraph/` index in the repo |
| codegraph to index/search | Not present in repo |
| Haxe: no Dynamic/cast/`js.Syntax.code` | Met in the sources reviewed (grep of `.hx` files found none); but Haxe is unused |
| Refer to original requirements | Not met: contradictions listed above |

## 6. Risk register

| ID | Severity | Risk | Finding |
|---|---|---|---|
| R1 | High | Stored XSS from crawled HTML in an authenticated origin | Fix per D4 |
| R2 | High | Duplicate/lost data under redelivery; counters wrong | B4 |
| R3 | High | Core features advertised but non-functional (search, preview, cancel) | B1-B3 |
| R4 | High | Unauthenticated SignalR hub; token endpoint open; path-contains auth bypass | Auth section |
| R5 | Medium | Path traversal and file overwrite via crafted URLs; arbitrary DB path read | Storage |
| R6 | Medium | Random HMAC key per process: sessions drop on restart, no cross-service validation | D5 |
| R7 | Medium | Vulnerable `Microsoft.OpenApi 2.0.0` (NU1903) | Update package |
| R8 | Medium | Secrets in compose and code defaults (`root` / `rootpassword`) | Move to env/secret store |
| R9 | Medium | CRC32 collisions silently merge distinct URLs/domains (32-bit keys, birthday risk at about 77k URLs) | Add exact-URL compare in the unique key, or a wider hash |
| R10 | Low | Unbounded in-memory dict, no response size cap, no politeness (robots.txt, per-host rate limit) | Document as limitations |

## 7. Evidence gaps (not audited)

- Docker compose startup, nginx proxying of `/api` and `/hubs`, and Dockerfile builds.
- Live behaviour of the UI (charts, tabs, sidebar, forecast, URL rewrite, delete refresh).
- Haxe compilation and whether `shared.js` matches the current `.hx` sources.
- Query counts and timings (no profiling exists).

## 8. Recommended acceptance gate before submission

1. Search and preview work end to end on a crawled fixture (B1).
2. Same message delivered twice leaves row counts unchanged (test).
3. Cancel stops fetching within seconds and the final status stays `Canceled` (test).
4. Poison message lands in the error queue (test or documented manual check).
5. Worker and API `/health` report real DB and broker state.
6. README contains schema, idempotency, retry/transient, DLQ, limitations, cut/next, and no claims from section 4 marked Misreported.
7. Nullable suppressions removed and build clean; Domain Link Ratio and fixture tests present.
8. Repo committed with a sensible history and `.gitignore` that no longer excludes `index.html`.
