using DataflowGraph.Abstractions;

namespace DataflowGraph.Resolution;

/// <summary>
/// Resolves operation names to IOperationExecutor instances.
/// Implementations use DI to resolve operations and cache them for performance.
/// This is the key abstraction that allows the library to be operation-agnostic.
/// 
/// Key responsibilities:
/// - Resolve operation name → IOperationExecutor
/// - Cache resolved instances for performance (operations are typically stateless)
/// - Provide list of registered operation names (for validation/documentation)
/// - Handle missing operations gracefully (clear error messages)
/// 
/// Architecture:
/// - Operations are registered in DI container (IServiceCollection)
/// - OperationResolver queries DI for all IOperationExecutor instances
/// - Builds a lookup dictionary by OperationName for O(1) resolution
/// - Caches resolved instances to avoid repeated DI lookups
/// 
/// </summary>
public interface IOperationResolver
{
    /// <summary>
    /// Resolves an operation executor by name.
    /// Uses cached lookup for performance (O(1) after first resolution).
    /// </summary>
    /// <param name="operationName">
    /// The operation name from the request.
    /// Must match the OperationName property of a registered IOperationExecutor.
    /// Case-insensitive matching (e.g., "FetchUsers" = "fetchusers").
    /// </param>
    /// <returns>The operation executor instance</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// - No operation with the specified name is registered
    /// - Error message includes list of registered operation names for debugging
    /// </exception>
    /// <example>
    /// <code>
    /// var executor = resolver.Resolve("FetchUsers");
    /// var result = await executor.ExecuteAsync(batchContext, graphContext, arguments, ct);
    /// </code>
    /// </example>
    IOperationExecutor Resolve(string operationName);

    /// <summary>
    /// Tries to resolve an operation executor by name.
    /// Safe alternative to Resolve() - returns false instead of throwing.
    /// Useful for validation before execution or optional operations.
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
    bool TryResolve(string operationName, out IOperationExecutor? executor);

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
    /// if (!registeredNames.Contains(requestedOperationName))
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
    IEnumerable<string> GetRegisteredOperationNames();
}