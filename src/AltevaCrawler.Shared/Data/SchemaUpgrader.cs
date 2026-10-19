namespace AltevaCrawler.Shared.Data;

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Brings a database created by an older build up to the current model. <c>EnsureCreated()</c> only creates
/// missing tables, it never alters existing ones, and the project has no EF migrations yet. Every step is
/// idempotent (it inspects <c>information_schema</c> first), so it is safe to run at every API start-up.
/// </summary>
public static class SchemaUpgrader
{
    /// <summary>MySQL's CRC32 over the column's utf8mb4 bytes equals <c>System.IO.Hashing.Crc32</c> over UTF-8 bytes.</summary>
    public const string UrlCrc32Expression = "CRC32(`NormalizedUrl`)";

    /// <summary>Collision-safe page identity: SHA-256 of the normalized URL as 32 raw bytes (unique-indexed).</summary>
    public const string UrlHashExpression = "UNHEX(SHA2(`NormalizedUrl`, 256))";

    public static async Task ApplyAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return;

        DbConnection connection = db.Database.GetDbConnection();
        bool openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);

        try
        {
            if (await ColumnExistsAsync(connection, "Pages", "UrlCrc32", ct) && !await IsGeneratedAsync(connection, "Pages", "UrlCrc32", ct))
            {
                logger?.LogWarning("Converting Pages.UrlCrc32 to a persisted generated column (CRC32 of NormalizedUrl).");
                await ExecuteAsync(connection, "ALTER TABLE `Pages` DROP COLUMN `UrlCrc32`", ct);
                await ExecuteAsync(connection,
                    $"ALTER TABLE `Pages` ADD COLUMN `UrlCrc32` INT UNSIGNED GENERATED ALWAYS AS ({UrlCrc32Expression}) STORED", ct);
                await ExecuteAsync(connection, "CREATE INDEX `IX_Pages_UrlCrc32` ON `Pages` (`UrlCrc32`)", ct);
            }

            if (await TableExistsAsync(connection, "Pages", ct) && !await ColumnExistsAsync(connection, "Pages", "ByteSize", ct))
            {
                logger?.LogWarning("Adding missing column Pages.ByteSize.");
                await ExecuteAsync(connection, "ALTER TABLE `Pages` ADD COLUMN `ByteSize` BIGINT NOT NULL DEFAULT 0", ct);
            }

            if (await TableExistsAsync(connection, "Pages", ct) && !await ColumnExistsAsync(connection, "Pages", "ErrorKind", ct))
            {
                logger?.LogWarning("Adding missing columns Pages.ErrorKind and Pages.ErrorMessage.");
                await ExecuteAsync(connection,
                    "ALTER TABLE `Pages` ADD COLUMN `ErrorKind` VARCHAR(32) NULL, ADD COLUMN `ErrorMessage` VARCHAR(512) NULL", ct);
            }

            if (await TableExistsAsync(connection, "Pages", ct) && !await ColumnExistsAsync(connection, "Pages", "UrlHash", ct))
            {
                logger?.LogWarning("Merging duplicate pages and adding the unique Pages.UrlHash key.");
                await MergeDuplicatePagesAsync(connection, ct);
                await ExecuteAsync(connection,
                    $"ALTER TABLE `Pages` ADD COLUMN `UrlHash` BINARY(32) GENERATED ALWAYS AS ({UrlHashExpression}) STORED", ct);
                await ExecuteAsync(connection, "CREATE UNIQUE INDEX `UX_Pages_UrlHash` ON `Pages` (`UrlHash`)", ct);
            }

            if (await TableExistsAsync(connection, "JobPageLinks", ct) && !await IndexExistsAsync(connection, "JobPageLinks", "UX_JobPageLinks_Edge", ct))
            {
                logger?.LogWarning("Removing duplicate edges and adding the unique JobPageLinks edge key.");
                await ExecuteAsync(connection,
                    "DELETE l1 FROM `JobPageLinks` l1 JOIN `JobPageLinks` l2 " +
                    "ON l1.`JobId` = l2.`JobId` AND l1.`SourcePageId` = l2.`SourcePageId` AND l1.`TargetPageId` = l2.`TargetPageId` AND l1.`Id` > l2.`Id`", ct);
                await ExecuteAsync(connection,
                    "CREATE UNIQUE INDEX `UX_JobPageLinks_Edge` ON `JobPageLinks` (`JobId`, `SourcePageId`, `TargetPageId`)", ct);
            }
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Older databases contain several rows for the same URL (check-then-insert races). Keep one row per URL (prefer a
    /// crawled one, then the newest crawl, then the lowest id), point every edge at it and delete the rest, so the unique key can be created.
    /// </summary>
    private static async Task MergeDuplicatePagesAsync(DbConnection c, CancellationToken ct)
    {
        await ExecuteAsync(c, "SET SESSION group_concat_max_len = 1000000", ct);
        await ExecuteAsync(c, "DROP TEMPORARY TABLE IF EXISTS `tmp_page_merge`", ct);
        await ExecuteAsync(c,
            "CREATE TEMPORARY TABLE `tmp_page_merge` AS " +
            "SELECT p.`Id` AS `DupId`, k.`KeepId` FROM `Pages` p JOIN (" +
            "  SELECT `NormalizedUrl`, CAST(SUBSTRING_INDEX(GROUP_CONCAT(`Id` ORDER BY `IsCrawled` DESC, `CrawledAt` DESC, `Id` ASC), ',', 1) AS UNSIGNED) AS `KeepId` " +
            "  FROM `Pages` GROUP BY `NormalizedUrl` HAVING COUNT(*) > 1) k " +
            "ON k.`NormalizedUrl` = p.`NormalizedUrl` WHERE p.`Id` <> k.`KeepId`", ct);
        await ExecuteAsync(c, "UPDATE `JobPageLinks` l JOIN `tmp_page_merge` m ON l.`SourcePageId` = m.`DupId` SET l.`SourcePageId` = m.`KeepId`", ct);
        await ExecuteAsync(c, "UPDATE `JobPageLinks` l JOIN `tmp_page_merge` m ON l.`TargetPageId` = m.`DupId` SET l.`TargetPageId` = m.`KeepId`", ct);
        await ExecuteAsync(c, "DELETE p FROM `Pages` p JOIN `tmp_page_merge` m ON p.`Id` = m.`DupId`", ct);
        await ExecuteAsync(c, "DROP TEMPORARY TABLE `tmp_page_merge`", ct);
    }

    private static async Task<bool> IndexExistsAsync(DbConnection c, string table, string index, CancellationToken ct) =>
        await ScalarAsync(c,
            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND INDEX_NAME = @i",
            ct, ("@t", table), ("@i", index)) > 0;

    private static async Task<bool> TableExistsAsync(DbConnection c, string table, CancellationToken ct) =>
        await ScalarAsync(c,
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t",
            ct, ("@t", table)) > 0;

    private static async Task<bool> ColumnExistsAsync(DbConnection c, string table, string column, CancellationToken ct) =>
        await ScalarAsync(c,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c",
            ct, ("@t", table), ("@c", column)) > 0;

    private static async Task<bool> IsGeneratedAsync(DbConnection c, string table, string column, CancellationToken ct) =>
        await ScalarAsync(c,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c AND EXTRA LIKE '%GENERATED%'",
            ct, ("@t", table), ("@c", column)) > 0;

    private static async Task<long> ScalarAsync(DbConnection c, string sql, CancellationToken ct, params (string Name, string Value)[] parameters)
    {
        await using DbCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) AddParameter(cmd, name, value);
        object? result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private static void AddParameter(DbCommand cmd, string name, string value)
    {
        DbParameter parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        cmd.Parameters.Add(parameter);
    }

    private static async Task ExecuteAsync(DbConnection c, string sql, CancellationToken ct)
    {
        await using DbCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
