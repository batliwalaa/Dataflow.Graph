using DataflowGraph.Abstractions;
using DataflowGraph.Resolution;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DataflowGraph.Tests.Resolution;

public class OperationResolverTests
{
    [Fact]
    public void Constructor_Throws_WhenNoOperationsRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var act = () => new OperationResolver(serviceProvider);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No IOperationExecutor instances found*");
    }

    [Fact]
    public void Constructor_Succeeds_WhenOperationsRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        services.AddSingleton<IOperationExecutor, TestOperationB>();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var resolver = new OperationResolver(serviceProvider);

        // Assert
        resolver.Should().NotBeNull();
    }

    [Fact]
    public void Resolve_ReturnsExecutor_WhenOperationExists()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act
        var executor = resolver.Resolve("TestOperationA");

        // Assert
        executor.Should().NotBeNull();
        executor.OperationName.Should().Be("TestOperationA");
        executor.Should().BeOfType<TestOperationA>();
    }

    [Fact]
    public void Resolve_Throws_WhenOperationNotFound()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act & Assert
        var act = () => resolver.Resolve("NonExistentOperation");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*")
            .WithMessage("*TestOperationA*");
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act
        var executor1 = resolver.Resolve("TestOperationA");
        var executor2 = resolver.Resolve("testoperationa");
        var executor3 = resolver.Resolve("TESTOPERATIONA");

        // Assert
        executor1.Should().BeSameAs(executor2);
        executor2.Should().BeSameAs(executor3);
    }

    [Fact]
    public void TryResolve_ReturnsTrue_WhenOperationExists()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act
        var success = resolver.TryResolve("TestOperationA", out var executor);

        // Assert
        success.Should().BeTrue();
        executor.Should().NotBeNull();
        executor!.OperationName.Should().Be("TestOperationA");
    }

    [Fact]
    public void TryResolve_ReturnsFalse_WhenOperationNotFound()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act
        var success = resolver.TryResolve("NonExistent", out var executor);

        // Assert
        success.Should().BeFalse();
        executor.Should().BeNull();
    }

    [Fact]
    public void TryResolve_ReturnsFalse_WhenOperationNameIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act
        var success = resolver.TryResolve(null!, out var executor);

        // Assert
        success.Should().BeFalse();
        executor.Should().BeNull();
    }

    [Fact]
    public void Resolve_CachesExecutors_ForSubsequentCalls()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act
        var executor1 = resolver.Resolve("TestOperationA");
        var executor2 = resolver.Resolve("TestOperationA");

        // Assert
        executor1.Should().BeSameAs(executor2);
    }

    [Fact]
    public void GetRegisteredOperationNames_ReturnsAllRegisteredNames()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        services.AddSingleton<IOperationExecutor, TestOperationB>();
        services.AddSingleton<IOperationExecutor, TestOperationC>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act
        var names = resolver.GetRegisteredOperationNames().ToList();

        // Assert
        names.Should().HaveCount(3);
        names.Should().Contain("TestOperationA");
        names.Should().Contain("TestOperationB");
        names.Should().Contain("TestOperationC");
        names.Should().BeInAscendingOrder(); // Should be sorted
    }

    [Fact]
    public void ClearCache_ClearsResolvedExecutors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperationExecutor, TestOperationA>();
        var serviceProvider = services.BuildServiceProvider();
        var resolver = new OperationResolver(serviceProvider);

        // Act - resolve once to populate cache
        resolver.Resolve("TestOperationA");
        var cacheSize1 = resolver.GetCacheSize();

        // Act - clear cache
        resolver.ClearCache();
        var cacheSize2 = resolver.GetCacheSize();

        // Assert
        cacheSize1.Should().Be(1);
        cacheSize2.Should().Be(0);
    }

    [Fact]
    public void Resolve_Throws_WhenServiceProviderIsNull()
    {
        // Act & Assert
        var act = () => new OperationResolver(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceProvider");
    }

    // Test Operation Implementations

    private class TestOperationA : IOperationExecutor
    {
        public string OperationName => "TestOperationA";
        public Task<OperationResult> ExecuteAsync(IBatchContext batchContext, IGraphContext graphContext, IDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => Task.FromResult(OperationResult.Success(OperationName, null));
    }

    private class TestOperationB : IOperationExecutor
    {
        public string OperationName => "TestOperationB";
        public Task<OperationResult> ExecuteAsync(IBatchContext batchContext, IGraphContext graphContext, IDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => Task.FromResult(OperationResult.Success(OperationName, null));
    }

    private class TestOperationC : IOperationExecutor
    {
        public string OperationName => "TestOperationC";
        public Task<OperationResult> ExecuteAsync(IBatchContext batchContext, IGraphContext graphContext, IDictionary<string, object?>? arguments, CancellationToken cancellationToken)
            => Task.FromResult(OperationResult.Success(OperationName, null));
    }
}