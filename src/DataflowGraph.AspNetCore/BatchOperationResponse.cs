namespace DataflowGraph.AspNetCore;

/// <summary>
/// HTTP response DTO for batch operation execution.
/// Returned to clients after batch execution completes.
/// Contains all operation results, execution statistics, and error information.
/// 
/// Why this exists:
/// - Standardized response format for all batch endpoints
/// - Provides complete visibility into batch execution
/// - Includes timing metrics for performance monitoring
/// - Contains per-operation results for debugging
/// - Supports partial success scenarios (some ops succeed, some fail)
/// 
/// Server Usage:
/// var result = await batchGraph.ExecuteAsync(operations, batchContext);
/// return Ok(BatchOperationResponse.FromGraphResult(result, request.BatchId));
/// 
/// Client Response (JSON):
/// {
///   "batchId": "batch-123",
///   "isSuccess": true,
///   "results": {
///     "FetchUsers": {
///       "operationName": "FetchUsers",
///       "isSuccess": true,
///       "value": { ...user data... },
///       "errorMessage": null
///     },
///     "GenerateReport": {
///       "operationName": "GenerateReport",
///       "isSuccess": true,
///       "value": { ...report data... },
///       "errorMessage": null
///     }
///   },
///   "duration": "00:00:01.2345678",
///   "durationMs": 1234.56,
///   "startTime": "2024-01-15T10:30:00Z",
///   "endTime": "2024-01-15T10:30:01Z",
///   "operationCount": 3,
///   "successCount": 3,
///   "failureCount": 0,
///   "errorMessage": null
/// }
/// 
/// </summary>
public class BatchOperationResponse
{
    /// <summary>
    /// Gets or sets the unique identifier for this batch request.
    /// Echoed from the request for correlation.
    /// Useful for tracking batch execution across systems.
    /// 
    /// Client Usage:
    /// - Store for later reference (status checks, support tickets)
    /// - Correlate with client-side analytics
    /// - Include in error reports
    /// 
    /// Example: "a1b2c3d4e5f6" (12-character hex string)
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the entire batch succeeded (all required operations).
    /// 
    /// True when:
    /// - All operations completed successfully
    /// - No required operations failed
    /// 
    /// False when:
    /// - One or more required operations failed
    /// - Batch was cancelled
    /// - Circular dependency detected
    /// 
    /// Client Guidance:
    /// - Check this first before processing results
    /// - If false, examine Failures or Results for details
    /// - Partial success is possible (some ops succeed, some fail)
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the results for each operation in the batch.
    /// Key is operation name, value contains result value and status.
    /// 
    /// Structure:
    /// {
    ///   "FetchUsers": {
    ///     "operationName": "FetchUsers",
    ///     "isSuccess": true,
    ///     "value": { ... },
    ///     "errorMessage": null
    ///   },
    ///   "GenerateReport": {
    ///     "operationName": "GenerateReport",
    ///     "isSuccess": false,
    ///     "value": null,
    ///     "errorMessage": "Database connection timeout"
    ///   }
    /// }
    /// 
    /// Client Usage:
    /// - Iterate through results to process successful operations
    /// - Check IsSuccess per operation (batch-level success may differ)
    /// - Use ErrorMessage for debugging failures
    /// </summary>
    public Dictionary<string, BatchOperationResult> Results { get; set; } = new();

    /// <summary>
    /// Gets or sets the total execution duration as TimeSpan.
    /// Calculated from StartTime to EndTime.
    /// 
    /// Example: "00:00:01.2345678" (1.23 seconds)
    /// 
    /// Client Usage:
    /// - Monitor batch performance
    /// - Detect slow operations
    /// - Set appropriate client-side timeouts
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the total execution duration in milliseconds.
    /// Convenience property for JSON serialization (TimeSpan serializes poorly in some clients).
    /// 
    /// Example: 1234.56 (milliseconds)
    /// 
    /// Client Usage:
    /// - Easier to parse than TimeSpan string
    /// - Use for charts, metrics, performance monitoring
    /// </summary>
    public double DurationMs => Duration.TotalMilliseconds;

