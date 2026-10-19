# Edge Cases Handled

This document highlights all edge cases, network behaviors, concurrency race conditions, and security safeguards handled by the **Alteva Web Crawler** system.

---

## 1. URL Normalization & Input Edge Cases
- **Lenient URL Scheme Input**: Accepts raw domain names like `auto.com`, `eseqtech.org`, or `matbia.org` and automatically prepends `https://`.
- **`www.` Subdomain Canonicalization**: Normalizes `www.example.com` and `example.com` to the same canonical domain representation.
- **Query Parameter Retention vs Port Trimming**: Preserves unique query parameters (`/item?id=1` vs `/item?id=2`) while dropping default ports (`:80`, `:443`) and trailing slashes.
- **Anchor & Fragment Stripping**: Strips `#section`, `#top`, or `#comments` fragments so identical pages with different anchors are deduplicated.
- **Relative & Protocol-Relative Resolution**: Resolves relative paths (`/about`, `../contact`) and protocol-relative links (`//cdn.example.com/asset`) against the page's base URL.
- **Non-HTTP Scheme Allow-Listing**: Strictly filters out `mailto:`, `tel:`, `javascript:`, `data:`, and `ftp:` schemes.

---

## 2. HTTP, Cloudflare & Network Error Edge Cases
- **Cloudflare & Anti-Bot Challenge Bypass**: Configured `HttpClient` with modern Chrome browser `User-Agent`, `Accept`, and `Accept-Language` headers to prevent Cloudflare bot blocks on sites like `matbia.org`.
- **4xx Dead Link Handling**: Excludes 4xx status codes (404, 403, 400) from Polly retries, publishing dead link status immediately without wasting retry cycles.
- **5xx / Network Timeout Retries**: Applies exponential backoff retries (2 attempts) on transient 5xx server errors and socket timeouts.
- **Automatic Decompression**: Automatically decodes `gzip`, `deflate`, and `br` compressed HTTP response bodies (`DecompressionMethods.All`).

---

## 3. Concurrency, Message Ordering & Foreign Key Race Conditions
- **Event Out-of-Order Delivery**: `ApiLinkDiscoveredConsumer` and `ApiPageCrawledConsumer` check if a `Job` exists in MySQL before creating `JobPageLinks` records, preventing orphan foreign key (`FK_JobPageLinks_Jobs_JobId`) violations if a job was pruned or cancelled.
- **Parallel Crawl Bounding**: Uses `SemaphoreSlim(5)` to bound concurrent HTTP requests, avoiding thread pool starvation or rate-limit bans.
- **Immediate In-Memory Deduplication**: Performs an $O(1)$ check against a per-job `HashSet<uint>` BEFORE issuing HTTP requests, preventing duplicate in-flight requests. It is per job on purpose: a cache shared across jobs made a second crawl of the same site skip all of its links.

---

## 4. Security & File System Edge Cases
- **XSS Prevention in Page Preview**: Extracts visible text from HTML using `HtmlAgilityPack` (removing `<script>`, `<style>`, and comment nodes) and renders plain text in `<pre>` with safe React keyword highlighting (`<mark>`), avoiding raw HTML `dangerouslySetInnerHTML` XSS vulnerabilities.
- **Safe Disk Directory Replication**: Replicates the URL folder structure on disk (`storage/{domain}/path/to/file.txt`), sanitising invalid path characters, keeping query strings apart with a hash suffix, and refusing any read, write or delete that resolves outside the storage root.

---

## 5. Graceful Shutdown & Pause/Resume Edge Cases
- **Cancel & Worker Shutdown**: each job has a `CancellationTokenSource` linked to the message consumer's token, so a cancel request or host shutdown stops fetches, writes and publishes, never publishes `Completed` for a canceled job, and is not reported to the broker as a fault (no retry that would restart the job).
- **Crawl Pause & Resume**: pauses queue execution in memory between batches without destroying the dedupe set or counters. Pausing, resuming or canceling a job in a final state is not yet blocked.

---

## 6. Infrastructure Outage Reporting
- **Database / Queue Outage Handling**: API `GET /health` reports the MySQL state and the worker `GET /health` reports the MassTransit bus state. The API's broker field is still a static string.
- **Worker Startup Without a Database**: a failed database initialisation is logged instead of silently swallowed.

---

## 7. Page Outcomes, Limits & Storage Failures
- **What is a crawled page**: only a 2xx HTML response that was stored counts (`PageCrawledEvent.IsSuccess`). 4xx (never retried), 5xx after retries, network errors and timeouts, non-HTML content types (PDF, images) and pages whose file could not be saved are recorded on the page row but excluded from `TotalPagesCrawled`, bytes and the page limit.
- **Max-pages overrun**: a slot is reserved atomically before a page is stored and released if the write fails, so concurrent tasks cannot exceed `MaxPagesLimit`.
- **Storage write failure**: the write is retried once; if it still fails the page is reported as failed and its slot is released, instead of silently losing the file.
- **Unexpected job error**: publishes `Failed` with a `FailureReason` (also written to `JobStatusTransitions`) so the job does not stay `Running`. An unexpected error on a single page publishes a failed-page event for that page instead of making it vanish.
- **Failed re-crawl of a stored page**: the API keeps the previously stored SimHash, path and size when a later event for the same page is a failure.
- **Same site crawled twice**: dedupe is per job, so both jobs fetch fully (the page rows are shared).
