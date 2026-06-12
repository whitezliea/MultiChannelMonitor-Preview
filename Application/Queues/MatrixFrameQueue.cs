using Domain.Measurements;

namespace Application.Queues;

public sealed class MatrixFrameQueue : AsyncItemQueue<MatrixFrame>;