    /// <summary>
    /// Gets or sets when the batch execution started (UTC).
    /// Useful for duration calculations and audit logging.
    /// 
    /// Format: ISO 8601 (e.g., "2024-01-15T10:30:00Z")
    /// 
    /// Client Usage:
    /// - Correlate with client-side timestamps
    /// - Calculate server-side processing time
    /// - Audit/compliance logging
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets when the batch execution completed (UTC).
    /// Useful for duration calculations and audit logging.
    /// 
    /// Format: ISO 8601 (e.g., "2024-01-15T10:30:01Z")
    /// 
    /// Client Usage:
    /// - Correlate with client-side timestamps
    /// - Calculate server-side processing time
    /// - Audit/compliance logging
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Gets or sets the count of operations that executed.
    /// Includes both successful and failed operations.
    /// 
    /// Client Usage:
    /// - Verify all requested operations ran
    /// - Detect skipped operations (count < requested)
    /// </summary>
    public int OperationCount { get; set; }

    /// <summary>
    /// Gets or sets the count of operations that succeeded.
    /// 
    /// Client Usage:
    /// - Quick success ratio calculation (SuccessCount / OperationCount)
    /// - Monitor batch health over time
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Gets or sets the count of operations that failed.
    /// 
    /// Client Usage:
    /// - Quick failure detection (FailureCount > 0)
    /// - Monitor error rates over time
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Gets or sets a summary error message if the batch failed.
    /// Null if batch succeeded.
    /// 
    /// Contains:
    /// - High-level failure description
    /// - Count of failures
    /// - First failure details
    /// 
    /// Example: "Batch completed with 2 error(s). First failure: Operation 'FetchUsers' - Database connection timeout"
    /// 
    /// Client Usage:
    /// - Display to users (high-level error)
    /// - Log for debugging
    /// - Don't rely on this for detailed error handling (use Results instead)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets detailed failure information for each failed operation.
    /// Convenience property for clients that only care about failures.
    /// 
    /// Structure:
    /// [
    ///   {
    ///     "operationName": "FetchUsers",
    ///     "errorMessage": "Database connection timeout",
    ///     "errorType": "SqlException"
    ///   },
    ///   {
    ///     "operationName": "SendNotification",
    ///     "errorMessage": "Dependency 'FetchUsers' failed",
    ///     "errorType": "InvalidOperationException"
    ///   }
    /// ]
    /// 
    /// Client Usage:
    /// - Display failure details to users
    /// - Implement retry logic for specific operations
    /// - Log for debugging
    /// </summary>
    public List<BatchFailureInfo> Failures { get; set; } = new();

    /// <summary>
    /// Gets or sets optional metadata echoed from the request.
    /// Useful for correlation and tracking.
    /// 
    /// Example:
    /// {
    ///   "Source": "MobileApp",
    ///   "Version": "2.1.0",
    ///   "CorrelationId": "corr-456"
    /// }
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Creates a BatchOperationResponse from a GraphResult.
    /// This is the primary method for creating responses in controllers.
    /// 
    /// Usage:
    /// var graphResult = await batchGraph.ExecuteAsync(operations, batchContext);
    /// return Ok(BatchOperationResponse.FromGraphResult(graphResult, request.BatchId));
    /// </summary>
    /// <param name="graphResult">The execution result from DataflowGraph</param>
    /// <param name="batchId">The batch ID from the request (echoed back)</param>
    /// <param name="metadata">Optional metadata from the request (echoed back)</param>
    /// <returns>A new BatchOperationResponse instance</returns>
    public static BatchOperationResponse FromGraphResult(
        GraphResult graphResult,
        string batchId,
        Dictionary<string, string>? metadata = null)
    {
        if (graphResult == null)
        {
            throw new ArgumentNullException(nameof(graphResult));
        }

        var response = new BatchOperationResponse
        {
            BatchId = batchId ?? string.Empty,
            IsSuccess = graphResult.IsSuccess,
            Duration = graphResult.Duration,
            StartTime = graphResult.StartTime,
            EndTime = graphResult.EndTime,
            OperationCount = graphResult.StepCount,
            SuccessCount = graphResult.SuccessCount,
            FailureCount = graphResult.FailureCount,
            Metadata = metadata ?? new Dictionary<string, string>(),
            Results = graphResult.Results.ToDictionary(
                k => k.Key,
                v => new BatchOperationResult
                {
                    OperationName = v.Value.OperationName,
                    Value = v.Value.Value,
                    IsSuccess = v.Value.IsSuccess,
                    ErrorMessage = v.Value.Exception?.Message
                }),
            Failures = graphResult.GetFailures()
                .Select(f => new BatchFailureInfo
                {
                    OperationName = f.Key,
                    ErrorMessage = f.Value.Exception?.Message,
                    ErrorType = f.Value.Exception?.GetType().Name
                })
                .ToList(),
            ErrorMessage = graphResult.HasFailures
                ? $"Batch completed with {graphResult.FailureCount} error(s). " +
                  $"First failure: Operation '{graphResult.GetFailures().First().Key}' - " +
                  $"{graphResult.GetFailures().First().Value.Exception?.Message}"
                : null
        };

        return response;
    }

