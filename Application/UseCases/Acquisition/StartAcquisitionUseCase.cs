namespace Application.UseCases.Acquisition;

public sealed class StartAcquisitionUseCase
{
    public Task ExecuteAsync(Func<CancellationToken, Task> startRuntime, CancellationToken cancellationToken) =>
        startRuntime(cancellationToken);
}
