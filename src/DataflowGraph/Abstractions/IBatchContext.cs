namespace DataflowGraph.Abstractions;

/// <summary>
/// Provides contextual information and shared state for batch execution.
/// Flows through all operations during batch execution.
/// Each operation receives the same BatchContext instance, enabling:
/// - Correlation across operations (BatchId, UserId, TenantId)
/// - Shared custom data (Data dictionary)
/// - Cancellation propagation
/// - Timing and tracking information
/// </summary>
public interface IBatchContext
{
    /// <summary>
    /// Gets the unique identifier for this batch execution.
    /// Generated at batch start. Useful for correlation, logging, and tracking across all operations.
    /// Example: "a1b2c3d4e5f6" (12-character hex string)
    /// </summary>
    string BatchId { get; }

    /// <summary>
    /// Gets when the batch execution started (UTC).
    /// Useful for duration calculations and timeout enforcement.
    /// </summary>
    DateTime StartTime { get; }

    /// <summary>
    /// Gets or sets custom data that can be shared across all operations.
    /// Thread-safe dictionary for passing state that isn't an operation result.
    /// Example: Store correlation IDs, feature flags, or cross-operation state.
    /// </summary>
    IDictionary<string, object?> Data { get; }

    /// <summary>
    /// Gets or sets the current user identifier (if applicable).
    /// Set by the calling code before batch execution.
    /// Operations can use this for user-specific logic or auditing.
    /// </summary>
    string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the current tenant identifier (if applicable).
    /// Set by the calling code before batch execution.
    /// Operations can use this for multi-tenant data isolation.
    /// </summary>
    string? TenantId { get; set; }

    /// <summary>
    /// Gets the cancellation token for this batch execution.
    /// All operations should respect this token for cooperative cancellation.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets a value from the custom data dictionary.
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="key">The data key</param>
    /// <returns>The value if found and type matches, default(T) otherwise</returns>
    T? GetData<T>(string key);

    /// <summary>
    /// Sets a value in the custom data dictionary.
    /// </summary>
    /// <typeparam name="T">The type of the value</typeparam>
    /// <param name="key">The data key</param>
    /// <param name="value">The value to store</param>
    void SetData<T>(string key, T value);

    /// <summary>
    /// Gets a required value from the custom data dictionary.
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="key">The data key</param>
    /// <returns>The value</returns>
    /// <exception cref="InvalidOperationException">Thrown when key not found or type mismatch</exception>
    T GetRequiredData<T>(string key);
}