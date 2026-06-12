using Domain.Alarms;
using Domain.Tags;

namespace Application.DTOs.Alarms;

public enum AlarmLifecycleChangeType
{
    Raised,
    Updated,
    Recovered
}

public sealed record AlarmLifecycleChange(
    AlarmLifecycleChangeType ChangeType,
    AlarmEvent Alarm);

public sealed record AlarmEvaluationResult(
    IReadOnlyList<TagRuntimeState> States,
    IReadOnlyList<AlarmLifecycleChange> LifecycleChanges);
