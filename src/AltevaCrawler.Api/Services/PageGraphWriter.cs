namespace AltevaCrawler.Api.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using AltevaCrawler.Shared.Data;
using AltevaCrawler.Shared.Entities;
using AltevaCrawler.Shared.Utilities;

/// <summary>
/// Idempotent, order-independent writes for the page graph. Events from the worker arrive concurrently and in any order
/// (a link event can be processed before the page event of its source page, the same event can be delivered twice), so
/// every write is "get or create" backed by the unique keys on <c>Pages.UrlHash</c> and
/// <c>JobPageLinks (JobId, SourcePageId, TargetPageId)</c>: a lost race re-reads the winner's row instead of failing or duplicating.
/// </summary>
public class PageGraphWriter
{
    private readonly AppDbContext _db;

    public PageGraphWriter(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the page for this normalized URL, creating an uncrawled placeholder row if none exists yet.</summary>
    public async Task<Page> GetOrCreatePageAsync(string normalizedUrl, CancellationToken ct = default)
    {
        uint crc = UrlNormalizer.ComputeCrc32(normalizedUrl);

        Page? existing = await FindAsync(normalizedUrl, crc, ct);
        if (existing != null) return existing;

        string domain = UrlNormalizer.ExtractDomain(normalizedUrl);
        var page = new Page
        {
            Url = normalizedUrl,
            NormalizedUrl = normalizedUrl,
            Domain = domain,
            DomainHash = UrlNormalizer.ComputeDomainHash(domain),
            IsCrawled = false
        };
        _db.Pages.Add(page);

        try
        {
            await _db.SaveChangesAsync(ct);
            return page;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Another consumer created the same URL first: use its row.
            _db.Entry(page).State = EntityState.Detached;
            return await FindAsync(normalizedUrl, crc, ct)
                ?? throw new InvalidOperationException($"Page {normalizedUrl} vanished after a duplicate-key conflict.");
        }
    }

    /// <summary>Adds the edge unless it already exists (same job, source and target). Returns true when a row was created.</summary>
    public async Task<bool> TryAddLinkAsync(string jobId, long sourcePageId, long targetPageId, CancellationToken ct = default)
    {
        bool exists = await _db.JobPageLinks.AsNoTracking().AnyAsync(l =>
            l.JobId == jobId && l.SourcePageId == sourcePageId && l.TargetPageId == targetPageId, ct);
        if (exists) return false;

        var link = new JobPageLink
        {
            JobId = jobId,
            SourcePageId = sourcePageId,
            TargetPageId = targetPageId,
            CreatedAt = DateTime.UtcNow
        };
        _db.JobPageLinks.Add(link);

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            _db.Entry(link).State = EntityState.Detached;
            return false;
        }
    }

    private Task<Page?> FindAsync(string normalizedUrl, uint crc, CancellationToken ct) =>
        _db.Pages.FirstOrDefaultAsync(p => p.UrlCrc32 == crc && p.NormalizedUrl == normalizedUrl, ct);

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}
