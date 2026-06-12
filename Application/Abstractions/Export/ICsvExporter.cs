namespace Application.Abstractions.Export;

public interface ICsvExporter
{
    Task ExportAsync<T>(IReadOnlyList<T> rows, string filePath, CancellationToken cancellationToken);
    Task ExportAsync<T>(IAsyncEnumerable<T> rows, string filePath, CancellationToken cancellationToken);
}
