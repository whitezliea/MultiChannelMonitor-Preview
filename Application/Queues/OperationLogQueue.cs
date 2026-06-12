using Domain.Logs;

namespace Application.Queues;

public sealed class OperationLogQueue : AsyncItemQueue<OperationLog>;
