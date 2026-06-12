using Domain.Alarms;

namespace Application.Queues;

public sealed class AlarmEventQueue : AsyncItemQueue<AlarmEvent>;
