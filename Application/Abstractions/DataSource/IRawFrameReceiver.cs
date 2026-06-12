using Domain.Measurements;

namespace Application.Abstractions.DataSource;

public interface IRawFrameReceiver
{
    Task ReceiveAsync(RawMeasurementFrame frame, CancellationToken cancellationToken);
}
