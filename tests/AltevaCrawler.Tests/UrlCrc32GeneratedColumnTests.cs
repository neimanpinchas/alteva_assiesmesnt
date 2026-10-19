namespace AltevaCrawler.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using AltevaCrawler.Shared.Data;
using AltevaCrawler.Shared.Entities;
using AltevaCrawler.Shared.Utilities;

/// <summary>
/// Pages.UrlCrc32 must be a persisted MySQL generated column (plan point 3), never a value assigned from code.
/// </summary>
public class UrlCrc32GeneratedColumnTests
{
    private const string ServerEnvVar = "ALTEVA_TEST_MYSQL";

    private static DbContextOptions<AppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, ServerVersion.Create(8, 0, 36, Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql))
            .Options;

    [Fact]
    public void Model_UrlCrc32_IsStoredComputedColumnOverNormalizedUrl()
    {
        using var db = new AppDbContext(Options("Server=localhost;Database=model_only;Uid=x;Pwd=x;"));
        var property = db.Model.FindEntityType(typeof(Page))!.FindProperty(nameof(Page.UrlCrc32))!;

        Assert.Equal(SchemaUpgrader.UrlCrc32Expression, property.GetComputedColumnSql());
        Assert.True(property.GetIsStored());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Contains(db.Model.FindEntityType(typeof(Page))!.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Page.UrlCrc32) }));
    }

    [Fact]
    public void CSharpCrc32_MatchesMySqlDocumentedVectors()
    {
        // From the MySQL manual: SELECT CRC32('MySQL') -> 3259397556; standard check value for "123456789".
        Assert.Equal(3259397556u, UrlNormalizer.ComputeCrc32("MySQL"));
        Assert.Equal(3421780262u, UrlNormalizer.ComputeCrc32("123456789"));
    }

    /// <summary>Runs only when ALTEVA_TEST_MYSQL points at a server (user with CREATE/DROP DATABASE), e.g.
    /// "Server=localhost;Port=3306;Uid=root;Pwd=...;". It uses and drops a scratch database.</summary>
    [Fact]
    public async Task MySql_LegacyPlainColumn_IsUpgradedAndGeneratedValueMatchesCSharp()
    {
        string? server = Environment.GetEnvironmentVariable(ServerEnvVar);
        if (string.IsNullOrWhiteSpace(server)) return;

        string dbName = "alteva_it_" + Guid.NewGuid().ToString("N")[..12];
        await using var admin = new AppDbContext(Options(server));
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE `{dbName}`");
        try
        {
            string cs = server.TrimEnd(';') + $";Database={dbName};";
            await using var db = new AppDbContext(Options(cs));
            await db.Database.EnsureCreatedAsync();

            // Emulate a database created by an older build: plain UrlCrc32 column, no ByteSize.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Pages` DROP COLUMN `UrlCrc32`, DROP COLUMN `ByteSize`, DROP COLUMN `ErrorKind`, DROP COLUMN `ErrorMessage`");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Pages` ADD COLUMN `UrlCrc32` INT UNSIGNED NOT NULL DEFAULT 0");

            await SchemaUpgrader.ApplyAsync(db);
            await SchemaUpgrader.ApplyAsync(db); // idempotent

            string[] urls =
            {
                "https://example.com/",
                "https://example.com/search?q=cat&page=2",
                "https://example.com/%D7%A9%D7%9C%D7%95%D7%9D",
                "http://sub.domain.co.il/a/b/c.html",
            };
            foreach (string url in urls)
            {
                db.Pages.Add(new Page { Url = url, NormalizedUrl = url, Domain = UrlNormalizer.ExtractDomain(url), DomainHash = 1 });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            // Error columns were re-added by the upgrader and round-trip through EF.
            var failed = new Page { Url = "https://bad.example/", NormalizedUrl = "https://bad.example/", Domain = "bad.example", DomainHash = 2,
                                    StatusCode = 500, ErrorKind = "ssl", ErrorMessage = "SSL/TLS error: UntrustedRoot" };
            db.Pages.Add(failed);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var reloaded = await db.Pages.AsNoTracking().SingleAsync(p => p.Id == failed.Id);
            Assert.Equal("ssl", reloaded.ErrorKind);
            Assert.Equal("SSL/TLS error: UntrustedRoot", reloaded.ErrorMessage);
            db.Pages.Remove(await db.Pages.SingleAsync(p => p.Id == failed.Id));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var stored = await db.Pages.AsNoTracking().ToListAsync();
            Assert.Equal(urls.Length, stored.Count);
            foreach (var page in stored)
            {
                Assert.Equal(UrlNormalizer.ComputeCrc32(page.NormalizedUrl), page.UrlCrc32);
            }

            // The integer index is usable for the lookup the consumers perform.
            uint crc = UrlNormalizer.ComputeCrc32(urls[1]);
            Assert.Single(await db.Pages.Where(p => p.UrlCrc32 == crc && p.NormalizedUrl == urls[1]).ToListAsync());
        }
        finally
        {
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS `{dbName}`");
        }
    }
}
