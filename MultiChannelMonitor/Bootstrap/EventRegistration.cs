using Application.Abstractions.Events;
using Application.Abstractions.Time;
using Application.Configuration;
using Application.Events;
using Application.Queues;
using Application.Services;
using Application.UseCases.Alarms;

namespace Presentation.Wpf.Bootstrap;

/// <summary>
/// 集中维护应用事件订阅关系，避免组合根同时承担对象创建和订阅细节。
/// </summary>
internal static class EventRegistration
{
    public static EventPipelineRegistration Create(
        IClock clock,
        TagRuntimeConfigurationStore tagRuntimeConfigurationStore,
        AlarmEventQueue alarmEventQueue,
        OperationLogService operationLogService,
        TagService tagService,
        AlarmService alarmService,
        HistoryService historyService,
        MeasurementMapService measurementMapService)
    {
        var eventPublisher = new ApplicationEventPublisher();

        RegisterMeasurementConsumers(eventPublisher, tagService, measurementMapService);
        RegisterHealthConsumers(eventPublisher, operationLogService);
        RegisterHistoryConsumers(eventPublisher, historyService, tagRuntimeConfigurationStore);
        RegisterAlarmConsumers(eventPublisher, alarmEventQueue, operationLogService);

        return new EventPipelineRegistration(
            eventPublisher,
            new AcknowledgeAlarmUseCase(alarmService, eventPublisher, clock));
    }

    private static void RegisterMeasurementConsumers(
        ApplicationEventPublisher eventPublisher,
        TagService tagService,
        MeasurementMapService measurementMapService)
    {
        eventPublisher.Register(
            new MeasurementMapFrameConsumer(measurementMapService),
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register(new TagCacheConsumer(tagService));
    }

    private static void RegisterHealthConsumers(
        ApplicationEventPublisher eventPublisher,
        OperationLogService operationLogService)
    {
        var consumer = new DataSourceHealthOperationLogConsumer(operationLogService);
        eventPublisher.Register<DataSourceTimedOutEvent>(
            consumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<DataSourceRecoveredEvent>(
            consumer,
            ApplicationEventHandlerFailurePolicy.Isolated);
    }

    private static void RegisterHistoryConsumers(
        ApplicationEventPublisher eventPublisher,
        HistoryService historyService,
        TagRuntimeConfigurationStore tagRuntimeConfigurationStore)
    {
        eventPublisher.Register(
            new HistoryRuntimeStateConsumer(historyService, tagRuntimeConfigurationStore),
            ApplicationEventHandlerFailurePolicy.Isolated);
    }

    private static void RegisterAlarmConsumers(
        ApplicationEventPublisher eventPublisher,
        AlarmEventQueue alarmEventQueue,
        OperationLogService operationLogService)
    {
        RegisterAlarmLifecycleConsumer(eventPublisher, new AlarmEventConsumer(alarmEventQueue));
        RegisterAlarmLifecycleConsumer(
            eventPublisher,
            new AlarmOperationLogConsumer(operationLogService));
    }

    private static void RegisterAlarmLifecycleConsumer<TConsumer>(
        ApplicationEventPublisher eventPublisher,
        TConsumer consumer)
        where TConsumer : IApplicationEventHandler<AlarmRaisedEvent>,
            IApplicationEventHandler<AlarmUpdatedEvent>,
            IApplicationEventHandler<AlarmRecoveredEvent>,
            IApplicationEventHandler<AlarmAcknowledgedEvent>
    {
        // 告警持久化和操作日志消费者都订阅完整生命周期，集中注册可避免漏掉事件类型。
        eventPublisher.Register<AlarmRaisedEvent>(consumer, ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmUpdatedEvent>(consumer, ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmRecoveredEvent>(consumer, ApplicationEventHandlerFailurePolicy.Isolated);
        eventPublisher.Register<AlarmAcknowledgedEvent>(consumer, ApplicationEventHandlerFailurePolicy.Isolated);
    }
}

internal sealed record EventPipelineRegistration(
    ApplicationEventPublisher EventPublisher,
    AcknowledgeAlarmUseCase AcknowledgeAlarmUseCase);
