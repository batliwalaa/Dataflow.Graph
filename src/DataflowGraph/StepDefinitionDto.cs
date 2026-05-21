namespace DataflowGraph;

/// <summary>
/// Declarative definition of a single step in a batch graph.
/// Used for scenarios where the batch graph structure comes from external sources:
/// - JSON configuration files
/// - Database-stored workflows
/// - Admin UI-built batch definitions
/// - Dynamic/runtime-defined batches
/// 
/// Why this exists:
/// - Allows batch graphs to be defined without recompiling code
/// - Separates graph structure (configuration) from operation logic (code)
/// - Enables admin users to build/modify batch workflows via UI
/// - Supports multi-tenant scenarios where each tenant has different batch flows
/// 
/// Conversion:
/// - StepDefinitionDto → BatchOperationDefinition (via FromStepDefinition)
/// - BatchOperationDefinition → Execution (via ExecutionStrategy)
/// 
/// </summary>
public class StepDefinitionDto
{
    /// <summary>
    /// Gets or sets the unique name of this step.
    /// Used to resolve to IOperationExecutor via IOperationResolver.
    /// Must match the OperationName property of a registered operation.
    /// 
    /// Example: "FetchUsers", "ValidateOrder", "SendNotification"
    /// 
    /// Validation:
    /// - Must not be null or empty
    /// - Must match a registered operation name
    /// - Must be unique within the batch definition
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of step names this step depends on.
    /// Dependent steps wait for their dependencies to complete before executing.
    /// In Parallel mode, independent steps run concurrently.
    /// 
    /// Example:
    /// - "GenerateReport" depends on ["FetchUsers", "FetchProducts"]
    /// - "SendNotification" depends on ["SaveOrder"]
    /// 
    /// Validation:
    /// - All dependency names must exist as steps in the same batch definition
    /// - Circular dependencies are detected and rejected at execution time
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Gets or sets parameters to pass to the operation.
    /// Operations receive these via ExecuteAsync(arguments) parameter.
    /// 
    /// Example (JSON):
    /// {
    ///   "tenantId": "tenant-123",
    ///   "includeInactive": true,
    ///   "maxResults": 100
    /// }
    /// 
    /// Example (C#):
    /// Parameters["tenantId"] = "tenant-123";
    /// Parameters["includeInactive"] = true;
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this step is required.
    /// If true (default), failure stops dependent steps.
    /// If false, dependent steps can still run (they'll see the failure).
    /// 
    /// Use false for:
    /// - Optional steps (e.g., "SendNotification" can fail without stopping the batch)
    /// - Best-effort steps (e.g., "LogToAnalytics" can fail silently)
    /// - Non-critical operations (e.g., "UpdateCache" can fail gracefully)
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry count for this step.
    /// Default is 0 (no retries).
    /// On failure, the step will be retried up to this many times before marking as failed.
    /// 
    /// Use for:
    /// - Transient failures (network timeouts, database deadlocks)
    /// - External API calls (may succeed on retry)
    /// 
    /// Don't use for:
    /// - Validation failures (won't succeed on retry)
    /// - Business logic errors (fix the data, don't retry)
    /// </summary>
    public int MaxRetries { get; set; } = 0;

    /// <summary>
    /// Gets or sets optional metadata for this step.
    /// Useful for custom tracking, auditing, or step-specific configuration.
    /// Not used by the core library - purely for consumer use.
    /// 
    /// Example:
    /// - Metadata["Description"] = "Fetches users from database"
    /// - Metadata["Owner"] = "UserTeam"
    /// - Metadata["Timeout"] = "30s"
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Validates this step definition.
    /// Called before batch execution to catch configuration errors early.
    /// </summary>
    /// <param name="allStepNames">List of all step names in the batch (for dependency validation)</param>
    /// <param name="registeredOperationNames">List of registered operation names (for resolution validation)</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    public List<string> Validate(IEnumerable<string> allStepNames, IEnumerable<string> registeredOperationNames)
    {
        var errors = new List<string>();

        // Validate name is not empty
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Step name is required");
        }

