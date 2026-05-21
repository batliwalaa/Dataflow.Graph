using System.Collections.Concurrent;
using DataflowGraph.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DataflowGraph.Resolution;

/// <summary>
/// DI-based operation resolver with caching.
/// Resolves operation names to IOperationExecutor instances using the service provider.
/// Caches resolved instances for performance (operations are typically stateless/singletons).
/// 
/// Key features:
/// - Queries DI for all registered IOperationExecutor instances at startup
/// - Builds a lookup dictionary by OperationName for O(1) resolution
/// - Caches resolved instances to avoid repeated DI lookups
/// - Case-insensitive operation name matching (e.g., "FetchUsers" = "fetchusers")
/// - Clear error messages with list of registered operations when not found
/// - Thread-safe for concurrent batch executions
/// 
/// Architecture:
/// 1. Constructor: Query DI for all IOperationExecutor instances
/// 2. Build lookup: Dictionary<operationName, IOperationExecutor>
/// 3. Cache: ConcurrentDictionary for thread-safe caching
/// 4. Resolve: O(1) lookup from cache
/// 
/// </summary>
internal class OperationResolver : IOperationResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IOperationExecutor> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IOperationExecutor> _nameLookup;
    private readonly IEnumerable<string> _registeredOperationNames;

    /// <summary>
    /// Initializes a new instance of the OperationResolver class.
    /// Queries DI for all registered IOperationExecutor instances and builds a lookup dictionary.
    /// </summary>
    /// <param name="serviceProvider">
    /// The service provider for DI resolution.
    /// Should be the root service provider (not a scoped provider).
    /// Operations are typically registered as singletons for caching to work effectively.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no IOperationExecutor instances are registered in DI.
    /// This indicates a configuration error - operations must be registered before use.
    /// </exception>
    public OperationResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Get all registered IOperationExecutor instances from DI
        _registeredOperationNames = [.. serviceProvider.GetServices<IOperationExecutor>().Select(e => e.OperationName)];

        // Build a lookup dictionary by OperationName for O(1) resolution
        // Group by name to handle potential duplicates (take first)
        _nameLookup = serviceProvider.GetServices<IOperationExecutor>()
            .GroupBy(e => e.OperationName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First(),
                StringComparer.OrdinalIgnoreCase);

        // Warn if no operations are registered (likely configuration error)
        if (_nameLookup.Count == 0)
        {
            throw new InvalidOperationException(
                "No IOperationExecutor instances found in DI. " +
                "Operations must be registered before using DataflowGraph. " +
                "Example: services.AddSingleton<IOperationExecutor, FetchUsersOperation>();");
        }
    }

    /// <summary>
    /// Resolves an operation executor by name.
    /// Uses cached lookup for performance (O(1) after first resolution).
    /// </summary>
    /// <param name="operationName">
    /// The operation name from the request.
    /// Must match the OperationName property of a registered IOperationExecutor.
    /// Case-insensitive matching (e.g., "FetchUsers" = "fetchusers" = "FETCHUSERS").
    /// </param>
    /// <returns>The operation executor instance (cached for subsequent calls)</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no operation with the specified name is registered.
    /// Error message includes list of registered operation names for debugging.
    /// </exception>
    /// <example>
    /// <code>
    /// var executor = resolver.Resolve("FetchUsers");
    /// var result = await executor.ExecuteAsync(batchContext, graphContext, arguments, ct);
    /// </code>
    /// </example>
    public IOperationExecutor Resolve(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);


        if (TryResolve(operationName, out var executor))
        {
            return executor!;
        }

        // Build helpful error message with list of registered operations
        var registeredNames = string.Join(", ", _registeredOperationNames.OrderBy(n => n));
        throw new InvalidOperationException(
            $"Operation '{operationName}' not found. " +
            $"Registered operations: [{registeredNames}]");
    }

    /// <summary>
    /// Tries to resolve an operation executor by name.
    /// Safe alternative to Resolve() - returns false instead of throwing.
    /// Uses cached lookup for performance (O(1) after first resolution).
    /// </summary>
    /// <param name="operationName">The operation name from the request</param>
    /// <param name="executor">
    /// The executor if found, null otherwise.
    /// Output parameter follows TryParse pattern.
    /// </param>
    /// <returns>
    /// True if operation was found and resolved.
    /// False if no operation with the specified name is registered.
    /// </returns>
    /// <example>
    /// <code>
    /// if (resolver.TryResolve("FetchUsers", out var executor))
    /// {
    ///     var result = await executor.ExecuteAsync(...);
    /// }
    /// else
    /// {
    ///     // Handle missing operation (log error, return 400, etc.)
    /// }
    /// </code>
    /// </example>
    public bool TryResolve(string operationName, out IOperationExecutor? executor)
    {
        if (operationName == null)
        {
            executor = null;
            return false;
        }

        // Check cache first (fastest path for repeated resolutions)
        if (_cache.TryGetValue(operationName, out executor))
        {
            return true;
        }

        // Check lookup table (first resolution for this operation name)
        if (_nameLookup.TryGetValue(operationName, out executor))
        {
            // Cache for future requests (thread-safe)
            _cache[operationName] = executor;
            return true;
        }

        // Operation not found
        executor = null;
        return false;
    }

    /// <summary>
    /// Gets all registered operation names.
    /// Useful for validation, documentation, and debugging.
    /// Returns names in alphabetical order for consistency.
    /// </summary>
    /// <returns>
    /// Enumerable of operation names that are registered in DI.
    /// Example: ["FetchUsers", "FetchProducts", "GenerateReport", "SendNotification"]
    /// </returns>
    /// <example>
    /// <code>
    /// // Validation: Check if requested operation exists
    /// var registeredNames = resolver.GetRegisteredOperationNames();
    /// if (!registeredNames.Contains(requestedOperationName, StringComparer.OrdinalIgnoreCase))
    /// {
    ///     throw new InvalidOperationException($"Unknown operation: {requestedOperationName}");
    /// }
    /// 
    /// // Documentation: List all available operations
    /// foreach (var name in resolver.GetRegisteredOperationNames())
    /// {
    ///     Console.WriteLine($"Available operation: {name}");
    /// }
    /// </code>
    /// </example>
    public IEnumerable<string> GetRegisteredOperationNames()
    {
        return _registeredOperationNames.OrderBy(n => n);
    }

    /// <summary>
    /// Clears the resolution cache.
    /// Useful for testing or dynamic operation registration scenarios.
    /// Warning: This will force re-resolution of all operations on next request.
    /// Only use in testing or special scenarios - not in normal operation.
    /// </summary>
    /// <example>
    /// <code>
    /// // In test setup
    /// [SetUp]
    /// public void SetUp()
    /// {
    ///     OperationResolver.ClearCache();
    /// }
    /// </code>
    /// </example>
    internal void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets cache statistics for monitoring and debugging.
    /// </summary>
    /// <returns>Number of cached operation executors</returns>
    internal int GetCacheSize()
    {
        return _cache.Count;
    }
}