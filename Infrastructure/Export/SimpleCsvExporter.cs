using System.Reflection;
using System.Text;
using Application.Abstractions.Export;

namespace Infrastructure.Export;

public sealed class SimpleCsvExporter : ICsvExporter
{
    public async Task ExportAsync<T>(IReadOnlyList<T> rows, string filePath, CancellationToken cancellationToken)
    {
        await ExportAsync(ToAsyncEnumerable(rows, cancellationToken), filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync<T>(IAsyncEnumerable<T> rows, string filePath, CancellationToken cancellationToken)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(string.Join(",", properties.Select(property => property.Name))).ConfigureAwait(false);

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var values = properties.Select(property => Convert.ToString(property.GetValue(row), global::System.Globalization.CultureInfo.InvariantCulture) ?? "");
            await writer.WriteLineAsync(string.Join(",", values.Select(Escape))).ConfigureAwait(false);
        }
    }

    private static string Escape(string value)
    {
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IReadOnlyList<T> rows,
        [global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }
}
