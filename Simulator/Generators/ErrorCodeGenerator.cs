namespace Simulator.Generators;

public sealed class ErrorCodeGenerator
{
    public int Generate(long sequenceNo) => sequenceNo % 150 == 0 ? 1 : 0;
}
