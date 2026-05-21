namespace DataflowGraph.Abstractions;

/// <summary>
/// Provides access to operation results during batch execution.
/// Passed to each IOperationExecutor so operations can read results from dependent operations.
/// Thread-safe implementation allows concurrent operation execution.
/// 
/// Key differences from IBatchContext:
/// - IBatchContext: Shared state, correlation, custom data (flows through ALL operations)
/// - IGraphContext: Operation results only (used to read outputs from other operations)
/// 
/// </summary>
public interface IGraphContext
{
    /// <summary>
    /// Gets the OperationResult for a completed operation.
    /// Contains the value, success status, and exception details (if failed).
    /// </summary>
    /// <param name="operationName">The name of the operation (must match OperationName property)</param>
    /// <returns>The OperationResult containing value, error status, and exception details</returns>
    /// <exception cref="InvalidOperationException">Thrown when operation has no result yet</exception>
    OperationResult GetResult(string operationName);

    /// <summary>
    /// Gets the strongly-typed value from a successful operation.
    /// Convenience method that combines GetResult() and type casting.
    /// </summary>
    /// <typeparam name="T">The expected result type (must match operation's return type)</typeparam>
    /// <param name="operationName">The name of the operation</param>
    /// <returns>The typed value</returns>
    /// <exception cref="InvalidOperationException">Thrown when operation failed, has no result, or type mismatch</exception>
    T GetValue<T>(string operationName);

    /// <summary>
    /// Tries to get the strongly-typed value from a successful operation.
    /// Safe alternative to GetValue{T} - returns false instead of throwing.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <param name="operationName">The name of the operation</param>
    /// <param name="value">The typed value if successful, default(T) otherwise</param>
    /// <returns>True if operation succeeded and type matches, false otherwise</returns>
    bool TryGetValue<T>(string operationName, out T value);

    /// <summary>
    /// Checks if an operation has completed (successfully or with failure).
    /// Useful for conditional logic based on whether a dependency has run.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <returns>True if the operation has a result, false otherwise</returns>
    bool HasResult(string operationName);

    /// <summary>
    /// Checks if an operation completed successfully.
    /// Combines HasResult() and success check in one call.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <returns>True if operation succeeded, false if failed or not completed</returns>
    bool IsSuccess(string operationName);

    /// <summary>
    /// Gets all operation results as a read-only dictionary.
    /// Useful for logging, debugging, or aggregate operations.
    /// </summary>
    IReadOnlyDictionary<string, OperationResult> AllResults { get; }
}