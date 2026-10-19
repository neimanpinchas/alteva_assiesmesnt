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
- **Immediate In-Memory Deduplication**: Performs an $O(1)$ check against `ConcurrentDictionary<uint, string>` BEFORE issuing HTTP requests, preventing duplicate in-flight requests.

---

## 4. Security & File System Edge Cases
- **XSS Prevention in Page Preview**: Extracts visible text from HTML using `HtmlAgilityPack` (removing `<script>`, `<style>`, and comment nodes) and renders plain text in `<pre>` with safe React keyword highlighting (`<mark>`), avoiding raw HTML `dangerouslySetInnerHTML` XSS vulnerabilities.
- **Safe Disk Directory Replication**: Replicates URL folder structure on disk (`./storage/{domain}/path/to/file.html`), sanitizing invalid path characters and preventing directory traversal attacks.

---

## 5. Graceful Shutdown & Pause/Resume Edge Cases
- **CTRL+C & Worker Shutdown**: Uses per-job `CancellationTokenSource` linked to `IHostApplicationLifetime` so `CTRL+C` or cancellation requests gracefully stop worker loops without overwriting `Canceled` status with `Completed`.
- **Crawl Pause & Resume**: Pauses queue execution gracefully in memory without destroying deduplication state or progress counters.

---

## 6. Infrastructure Outage Reporting
- **Database / Queue Outage Handling**: `GET /health` returns structured JSON status (`Healthy` / `Degraded`) for MySQL and RabbitMQ connections, preventing unhandled runtime exceptions.
