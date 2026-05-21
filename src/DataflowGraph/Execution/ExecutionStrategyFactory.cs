using DataflowGraph.Resolution;

namespace DataflowGraph.Execution;

/// <summary>
/// Factory for creating execution strategies based on ProcessingType.
/// Implements the Factory pattern to centralize strategy creation logic.
/// 
/// Benefits:
/// - Single responsibility (only creates strategies)
/// - Easy to extend (add new strategy types without modifying BatchGraph)
/// - Consistent strategy creation across the library
/// - Testable (can mock the factory in unit tests)
/// 
/// Usage:
/// var strategy = ExecutionStrategyFactory.Create(
///     ProcessingType.Parallel,
///     resolver,
///     maxDegreeOfParallelism: 4,
///     onError: null,
///     onComplete: null);
/// 
/// </summary>
internal static class ExecutionStrategyFactory
{
    /// <summary>
    /// Creates an execution strategy for the specified processing type.
    /// </summary>
    /// <param name="processingType">
    /// The processing type that determines which strategy to create.
    /// - Serial: Creates SerialExecutionStrategy (sequential execution)
    /// - Parallel: Creates ParallelExecutionStrategy (hybrid parallel execution)
    /// </param>
    /// <param name="operationResolver">
    /// Resolves operation names to IOperationExecutor instances.
    /// Passed to the strategy for operation resolution during execution.
    /// </param>
    /// <param name="maxDegreeOfParallelism">
    /// Maximum number of operations that can run concurrently.
    /// Only used by ParallelExecutionStrategy (ignored by SerialExecutionStrategy).
    /// Default is 4. Set to -1 or int.MaxValue for unlimited parallelism.
    /// </param>
    /// <param name="onError">
    /// Optional callback invoked when an operation fails.
    /// Passed to the strategy for error notification.
    /// Signature: (operationName, exception)
    /// </param>
    /// <param name="onComplete">
    /// Optional callback invoked when an operation completes successfully.
    /// Passed to the strategy for completion notification.
    /// Signature: (operationName, duration)
    /// </param>
    /// <returns>
    /// An IExecutionStrategy instance appropriate for the specified processing type.
    /// - SerialExecutionStrategy for ProcessingType.Serial
    /// - ParallelExecutionStrategy for ProcessingType.Parallel
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when processingType is not a known value.
    /// This indicates a programming error or invalid configuration.
    /// </exception>
    /// <example>
    /// <code>
    /// // Create serial strategy
    /// var serialStrategy = ExecutionStrategyFactory.Create(
    ///     ProcessingType.Serial,
    ///     resolver,
    ///     maxDegreeOfParallelism: 1,
    ///     onError: (name, ex) => Console.WriteLine($"Error: {name}"),
    ///     onComplete: (name, duration) => Console.WriteLine($"Complete: {name}"));
    /// 
    /// // Create parallel strategy
    /// var parallelStrategy = ExecutionStrategyFactory.Create(
    ///     ProcessingType.Parallel,
    ///     resolver,
    ///     maxDegreeOfParallelism: 4,
    ///     onError: null,
    ///     onComplete: null);
    /// </code>
    /// </example>
    public static IExecutionStrategy Create(
        ProcessingType processingType,
        IOperationResolver operationResolver,
        int maxDegreeOfParallelism = 4,
        Action<string, Exception>? onError = null,
        Action<string, TimeSpan>? onComplete = null)
    {
        return processingType switch
        {
            ProcessingType.Serial => new SerialExecutionStrategy(
                operationResolver,
                onError,
                onComplete),

            ProcessingType.Parallel => new ParallelExecutionStrategy(
                operationResolver,
                maxDegreeOfParallelism,
                onError,
                onComplete),

            _ => throw new ArgumentOutOfRangeException(
                nameof(processingType),
                processingType,
                $"Unknown processing type: {processingType}. " +
                $"Valid values are: {nameof(ProcessingType.Serial)} ({(int)ProcessingType.Serial}), " +
                $"{nameof(ProcessingType.Parallel)} ({(int)ProcessingType.Parallel})")
        };
    }

    /// <summary>
    /// Gets the default processing type.
    /// Used when no processing type is specified in configuration.
    /// </summary>
    /// <returns>ProcessingType.Parallel (recommended for most scenarios)</returns>
    public static ProcessingType GetDefaultProcessingType()
    {
        return ProcessingType.Parallel;
    }

    /// <summary>
    /// Gets the default maximum degree of parallelism.
    /// Used when no parallelism setting is specified in configuration.
    /// </summary>
    /// <returns>4 (balanced for most scenarios)</returns>
    public static int GetDefaultMaxDegreeOfParallelism()
    {
        return 4;
    }

    /// <summary>
    /// Validates the processing type and parallelism settings.
    /// Returns a list of validation warnings (if any).
    /// </summary>
    /// <param name="processingType">The processing type to validate</param>
    /// <param name="maxDegreeOfParallelism">The parallelism setting to validate</param>
    /// <returns>List of validation warnings (empty if valid)</returns>
    public static List<string> ValidateSettings(
        ProcessingType processingType,
        int maxDegreeOfParallelism)
    {
        var warnings = new List<string>();

        // Warn if parallelism setting is ignored in Serial mode
        if (processingType == ProcessingType.Serial && maxDegreeOfParallelism > 1)
        {
            warnings.Add(
                $"MaxDegreeOfParallelism ({maxDegreeOfParallelism}) is ignored in Serial mode. " +
                $"Set to 1 or use Parallel mode for concurrent execution.");
        }

        // Warn if parallelism is too high
        if (maxDegreeOfParallelism > 100)
        {
            warnings.Add(
                $"MaxDegreeOfParallelism ({maxDegreeOfParallelism}) is very high. " +
                $"This may cause resource exhaustion. Consider a lower value (e.g., 4-16).");
        }

        // Warn if parallelism is too low
        if (processingType == ProcessingType.Parallel && maxDegreeOfParallelism < 1)
        {
            warnings.Add(
                $"MaxDegreeOfParallelism ({maxDegreeOfParallelism}) is less than 1. " +
                $"This will limit concurrency. Consider setting to 4 or higher.");
        }

        return warnings;
    }
}