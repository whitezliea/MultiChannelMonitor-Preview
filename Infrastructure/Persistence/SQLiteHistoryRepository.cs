using Application.Abstractions.Persistence;
using Domain.Common;
using Domain.Tags;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public sealed class SQLiteHistoryRepository : IHistoryRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SQLiteHistoryRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AppendAsync(
        IReadOnlyCollection<TagValue> samples,
        CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return;
        }

        foreach (var sample in samples)
        {
            UtcDateTime.Require(sample.Timestamp, $"{nameof(samples)}.{nameof(sample.Timestamp)}");
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
            INSERT OR IGNORE INTO history_samples (
                tag_id,
                value,
                timestamp_utc_ticks,
                quality,
                alarm_state,
                source,
                sequence_no)
            VALUES (
                $tagId,
                $value,
                $timestampUtcTicks,
                $quality,
                $alarmState,
                $source,
                $sequenceNo);
            """;
        var tagIdParameter = command.Parameters.Add("$tagId", SqliteType.Text);
        var valueParameter = command.Parameters.Add("$value", SqliteType.Real);
        var timestampParameter = command.Parameters.Add("$timestampUtcTicks", SqliteType.Integer);
        var qualityParameter = command.Parameters.Add("$quality", SqliteType.Integer);
        var alarmStateParameter = command.Parameters.Add("$alarmState", SqliteType.Integer);
        var sourceParameter = command.Parameters.Add("$source", SqliteType.Text);
        var sequenceParameter = command.Parameters.Add("$sequenceNo", SqliteType.Integer);

        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tagIdParameter.Value = sample.TagId;
            valueParameter.Value = sample.Value;
            timestampParameter.Value = sample.Timestamp.Ticks;
            qualityParameter.Value = (int)sample.Quality;
            alarmStateParameter.Value = (int)sample.AlarmState;
            sourceParameter.Value = sample.Source;
            sequenceParameter.Value = sample.SequenceNo;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryQueryResult<TagValue>> QueryAsync(
        HistoryQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();

        await using var connection = await _connectionFactory
            .OpenReadConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        long totalCount;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = """
                SELECT COUNT(*)
                FROM history_samples
                WHERE tag_id = $tagId
                  AND timestamp_utc_ticks >= $startTimeUtcTicks
                  AND timestamp_utc_ticks <= $endTimeUtcTicks;
                """;
            AddQueryParameters(countCommand, query);
            totalCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        }

        await using var command = connection.CreateCommand();
        var sortDirection = query.SortDirection == HistorySortDirection.Descending ? "DESC" : "ASC";
        command.CommandText = $"""
            SELECT
                tag_id,
                value,
                timestamp_utc_ticks,
                quality,
                alarm_state,
                source,
                sequence_no
            FROM history_samples
            WHERE tag_id = $tagId
              AND timestamp_utc_ticks >= $startTimeUtcTicks
              AND timestamp_utc_ticks <= $endTimeUtcTicks
            ORDER BY timestamp_utc_ticks {sortDirection}, id {sortDirection}
            LIMIT $pageSize OFFSET $offset;
            """;
        AddQueryParameters(command, query);
        command.Parameters.AddWithValue("$pageSize", query.PageSize);
        command.Parameters.AddWithValue("$offset", checked((query.Page - 1) * query.PageSize));

        var samples = new List<TagValue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            samples.Add(new TagValue(
                reader.GetString(0),
                reader.GetDouble(1),
                new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
                (TagQuality)reader.GetInt32(3),
                (TagAlarmState)reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt64(6)));
        }

        return new HistoryQueryResult<TagValue>(samples, totalCount, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<TagValue>> QueryAsync(
        string tagId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken) =>
        (await QueryAsync(
            new HistoryQuery(tagId, startTime, endTime, 1, HistoryQuery.MaximumPageSize),
            cancellationToken).ConfigureAwait(false)).Items;

    public async Task<int> DeleteBeforeAsync(
        DateTime cutoffUtc,
        int maxRows,
        CancellationToken cancellationToken)
    {
        UtcDateTime.Require(cutoffUtc, nameof(cutoffUtc));
        if (maxRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows));
        }

        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM history_samples
            WHERE id IN (
                SELECT id
                FROM history_samples
                WHERE timestamp_utc_ticks < $cutoffUtcTicks
                ORDER BY timestamp_utc_ticks
                LIMIT $maxRows
            );
            """;
        command.Parameters.AddWithValue("$cutoffUtcTicks", cutoffUtc.Ticks);
        command.Parameters.AddWithValue("$maxRows", maxRows);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddQueryParameters(SqliteCommand command, HistoryQuery query)
    {
        command.Parameters.AddWithValue("$tagId", query.TagId);
        command.Parameters.AddWithValue("$startTimeUtcTicks", query.StartTimeUtc.Ticks);
        command.Parameters.AddWithValue("$endTimeUtcTicks", query.EndTimeUtc.Ticks);
    }
}