    /// <summary>
    /// Creates a success response for empty batches or quick returns.
    /// </summary>
    public static BatchOperationResponse CreateSuccess(string batchId)
    {
        return new BatchOperationResponse
        {
            BatchId = batchId,
            IsSuccess = true,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            Duration = TimeSpan.Zero,
            OperationCount = 0,
            SuccessCount = 0,
            FailureCount = 0
        };
    }

    /// <summary>
    /// Creates an error response for validation failures or early exits.
    /// </summary>
    public static BatchOperationResponse CreateError(string batchId, string errorMessage)
    {
        return new BatchOperationResponse
        {
            BatchId = batchId,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            Duration = TimeSpan.Zero,
            OperationCount = 0,
            SuccessCount = 0,
            FailureCount = 0
        };
    }

    /// <summary>
    /// Returns a string representation of the batch response.
    /// Useful for logging and debugging.
    /// </summary>
    public override string ToString()
    {
        var status = IsSuccess ? "Success" : $"Failed ({FailureCount} errors)";
        return $"BatchOperationResponse(BatchId={BatchId}, {status}, Duration={DurationMs}ms)";
    }

    /// <summary>
    /// Creates a summary object for logging or metrics.
    /// Excludes large result values for cleaner logging.
    /// </summary>
    public object ToSummary()
    {
        return new
        {
            BatchId,
            IsSuccess,
            OperationCount,
            SuccessCount,
            FailureCount,
            DurationMs,
            StartTime,
            EndTime,
            FailureNames = Failures.Select(f => f.OperationName).ToList()
        };
    }
}

/// <summary>
/// Represents the result of a single operation within a batch response.
/// Simplified for HTTP serialization (compared to internal OperationResult).
/// 
/// This is what clients see for each operation in the Results dictionary.
/// </summary>
public class BatchOperationResult
{
    /// <summary>
    /// Gets or sets the name of the operation.
    /// Matches the Name from the request.
    /// </summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result value (null if operation failed or void return).
    /// 
    /// Structure depends on the operation:
    /// - FetchUsers: Array of user objects
    /// - GenerateReport: Report object or file URL
    /// - SendNotification: null (void operation)
    /// 
    /// Client Usage:
    /// - Deserialize to expected type
    /// - Check IsSuccess before using value
    /// - Value may be null for void operations even on success
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets or sets whether the operation succeeded.
    /// 
    /// True: Operation completed without exceptions
    /// False: Operation threw an exception or was skipped
    /// 
    /// Client Usage:
    /// - Check before using Value
    /// - Some operations may succeed while batch fails (or vice versa)
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the error message if the operation failed.
    /// Null if operation succeeded.
    /// 
    /// Contains:
    /// - Exception message (not full stack trace)
    /// - Dependency failure messages (if skipped)
    /// 
    /// Client Usage:
    /// - Display to users for debugging
    /// - Log for support tickets
    /// - Don't expose in production UI (use generic messages)
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents detailed failure information for a failed operation.
/// Used in BatchOperationResponse.Failures array.
/// 
/// This is a convenience type for clients that only care about failures.
/// </summary>
public class BatchFailureInfo
{
    /// <summary>
    /// Gets or sets the name of the failed operation.
    /// </summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message.
    /// Contains exception message or dependency failure description.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the exception type name.
    /// Useful for categorizing failures (e.g., "SqlException", "HttpRequestException").
    /// </summary>
    public string? ErrorType { get; set; }
}
