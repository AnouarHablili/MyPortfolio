namespace MyPortfolio.Core.Shared;

// Generic, non-value type Result for commands that don't return data (e.g., Delete operation)
public record Result(bool IsSuccess, string Error)
{
    // Static factory methods for creating a failure result
    public static Result Failure(string error) => new(false, error);

    // Static factory methods for creating a successful result
    public static Result Success() => new(true, string.Empty);
}

// Generic Result<T> for queries or commands that return data
public record Result<T>(bool IsSuccess, T? Value, string Error) : Result(IsSuccess, Error) where T : class
{
    // Static factory methods for creating a failure result (calls base class)
    public new static Result<T> Failure(string error) => new(false, null, error);

    // Static factory methods for creating a successful result
    public static Result<T> Success(T value) => new(true, value, string.Empty);

    // Explicit cast operator to allow checking Result<T> in an if statement (IsSuccess)
    public static implicit operator bool(Result<T> result) => result.IsSuccess;
}