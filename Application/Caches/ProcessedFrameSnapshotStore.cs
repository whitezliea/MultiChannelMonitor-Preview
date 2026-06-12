using Application.DTOs.MeasurementMap;
using Application.DTOs.UI;

namespace Application.Caches;

public sealed class ProcessedFrameSnapshotStore
{
    private ProcessedFrameSnapshot? _snapshot;

    public void Update(ProcessedFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        Volatile.Write(ref _snapshot, Clone(snapshot));
    }

    public ProcessedFrameSnapshot? GetLatest()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot is null ? null : Clone(snapshot);
    }

    private static ProcessedFrameSnapshot Clone(ProcessedFrameSnapshot snapshot) =>
        snapshot with
        {
            TagRuntimeStates = snapshot.TagRuntimeStates.ToArray(),
            MatrixAnalysis = snapshot.MatrixAnalysis is null ? null : Clone(snapshot.MatrixAnalysis)
        };

    private static MatrixAnalysisSnapshotDto Clone(MatrixAnalysisSnapshotDto analysis) =>
        analysis with
        {
            Frame = analysis.Frame with { Values = (double[,])analysis.Frame.Values.Clone() },
            AbnormalPoints = analysis.AbnormalPoints.ToArray()
        };
}
