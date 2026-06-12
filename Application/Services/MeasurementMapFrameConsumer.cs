using Application.Abstractions.Events;
using Application.Events;

namespace Application.Services;

public sealed class MeasurementMapFrameConsumer : IApplicationEventHandler<RawFrameReceivedEvent>
{
    private readonly MeasurementMapService _measurementMapService;

    public MeasurementMapFrameConsumer(MeasurementMapService measurementMapService)
    {
        _measurementMapService = measurementMapService;
    }

    public ValueTask HandleAsync(RawFrameReceivedEvent applicationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (applicationEvent.Frame.MatrixValues is not null)
        {
            _measurementMapService.Update(applicationEvent.Frame.MatrixValues with
            {
                SourceFrameId = applicationEvent.Frame.FrameId,
                SequenceNo = applicationEvent.Frame.SequenceNo
            });
        }

        return ValueTask.CompletedTask;
    }
}
