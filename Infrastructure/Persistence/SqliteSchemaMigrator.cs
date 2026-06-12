using AppLogging;
using Domain.Common;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

internal sealed class SqliteSchemaMigrator
{
    private const string CreateMigrationTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at_utc TEXT NOT NULL
        );
        """;

    private readonly IReadOnlyList<SqliteSchemaMigration> _migrations;
    private readonly Func<DateTime> _utcNow;

    public SqliteSchemaMigrator(
        IEnumerable<SqliteSchemaMigration> migrations,
        Func<DateTime>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        _migrations = migrations.OrderBy(migration => migration.Version).ToArray();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        ValidateMigrationDefinitions(_migrations);
    }

    public int TargetVersion => _migrations.Count == 0 ? 0 : _migrations[^1].Version;

    public async Task<int> MigrateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await EnsureMigrationTableAsync(connection, cancellationToken).ConfigureAwait(false);
        var appliedVersions = await GetAppliedVersionsAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateAppliedVersions(appliedVersions);

        var currentVersion = appliedVersions.Count == 0 ? 0 : appliedVersions[^1];
        AppLogger.Info(
            "SQLite schema migration started | CurrentVersion: {0} | TargetVersion: {1}",
            currentVersion,
            TargetVersion);

        foreach (var migration in _migrations.Where(migration => migration.Version > currentVersion))
        {
            await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
            currentVersion = migration.Version;
        }

        AppLogger.Info("SQLite schema migration completed | Version: {0}", currentVersion);
        return currentVersion;
    }

    private static void ValidateMigrationDefinitions(IReadOnlyList<SqliteSchemaMigration> migrations)
    {
        for (var index = 0; index < migrations.Count; index++)
        {
            var expectedVersion = index + 1;
            var migration = migrations[index];
            if (migration.Version != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"SQLite migrations must be contiguous and start at version 1. " +
                    $"Expected version {expectedVersion}, found {migration.Version}.");
            }

            if (string.IsNullOrWhiteSpace(migration.Sql))
            {
                throw new InvalidOperationException($"SQLite migration {migration.Version} has no SQL.");
            }
        }
    }

    private void ValidateAppliedVersions(IReadOnlyList<int> appliedVersions)
    {
        for (var index = 0; index < appliedVersions.Count; index++)
        {
            var expectedVersion = index + 1;
            var appliedVersion = appliedVersions[index];
            if (appliedVersion != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"SQLite schema migration history is not contiguous. " +
                    $"Expected version {expectedVersion}, found {appliedVersion}.");
            }
        }

        if (appliedVersions.Count > 0 && appliedVersions[^1] > TargetVersion)
        {
            throw new InvalidOperationException(
                $"SQLite database schema version {appliedVersions[^1]} is newer than " +
                $"the supported version {TargetVersion}.");
        }
    }

    private static async Task EnsureMigrationTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CreateMigrationTableSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<int>> GetAppliedVersionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";

        var versions = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private async Task ApplyMigrationAsync(
        SqliteConnection connection,
        SqliteSchemaMigration migration,
        CancellationToken cancellationToken)
    {
        AppLogger.Info("Applying SQLite schema migration | Version: {0}", migration.Version);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using (var migrationCommand = connection.CreateCommand())
            {
                migrationCommand.Transaction = (SqliteTransaction)transaction;
                migrationCommand.CommandText = migration.Sql;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var historyCommand = connection.CreateCommand())
            {
                historyCommand.Transaction = (SqliteTransaction)transaction;
                historyCommand.CommandText = """
                    INSERT INTO schema_migrations (version, applied_at_utc)
                    VALUES ($version, $appliedAtUtc);
                    """;
                historyCommand.Parameters.AddWithValue("$version", migration.Version);
                historyCommand.Parameters.AddWithValue(
                    "$appliedAtUtc",
                    UtcDateTime.Require(_utcNow(), "appliedAtUtc")
                        .ToString("O", global::System.Globalization.CultureInfo.InvariantCulture));
                await historyCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppLogger.Info("Applied SQLite schema migration | Version: {0}", migration.Version);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            AppLogger.Error("SQLite schema migration failed and was rolled back | Version: {0}", migration.Version);
            throw;
        }
    }
}
