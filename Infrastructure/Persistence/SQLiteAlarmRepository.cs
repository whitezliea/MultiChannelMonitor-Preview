using Application.Abstractions.Persistence;
using Domain.Common;
using Domain.Alarms;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public sealed class SQLiteAlarmRepository : IAlarmRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SQLiteAlarmRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AppendAsync(
        IReadOnlyCollection<AlarmEvent> alarms,
        CancellationToken cancellationToken)
    {
        if (alarms.Count == 0)
        {
            return;
        }

        foreach (var alarm in alarms)
        {
            UtcDateTime.Require(alarm.TriggerTime, $"{nameof(alarms)}.{nameof(alarm.TriggerTime)}");
            UtcDateTime.Require(alarm.AcknowledgeTime, $"{nameof(alarms)}.{nameof(alarm.AcknowledgeTime)}");
            UtcDateTime.Require(alarm.RecoverTime, $"{nameof(alarms)}.{nameof(alarm.RecoverTime)}");
            UtcDateTime.Require(alarm.LastUpdatedTime, $"{nameof(alarms)}.{nameof(alarm.LastUpdatedTime)}");
        }

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO alarm_events (
                alarm_id,
                tag_id,
                level,
                state,
                trigger_value,
                trigger_time_utc_ticks,
                message,
                acknowledge_time_utc_ticks,
                recover_time_utc_ticks,
                alarm_type,
                last_updated_time_utc_ticks,
                close_reason)
            VALUES (
                $alarmId,
                $tagId,
                $level,
                $state,
                $triggerValue,
                $triggerTimeUtcTicks,
                $message,
                $acknowledgeTimeUtcTicks,
                $recoverTimeUtcTicks,
                $alarmType,
                $lastUpdatedTimeUtcTicks,
                $closeReason)
            ON CONFLICT(alarm_id) DO UPDATE SET
                tag_id = excluded.tag_id,
                level = excluded.level,
                state = excluded.state,
                trigger_value = excluded.trigger_value,
                trigger_time_utc_ticks = excluded.trigger_time_utc_ticks,
                message = excluded.message,
                acknowledge_time_utc_ticks = excluded.acknowledge_time_utc_ticks,
                recover_time_utc_ticks = excluded.recover_time_utc_ticks,
                alarm_type = excluded.alarm_type,
                last_updated_time_utc_ticks = excluded.last_updated_time_utc_ticks,
                close_reason = excluded.close_reason;
            """;
        var alarmIdParameter = command.Parameters.Add("$alarmId", SqliteType.Text);
        var tagIdParameter = command.Parameters.Add("$tagId", SqliteType.Text);
        var levelParameter = command.Parameters.Add("$level", SqliteType.Integer);
        var stateParameter = command.Parameters.Add("$state", SqliteType.Integer);
        var triggerValueParameter = command.Parameters.Add("$triggerValue", SqliteType.Real);
        var triggerTimeParameter = command.Parameters.Add("$triggerTimeUtcTicks", SqliteType.Integer);
        var messageParameter = command.Parameters.Add("$message", SqliteType.Text);
        var acknowledgeTimeParameter = command.Parameters.Add("$acknowledgeTimeUtcTicks", SqliteType.Integer);
        var recoverTimeParameter = command.Parameters.Add("$recoverTimeUtcTicks", SqliteType.Integer);
        var alarmTypeParameter = command.Parameters.Add("$alarmType", SqliteType.Integer);
        var lastUpdatedTimeParameter = command.Parameters.Add("$lastUpdatedTimeUtcTicks", SqliteType.Integer);
        var closeReasonParameter = command.Parameters.Add("$closeReason", SqliteType.Text);

        foreach (var alarm in alarms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            alarmIdParameter.Value = alarm.AlarmId.ToString("D");
            tagIdParameter.Value = alarm.TagId;
            levelParameter.Value = (int)alarm.Level;
            stateParameter.Value = (int)alarm.State;
            triggerValueParameter.Value = alarm.TriggerValue;
            triggerTimeParameter.Value = alarm.TriggerTime.Ticks;
            messageParameter.Value = alarm.Message;
            acknowledgeTimeParameter.Value = ToDatabaseValue(alarm.AcknowledgeTime);
            recoverTimeParameter.Value = ToDatabaseValue(alarm.RecoverTime);
            alarmTypeParameter.Value = (int)alarm.AlarmType;
            lastUpdatedTimeParameter.Value = ToDatabaseValue(alarm.LastUpdatedTime ?? alarm.TriggerTime);
            closeReasonParameter.Value = alarm.CloseReason is null ? DBNull.Value : alarm.CloseReason;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlarmEvent>> QueryLatestAsync(
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory
            .OpenReadConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                alarm_id,
                tag_id,
                level,
                state,
                trigger_value,
                trigger_time_utc_ticks,
                message,
                acknowledge_time_utc_ticks,
                recover_time_utc_ticks,
                alarm_type,
                last_updated_time_utc_ticks,
                close_reason
            FROM alarm_events
            ORDER BY trigger_time_utc_ticks DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        var alarms = new List<AlarmEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            alarms.Add(ReadAlarm(reader));
        }

        return alarms;
    }

    public async Task<IReadOnlyList<AlarmEvent>> QueryOpenAlarmsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectColumns}
            FROM alarm_events
            WHERE state IN ($active, $acknowledged)
            ORDER BY trigger_time_utc_ticks DESC;
            """;
        command.Parameters.AddWithValue("$active", (int)AlarmState.Active);
        command.Parameters.AddWithValue("$acknowledged", (int)AlarmState.Acknowledged);
        return await ReadAlarmsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AlarmQueryResult> QueryAsync(AlarmQuery query, CancellationToken cancellationToken)
    {
        query.Validate();
        await using var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        var where = """
            WHERE trigger_time_utc_ticks >= $startUtcTicks
              AND trigger_time_utc_ticks <= $endUtcTicks
              AND ($tagId IS NULL OR tag_id = $tagId)
              AND ($level IS NULL OR level = $level)
              AND ($state IS NULL OR state = $state)
            """;
        long totalCount;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"SELECT COUNT(*) FROM alarm_events {where};";
            AddQueryParameters(countCommand, query);
            totalCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        }

        await using var command = connection.CreateCommand();
        var direction = query.SortDirection == AlarmSortDirection.Ascending ? "ASC" : "DESC";
        command.CommandText = $"""
            {SelectColumns}
            FROM alarm_events
            {where}
            ORDER BY trigger_time_utc_ticks {direction}, alarm_id {direction}
            LIMIT $pageSize OFFSET $offset;
            """;
        AddQueryParameters(command, query);
        command.Parameters.AddWithValue("$pageSize", query.PageSize);
        command.Parameters.AddWithValue("$offset", checked((query.Page - 1) * query.PageSize));
        var items = await ReadAlarmsAsync(command, cancellationToken).ConfigureAwait(false);
        return new AlarmQueryResult(items, totalCount, query.Page, query.PageSize);
    }

    private const string SelectColumns = """
        SELECT alarm_id, tag_id, level, state, trigger_value,
               trigger_time_utc_ticks, message, acknowledge_time_utc_ticks,
               recover_time_utc_ticks, alarm_type, last_updated_time_utc_ticks,
               close_reason
        """;

    private static void AddQueryParameters(SqliteCommand command, AlarmQuery query)
    {
        command.Parameters.AddWithValue("$startUtcTicks", query.StartTimeUtc.Ticks);
        command.Parameters.AddWithValue("$endUtcTicks", query.EndTimeUtc.Ticks);
        command.Parameters.AddWithValue("$tagId", string.IsNullOrWhiteSpace(query.TagId) ? DBNull.Value : query.TagId.Trim());
        command.Parameters.AddWithValue("$level", query.Level.HasValue ? (int)query.Level.Value : DBNull.Value);
        command.Parameters.AddWithValue("$state", query.State.HasValue ? (int)query.State.Value : DBNull.Value);
    }

    private static async Task<IReadOnlyList<AlarmEvent>> ReadAlarmsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var alarms = new List<AlarmEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) alarms.Add(ReadAlarm(reader));
        return alarms;
    }

    private static AlarmEvent ReadAlarm(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        (AlarmLevel)reader.GetInt32(2),
        (AlarmState)reader.GetInt32(3),
        reader.GetDouble(4),
        new DateTime(reader.GetInt64(5), DateTimeKind.Utc),
        reader.GetString(6),
        ReadNullableDateTime(reader, 7),
        ReadNullableDateTime(reader, 8),
        (Domain.Tags.TagAlarmState)reader.GetInt32(9),
        ReadNullableDateTime(reader, 10),
        reader.IsDBNull(11) ? null : reader.GetString(11));

    private static object ToDatabaseValue(DateTime? value) =>
        value.HasValue ? value.Value.Ticks : DBNull.Value;

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTime(reader.GetInt64(ordinal), DateTimeKind.Utc);
}
