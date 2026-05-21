namespace DataflowGraph.AspNetCore;

/// <summary>
/// Represents a single operation within a batch HTTP request.
/// Sent by clients as part of BatchOperationRequest.Operations array.
/// 
/// Why this exists:
/// - Allows clients to specify which operations to run
/// - Supports operation-level configuration (arguments, dependencies, retries)
/// - Separates HTTP layer from core library (core uses BatchOperationDefinition)
/// - Enables flexible batch composition from client side
/// 
/// Client Usage (JSON):
/// POST /api/batch
/// {
///   "batchId": "batch-123",
///   "operations": [
///     {
///       "name": "FetchUsers",
///       "arguments": { "tenantId": "123", "includeInactive": true },
///       "dependsOn": [],
///       "isRequired": true,
///       "maxRetries": 3
///     },
///     {
///       "name": "GenerateReport",
///       "arguments": { "format": "PDF" },
///       "dependsOn": ["FetchUsers", "FetchProducts"],
///       "isRequired": true,
///       "maxRetries": 0
///     }
///   ]
/// }
/// 
/// </summary>
public class BatchOperationItem
{
    /// <summary>
    /// Gets or sets the unique name of the operation.
    /// This name is resolved to an IOperationExecutor via DI.
    /// Must match the OperationName property of a registered operation.
    /// 
    /// Validation:
    /// - Must not be null or empty
    /// - Must match a registered operation name (case-insensitive)
    /// - Must be unique within the batch request (no duplicates)
    /// 
    /// Examples:
    /// - "FetchUsers"
    /// - "ValidateOrder"
    /// - "SendNotification"
    /// - "ProcessPayment"
    /// 
    /// Client Guidance:
    /// - Operation names are defined by the API provider
    /// - Check API documentation for available operations
    /// - Names are case-insensitive ("FetchUsers" = "fetchusers")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of operation names this operation depends on.
    /// Dependent operations wait for their dependencies to complete before executing.
    /// In Parallel mode, independent operations run concurrently.
    /// 
    /// Validation:
    /// - All dependency names must exist as operations in the same batch request
    /// - Circular dependencies are detected and rejected
    /// - Self-dependency (operation depends on itself) is rejected
    /// 
    /// Examples:
    /// - "GenerateReport" depends on ["FetchUsers", "FetchProducts"]
    /// - "SendNotification" depends on ["SaveOrder"]
    /// - Root operations have empty dependsOn list []
    /// 
    /// Execution Flow:
    /// 1. Operations with no dependencies run first
    /// 2. Operations wait for all dependencies to complete
    /// 3. If a required dependency fails, dependent operations are skipped
    /// 4. If ContinueOnError = true, operations run regardless of dependency status
    /// </summary>
    public List<string> DependsOn { get; set; } = new();

    /// <summary>
    /// Gets or sets optional arguments to pass to the operation.
    /// Operations receive these via ExecuteAsync(arguments) parameter.
    /// 
    /// Key-value pairs where:
    /// - Key: string (parameter name)
    /// - Value: object? (parameter value - can be string, number, bool, null)
    /// 
    /// Examples:
    /// {
    ///   "tenantId": "tenant-123",
    ///   "includeInactive": true,
    ///   "maxResults": 100,
    ///   "format": "PDF",
    ///   "notifyUser": false
    /// }
    /// 
    /// Operation Usage:
    /// public class FetchUsersOperation : BaseOperation<List<User>>
    /// {
    ///     protected override async Task<List<User>> ExecuteCoreAsync(...)
    ///     {
    ///         var tenantId = arguments?.GetValueOrDefault("tenantId")?.ToString();
    ///         var includeInactive = arguments?.GetValueOrDefault("includeInactive") as bool? ?? false;
    ///         // Use arguments in operation logic
    ///     }
    /// }
    /// 
    /// Client Guidance:
    /// - Check API documentation for available arguments per operation
    /// - Arguments are optional (empty dictionary is valid)
    /// - Values are serialized as JSON (strings, numbers, bools, null)
    /// - Complex objects may not serialize correctly (use simple types)
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this operation is required.
    /// If true (default), failure stops dependent operations.
    /// If false, dependent operations can still run (they'll see the failure).
    /// 
    /// When true (default):
    /// - Operation failure marks dependent operations as skipped
    /// - Dependent operations receive error about failed dependency
    /// - Batch may fail entirely if no ContinueOnError
    /// 
    /// When false:
    /// - Operation failure doesn't block dependent operations
    /// - Dependent operations can check dependency status via graphContext.IsSuccess()
    /// - Useful for optional/best-effort operations
    /// 
    /// Use false for:
    /// - Notification operations (nice-to-have, not critical)
    /// - Analytics/logging operations (shouldn't block main flow)
    /// - Cache updates (can fail without stopping batch)
    /// - Secondary operations (primary result is what matters)
    /// 
    /// Examples:
    /// - "SendNotification" → isRequired: false (email can fail, order still saved)
    /// - "LogToAnalytics" → isRequired: false (analytics shouldn't block checkout)
    /// - "UpdateCache" → isRequired: false (cache miss is acceptable)
    /// - "ProcessPayment" → isRequired: true (payment must succeed)
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry count for this operation.
    /// Default is 0 (no retries).
    /// On failure, the operation will be retried up to this many times before marking as failed.
    /// 
    /// Retry Behavior:
    /// - Retries use exponential backoff (100ms, 200ms, 400ms, ...)
    /// - Only retries on exception (not on validation failures)
    /// - OperationCanceledException is never retried
    /// - Each retry gets a fresh execution (new context, same arguments)
    /// 
    /// Use for:
    /// - Transient failures (network timeouts, database deadlocks)
    /// - External API calls (may succeed on retry)
    /// - Rate-limited services (retry after backoff)
    /// 
    /// Don't use for:
    /// - Validation failures (won't succeed on retry)
    /// - Business logic errors (fix the data, don't retry)
    /// - Authentication failures (retry won't help)
    /// 
    /// Recommendations:
    /// - Database operations: 1-3 retries
    /// - External APIs: 2-5 retries (respect rate limits)
    /// - Internal services: 1-2 retries
    /// - Validation operations: 0 retries
    /// 
    /// Example:
    /// - maxRetries: 0 → No retry (fail immediately)
    /// - maxRetries: 3 → Try up to 4 times total (initial + 3 retries)
    /// </summary>
    public int MaxRetries { get; set; } = 0;