        // Validate name matches a registered operation
        if (!string.IsNullOrWhiteSpace(Name) &&
            !registeredOperationNames.Contains(Name, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Step '{Name}' does not match any registered operation. Available: [{string.Join(", ", registeredOperationNames)}]");
        }

        // Validate dependencies exist as steps in the batch
        foreach (var dependency in Dependencies)
        {
            if (!allStepNames.Contains(dependency, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Step '{Name}' depends on '{dependency}' which is not defined in this batch");
            }
        }

        // Validate no circular self-dependency
        if (Dependencies.Contains(Name, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Step '{Name}' cannot depend on itself");
        }

        // Validate retry count is non-negative
        if (MaxRetries < 0)
        {
            errors.Add($"Step '{Name}' has invalid MaxRetries value: {MaxRetries} (must be >= 0)");
        }

        return errors;
    }

    /// <summary>
    /// Creates a StepDefinitionDto from individual parameters.
    /// Fluent factory method for easy step definition in code.
    /// </summary>
    /// <param name="name">The step name</param>
    /// <param name="dependencies">List of dependency step names</param>
    /// <param name="parameters">Optional parameters dictionary</param>
    /// <param name="isRequired">Whether this step is required</param>
    /// <param name="maxRetries">Maximum retry count</param>
    /// <param name="metadata">Optional metadata dictionary</param>
    /// <returns>A new StepDefinitionDto instance</returns>
    public static StepDefinitionDto Create(
        string name,
        IEnumerable<string>? dependencies = null,
        Dictionary<string, object?>? parameters = null,
        bool isRequired = true,
        int maxRetries = 0,
        Dictionary<string, string>? metadata = null)
    {
        return new StepDefinitionDto
        {
            Name = name,
            Dependencies = dependencies?.ToList() ?? new List<string>(),
            Parameters = parameters ?? new Dictionary<string, object?>(),
            IsRequired = isRequired,
            MaxRetries = maxRetries,
            Metadata = metadata ?? new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// Adds a dependency to this step.
    /// Fluent method for building step definitions in code.
    /// </summary>
    /// <param name="dependencyName">The name of the dependency step</param>
    /// <returns>This StepDefinitionDto instance for fluent chaining</returns>
    public StepDefinitionDto WithDependency(string dependencyName)
    {
        Dependencies.Add(dependencyName);
        return this;
    }

    /// <summary>
    /// Adds multiple dependencies to this step.
    /// Fluent method for building step definitions in code.
    /// </summary>
    /// <param name="dependencyNames">The names of the dependency steps</param>
    /// <returns>This StepDefinitionDto instance for fluent chaining</returns>
    public StepDefinitionDto WithDependencies(params string[] dependencyNames)
    {
        Dependencies.AddRange(dependencyNames);
        return this;
    }

    /// <summary>
    /// Adds a parameter to this step.
    /// Fluent method for building step definitions in code.
    /// </summary>
    /// <param name="key">The parameter key</param>
    /// <param name="value">The parameter value</param>
    /// <returns>This StepDefinitionDto instance for fluent chaining</returns>
    public StepDefinitionDto WithParameter(string key, object? value)
    {
        Parameters[key] = value;
        return this;
    }

    /// <summary>
    /// Adds metadata to this step.
    /// Fluent method for building step definitions in code.
    /// </summary>
    /// <param name="key">The metadata key</param>
    /// <param name="value">The metadata value</param>
    /// <returns>This StepDefinitionDto instance for fluent chaining</returns>
    public StepDefinitionDto WithMetadata(string key, string value)
    {
        Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Returns a string representation of the step definition.
    /// Useful for logging and debugging.
    /// </summary>
    public override string ToString()
    {
        var deps = Dependencies.Any() ? $" (depends on: {string.Join(", ", Dependencies)})" : "";
        return $"Step('{Name}'{deps}, Required={IsRequired}, Retries={MaxRetries})";
    }
}