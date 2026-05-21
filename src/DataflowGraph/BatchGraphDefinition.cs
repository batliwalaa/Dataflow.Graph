#pragma warning disable IDE0290 // Use primary constructor
namespace DataflowGraph;

/// <summary>
/// Declarative definition of a complete batch graph.
/// Used for scenarios where the entire batch structure comes from external sources.
/// </summary>
public class BatchGraphDefinition
{
    /// <summary>
    /// Gets or sets the unique identifier for this batch definition.
    /// </summary>
    public string BatchId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Gets or sets the display name for this batch definition.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description of this batch definition.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of step definitions in this batch.
    /// </summary>
    public List<StepDefinitionDto> Steps { get; set; } = [];

    /// <summary>
    /// Gets or sets the processing type (Serial or Parallel).
    /// </summary>
    public ProcessingType ProcessingType { get; set; } = ProcessingType.Parallel;

    /// <summary>
    /// Gets or sets the maximum degree of parallelism when using Parallel mode.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// Gets or sets whether to continue processing after a step failure.
    /// </summary>
    public bool ContinueOnError { get; set; } = false;

    /// <summary>
    /// Gets or sets the batch timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets metadata for this batch definition.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Gets or sets when this batch definition was created (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when this batch definition was last modified (UTC).
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the version of this batch definition.
    /// </summary>
    public string? Version { get; set; } = "1.0";

    /// <summary>
    /// Validates this batch graph definition.
    /// </summary>
    public BatchValidationResult Validate(IEnumerable<string> registeredOperationNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (Steps.Count == 0)
        {
            errors.Add("Batch definition must have at least one step");
            return new BatchValidationResult(false, errors, warnings);
        }

        var duplicateNames = Steps
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            errors.Add($"Duplicate step names: [{string.Join(", ", duplicateNames)}]");
        }

        var allStepNames = Steps.Select(s => s.Name);
        foreach (var step in Steps)
        {
            var stepErrors = step.Validate(allStepNames, registeredOperationNames);
            errors.AddRange(stepErrors);
        }

        var cycleResult = DetectCircularDependencies();
        if (!cycleResult.IsValid)
        {
            errors.Add($"Circular dependency detected: {cycleResult.CyclePath}");
        }

        if (ProcessingType == ProcessingType.Serial && MaxDegreeOfParallelism > 1)
        {
            warnings.Add($"MaxDegreeOfParallelism ({MaxDegreeOfParallelism}) is ignored in Serial mode");
        }

        if (TimeoutSeconds.HasValue && TimeoutSeconds <= 0)
        {
            warnings.Add($"TimeoutSeconds ({TimeoutSeconds}) is invalid. Use null for no timeout or positive value");
        }

        return new BatchValidationResult(errors.Count == 0, errors, warnings);
    }

    /// <summary>
    /// Detects circular dependencies in the batch graph.
    /// </summary>
    private BatchValidationResult DetectCircularDependencies()
    {
        var adjacencyList = Steps.ToDictionary(
            s => s.Name,
            s => s.Dependencies,
            StringComparer.OrdinalIgnoreCase);

        var color = Steps.ToDictionary(s => s.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        string? FindCycle(string node, string? parentNode)
        {
            color[node] = 1;
            parent[node] = parentNode;

            if (adjacencyList.TryGetValue(node, out var dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    if (color.GetValueOrDefault(dependency, 0) == 1)
                    {
                        var cyclePath = new List<string> { dependency };
                        var current = node;
                        while (current != dependency && current != null)
                        {
                            cyclePath.Add(current);
                            current = parent.GetValueOrDefault(current);
                        }
                        cyclePath.Add(dependency);
                        cyclePath.Reverse();
                        return string.Join(" → ", cyclePath);
                    }
                    else if (color.GetValueOrDefault(dependency, 0) == 0)
                    {
                        var cycle = FindCycle(dependency, node);
                        if (cycle != null)
                        {
                            return cycle;
                        }
                    }
                }
            }

            color[node] = 2;
            return null;
        }

        foreach (var step in Steps)
        {
            if (color.GetValueOrDefault(step.Name, 0) == 0)
            {
                var cycle = FindCycle(step.Name, null);
                if (cycle != null)
                {
                    return new BatchValidationResult(false, [], [], cycle);
                }
            }
        }

        return new BatchValidationResult(true, [], []);
    }

    /// <summary>
    /// Converts this batch graph definition to a list of BatchOperationDefinition.
    /// </summary>
    public List<BatchOperationDefinition> ToOperationDefinitions()
    {
        return [.. Steps.Select(BatchOperationDefinition.FromStepDefinition)];
    }

    /// <summary>
    /// Creates a BatchGraphDefinition from individual parameters.
    /// </summary>
    public static BatchGraphDefinition Create(
        string? batchId = null,
        string? displayName = null,
        string? description = null,
        ProcessingType processingType = ProcessingType.Parallel,
        int maxDegreeOfParallelism = 4,
        int? timeoutSeconds = 300)
    {
        return new BatchGraphDefinition
        {
            BatchId = batchId ?? Guid.NewGuid().ToString("N")[..12],
            DisplayName = displayName,
            Description = description,
            ProcessingType = processingType,
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            TimeoutSeconds = timeoutSeconds
        };
    }

    /// <summary>
    /// Adds a step to this batch definition.
    /// </summary>
    public BatchGraphDefinition AddStep(StepDefinitionDto step)
    {
        Steps.Add(step);
        ModifiedAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>
    /// Adds multiple steps to this batch definition.
    /// </summary>
    public BatchGraphDefinition AddSteps(IEnumerable<StepDefinitionDto> steps)
    {
        Steps.AddRange(steps);
        ModifiedAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>
    /// Returns a string representation of the batch definition.
    /// </summary>
    public override string ToString()
    {
        return $"BatchGraphDefinition('{BatchId}' - {DisplayName ?? "unnamed"}, Steps={Steps.Count}, Mode={ProcessingType})";
    }
}

/// <summary>
/// Result of batch graph definition validation.
/// </summary>
public class BatchValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the list of validation errors.
    /// </summary>
    public List<string> Errors { get; }

    /// <summary>
    /// Gets the list of validation warnings.
    /// </summary>
    public List<string> Warnings { get; }

    /// <summary>
    /// Gets the circular dependency cycle path if detected.
    /// </summary>
    public string? CyclePath { get; }

    /// <summary>
    /// Initializes a new instance of the BatchValidationResult class.
    /// </summary>
    public BatchValidationResult(
        bool isValid,
        List<string> errors,
        List<string> warnings,
        string? cyclePath = null)
    {
        IsValid = isValid;
        Errors = errors ?? [];
        Warnings = warnings ?? [];
        CyclePath = cyclePath;
    }

    /// <summary>
    /// Returns a string representation of the validation result.
    /// Useful for logging and error messages.
    /// </summary>
    public override string ToString()
    {
        if (IsValid)
        {
            return $"Validation passed ({Warnings.Count} warnings)";
        }

        var errorList = string.Join("; ", Errors);
        var cycleInfo = CyclePath != null ? $" Cycle: {CyclePath}" : "";
        return $"Validation failed: {errorList}{cycleInfo}";
    }
}
#pragma warning restore IDE0290 // Use primary constructor