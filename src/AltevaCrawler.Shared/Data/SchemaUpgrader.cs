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
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

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
