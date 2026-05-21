using DataflowGraph.Abstractions;
using System.Collections.Concurrent;

namespace DataflowGraph;

/// <summary>
/// Default implementation of IBatchContext.
/// Provides contextual information and shared state for batch execution.
/// Thread-safe for concurrent operation execution (uses ConcurrentDictionary).
/// 
/// Key features:
/// - Auto-generates BatchId if not provided
/// - Tracks StartTime for duration calculations
/// - Thread-safe Data dictionary for cross-operation state
/// - Supports UserId and TenantId for multi-tenant scenarios
/// - CancellationToken propagation to all operations
/// - Clone() method for creating child contexts
/// 
/// </summary>
public class BatchContext : IBatchContext
{
    private readonly ConcurrentDictionary<string, object?> _data;

    /// <summary>
    /// Gets the unique identifier for this batch execution.
    /// Generated at construction time if not provided.
    /// Format: 12-character hex string (e.g., "a1b2c3d4e5f6")
    /// Used for correlation, logging, and tracking across all operations.
    /// </summary>
    public string BatchId { get; }

    /// <summary>
    /// Gets when the batch execution started (UTC).
    /// Set at construction time.
    /// Used for duration calculations and timeout enforcement.
    /// </summary>
    public DateTime StartTime { get; }

    /// <summary>
    /// Gets or sets custom data that can be shared across all operations.
    /// Thread-safe dictionary for passing state that isn't an operation result.
    /// 
    /// Examples:
    /// - Correlation IDs for external systems
    /// - Feature flags that affect multiple operations
    /// - Cross-operation state (e.g., "SkipValidation" flag)
    /// - Caching configuration
    /// </summary>
    public IDictionary<string, object?> Data => _data;

    /// <summary>
    /// Gets or sets the current user identifier (if applicable).
    /// Set by the calling code before batch execution.
    /// Operations can use this for user-specific logic or auditing.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the current tenant identifier (if applicable).
    /// Set by the calling code before batch execution.
    /// Operations can use this for multi-tenant data isolation.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets the cancellation token for this batch execution.
    /// All operations should respect this token for cooperative cancellation.
    /// </summary>
    public CancellationToken CancellationToken => _cancellationToken;

    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Initializes a new instance of the BatchContext class.
    /// </summary>
    /// <param name="batchId">
    /// Optional batch ID. If not provided, a new GUID-based ID is generated.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for the batch.
    /// </param>
    /// <param name="userId">
    /// Optional user identifier. Can also be set after construction.
    /// </param>
    /// <param name="tenantId">
    /// Optional tenant identifier. Can also be set after construction.
    /// </param>
    public BatchContext(
        string? batchId = null,
        CancellationToken cancellationToken = default,
        string? userId = null,
        string? tenantId = null)
    {
        BatchId = batchId ?? Guid.NewGuid().ToString("N")[..12];
        StartTime = DateTime.UtcNow;
        _cancellationToken = cancellationToken;
        _data = new ConcurrentDictionary<string, object?>();
        UserId = userId;
        TenantId = tenantId;
    }

    /// <summary>
    /// Private constructor for Clone() method.
    /// Allows copying all fields including readonly ones.
    /// </summary>
    private BatchContext(
        string batchId,
        DateTime startTime,
        CancellationToken cancellationToken,
        string? userId,
        string? tenantId,
        ConcurrentDictionary<string, object?> data)
    {
        BatchId = batchId;
        StartTime = startTime;
        _cancellationToken = cancellationToken;
        _data = data;
        UserId = userId;
        TenantId = tenantId;
    }

    /// <summary>
    /// Gets a value from the custom data dictionary.
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="key">The data key</param>
    /// <returns>The value if found and type matches, default(T) otherwise</returns>
    public T? GetData<T>(string key)
    {
        if (_data.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Sets a value in the custom data dictionary.
    /// Thread-safe - can be called from multiple operations concurrently.
    /// </summary>
    /// <typeparam name="T">The type of the value</typeparam>
    /// <param name="key">The data key (case-sensitive)</param>
    /// <param name="value">The value to store (can be null)</param>
    public void SetData<T>(string key, T value)
    {
        _data[key] = value;
    }

    /// <summary>
    /// Gets a required value from the custom data dictionary.
    /// Throws if the key is missing or type doesn't match.
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="key">The data key</param>
    /// <returns>The value</returns>
    /// <exception cref="InvalidOperationException">Thrown when key not found or type mismatch</exception>
    public T GetRequiredData<T>(string key)
    {
        if (_data.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
            {
                return typedValue;
            }
            throw new InvalidOperationException(
                $"Data key '{key}' exists but type mismatch. Expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}");
        }
        throw new InvalidOperationException($"Required data key '{key}' not found in batch context");
    }

    /// <summary>
    /// Creates a new BatchContext with the same values but a new cancellation token.
    /// Useful for child batch operations or nested batch execution.
    /// </summary>
    /// <param name="cancellationToken">New cancellation token for the child context</param>
    /// <returns>A new BatchContext instance with shared values</returns>
    public BatchContext Clone(CancellationToken cancellationToken)
    {
        // Create a shallow copy of the data dictionary
        var copiedData = new ConcurrentDictionary<string, object?>(_data);

        // Use private constructor to copy all fields including readonly ones
        return new BatchContext(
            BatchId,
            StartTime,
            cancellationToken,
            UserId,
            TenantId,
            copiedData);
    }

    /// <summary>
    /// Returns a string representation of the batch context.
    /// Useful for logging and debugging.
    /// </summary>
    public override string ToString()
    {
        return $"BatchContext(BatchId={BatchId}, UserId={UserId ?? "null"}, TenantId={TenantId ?? "null"}, DataCount={_data.Count})";
    }
}