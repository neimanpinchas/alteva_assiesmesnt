# Engineering Tricks & High-Performance Optimization Patterns

This document details the architectural insights, database design philosophy, performance techniques, and domain patterns implemented in the **Alteva Web Crawler** system.

---

## 📐 Database Structure Insights & Design Philosophy

The database schema (`Jobs`, `Pages`, `JobPageLinks`, `JobStatusTransitions`) was designed around specific performance insights:

### 1. Indexed Persistent `url_crc32` Integer Column for $O(1)$ Deduplication
- **Insight**: Comparing `VARCHAR(2048)` strings on every link check causes heavy B-tree index traversal and disk I/O in MySQL.
- **Implementation**: The `Pages` table features `UrlCrc32` (`INT UNSIGNED`) with index `IX_Pages_UrlCrc32`. Lookups filter on the 32-bit CRC32 integer first before verifying exact URL string equality.

### 2. Database Ground Truth + In-Memory Cache for Interruptible Crawling
- **Insight**: The database must remain the single source of truth so crawling jobs can be interrupted (paused/killed) and resumed at any time without data corruption.
- **Implementation**: In-memory `ConcurrentDictionary<uint, string>`LRU cache suppresses duplicates in memory during execution, while all verified state is saved in MySQL.

### 3. Many-to-Many `JobPageLinks` Graph for Shared Page Disambiguation
- **Insight**: A single web page can be linked by multiple parent pages across different crawl jobs. A simple `ParentPageId` column on `Pages` would duplicate page content rows across jobs.
- **Implementation**:
  - `Pages` stores unique page metadata & content ONCE per normalized URL.
  - `JobPageLinks` records many-to-many directed graph edges (`JobId`, `SourcePageId`, `TargetPageId`).
  - Indexed foreign keys `IX_JobPageLinks_Job_Source` and `IX_JobPageLinks_Job_Target` allow instant sub-page tree retrieval.
  - **Index Disabling Note**: For ultra-high-throughput bulk link insertion, `IX_JobPageLinks_Job_Source` and `IX_JobPageLinks_Job_Target` non-clustered indexes can be temporarily disabled via `ALTER TABLE JobPageLinks DISABLE KEYS;` during mass ingestion and rebuilt afterward (`ENABLE KEYS;`).

### 4. Sequential Time-Ordered UUID v7 (`Guid.CreateVersion7()`)
- **Insight**: Random UUID v4 causes severe B-tree page splits and index fragmentation in MySQL InnoDB clustered indexes.
- **Implementation**: Job IDs use sequential time-ordered UUID v7 (`Guid.CreateVersion7()`), ensuring monotonically increasing primary keys that preserve page insertion locality and support fast date-range queries.

### 5. Single `CHAR(1)` Status Column & Status Transition Audit Table
- **Insight**: External SQL tools, non-typesafe scripts, and BI dashboards need simple status filtering without complex joins.
- **Implementation**:
  - `Jobs.status` uses a single `CHAR(1)` (`'P'`, `'R'`, `'C'`, `'F'`, `'X'`), mapped to C# `JobStatus` enum (`Pending`, `Running`, `Completed`, `Failed`, `Canceled`).
  - `JobStatusTransitions` table records full state transition history with timestamps and audit reasons.

### 6. On-the-Fly Domain Link Ratio & Indexed `domain_hash`
- **Insight**: Storing static domain link ratios on page rows creates stale data when new links are discovered.
- **Implementation**: Domain link ratios are calculated dynamically on the tree branches. `Pages` indexes `domain_hash` (`INT UNSIGNED`), enabling instant `COUNT(*)` domain matching queries.

---

## ⚡ Additional Performance Tricks

### 7. 64-Bit SimHash & Bitwise Hamming Distance Search
- Tokenizes page text into a 64-bit integer fingerprint (`SimHash`). Search queries are hashed into a 64-bit fingerprint and matched using bitwise XOR and Population Count (`BitOperations.PopCount(queryHash ^ pageHash)`).

### 8. Download Reuse Shortcuts & Bandwidth Conservation
- When a page URL was previously crawled by another job, the worker links the existing page in `JobPageLinks` without re-downloading HTML, saving network bandwidth and disk space.

### 9. Stateless HMAC Session Token Verification
- Session tokens are cryptographically signed using HMAC-SHA256 (`Header.Payload.Signature`) and verified in memory by `AuthFilter.cs` without querying MySQL.

### 10. Twin Y-Axes 2D Progress Chart & Smooth Forecast
- Live progress is rendered on a 2D chart using two separate Y-axes (Left for page counts, Right for download bytes), with a smooth forecast continuation curve based on queue depth.
