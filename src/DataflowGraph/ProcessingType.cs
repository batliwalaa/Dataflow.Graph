namespace DataflowGraph;

/// <summary>
/// Defines how operations in the batch are executed.
/// Controls whether operations run sequentially or with hybrid parallelism based on dependencies.
/// 
/// Processing modes:
/// - Serial: Operations execute one-by-one in order (no parallelism)
/// - Parallel: Independent operations run concurrently, dependent operations wait (hybrid)
/// 
/// </summary>
public enum ProcessingType
{
    /// <summary>
    /// <b>Serial Mode:</b> Operations execute one after another in the order they are defined.
    /// 
    /// Characteristics:
    /// - ❌ No parallelism (all operations run sequentially)
    /// - ✅ Predictable execution order
    /// - ✅ Easier to debug (clear step-by-step flow)
    /// - ✅ Safe for non-thread-safe operations
    /// - ⚠️ Slower for independent operations (no concurrency benefit)
    /// 
    /// Use when:
    /// - Operations must run in strict order
    /// - Operations share non-thread-safe resources
    /// - Debugging/tracing is critical
    /// - Performance is not a concern
    /// 
    /// Example execution flow:
    /// FetchUsers → ValidateUsers → SaveUsers → SendNotification
    /// (each waits for previous to complete)
    /// </summary>
    Serial = 0,

    /// <summary>
    /// <b>Parallel Mode (Default):</b> Hybrid execution based on dependencies.
    /// 
    /// Characteristics:
    /// - ✅ Independent operations run concurrently (parallel)
    /// - ✅ Dependent operations wait for prerequisites (serial relative to dependencies)
    /// - ✅ Maximum performance while respecting data dependencies
    /// - ✅ Thread-safe execution (uses ConcurrentDictionary, SemaphoreSlim)
    /// - ⚠️ Slightly harder to debug (non-deterministic order for independent ops)
    /// 
    /// Use when:
    /// - You have independent operations that can run in parallel
    /// - Performance is important (reduce total batch time)
    /// - Operations are thread-safe
    /// - You want the library to optimize execution automatically
    /// 
    /// Example execution flow:
    /// FetchUsers ──┬──→ ValidateUsers ──→ SaveUsers ──→ SendNotification
    /// FetchProducts┘    (parallel)          (serial relative to deps)
    /// 
    /// Total time: ~100ms (not 400ms) because FetchUsers and FetchProducts run in parallel
    /// </summary>
    Parallel = 1
}