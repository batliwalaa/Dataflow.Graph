using DataflowGraph;
using DataflowGraph.Abstractions;
using DataflowGraph.Resolution;

#pragma warning disable IDE0130
#pragma warning disable IDE0290 // Use primary constructor
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering DataflowGraph services in the DI container.
/// </summary>
public static class DataflowGraphServiceCollectionExtensions
{
    /// <summary>
    /// Adds DataflowGraph services to the DI container with default options.
    /// </summary>
    public static IDataflowGraphBuilder AddDataflowGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOperationResolver, OperationResolver>();
        services.AddTransient<IBatchGraphFactory, BatchGraphFactory>();

        return new DataflowGraphBuilder(services);
    }

    /// <summary>
    /// Adds DataflowGraph services to the DI container with configuration options.
    /// </summary>
    public static IDataflowGraphBuilder AddDataflowGraph(
        this IServiceCollection services,
        Action<DataflowGraphOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);

        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.AddSingleton<IOperationResolver, OperationResolver>();
        services.AddTransient<IBatchGraphFactory, BatchGraphFactory>();

        return new DataflowGraphBuilder(services);
    }

    /// <summary>
    /// Adds DataflowGraph services and auto-registers all IOperationExecutor implementations from the calling assembly.
    /// </summary>
    public static IDataflowGraphBuilder AddDataflowGraphWithAutoDiscovery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // FIX: Chain the builder correctly
        return services.AddDataflowGraph()
            .AddOperationsFromAssembly(System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Registers all IOperationExecutor implementations from the specified assembly.
    /// </summary>
    public static IDataflowGraphBuilder AddOperationsFromAssembly(
        this IDataflowGraphBuilder builder,
        System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ArgumentNullException.ThrowIfNull(assembly);

        var operationTypes = assembly.GetTypes()
            .Where(t => typeof(IOperationExecutor).IsAssignableFrom(t) &&
                        !t.IsInterface &&
                        !t.IsAbstract);

        foreach (var operationType in operationTypes)
        {
            builder.Services.AddSingleton(typeof(IOperationExecutor), operationType);
        }

        return builder;
    }

    /// <summary>
    /// Registers a specific operation type.
    /// </summary>
    public static IDataflowGraphBuilder AddOperation<TOperation>(this IDataflowGraphBuilder builder)
        where TOperation : class, IOperationExecutor
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IOperationExecutor, TOperation>();
        return builder;
    }

    /// <summary>
    /// Registers a specific operation type with a custom lifetime.
    /// </summary>
    public static IDataflowGraphBuilder AddOperation<TOperation>(
        this IDataflowGraphBuilder builder,
        ServiceLifetime lifetime)
        where TOperation : class, IOperationExecutor
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Add(new ServiceDescriptor(
            typeof(IOperationExecutor),
            typeof(TOperation),
            lifetime));

        return builder;
    }
}

/// <summary>
/// Builder interface for fluent DataflowGraph configuration.
/// </summary>
public interface IDataflowGraphBuilder
{
    /// <summary>
    /// The IServiceCollection being configured.
    /// </summary>
    IServiceCollection Services { get; }
}

/// <summary>
/// Concrete implementation of IDataflowGraphBuilder.
/// </summary>
internal class DataflowGraphBuilder : IDataflowGraphBuilder
{
    public IServiceCollection Services { get; }

    public DataflowGraphBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }
}

/// <summary>
/// Configuration options for DataflowGraph.
/// </summary>
public class DataflowGraphOptions
{
    /// <summary>
    /// Gets or sets the default processing type.
    /// </summary>
    public ProcessingType ProcessingType { get; set; } = ProcessingType.Parallel;

    /// <summary>
    /// Gets or sets the default maximum degree of parallelism.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>
    /// Gets or sets the default batch timeout in seconds.
    /// </summary>
    public int? DefaultTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets whether to enable operation result caching.
    /// </summary>
    public bool EnableResultCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate batch definitions before execution.
    /// </summary>
    public bool ValidateDefinitions { get; set; } = true;
}
#pragma warning restore IDE0130
#pragma warning restore IDE0290 // Use primary constructor