namespace DataflowGraph;

/// <summary>
/// Represents the result of a single operation execution.
/// Contains the operation value, error status, and exception details if failed.
/// Enables error isolation - one operation failure doesn't kill the entire batch.
/// 
/// Key features:
/// - Stores operation name for identification
/// - Tracks success/failure status
/// - Captures exception details (BatchException)
/// - Provides typed value access (GetValue{T}, TryGetValue{T})
/// - Factory methods for creating Success/Failure results
/// 
/// </summary>
public class OperationResult
{
    /// <summary>
    /// Gets the name of the operation that produced this result.
    /// Matches the OperationName property of the IOperationExecutor.
    /// Example: "FetchUsers", "ValidateOrder", "SendNotification"
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// Gets the value produced by the operation.
    /// Null if the operation failed or has a void return type.
    /// Use GetValue{T}() or TryGetValue{T}() for type-safe access.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets whether the operation failed with an exception.
    /// True = operation threw an exception.
    /// False = operation completed successfully.
    /// </summary>
    public bool IsFaulted { get; }

    /// <summary>
    /// Gets the exception that caused the operation failure.
    /// Null if the operation succeeded.
    /// Contains step-specific details via BatchException.
    /// </summary>
    public BatchException? Exception { get; }

    /// <summary>
    /// Gets whether the operation completed successfully.
    /// Convenience property (opposite of IsFaulted).
    /// </summary>
    public bool IsSuccess => !IsFaulted;

    /// <summary>
    /// Initializes a new instance of the OperationResult class.
    /// Private constructor - use Success() and Failure() factory methods.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <param name="value">The operation result value (null for void or failed)</param>
    /// <param name="isFaulted">Whether the operation failed</param>
    /// <param name="exception">The exception if failed (null if succeeded)</param>
    private OperationResult(string operationName, object? value, bool isFaulted, BatchException? exception)
    {
        OperationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
        Value = value;
        IsFaulted = isFaulted;
        Exception = exception;
    }

    /// <summary>
    /// Gets the strongly-typed value if the operation succeeded.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <returns>The typed value</returns>
    /// <exception cref="InvalidOperationException">Thrown if operation failed or type mismatch</exception>
    public T GetValue<T>()
    {
        if (IsFaulted)
        {
            throw new InvalidOperationException(
                $"Cannot get value from failed operation '{OperationName}'. Check IsSuccess first. Exception: {Exception?.Message}");
        }

        if (Value is T typedValue)
        {
            return typedValue;
        }

        throw new InvalidOperationException(
            $"Operation '{OperationName}' value type mismatch. Expected {typeof(T).Name}, got {Value?.GetType().Name ?? "null"}");
    }

    /// <summary>
    /// Tries to get the strongly-typed value if the operation succeeded.
    /// Safe alternative to GetValue{T} - returns false instead of throwing.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <param name="value">The typed value if successful, default(T) otherwise</param>
    /// <returns>True if operation succeeded and type matches, false otherwise</returns>
    public bool TryGetValue<T>(out T value)
    {
        if (IsFaulted || Value is not T typedValue)
        {
            value = default!;
            return false;
        }

        value = typedValue;
        return true;
    }

    /// <summary>
    /// Creates a successful OperationResult.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <param name="value">The operation result value (can be null for void operations)</param>
    /// <returns>A new OperationResult instance with IsSuccess = true</returns>
    public static OperationResult Success(string operationName, object? value)
    {
        return new OperationResult(operationName, value, false, null);
    }

    /// <summary>
    /// Creates a failed OperationResult.
    /// Automatically wraps non-BatchException exceptions in BatchException.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <param name="exception">The exception that caused the failure</param>
    /// <returns>A new OperationResult instance with IsFaulted = true</returns>
    public static OperationResult Failure(string operationName, Exception exception)
    {
        var batchException = exception is BatchException be ? be : BatchException.FromStep(operationName, exception);
        return new OperationResult(operationName, null, true, batchException);
    }

    /// <summary>
    /// Creates a failed OperationResult with a custom BatchException.
    /// Use when you need to provide custom error details.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <param name="batchException">The BatchException with operation-specific details</param>
    /// <returns>A new OperationResult instance with IsFaulted = true</returns>
    public static OperationResult Failure(string operationName, BatchException batchException)
    {
        return new OperationResult(operationName, null, true, batchException);
    }
}