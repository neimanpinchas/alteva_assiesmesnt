# Alteva Crawler — Distributed Job-Based Web Crawler System

A high-performance, event-driven web crawler built with **.NET 8**, **MassTransit + RabbitMQ**, **MySQL 8**, and **React 18 (Vite + TypeScript + Recharts + rc-tree)**.

For database design insights and performance engineering patterns, see **[TRICKS.md](TRICKS.md)**.  
For network, concurrency, and security edge cases, see **[EDGE_CASES.md](EDGE_CASES.md)**.

---

## 🏛️ System Architecture

```mermaid
flowchart TD
    UI["React 18 + Vite UI (SignalR + Recharts)"] -->|REST API / WebSockets| API["Crawl API Orchestrator (.NET 8)"]
    API -->|Publish Crawl Requested| RMQ["RabbitMQ Broker (MassTransit)"]
    RMQ -->|Consume Crawl Requested| WORKER["Crawl Worker (.NET 8)"]
    WORKER -->|Publish PageCrawled / LinkDiscovered| RMQ
    RMQ -->|Consume Events & Persist| API
    API -->|CRUD Pages / Edges / CRC32 Index| DB[("MySQL 8 Database")]
    WORKER -->|Store Raw HTML| DISK["Local Disk Storage ./storage/"]
```

---

## 📐 Key Database & Architecture Insights

1. **Persistent `url_crc32` Integer Index for $O(1)$ Deduplication**:
   - Uses an indexed `INT UNSIGNED` CRC32 hash column on `Pages` to replace slow string comparisons on long `VARCHAR(2048)` URLs.

2. **Database Ground Truth + In-Memory LRU Cache**:
   - The database stores all verified state as ground truth so crawling jobs can be paused, interrupted, or resumed at any time without data loss.

3. **Many-to-Many `JobPageLinks` Graph**:
   - Stores page content ONCE per URL in `Pages`, while many-to-many `JobPageLinks` records directed graph edges (`JobId`, `SourcePageId`, `TargetPageId`). Indexing `(JobId, SourcePageId)` and `(JobId, TargetPageId)` allows instant sub-page tree retrieval (with optional index disabling during bulk link imports).

4. **Time-Ordered UUID v7 Primary Keys**:
   - Job IDs use sequential `Guid.CreateVersion7()`, preventing MySQL InnoDB B-tree page splits and index fragmentation.

5. **Single `CHAR(1)` Status Column with Transition History**:
   - Uses `'P'`, `'R'`, `'C'`, `'F'`, `'X'` status characters for non-typesafe SQL script compatibility, while `JobStatusTransitions` table logs full state audit history.

6. **Stateless HMAC Session Token Verification**:
   - API middleware (`AuthFilter.cs`) verifies session tokens in memory statelessly without touching MySQL.

7. **64-bit SimHash & Hamming Distance Search**:
   - Computes 64-bit SimHash fingerprints and matches search keywords via bitwise XOR population count.

8. **Resilient Health & Outage Reporting**:
   - `GET /health` returns structured JSON status (`Healthy` / `Degraded`) for database and message queue connectivity.

---

## 🗄️ Page Content Storage (File Store) — Requirement & Trade-offs

Raw page content is **not stored in MySQL**. The worker writes each fetched page to a file store, mirroring the site's folder structure (one root folder per domain), and the database keeps only metadata: `StoragePath`, `ContentHash` (to cross-verify the file), `ByteSize` and `SimHash`.

**Why:** page bodies are large and mostly read-once. Keeping them out of `Pages` avoids table and buffer-pool bloat, keeps backups and replication small, and keeps index scans fast.

**Trade-offs:**
- The database is no longer self-contained. A restore needs the DB **and** the file store, and the two can drift (a row without a file, or an orphan file). `ContentHash` lets us detect this, and delete/prune must remove both.
- Two services need the files: the **worker writes** them and the **API reads** them (page preview, text extraction). Today this only works if both see the same directory.

**Requirement (deployment):**
- The worker and the API **must share the same storage root** (the same machine, or the same mounted volume). `docker-compose.yml` must mount one volume on both services; the API currently has none, so preview does not work in compose until that is added.
- The storage root comes from configuration (`Storage:RootPath`), and the database stores a **path relative to that root**, never an absolute worker path. The API must refuse to read any path that resolves outside the root.

**Known limitation / next step:** a shared local volume does not scale to multiple machines. For multi-host deployments we will move the store behind an interface (e.g. `IPageStore`: `Save`, `Open`, `Delete`) and use object storage such as **S3** (or Azure Blob). Only `StoragePath` semantics (becoming an object key) and the store implementation change; the event and DB schema stay the same.

---

## 🚀 How to Run Locally

### Option 1: Local Development Mode
```bash
# Clean active ports and start local background services
./start-local.sh

# Start React UI
cd src/AltevaCrawler.Ui
npm run dev
```

### Option 2: Multi-Container Docker Compose
```bash
docker-compose up --build
```
- React UI: `http://localhost:5173` (or `http://localhost:3000`)
- API Health Check: `http://localhost:5000/health`
- RabbitMQ Management: `http://localhost:15672` (guest / guest)

---

## 🧪 Running Unit Tests

```bash
dotnet test
```
All **28 unit tests** pass cleanly, covering:
- URL normalization & lenient scheme prepending (`www.` stripping, allow-listing, query strings)
- Domain extraction & domain hash calculation
- 64-bit SimHash calculation & Hamming distance
- Haxe C# vs native C# parity
- Stateless HMAC session token creation & verification
