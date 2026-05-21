using DataflowGraph.Abstractions;
using DataflowGraph.Execution;
using DataflowGraph.Resolution;
using FluentAssertions;
using Moq;
using System.Collections.Concurrent;

namespace DataflowGraph.Tests.Execution;

public class ParallelExecutionStrategyTests
{
    private readonly Mock<IOperationResolver> _mockResolver;

    public ParallelExecutionStrategyTests()
    {
        _mockResolver = new Mock<IOperationResolver>();
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesIndependentOperations_InParallel()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1"),
            BatchOperationDefinition.Create("Op2"),
            BatchOperationDefinition.Create("Op3")
        };

        var executionTimes = new ConcurrentDictionary<string, DateTime>();

        var executor1 = CreateMockExecutor("Op1", () => executionTimes["Op1"] = DateTime.UtcNow);
        var executor2 = CreateMockExecutor("Op2", () => executionTimes["Op2"] = DateTime.UtcNow);
        var executor3 = CreateMockExecutor("Op3", () => executionTimes["Op3"] = DateTime.UtcNow);

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);
        _mockResolver.Setup(r => r.Resolve("Op3")).Returns(executor3.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1", "Op2", "Op3"]);

        var strategy = new ParallelExecutionStrategy(_mockResolver.Object, maxDegreeOfParallelism: 3);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - All operations executed
        graphContext.HasResult("Op1").Should().BeTrue();
        graphContext.HasResult("Op2").Should().BeTrue();
        graphContext.HasResult("Op3").Should().BeTrue();

        // All should have similar start times (within 200ms = parallel execution)
        var times = executionTimes.Values.OrderBy(t => t).ToList();
        if (times.Count >= 2)
        {
            (times[1] - times[0]).TotalMilliseconds.Should().BeLessThan(200);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RespectsMaxDegreeOfParallelism()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1"),
            BatchOperationDefinition.Create("Op2"),
            BatchOperationDefinition.Create("Op3"),
            BatchOperationDefinition.Create("Op4")
        };

        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var lockObj = new object();

        var executor = new Mock<IOperationExecutor>();
        executor.Setup(e => e.OperationName).Returns("Op");
        executor.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>  // ✅ Correct: Let Moq infer the type
            {
                lock (lockObj)
                {
                    currentConcurrent++;
                    if (currentConcurrent > maxConcurrent)
                    {
                        maxConcurrent = currentConcurrent;
                    }
                }

                await Task.Delay(50);

                lock (lockObj)
                {
                    currentConcurrent--;
                }

                return OperationResult.Success("Op", null);
            });

        _mockResolver.Setup(r => r.Resolve(It.IsAny<string>())).Returns(executor.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1", "Op2", "Op3", "Op4"]);

        var strategy = new ParallelExecutionStrategy(_mockResolver.Object, maxDegreeOfParallelism: 2);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Never exceeded max degree of parallelism
        maxConcurrent.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_WaitsForDependencies_BeforeExecuting()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1"),
            BatchOperationDefinition.Create("Op2"),
            BatchOperationDefinition.Create("Op3", dependsOn: ["Op1", "Op2"])
        };

        // Use ConcurrentQueue to preserve FIFO order
        var executionOrder = new ConcurrentQueue<string>();

        var executor1 = CreateMockExecutor("Op1", () => executionOrder.Enqueue("Op1"));
        var executor2 = CreateMockExecutor("Op2", () => executionOrder.Enqueue("Op2"));
        var executor3 = CreateMockExecutor("Op3", () => executionOrder.Enqueue("Op3"));

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);
        _mockResolver.Setup(r => r.Resolve("Op3")).Returns(executor3.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1", "Op2", "Op3"]);

        var strategy = new ParallelExecutionStrategy(_mockResolver.Object, maxDegreeOfParallelism: 3);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Op3 executed AFTER both Op1 and Op2
        var order = executionOrder.ToList();
        order.Should().Contain("Op1");
        order.Should().Contain("Op2");
        order.Should().Contain("Op3");

        // Op3 should be last (after both dependencies)
        order.IndexOf("Op3").Should().BeGreaterThan(order.IndexOf("Op1"));
        order.IndexOf("Op3").Should().BeGreaterThan(order.IndexOf("Op2"));
    }

    [Fact]
    public async Task ExecuteAsync_OptionalOperation_CanExecute_WhenDependencyFails()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            // Op1 is optional and fails - no throw
            BatchOperationDefinition.Create("Op1", isRequired: false),
        
            // Op2 is optional and depends on Op1
            // Optional operations CAN execute even if dependency fails
            // (they can check dependency status in their logic)
            BatchOperationDefinition.Create("Op2", dependsOn: ["Op1"], isRequired: false)
        };

        var executor1 = CreateMockExecutor("Op1", shouldFail: true);
        var executor2 = CreateMockExecutor("Op2");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1", "Op2"]);

        var strategy = new ParallelExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Should NOT throw because both are optional
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Op1 failed but didn't throw (optional)
        graphContext.HasResult("Op1").Should().BeTrue();
        graphContext.IsSuccess("Op1").Should().BeFalse();  // Op1 failed

        // Op2 may have executed (optional ops can handle failed deps)
        // The key: no exception was thrown
        // We just verify execution completed without throwing
        graphContext.HasResult("Op2").Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Throws_WhenRequiredOperationFails()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1"),  // Required, fails
            BatchOperationDefinition.Create("Op2")   // Required, won't be reached
        };

        var executor1 = CreateMockExecutor("Op1", shouldFail: true);
        var executor2 = CreateMockExecutor("Op2");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1", "Op2"]);

        var strategy = new ParallelExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act & Assert
        var act = () => strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);
        await act.Should().ThrowAsync<BatchException>();

        graphContext.HasResult("Op1").Should().BeTrue();
        graphContext.IsSuccess("Op1").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RetriesOperation_OnFailure()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1", maxRetries: 2)
        };

        var attemptCount = 0;
        var executor = new Mock<IOperationExecutor>();
        executor.Setup(e => e.OperationName).Returns("Op1");
        executor.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                attemptCount++;
                if (attemptCount < 3)
                {
                    throw new InvalidOperationException("Transient error");
                }
                return OperationResult.Success("Op1", "success");
            });

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1"]);

        var strategy = new ParallelExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Should retry until success (3 attempts total)
        attemptCount.Should().Be(3);
        graphContext.IsSuccess("Op1").Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_InvokesOnErrorCallback_WhenOperationFails()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1", isRequired: false)  // Optional, so no throw
        };

        var executor = CreateMockExecutor("Op1", shouldFail: true);
        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1"]);

        Exception? capturedException = null;
        string? capturedName = null;

        var strategy = new ParallelExecutionStrategy(
            _mockResolver.Object,
            onError: (name, ex) => { capturedName = name; capturedException = ex; });

        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Won't throw because Op1 is optional
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert
        capturedName.Should().Be("Op1");
        capturedException.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_InvokesOnCompleteCallback_WhenOperationSucceeds()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1")
        };

        var executor = CreateMockExecutor("Op1");
        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor.Object);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1"]);

        string? capturedName = null;
        TimeSpan capturedDuration = TimeSpan.Zero;

        var strategy = new ParallelExecutionStrategy(
            _mockResolver.Object,
            onComplete: (name, duration) => { capturedName = name; capturedDuration = duration; });

        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert
        capturedName.Should().Be("Op1");
        capturedDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_StopsExecution_WhenTokenCancelled()
    {
        // Arrange - Simple setup with optional operations
        var operations = new List<BatchOperationDefinition>
    {
        BatchOperationDefinition.Create("Op1", isRequired: false),
        BatchOperationDefinition.Create("Op2", isRequired: false),
        BatchOperationDefinition.Create("Op3", isRequired: false)
    };

        using var cts = new CancellationTokenSource();
        var executionOrder = new ConcurrentBag<string>();

        // Op1 executes and cancels
        var executor1 = new Mock<IOperationExecutor>();
        executor1.Setup(e => e.OperationName).Returns("Op1");
        executor1.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                executionOrder.Add("Op1");
                cts.Cancel();
                return OperationResult.Success("Op1", null);
            });

        // Op2 and Op3 check cancellation
        IOperationExecutor createCancellableExecutor(string name)
        {
            var executor = new Mock<IOperationExecutor>();
            executor.Setup(e => e.OperationName).Returns(name);
            executor.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    if (cts.Token.IsCancellationRequested)
                    {
                        return OperationResult.Failure(name, new OperationCanceledException(cts.Token));
                    }
                    executionOrder.Add(name);
                    return OperationResult.Success(name, null);
                });
            return executor.Object;
        }

        var executor2 = createCancellableExecutor("Op2");
        var executor3 = createCancellableExecutor("Op3");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2);
        _mockResolver.Setup(r => r.Resolve("Op3")).Returns(executor3);
        _mockResolver.Setup(r => r.TryResolve(It.IsAny<string>(), out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames()).Returns(["Op1", "Op2", "Op3"]);

        var strategy = new ParallelExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Will throw when cancelled (expected behavior)
        var act = () => strategy.ExecuteAsync(operations, graphContext, batchContext, cts.Token);

        // Assert - Expect OperationCanceledException (cancellation is expected)
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Op1 should have executed before cancellation
        graphContext.HasResult("Op1").Should().BeTrue();
        executionOrder.Should().Contain("Op1");
    }

    private static Mock<IOperationExecutor> CreateMockExecutor(
        string name,
        Action? onExecute = null,
        bool shouldFail = false)
    {
        var executor = new Mock<IOperationExecutor>();
        executor.Setup(e => e.OperationName).Returns(name);
        executor.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (onExecute != null)
                {
                    await Task.Run(onExecute);
                }

                if (shouldFail)
                {
                    throw new InvalidOperationException("Test failure");
                }

                return OperationResult.Success(name, "result");
            });
        return executor;
    }
}