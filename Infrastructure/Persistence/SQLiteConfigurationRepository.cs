using Application.Abstractions.Persistence;
using Application.Configuration;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public sealed class SQLiteConfigurationRepository : IConfigurationRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SQLiteConfigurationRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TagRuntimeConfiguration>> LoadTagConfigurationsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tag_id, alarm_enabled, warning_low, alarm_low,
                   warning_high, alarm_high, is_historized, history_interval_ms
            FROM tag_runtime_settings;
            """;
        var result = new List<TagRuntimeConfiguration>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new TagRuntimeConfiguration(
                reader.GetString(0),
                reader.GetBoolean(1),
                ReadNullableDouble(reader, 2),
                ReadNullableDouble(reader, 3),
                ReadNullableDouble(reader, 4),
                ReadNullableDouble(reader, 5),
                reader.GetBoolean(6),
                reader.GetInt32(7)));
        }

        return result;
    }

    public async Task SaveTagConfigurationsAsync(
        IReadOnlyCollection<TagRuntimeConfiguration> configurations,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO tag_runtime_settings (
                tag_id, alarm_enabled, warning_low, alarm_low,
                warning_high, alarm_high, is_historized, history_interval_ms)
            VALUES ($tagId, $alarmEnabled, $warningLow, $alarmLow,
                    $warningHigh, $alarmHigh, $isHistorized, $historyIntervalMs)
            ON CONFLICT(tag_id) DO UPDATE SET
                alarm_enabled = excluded.alarm_enabled,
                warning_low = excluded.warning_low,
                alarm_low = excluded.alarm_low,
                warning_high = excluded.warning_high,
                alarm_high = excluded.alarm_high,
                is_historized = excluded.is_historized,
                history_interval_ms = excluded.history_interval_ms;
            """;
        foreach (var configuration in configurations)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$tagId", configuration.TagId);
            command.Parameters.AddWithValue("$alarmEnabled", configuration.AlarmEnabled);
            command.Parameters.AddWithValue("$warningLow", ToDb(configuration.WarningLow));
            command.Parameters.AddWithValue("$alarmLow", ToDb(configuration.AlarmLow));
            command.Parameters.AddWithValue("$warningHigh", ToDb(configuration.WarningHigh));
            command.Parameters.AddWithValue("$alarmHigh", ToDb(configuration.AlarmHigh));
            command.Parameters.AddWithValue("$isHistorized", configuration.IsHistorized);
            command.Parameters.AddWithValue("$historyIntervalMs", configuration.HistoryIntervalMs);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadRuntimeSettingsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT setting_key, setting_value FROM runtime_settings;";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    public async Task SaveRuntimeSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO runtime_settings (setting_key, setting_value)
            VALUES ($key, $value)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value;
            """;
        foreach (var setting in settings)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$key", setting.Key);
            command.Parameters.AddWithValue("$value", setting.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static double? ReadNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static object ToDb(double? value) => value.HasValue ? value.Value : DBNull.Value;
}
