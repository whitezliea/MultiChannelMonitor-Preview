using Domain.Measurements;

namespace Application.Caches;

public sealed class MatrixFrameCache
{
    private readonly object _syncRoot = new();
    private MatrixFrame? _latestFrame;

    public void Update(MatrixFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        MeasurementTimeContract.Validate(frame);
        lock (_syncRoot)
        {
            _latestFrame = frame with { Values = (double[,])frame.Values.Clone() };
        }
    }

    public MatrixFrame? GetLatest()
    {
        lock (_syncRoot)
        {
            return _latestFrame is null
                ? null
                : _latestFrame with { Values = (double[,])_latestFrame.Values.Clone() };
        }
    }
}
