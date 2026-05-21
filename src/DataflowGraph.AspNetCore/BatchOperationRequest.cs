namespace DataflowGraph.AspNetCore;

/// <summary>
/// HTTP request DTO for batch operation execution.
/// Sent by clients (mobile, web, external APIs) to execute multiple operations in a single request.
/// </summary>
public class BatchOperationRequest
{
    /// <summary>
    /// Gets or sets the unique identifier for this batch request.
    /// </summary>
    public string BatchId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Gets or sets the list of operations to execute.
    /// </summary>
    public List<BatchOperationItem> Operations { get; set; } = [];

    /// <summary>
    /// Gets or sets the processing type (Serial or Parallel).
    /// </summary>
    public ProcessingType ProcessingType { get; set; } = ProcessingType.Parallel;

    /// <summary>
    /// Gets or sets the maximum degree of parallelism when using Parallel mode.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// Gets or sets whether to continue processing after an operation failure.
    /// </summary>
    public bool ContinueOnError { get; set; } = false;

    /// <summary>
    /// Gets or sets optional metadata for this batch request.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Gets or sets the batch timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Validates this batch request.
    /// </summary>
    public BatchValidationResult Validate(IEnumerable<string> registeredOperationNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (Operations.Count == 0)
        {
            errors.Add("Batch request must have at least one operation");
            return new BatchValidationResult(false, errors, warnings);
        }

        var duplicateNames = Operations
            .GroupBy(op => op.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            errors.Add($"Duplicate operation names: [{string.Join(", ", duplicateNames)}]");
        }

        var allOperationNames = Operations.Select(op => op.Name);
        foreach (var operation in Operations)
        {
            if (!registeredOperationNames.Contains(operation.Name, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Operation '{operation.Name}' does not match any registered operation. " +
                          $"Available: [{string.Join(", ", registeredOperationNames)}]");
            }

            foreach (var dependency in operation.DependsOn)
            {
                if (!allOperationNames.Contains(dependency, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Operation '{operation.Name}' depends on '{dependency}' which is not in this batch");
                }
            }

            if (operation.DependsOn.Contains(operation.Name, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Operation '{operation.Name}' cannot depend on itself");
            }

            if (operation.MaxRetries < 0)
            {
                errors.Add($"Operation '{operation.Name}' has invalid MaxRetries value: {operation.MaxRetries}");
            }
        }

        if (ProcessingType == ProcessingType.Serial && MaxDegreeOfParallelism > 1)
        {
            warnings.Add($"MaxDegreeOfParallelism ({MaxDegreeOfParallelism}) is ignored in Serial mode");
        }

        if (MaxDegreeOfParallelism > 100)
        {
            warnings.Add($"MaxDegreeOfParallelism ({MaxDegreeOfParallelism}) is very high. Consider a lower value.");
        }

        if (TimeoutSeconds.HasValue && TimeoutSeconds <= 0)
        {
            warnings.Add($"TimeoutSeconds ({TimeoutSeconds}) is invalid. Use null for no timeout or positive value");
        }

        return new BatchValidationResult(errors.Count == 0, errors, warnings);
    }

    /// <summary>
    /// Converts this BatchOperationRequest to a list of BatchOperationDefinition.
    /// Uses extension method from BatchOperationDefinitionExtensions (in AspNetCore package).
    /// </summary>
    public List<BatchOperationDefinition> ToOperationDefinitions()
    {
        return this.Operations.ToOperationDefinitions();
    }

    /// <summary>
    /// Creates a BatchOperationRequest from individual parameters.
    /// </summary>
    public static BatchOperationRequest Create(
        string? batchId = null,
        ProcessingType processingType = ProcessingType.Parallel,
        int maxDegreeOfParallelism = 4,
        int? timeoutSeconds = 300)
    {
        return new BatchOperationRequest
        {
            BatchId = batchId ?? Guid.NewGuid().ToString("N")[..12],
            ProcessingType = processingType,
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            TimeoutSeconds = timeoutSeconds
        };
    }

    /// <summary>
    /// Adds an operation to this batch request.
    /// </summary>
    public BatchOperationRequest AddOperation(
        string name,
        Dictionary<string, object?>? arguments = null,
        IEnumerable<string>? dependsOn = null,
        bool isRequired = true,
        int maxRetries = 0)
    {
        Operations.Add(new BatchOperationItem
        {
            Name = name,
            Arguments = arguments ?? [],
            DependsOn = dependsOn?.ToList() ?? [],
            IsRequired = isRequired,
            MaxRetries = maxRetries
        });
        return this;
    }

    /// <summary>
    /// Adds metadata to this batch request.
    /// </summary>
    public BatchOperationRequest WithMetadata(string key, string value)
    {
        Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Returns a string representation of the batch request.
    /// </summary>
    public override string ToString()
    {
        return $"BatchOperationRequest(BatchId={BatchId}, Operations={Operations.Count}, Mode={ProcessingType})";
    }
}