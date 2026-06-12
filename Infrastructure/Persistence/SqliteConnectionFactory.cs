using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _writeConnectionString;
    private readonly string _readConnectionString;
    private readonly SqliteSchemaMigrator _schemaMigrator;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteConnectionFactory(string? databasePath = null)
        : this(databasePath, new SqliteSchemaMigrator(SqliteSchemaMigrations.All))
    {
    }

    internal SqliteConnectionFactory(
        string? databasePath,
        SqliteSchemaMigrator schemaMigrator)
    {
        _schemaMigrator = schemaMigrator ?? throw new ArgumentNullException(nameof(schemaMigrator));
        DatabasePath = Path.GetFullPath(databasePath ?? GetDefaultDatabasePath());
        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("SQLite database directory cannot be resolved.");
        Directory.CreateDirectory(directory);

        _writeConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
        _readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public string DatabasePath { get; }

    public static int CurrentSchemaVersion => SqliteSchemaMigrations.CurrentVersion;

    public static string GetDefaultDatabasePath() =>
        Path.Combine(AppContext.BaseDirectory, "data", "multichannel-monitor.db");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = await OpenCoreAsync(
                _writeConnectionString,
                cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA busy_timeout=5000;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var modeCommand = connection.CreateCommand();
            modeCommand.CommandText = "PRAGMA journal_mode;";
            var journalMode = Convert.ToString(
                await modeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SQLite WAL mode was not enabled. Current mode: {journalMode ?? "unknown"}.");
            }

            var schemaVersion = await _schemaMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            if (schemaVersion != _schemaMigrator.TargetVersion)
            {
                throw new InvalidOperationException(
                    $"SQLite schema migration did not reach target version {_schemaMigrator.TargetVersion}. " +
                    $"Current version: {schemaVersion}.");
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var connection = await OpenCoreAsync(_writeConnectionString, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout=5000;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    public async Task<SqliteConnection> OpenReadConnectionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var connection = await OpenCoreAsync(_readConnectionString, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout=5000;
            PRAGMA query_only=ON;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<SqliteConnection> OpenCoreAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
