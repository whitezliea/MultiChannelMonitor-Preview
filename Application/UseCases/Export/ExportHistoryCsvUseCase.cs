using Application.Abstractions.Export;
using Application.Abstractions.Persistence;
using Application.Services;
using Domain.Logs;
using Domain.Tags;

namespace Application.UseCases.Export;

public sealed class ExportHistoryCsvUseCase
{
    private readonly ICsvExporter _csvExporter;
    private readonly IHistoryRepository? _repository;
    private readonly OperationLogService? _operationLogService;

    public ExportHistoryCsvUseCase(ICsvExporter csvExporter)
    {
        _csvExporter = csvExporter;
    }

    public ExportHistoryCsvUseCase(
        ICsvExporter csvExporter,
        IHistoryRepository repository,
        OperationLogService operationLogService)
    {
        _csvExporter = csvExporter;
        _repository = repository;
        _operationLogService = operationLogService;
    }

    public Task ExecuteAsync(IReadOnlyList<TagValue> samples, string filePath, CancellationToken cancellationToken) =>
        _csvExporter.ExportAsync(samples, filePath, cancellationToken);

    public async Task<long> ExecuteAsync(
        HistoryQuery query,
        string filePath,
        TimeZoneInfo displayTimeZone,
        CancellationToken cancellationToken)
    {
        if (_repository is null)
        {
            throw new InvalidOperationException("Paged history export is not configured.");
        }

        query.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(displayTimeZone);
        var fullPath = Path.GetFullPath(filePath);
        var temporaryPath = fullPath + ".tmp";
        long exportedCount = 0;
        try
        {
            await _csvExporter.ExportAsync(ReadRowsAsync(), temporaryPath, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
            if (_operationLogService is not null)
            {
                await _operationLogService.WriteAsync(
                    OperationLogLevel.Info,
                    "Export",
                    "HistoryCsv.Exported",
                    nameof(ExportHistoryCsvUseCase),
                    "History CSV exported.",
                    $"TagId={query.TagId}; Path={fullPath}; Rows={exportedCount}",
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }

            return exportedCount;
        }
        catch (Exception exception)
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (_operationLogService is not null && exception is not OperationCanceledException)
            {
                await _operationLogService.WriteAsync(
                    OperationLogLevel.Error,
                    "Export",
                    "HistoryCsv.Failed",
                    nameof(ExportHistoryCsvUseCase),
                    "History CSV export failed.",
                    $"TagId={query.TagId}; Path={fullPath}; Error={exception.Message}",
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        async IAsyncEnumerable<HistoryCsvRow> ReadRowsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken enumerationCancellation = default)
        {
            const int exportPageSize = HistoryQuery.MaximumPageSize;
            var page = 1;
            while (true)
            {
                var result = await _repository.QueryAsync(
                    query with { Page = page, PageSize = exportPageSize },
                    enumerationCancellation).ConfigureAwait(false);
                foreach (var sample in result.Items)
                {
                    enumerationCancellation.ThrowIfCancellationRequested();
                    exportedCount++;
                    yield return new HistoryCsvRow(
                        sample.TagId,
                        sample.Timestamp.ToString("O", global::System.Globalization.CultureInfo.InvariantCulture),
                        TimeZoneInfo.ConvertTimeFromUtc(sample.Timestamp, displayTimeZone)
                            .ToString("yyyy-MM-dd HH:mm:ss.fff", global::System.Globalization.CultureInfo.InvariantCulture),
                        displayTimeZone.Id,
                        sample.Value,
                        sample.Quality.ToString(),
                        sample.AlarmState.ToString(),
                        sample.Source,
                        sample.SequenceNo);
                }

                if (!result.HasNextPage)
                {
                    yield break;
                }

                page++;
            }
        }
    }

    public sealed record HistoryCsvRow(
        string TagId,
        string TimestampUtc,
        string TimestampLocal,
        string LocalTimeZone,
        double Value,
        string Quality,
        string AlarmState,
        string Source,
        long SequenceNo);
}
