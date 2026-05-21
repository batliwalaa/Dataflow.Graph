using DataflowGraph.Abstractions;

namespace DataflowGraph;

/// <summary>
/// Represents the result of a batch graph execution.
/// Contains all operation results and provides convenient access methods.
/// Returned by IBatchGraph.ExecuteAsync().
/// 
/// Key features:
/// - Access to all operation results (Results dictionary)
/// - Execution statistics (Duration, StepCount, SuccessCount, FailureCount)
/// - Convenience methods (GetValue, TryGetValue, IsStepSuccess)
/// - Error handling (GetFailures, ThrowOnFailure)
/// - Timing information (StartTime, EndTime, Duration)
/// 
/// Usage:
/// var result = await batchGraph.ExecuteAsync(operations, batchContext);
/// 
/// if (result.IsSuccess)
/// {
///     var users = result.GetValue<List<User>>("FetchUsers");
/// }
/// else
/// {
///     var failures = result.GetFailures();
///     // Handle errors
/// }
/// 
/// </summary>
public class GraphResult
{
    private readonly IGraphContext _context;

    /// <summary>
    /// Gets all operation results as a read-only dictionary.
    /// Key is operation name, value is OperationResult.
    /// 
    /// Example:
    /// foreach (var kvp in result.Results)
    /// {
    ///     Console.WriteLine($"{kvp.Key}: {(kvp.Value.IsSuccess ? "Success" : "Failed")}");
    /// }
    /// </summary>
    public IReadOnlyDictionary<string, OperationResult> Results { get; }

    /// <summary>
    /// Gets the total execution duration.
    /// Calculated from StartTime to EndTime.
    /// 
    /// Example:
    /// Console.WriteLine($"Batch completed in {result.Duration}");
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets when the batch execution started (UTC).
    /// Useful for duration calculations and audit logging.
    /// </summary>
    public DateTime StartTime { get; }

    /// <summary>
    /// Gets when the batch execution completed (UTC).
    /// Useful for duration calculations and audit logging.
    /// </summary>
    public DateTime EndTime { get; }

    /// <summary>
    /// Gets whether all operations completed successfully.
    /// True = no failures.
    /// False = one or more operations failed.
    /// 
    /// Example:
    /// if (result.IsSuccess)
    /// {
    ///     // All operations succeeded
    /// }
    /// </summary>
    public bool IsSuccess => Results.Values.All(r => r.IsSuccess);

    /// <summary>
    /// Gets whether any operation failed.
    /// True = one or more failures.
    /// False = all operations succeeded.
    /// 
    /// Example:
    /// if (result.HasFailures)
    /// {
    ///     // Handle errors
    /// }
    /// </summary>
    public bool HasFailures => Results.Values.Any(r => r.IsFaulted);

    /// <summary>
    /// Gets the count of operations that executed.
    /// Includes both successful and failed operations.
    /// </summary>
    public int StepCount => Results.Count;

    /// <summary>
    /// Gets the count of operations that succeeded.
    /// </summary>
    public int SuccessCount => Results.Count(r => r.Value.IsSuccess);

    /// <summary>
    /// Gets the count of operations that failed.
    /// </summary>
    public int FailureCount => Results.Count(r => r.Value.IsFaulted);

