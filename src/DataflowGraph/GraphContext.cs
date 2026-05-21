using System.Collections.Concurrent;
using DataflowGraph.Abstractions;

namespace DataflowGraph;

/// <summary>
/// Thread-safe implementation of IGraphContext that stores OperationResult objects
/// during batch graph execution. Uses ConcurrentDictionary for safe concurrent access
/// when steps run in parallel. Preserves error information per step.
/// </summary>
public class GraphContext : IGraphContext
{
    private readonly ConcurrentDictionary<string, OperationResult> _results = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores a step result for use by dependent steps.
    /// </summary>
    /// <param name="result">The OperationResult containing value and error status</param>
    internal void SetResult(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _results[result.OperationName] = result;
    }

    /// <summary>
    /// Gets the OperationResult for a completed step.
    /// </summary>
    /// <param name="stepName">The name of the step</param>
    /// <returns>The OperationResult containing value, error status, and exception details</returns>
    /// <exception cref="InvalidOperationException">Thrown when step has no result</exception>
    public OperationResult GetResult(string stepName)
    {
        if (_results.TryGetValue(stepName, out var result))
        {
            return result;
        }
        throw new InvalidOperationException($"No result found for step '{stepName}'. Ensure the step has completed.");
    }

    /// <summary>
    /// Gets the strongly-typed value from a successful step.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <param name="stepName">The name of the step</param>
    /// <returns>The typed value</returns>
    /// <exception cref="InvalidOperationException">Thrown when step failed or type mismatch</exception>
    public T GetValue<T>(string stepName)
    {
        var result = GetResult(stepName);
        return result.GetValue<T>();
    }

    /// <summary>
    /// Tries to get the strongly-typed value from a successful step.
    /// </summary>
    /// <typeparam name="T">The expected result type</typeparam>
    /// <param name="stepName">The name of the step</param>
    /// <param name="value">The typed value if successful, default otherwise</param>
    /// <returns>True if step succeeded and type matches, false otherwise</returns>
    public bool TryGetValue<T>(string stepName, out T value)
    {
        if (_results.TryGetValue(stepName, out var result))
        {
            return result.TryGetValue(out value);
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// Checks if a step has completed (successfully or with failure).
    /// </summary>
    /// <param name="stepName">The name of the step</param>
    /// <returns>True if the step has a result, false otherwise</returns>
    public bool HasResult(string stepName)
    {
        return _results.ContainsKey(stepName);
    }

    /// <summary>
    /// Checks if a step completed successfully.
    /// </summary>
    /// <param name="stepName">The name of the step</param>
    /// <returns>True if step succeeded, false if failed or not completed</returns>
    public bool IsSuccess(string stepName)
    {
        return _results.TryGetValue(stepName, out var result) && result.IsSuccess;
    }

    /// <summary>
    /// Gets all step results as a read-only dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, OperationResult> AllResults => _results;
}