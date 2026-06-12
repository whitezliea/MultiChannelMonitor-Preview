namespace Domain.Common;

public sealed record Result(bool IsSuccess, string? ErrorMessage = null)
{
    public static Result Success() => new(true);
    public static Result Failure(string errorMessage) => new(false, errorMessage);
}