    /// <summary>
    /// Initializes a new instance of the GraphResult class.
    /// Internal constructor - use FromContext() factory method.
    /// </summary>
    /// <param name="context">The graph context containing operation results</param>
    /// <param name="duration">The total execution duration</param>
    /// <param name="startTime">When execution started</param>
    /// <param name="endTime">When execution completed</param>
    internal GraphResult(
        IGraphContext context,
        TimeSpan duration,
        DateTime startTime,
        DateTime endTime)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Duration = duration;
        StartTime = startTime;
        EndTime = endTime;
        Results = context.AllResults;
    }

    /// <summary>
    /// Gets the OperationResult for a specific operation.
    /// Contains the value, success status, and exception details (if failed).
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <returns>The OperationResult for the operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when operation has no result</exception>
    /// <example>
    /// <code>
    /// var fetchResult = result.GetResult("FetchUsers");
    /// if (fetchResult.IsSuccess)
    /// {
    ///     var users = fetchResult.GetValue<List<User>>();
    /// }
    /// else
    /// {
    ///     _logger.LogError(fetchResult.Exception, "FetchUsers failed");
    /// }
    /// </code>
    /// </example>
    public OperationResult GetResult(string operationName)
    {
        return _context.GetResult(operationName);
    }

    /// <summary>
    /// Gets the strongly-typed value from a successful operation.
    /// Convenience method that combines GetResult() and type casting.
    /// Throws if operation failed or type doesn't match.
    /// </summary>
    /// <typeparam name="T">The expected result type (must match operation's return type)</typeparam>
    /// <param name="operationName">The name of the operation</param>
    /// <returns>The typed value</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// - Operation has no result
    /// - Operation failed (IsFaulted = true)
    /// - Type mismatch (stored value is not type T)
    /// </exception>
    /// <example>
    /// <code>
    /// var users = result.GetValue<List<User>>("FetchUsers");
    /// </code>
    /// </example>
    public T GetValue<T>(string operationName)
    {
        return _context.GetValue<T>(operationName);
    }

    /// <summary>
    /// Tries to get the strongly-typed value from a successful operation.
    /// Safe alternative to GetValue{T} - returns false instead of throwing.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <param name="operationName">The name of the operation</param>
    /// <param name="value">The typed value if successful, default(T) otherwise</param>
    /// <returns>
    /// True if:
    /// - Operation has a result
    /// - Operation succeeded (IsFaulted = false)
    /// - Value type matches T
    /// False otherwise
    /// </returns>
    /// <example>
    /// <code>
    /// if (result.TryGetValue<List<User>>("FetchUsers", out var users))
    /// {
    ///     // Use users
    /// }
    /// else
    /// {
    ///     // Handle missing/failed result
    /// }
    /// </code>
    /// </example>
    public bool TryGetValue<T>(string operationName, out T value)
    {
        return _context.TryGetValue(operationName, out value);
    }

    /// <summary>
    /// Checks if a specific operation completed successfully.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <returns>
    /// True if operation succeeded.
    /// False if operation failed or hasn't completed.
    /// </returns>
    /// <example>
    /// <code>
    /// if (result.IsStepSuccess("FetchUsers"))
    /// {
    ///     // Proceed with users data
    /// }
    /// </code>
    /// </example>
    public bool IsStepSuccess(string operationName)
    {
        return _context.IsSuccess(operationName);
    }

    /// <summary>
    /// Gets all failed operation results.
    /// Useful for error reporting and debugging.
    /// </summary>
    /// <returns>
    /// Dictionary of failed operation names to their OperationResult.
    /// Empty if no operations failed.
    /// </returns>
    /// <example>
    /// <code>
    /// if (result.HasFailures)
    /// {
    ///     var failures = result.GetFailures();
    ///     foreach (var failure in failures)
    ///     {
    ///         _logger.LogError(failure.Value.Exception, "Operation {Name} failed", failure.Key);
    ///     }
    /// }
    /// </code>
    /// </example>
    public IReadOnlyDictionary<string, OperationResult> GetFailures()
    {
        return Results.Where(r => r.Value.IsFaulted)
                      .ToDictionary(k => k.Key, v => v.Value);
    }

    /// <summary>
    /// Gets all successful operation results.
    /// Useful for processing only successful operations.
    /// </summary>
    /// <returns>
    /// Dictionary of successful operation names to their OperationResult.
    /// Empty if no operations succeeded.
    /// </returns>
    /// <example>
    /// <code>
    /// var successes = result.GetSuccesses();
    /// foreach (var success in successes)
    /// {
    ///     _logger.LogInformation("Operation {Name} succeeded", success.Key);
    /// }
    /// </code>
    /// </example>
    public IReadOnlyDictionary<string, OperationResult> GetSuccesses()
    {
        return Results.Where(r => r.Value.IsSuccess)
                      .ToDictionary(k => k.Key, v => v.Value);
    }

    /// <summary>
    /// Throws an exception if any operation failed.
    /// Useful for failing fast when batch has errors.
    /// </summary>
    /// <exception cref="BatchException">
    /// Thrown when any operation failed.
    /// Contains details about the first failure.
    /// </exception>
    /// <example>
    /// <code>
    /// var result = await batchGraph.ExecuteAsync(operations, batchContext);
    /// 
    /// // Throw if any operation failed
    /// result.ThrowOnFailure();
    /// 
    /// // If we reach here, all operations succeeded
    /// var users = result.GetValue<List<User>>("FetchUsers");
    /// </code>
    /// </example>
    public void ThrowOnFailure()
    {
        var failures = GetFailures();
        if (failures.Count > 0)
        {
            var firstFailure = failures.First();
            var message = $"Batch execution failed with {failures.Count} error(s). " +
                         $"First failure: Operation '{firstFailure.Key}' - {firstFailure.Value.Exception?.Message}";
            throw new BatchException("Batch", message, firstFailure.Value.Exception);
        }
    }

    /// <summary>
    /// Creates a GraphResult from a GraphContext.
    /// Factory method used internally by BatchGraph.
    /// </summary>
    /// <param name="context">The graph context containing operation results</param>
    /// <param name="startTime">When execution started</param>
    /// <returns>A new GraphResult instance</returns>
    internal static GraphResult FromContext(IGraphContext context, DateTime startTime)
    {
        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;
        return new GraphResult(context, duration, startTime, endTime);
    }

    /// <summary>
    /// Returns a string representation of the batch result.
    /// Useful for logging and debugging.
    /// </summary>
    public override string ToString()
    {
        var status = IsSuccess ? "Success" : $"Failed ({FailureCount} errors)";
        return $"GraphResult({status}, Steps={StepCount}, Duration={Duration})";
    }

    /// <summary>
    /// Creates a summary of the batch execution.
    /// Useful for logging, metrics, or API responses.
    /// </summary>
    /// <returns>Anonymous object with key execution metrics</returns>
    public object ToSummary()
    {
        return new
        {
            IsSuccess,
            StepCount,
            SuccessCount,
            FailureCount,
            DurationMs = Duration.TotalMilliseconds,
            StartTime,
            EndTime,
            Failures = GetFailures().Select(f => new
            {
                f.Key,
                Message = f.Value.Exception?.Message
            }).ToList()
        };
    }
}