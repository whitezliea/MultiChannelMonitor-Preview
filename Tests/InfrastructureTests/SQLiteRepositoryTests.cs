using Application.BackgroundWorkers;
using Application.Configuration;
using Application.Queues;
using Domain.Alarms;
using Domain.Logs;
using Domain.Tags;
using Infrastructure.Persistence;

namespace Tests.InfrastructureTests;

public sealed class SQLiteRepositoryTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "MultiChannelMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultDatabasePath_IsInsideExecutableDataDirectory()
    {
        var expected = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "multichannel-monitor.db"));

        Assert.Equal(expected, SqliteConnectionFactory.GetDefaultDatabasePath());
    }

    [Fact]
    public async Task Initialize_CreatesSchemaMigrationHistoryAndEnablesWalMode()
    {
        var factory = CreateFactory();

        await factory.InitializeAsync();

        Assert.True(File.Exists(factory.DatabasePath));
        await using var connection = await factory.OpenConnectionAsync();
        await using var modeCommand = connection.CreateCommand();
        modeCommand.CommandText = "PRAGMA journal_mode;";
        var mode = Convert.ToString(await modeCommand.ExecuteScalarAsync());
        Assert.Equal("wal", mode, ignoreCase: true);

        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('history_samples', 'alarm_events', 'operation_logs',
                           'tag_runtime_settings', 'runtime_settings', 'schema_migrations');
            """;
        Assert.Equal(6L, Convert.ToInt64(await tableCommand.ExecuteScalarAsync()));

        await using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText = """
            SELECT version, applied_at_utc
            FROM schema_migrations
            ORDER BY version;
            """;
        await using var reader = await migrationCommand.ExecuteReaderAsync();
        var versions = new List<int>();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt32(0));
            Assert.True(DateTime.TryParse(
                reader.GetString(1),
                global::System.Globalization.CultureInfo.InvariantCulture,
                global::System.Globalization.DateTimeStyles.RoundtripKind,
                out var appliedAt));
            Assert.Equal(DateTimeKind.Utc, appliedAt.Kind);
        }

        Assert.Equal(Enumerable.Range(1, SqliteConnectionFactory.CurrentSchemaVersion), versions);
    }

    [Fact]
    public async Task Initialize_WhenReopened_DoesNotApplyMigrationTwice()
    {
        var databasePath = GetDatabasePath();
        await new SqliteConnectionFactory(databasePath).InitializeAsync();
        await new SqliteConnectionFactory(databasePath).InitializeAsync();

        await using var connection = await new SqliteConnectionFactory(databasePath).OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";

        Assert.Equal(
            SqliteConnectionFactory.CurrentSchemaVersion,
            Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Initialize_AdoptsLegacyUnversionedSchemaWithoutLosingData()
    {
        var databasePath = GetDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using (var legacyConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
        {
            await legacyConnection.OpenAsync();
            await using var legacyCommand = legacyConnection.CreateCommand();
            legacyCommand.CommandText = """
                CREATE TABLE history_samples (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    tag_id TEXT NOT NULL,
                    value REAL NOT NULL,
                    timestamp_utc_ticks INTEGER NOT NULL,
                    quality INTEGER NOT NULL,
                    alarm_state INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    sequence_no INTEGER NOT NULL
                );
                INSERT INTO history_samples (
                    tag_id, value, timestamp_utc_ticks, quality, alarm_state, source, sequence_no)
                VALUES ('LEGACY.TAG', 12.5, 1, 0, 0, 'legacy', 1);
                """;
            await legacyCommand.ExecuteNonQueryAsync();
        }

        var factory = new SqliteConnectionFactory(databasePath);
        await factory.InitializeAsync();

        await using var connection = await factory.OpenConnectionAsync();
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM history_samples WHERE tag_id = 'LEGACY.TAG';";
        Assert.Equal(1L, Convert.ToInt64(await countCommand.ExecuteScalarAsync()));

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal(
            SqliteConnectionFactory.CurrentSchemaVersion,
            Convert.ToInt32(await versionCommand.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Migrator_WhenMigrationFails_RollsBackSchemaAndVersionRecord()
    {
        var databasePath = GetDatabasePath();
        var migrator = new SqliteSchemaMigrator(
        [
            new SqliteSchemaMigration(1, "CREATE TABLE stable_table (id INTEGER PRIMARY KEY);"),
            new SqliteSchemaMigration(
                2,
                """
                CREATE TABLE rolled_back_table (id INTEGER PRIMARY KEY);
                INSERT INTO missing_table (id) VALUES (1);
                """)
        ]);
        var factory = new SqliteConnectionFactory(databasePath, migrator);

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => factory.InitializeAsync());

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM schema_migrations WHERE version = 1),
                (SELECT COUNT(*) FROM schema_migrations WHERE version = 2),
                (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'stable_table'),
                (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'rolled_back_table');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
    }

    [Fact]
    public async Task Initialize_WhenDatabaseVersionIsNewer_RejectsDatabase()
    {
        var databasePath = GetDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL
                );
                INSERT INTO schema_migrations (version, applied_at_utc)
                VALUES (1, '2026-06-11T00:00:00.0000000Z'),
                       (2, '2026-06-11T00:00:01.0000000Z'),
                       (3, '2026-06-11T00:00:02.0000000Z'),
                       (4, '2026-06-11T00:00:03.0000000Z'),
                       (5, '2026-06-11T00:00:04.0000000Z'),
                       (6, '2026-06-11T00:00:05.0000000Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SqliteConnectionFactory(databasePath).InitializeAsync());

        Assert.Contains("newer than", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OperationLogRepository_PersistsAndFiltersAcrossReopen()
    {
        var databasePath = GetDatabasePath();
        var timestamp = DateTime.UtcNow;
        var repository = new SQLiteOperationLogRepository(new SqliteConnectionFactory(databasePath));
        await repository.AppendAsync(
            [
                new OperationLog(
                    timestamp,
                    OperationLogLevel.Info,
                    "Acquisition",
                    "Started",
                    "Acquisition.Started",
                    "test",
                    "detail-a",
                    "correlation-a"),
                new OperationLog(
                    timestamp.AddSeconds(1),
                    OperationLogLevel.Warning,
                    "Alarm",
                    "Acknowledged",
                    "Alarm.Acknowledged",
                    "test",
                    CorrelationId: "correlation-b")
            ],
            CancellationToken.None);

        var reopened = new SQLiteOperationLogRepository(new SqliteConnectionFactory(databasePath));
        var result = await reopened.QueryAsync(
            new OperationLogQuery(
                timestamp.AddSeconds(-1),
                timestamp.AddSeconds(2),
                OperationLogLevel.Warning,
                "alarm",
                20),
            CancellationToken.None);

        var log = Assert.Single(result);
        Assert.True(log.Id > 0);
        Assert.Equal("Alarm.Acknowledged", log.Action);
        Assert.Equal("correlation-b", log.CorrelationId);
        Assert.Equal(DateTimeKind.Utc, log.Timestamp.Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task OperationLogRepository_RejectsNonUtcTimestamps(DateTimeKind kind)
    {
        var repository = new SQLiteOperationLogRepository(CreateFactory());
        var log = new OperationLog(
            DateTime.SpecifyKind(DateTime.UtcNow, kind),
            OperationLogLevel.Info,
            "Test",
            "Message",
            "Test.Action",
            "test");

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.AppendAsync([log], CancellationToken.None));
    }

    [Fact]
    public async Task ConfigurationRepository_PersistsTagAndRuntimeSettingsAcrossReopen()
    {
        var databasePath = GetDatabasePath();
        var repository = new SQLiteConfigurationRepository(new SqliteConnectionFactory(databasePath));
        var configuration = new TagRuntimeConfiguration(
            "TEST.TAG", false, 2, 1, 8, 9, true, 2500);
        await repository.SaveTagConfigurationsAsync([configuration], CancellationToken.None);
        await repository.SaveRuntimeSettingsAsync(
            new Dictionary<string, string>
            {
                [RuntimeSettingKeys.UiRefreshIntervalMs] = "250",
                [RuntimeSettingKeys.DataGenerateIntervalMs] = "750"
            },
            CancellationToken.None);

        var reopened = new SQLiteConfigurationRepository(new SqliteConnectionFactory(databasePath));
        var tags = await reopened.LoadTagConfigurationsAsync(CancellationToken.None);
        var runtime = await reopened.LoadRuntimeSettingsAsync(CancellationToken.None);

        Assert.Equal(configuration with { Revision = 0 }, Assert.Single(tags));
        Assert.Equal("250", runtime[RuntimeSettingKeys.UiRefreshIntervalMs]);
        Assert.Equal("750", runtime[RuntimeSettingKeys.DataGenerateIntervalMs]);
    }

    [Fact]
    public async Task HistoryRepository_PersistsBatchAndCanQueryAfterReopen()
    {
        var databasePath = GetDatabasePath();
        var firstRepository = new SQLiteHistoryRepository(new SqliteConnectionFactory(databasePath));
        var timestamp = DateTime.UtcNow;
        await firstRepository.AppendAsync(
            [
                new TagValue("TAG.A", 10, timestamp, TagQuality.Good, TagAlarmState.Normal, "frame-1", 1),
                new TagValue("TAG.A", 11, timestamp.AddSeconds(1), TagQuality.Good, TagAlarmState.WarningHigh, "frame-2", 2),
                new TagValue("TAG.B", 20, timestamp, TagQuality.Bad, TagAlarmState.Invalid, "frame-1", 1)
            ],
            CancellationToken.None);

        var reopenedRepository = new SQLiteHistoryRepository(new SqliteConnectionFactory(databasePath));
        var samples = await reopenedRepository.QueryAsync(
            "TAG.A",
            timestamp.AddSeconds(-1),
            timestamp.AddSeconds(2),
            CancellationToken.None);

        Assert.Equal(2, samples.Count);
        Assert.Equal([10d, 11d], samples.Select(sample => sample.Value));
        Assert.Equal([1L, 2L], samples.Select(sample => sample.SequenceNo));
        Assert.Equal(TagAlarmState.WarningHigh, samples[1].AlarmState);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task HistoryRepository_RejectsNonUtcSamples(DateTimeKind kind)
    {
        var repository = new SQLiteHistoryRepository(CreateFactory());
        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow, kind);
        var sample = new TagValue(
            "TAG.A",
            1,
            timestamp,
            TagQuality.Good,
            TagAlarmState.Normal,
            "test",
            1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.AppendAsync([sample], CancellationToken.None));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task HistoryRepository_RejectsNonUtcQueryRange(DateTimeKind kind)
    {
        var repository = new SQLiteHistoryRepository(CreateFactory());
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), kind);
        var end = DateTime.SpecifyKind(DateTime.UtcNow, kind);

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.QueryAsync("TAG.A", start, end, CancellationToken.None));
    }

    [Fact]
    public async Task HistoryRepository_CurrentUtcSampleIsVisibleThroughCurrentUtcEndTime()
    {
        var repository = new SQLiteHistoryRepository(CreateFactory());
        var timestamp = DateTime.UtcNow.AddMilliseconds(-10);
        await repository.AppendAsync(
            [new TagValue("TAG.NOW", 1, timestamp, TagQuality.Good, TagAlarmState.Normal, "test", 1)],
            CancellationToken.None);

        var samples = await repository.QueryAsync(
            "TAG.NOW",
            timestamp.AddMinutes(-1),
            DateTime.UtcNow,
            CancellationToken.None);

        Assert.Single(samples);
        Assert.Equal(DateTimeKind.Utc, samples[0].Timestamp.Kind);
    }

    [Fact]
    public async Task HistoryRepository_IgnoresDuplicateTagFromSameSourceFrame()
    {
        var repository = new SQLiteHistoryRepository(CreateFactory());
        var timestamp = DateTime.UtcNow;
        var sample = new TagValue(
            "TAG.DEDUP",
            1,
            timestamp,
            TagQuality.Good,
            TagAlarmState.Normal,
            Guid.NewGuid().ToString("D"),
            1);

        await repository.AppendAsync([sample], CancellationToken.None);
        await repository.AppendAsync([sample], CancellationToken.None);
        var result = await repository.QueryAsync(
            new Application.Abstractions.Persistence.HistoryQuery(
                sample.TagId,
                timestamp.AddSeconds(-1),
                timestamp.AddSeconds(1)),
            CancellationToken.None);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task AlarmRepository_UpsertsLifecycleAndCanQueryAfterReopen()
    {
        var databasePath = GetDatabasePath();
        var repository = new SQLiteAlarmRepository(new SqliteConnectionFactory(databasePath));
        var triggerTime = DateTime.UtcNow;
        var alarm = new AlarmEvent(
            Guid.NewGuid(),
            "TAG.ALARM",
            AlarmLevel.Alarm,
            AlarmState.Active,
            25,
            triggerTime,
            "Raised");
        await repository.AppendAsync([alarm], CancellationToken.None);
        var recovered = alarm with
        {
            State = AlarmState.Recovered,
            Message = "Recovered",
            AcknowledgeTime = triggerTime.AddSeconds(1),
            RecoverTime = triggerTime.AddSeconds(2)
        };
        await repository.AppendAsync([recovered], CancellationToken.None);

        var reopenedRepository = new SQLiteAlarmRepository(new SqliteConnectionFactory(databasePath));
        var persisted = Assert.Single(await reopenedRepository.QueryLatestAsync(10, CancellationToken.None));

        Assert.Equal(alarm.AlarmId, persisted.AlarmId);
        Assert.Equal(AlarmState.Recovered, persisted.State);
        Assert.Equal("Recovered", persisted.Message);
        Assert.Equal(recovered.AcknowledgeTime?.ToUniversalTime(), persisted.AcknowledgeTime);
        Assert.Equal(recovered.RecoverTime?.ToUniversalTime(), persisted.RecoverTime);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task AlarmRepository_RejectsNonUtcLifecycleTimes(DateTimeKind kind)
    {
        var repository = new SQLiteAlarmRepository(CreateFactory());
        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow, kind);
        var alarm = new AlarmEvent(
            Guid.NewGuid(),
            "TAG.ALARM",
            AlarmLevel.Alarm,
            AlarmState.Active,
            25,
            timestamp,
            "Alarm");

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.AppendAsync([alarm], CancellationToken.None));
    }

    [Fact]
    public async Task WalMode_AllowsWriteWhileReadCursorIsOpen()
    {
        var factory = CreateFactory();
        var repository = new SQLiteHistoryRepository(factory);
        var timestamp = DateTime.UtcNow;
        await repository.AppendAsync(
            [new TagValue("TAG.A", 1, timestamp, TagQuality.Good, TagAlarmState.Normal, "frame-1", 1)],
            CancellationToken.None);

        await using var readConnection = await factory.OpenReadConnectionAsync();
        await using var readCommand = readConnection.CreateCommand();
        readCommand.CommandText = "SELECT * FROM history_samples;";
        await using var reader = await readCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        await repository.AppendAsync(
            [new TagValue("TAG.A", 2, timestamp.AddSeconds(1), TagQuality.Good, TagAlarmState.Normal, "frame-2", 2)],
            CancellationToken.None);

        var samples = await repository.QueryAsync(
            "TAG.A",
            timestamp.AddSeconds(-1),
            timestamp.AddSeconds(2),
            CancellationToken.None);
        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public async Task AlarmPersistWorker_FlushesQueuedEventsWhenStopped()
    {
        var repository = new SQLiteAlarmRepository(CreateFactory());
        var queue = new AlarmEventQueue();
        var worker = new AlarmPersistWorker(
            queue,
            repository,
            TimeSpan.FromSeconds(30),
            maxBatchSize: 100);
        using var cancellation = new CancellationTokenSource();
        var workerTask = worker.RunAsync(cancellation.Token);
        var alarm = new AlarmEvent(
            Guid.NewGuid(),
            "TAG.ALARM",
            AlarmLevel.Warning,
            AlarmState.Active,
            12,
            DateTime.UtcNow,
            "Queued alarm");

        await queue.EnqueueAsync(alarm, CancellationToken.None);
        cancellation.Cancel();
        await workerTask;

        var persisted = Assert.Single(await repository.QueryLatestAsync(10, CancellationToken.None));
        Assert.Equal(alarm.AlarmId, persisted.AlarmId);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private SqliteConnectionFactory CreateFactory() =>
        new(GetDatabasePath());

    private string GetDatabasePath() =>
        Path.Combine(_testDirectory, "data", "test.db");
}
