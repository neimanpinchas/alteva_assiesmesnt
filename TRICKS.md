# Engineering Tricks & High-Performance Optimization Patterns

This document details the architectural tricks, performance techniques, and domain patterns implemented in the **Alteva Web Crawler** system.

---

## 1. 64-Bit SimHash & Bitwise Hamming Distance Search
- **Concept**: Web page text is tokenized and reduced to a single 64-bit integer fingerprint (`SimHash`).
- **Optimization**: Search queries are hashed into a 64-bit fingerprint, and keyword relevance is measured using **bitwise XOR and Population Count** (`BitOperations.PopCount(querySimHash ^ pageSimHash)`).
- **Benefit**: Extremely fast, low-memory keyword search across thousands of pages without needing a full-text search engine.

---

## 2. In-Memory `ConcurrentDictionary<uint, string>` De-duplication
- **Concept**: Instead of querying MySQL before fetching every candidate link, the worker maintains an in-memory CRC32 dictionary (`ConcurrentDictionary<uint, string>`).
- **Optimization**: Discovered links pass their pre-computed `UrlCrc32` directly in RabbitMQ message DTOs (`LinkDiscoveredEvent`).
- **Benefit**: Immediate $O(1)$ duplicate suppression before issuing HTTP GET requests, eliminating redundant web traffic and database round-trips.

---

## 3. Persistent Generated `url_crc32` Column in MySQL
- **Concept**: The `Pages` table features `UrlCrc32` with a database index (`IX_Pages_UrlCrc32`).
- **Optimization**: Exact-match URL lookups operate on a 32-bit unsigned integer index instead of comparing long `VARCHAR(2048)` strings.
- **Benefit**: Compact B-tree index size and fast index scans.

---

## 4. Single `CHAR(1)` Enum Persistence with C# Type-Safety
- **Concept**: Job status is stored in MySQL as a single `CHAR(1)` column (`'P'`, `'R'`, `'C'`, `'F'`, `'X'`).
- **Optimization**: Non-typesafe SQL scripts and external BI tools can easily filter statuses (`WHERE status = 'R'`), while C# code interacts strictly via the `JobStatus` enum (`Pending`, `Running`, `Completed`, `Failed`, `Canceled`).

---

## 5. Download Reuse Shortcuts & Bandwidth Conservation
- **Concept**: When crawling a URL already stored by another job, the worker creates a relationship in `JobPageLinks` without re-downloading the page.
- **Optimization**: Increments job page counts while adding zero download bytes.
- **Benefit**: Saves network bandwidth, reduces disk usage, and accelerates crawl completion.

---

## 6. On-The-Fly Domain Link Ratio & Indexed `domain_hash`
- **Concept**: The Domain Link Ratio is not statically stored on the page row; it is computed on the fly when retrieving tree branches.
- **Optimization**: MySQL indexes `domain_hash` (`INT UNSIGNED`), allowing fast `COUNT(*)` domain aggregations.

---

## 7. Time-Ordered UUID v7 Primary Keys
- **Concept**: Job IDs use `Guid.CreateVersion7()`.
- **Optimization**: Unlike random UUID v4, UUID v7 is monotonically increasing based on timestamp.
- **Benefit**: Prevents MySQL B-tree page fragmentation during high-throughput insertions.

---

## 8. Stateless HMAC Session Token Verification
- **Concept**: Session tokens are cryptographically signed using HMAC-SHA256 (`Header.Payload.Signature`).
- **Optimization**: API middleware (`AuthFilter.cs`) verifies tokens in memory statelessly without touching MySQL.

---

## 9. Haxe Cross-Target Compilation & C# Contract Interfaces
- **Concept**: Utility logic in `src/SharedHaxe` is compiled to both C# (`AltevaCrawler.Shared/HaxeGenerated`) and JavaScript (`AltevaCrawler.Ui/src/haxeGenerated/shared.js`).
- **Optimization**: C# contract interfaces (`IUrlUtils`, `ISimHash`, `ICrypto`) ensure both targets adhere to identical signatures.

---

## 10. Twin Y-Axes 2D Progress Chart & Smooth Forecast
- **Concept**: Live crawl progress is plotted on a 2D chart using two separate Y-axes (Left for page counts, Right for download bytes).
- **Optimization**: Forecast projection continues smoothly from the current page count rather than starting at zero.
