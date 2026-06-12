using Application.Abstractions.Persistence;
using Domain.Common;
using Domain.Logs;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public sealed class SQLiteOperationLogRepository : IOperationLogRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SQLiteOperationLogRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AppendAsync(
        IReadOnlyCollection<OperationLog> logs,
        CancellationToken cancellationToken)
    {
        if (logs.Count == 0)
        {
            return;
        }

        foreach (var log in logs)
        {
            Validate(log);
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
            INSERT INTO operation_logs (
                timestamp_utc_ticks, level, category, action, source,
                message, detail, correlation_id)
            VALUES (
                $timestamp, $level, $category, $action, $source,
                $message, $detail, $correlationId);
            """;
        var timestampParameter = command.Parameters.Add("$timestamp", SqliteType.Integer);
        var levelParameter = command.Parameters.Add("$level", SqliteType.Integer);
        var categoryParameter = command.Parameters.Add("$category", SqliteType.Text);
        var actionParameter = command.Parameters.Add("$action", SqliteType.Text);
        var sourceParameter = command.Parameters.Add("$source", SqliteType.Text);
        var messageParameter = command.Parameters.Add("$message", SqliteType.Text);
        var detailParameter = command.Parameters.Add("$detail", SqliteType.Text);
        var correlationIdParameter = command.Parameters.Add("$correlationId", SqliteType.Text);

        foreach (var log in logs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timestampParameter.Value = log.Timestamp.Ticks;
            levelParameter.Value = (int)log.Level;
            categoryParameter.Value = log.Category;
            actionParameter.Value = log.Action;
            sourceParameter.Value = log.Source;
            messageParameter.Value = log.Message;
            detailParameter.Value = (object?)log.Detail ?? DBNull.Value;
            correlationIdParameter.Value = (object?)log.CorrelationId ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<OperationLog>> QueryLatestAsync(
        int count,
        CancellationToken cancellationToken) =>
        QueryAsync(
            new OperationLogQuery(
                DateTime.UnixEpoch,
                DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
                MaxCount: count),
            cancellationToken);

    public async Task<IReadOnlyList<OperationLog>> QueryAsync(
        OperationLogQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);
        if (query.MaxCount == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory
            .OpenReadConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, timestamp_utc_ticks, level, category, action, source,
                   message, detail, correlation_id
            FROM operation_logs
            WHERE timestamp_utc_ticks >= $startTime
              AND timestamp_utc_ticks <= $endTime
              AND ($level IS NULL OR level = $level)
              AND ($category = '' OR category = $category COLLATE NOCASE)
            ORDER BY timestamp_utc_ticks DESC, id DESC
            LIMIT $maxCount;
            """;
        command.Parameters.AddWithValue("$startTime", query.StartTimeUtc.Ticks);
        command.Parameters.AddWithValue("$endTime", query.EndTimeUtc.Ticks);
        command.Parameters.AddWithValue("$level", query.Level.HasValue ? (int)query.Level.Value : DBNull.Value);
        command.Parameters.AddWithValue("$category", query.Category?.Trim() ?? "");
        command.Parameters.AddWithValue("$maxCount", query.MaxCount);

        var result = new List<OperationLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new OperationLog(
                new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
                (OperationLogLevel)reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(6),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetInt64(0)));
        }

        return result;
    }

    private static void Validate(OperationLog log)
    {
        UtcDateTime.Require(log.Timestamp, nameof(log.Timestamp));
        ArgumentException.ThrowIfNullOrWhiteSpace(log.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(log.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(log.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(log.Message);
    }

    private static void Validate(OperationLogQuery query)
    {
        UtcDateTime.Require(query.StartTimeUtc, nameof(query.StartTimeUtc));
        UtcDateTime.Require(query.EndTimeUtc, nameof(query.EndTimeUtc));
        if (query.StartTimeUtc > query.EndTimeUtc)
        {
            throw new ArgumentException("Operation log start time must not be later than end time.", nameof(query));
        }

        if (query.MaxCount is < 0 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Operation log MaxCount must be between 0 and 5000.");
        }
    }
}