    /// <summary>
    /// Creates a BatchOperationItem from individual parameters.
    /// Fluent factory method for easy request building in code or tests.
    /// </summary>
    /// <param name="name">The operation name</param>
    /// <param name="arguments">Optional arguments dictionary</param>
    /// <param name="dependsOn">List of dependency operation names</param>
    /// <param name="isRequired">Whether this operation is required</param>
    /// <param name="maxRetries">Maximum retry count</param>
    /// <returns>A new BatchOperationItem instance</returns>
    public static BatchOperationItem Create(
        string name,
        Dictionary<string, object?>? arguments = null,
        IEnumerable<string>? dependsOn = null,
        bool isRequired = true,
        int maxRetries = 0)
    {
        return new BatchOperationItem
        {
            Name = name,
            Arguments = arguments ?? new Dictionary<string, object?>(),
            DependsOn = dependsOn?.ToList() ?? new List<string>(),
            IsRequired = isRequired,
            MaxRetries = maxRetries
        };
    }

    /// <summary>
    /// Adds a dependency to this operation.
    /// Fluent method for building operation items in code or tests.
    /// </summary>
    /// <param name="dependencyName">The name of the dependency operation</param>
    /// <returns>This BatchOperationItem instance for fluent chaining</returns>
    public BatchOperationItem WithDependency(string dependencyName)
    {
        DependsOn.Add(dependencyName);
        return this;
    }

    /// <summary>
    /// Adds multiple dependencies to this operation.
    /// Fluent method for building operation items in code or tests.
    /// </summary>
    /// <param name="dependencyNames">The names of the dependency operations</param>
    /// <returns>This BatchOperationItem instance for fluent chaining</returns>
    public BatchOperationItem WithDependencies(params string[] dependencyNames)
    {
        DependsOn.AddRange(dependencyNames);
        return this;
    }

    /// <summary>
    /// Adds an argument to this operation.
    /// Fluent method for building operation items in code or tests.
    /// </summary>
    /// <param name="key">The argument key</param>
    /// <param name="value">The argument value</param>
    /// <returns>This BatchOperationItem instance for fluent chaining</returns>
    public BatchOperationItem WithArgument(string key, object? value)
    {
        Arguments[key] = value;
        return this;
    }

    /// <summary>
    /// Sets whether this operation is required.
    /// Fluent method for building operation items in code or tests.
    /// </summary>
    /// <param name="required">Whether this operation is required</param>
    /// <returns>This BatchOperationItem instance for fluent chaining</returns>
    public BatchOperationItem WithIsRequired(bool required)
    {
        IsRequired = required;
        return this;
    }

    /// <summary>
    /// Sets the maximum retry count for this operation.
    /// Fluent method for building operation items in code or tests.
    /// </summary>
    /// <param name="retries">Maximum retry count</param>
    /// <returns>This BatchOperationItem instance for fluent chaining</returns>
    public BatchOperationItem WithMaxRetries(int retries)
    {
        MaxRetries = retries;
        return this;
    }

    /// <summary>
    /// Returns a string representation of the operation item.
    /// Useful for logging and debugging.
    /// </summary>
    public override string ToString()
    {
        var deps = DependsOn.Any() ? $" (depends on: {string.Join(", ", DependsOn)})" : "";
        var args = Arguments.Any() ? $" (args: {Arguments.Count})" : "";
        return $"OperationItem('{Name}'{deps}{args}, Required={IsRequired}, Retries={MaxRetries})";
    }
}