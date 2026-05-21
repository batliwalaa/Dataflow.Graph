namespace DataflowGraph.AspNetCore;

/// <summary>
/// Extension methods for BatchOperationDefinition.
/// Provides conversion between Core and AspNetCore types.
/// 
/// Why this exists:
/// - Keeps Core library free of AspNetCore dependencies
/// - Conversion logic lives in AspNetCore package (correct layer)
/// - Prevents circular dependency
/// </summary>
public static class BatchOperationDefinitionExtensions
{
    /// <summary>
    /// Converts a BatchOperationItem (HTTP DTO) to BatchOperationDefinition (Core).
    /// Used when converting HTTP requests to internal execution format.
    /// </summary>
    /// <param name="item">The HTTP request operation item</param>
    /// <returns>A new BatchOperationDefinition instance</returns>
    public static BatchOperationDefinition ToOperationDefinition(this BatchOperationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new BatchOperationDefinition
        {
            Name = item.Name,
            DependsOn = item.DependsOn,
            Arguments = item.Arguments,
            IsRequired = item.IsRequired,
            MaxRetries = item.MaxRetries
        };
    }

    /// <summary>
    /// Converts a list of BatchOperationItem to BatchOperationDefinition.
    /// </summary>
    /// <param name="items">The HTTP request operation items</param>
    /// <returns>List of BatchOperationDefinition instances</returns>
    public static List<BatchOperationDefinition> ToOperationDefinitions(this IEnumerable<BatchOperationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return [.. items.Select(item => item.ToOperationDefinition())];
    }

    /// <summary>
    /// Converts a BatchOperationRequest to BatchOperationDefinition list.
    /// Convenience method for controllers.
    /// </summary>
    /// <param name="request">The HTTP batch request</param>
    /// <returns>List of BatchOperationDefinition instances</returns>
    public static List<BatchOperationDefinition> ToOperationDefinitions(this BatchOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Operations.ToOperationDefinitions();
    }
}