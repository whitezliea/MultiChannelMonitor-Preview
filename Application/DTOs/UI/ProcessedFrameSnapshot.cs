using Application.DTOs.MeasurementMap;
using Domain.Common;
using Domain.Tags;

namespace Application.DTOs.UI;

public sealed record ProcessedFrameSnapshot(
    Guid SourceFrameId,
    long SequenceNo,
    DateTime TimestampUtc,
    IReadOnlyList<TagRuntimeState> TagRuntimeStates,
    MatrixAnalysisSnapshotDto? MatrixAnalysis)
{
    public void Validate()
    {
        if (SourceFrameId == Guid.Empty) throw new ArgumentException("Processed frame source id must not be empty.");
        UtcDateTime.Require(TimestampUtc, nameof(TimestampUtc));
        if (TagRuntimeStates.Any(state => state.SourceFrameId != SourceFrameId || state.SequenceNo != SequenceNo))
        {
            throw new ArgumentException("All processed Tag states must belong to the same source frame.");
        }

        if (MatrixAnalysis is not null
            && (MatrixAnalysis.SourceFrameId != SourceFrameId || MatrixAnalysis.SequenceNo != SequenceNo))
        {
            throw new ArgumentException("Processed matrix analysis must belong to the same source frame.");
        }
    }
}
