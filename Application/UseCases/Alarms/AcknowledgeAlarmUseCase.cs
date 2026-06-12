using Application.Abstractions.Events;
using Application.Abstractions.Time;
using Application.Events;
using Application.Services;
using AppLogging;

namespace Application.UseCases.Alarms;

public sealed class AcknowledgeAlarmUseCase
{
    private readonly AlarmService _alarmService;
    private readonly IApplicationEventPublisher _eventPublisher;
    private readonly IClock _clock;

    public AcknowledgeAlarmUseCase(
        AlarmService alarmService,
        IApplicationEventPublisher eventPublisher,
        IClock clock)
    {
        _alarmService = alarmService;
        _eventPublisher = eventPublisher;
        _clock = clock;
    }

    public bool Execute(Guid alarmId) =>
        ExecuteAsync(alarmId, _clock.UtcNow, CancellationToken.None).GetAwaiter().GetResult();

    public Task<bool> ExecuteAsync(
        Guid alarmId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(alarmId, _clock.UtcNow, cancellationToken);

    public async Task<bool> ExecuteAsync(
        Guid alarmId,
        DateTime acknowledgedAt,
        CancellationToken cancellationToken = default)
    {
        Domain.Common.UtcDateTime.Require(acknowledgedAt, nameof(acknowledgedAt));
        if (!_alarmService.TryAcknowledge(alarmId, acknowledgedAt, out var alarm)
            || alarm is null)
        {
            return false;
        }

        try
        {
            await _eventPublisher.PublishAsync(
                new AlarmAcknowledgedEvent(alarm),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error(
                exception,
                "Alarm acknowledged event publish failed | AlarmId: {0}",
                alarm.AlarmId);
        }

        return true;
    }
}
