using DataflowGraph.Abstractions;
using DataflowGraph.Execution;
using DataflowGraph.Resolution;
using FluentAssertions;
using Moq;

namespace DataflowGraph.Tests.Execution;

public class SerialExecutionStrategyTests
{
    private readonly Mock<IOperationResolver> _mockResolver;
    private readonly Mock<IOperationExecutor> _mockExecutor;

    public SerialExecutionStrategyTests()
    {
        _mockResolver = new Mock<IOperationResolver>();
        _mockExecutor = new Mock<IOperationExecutor>();
        _mockExecutor.Setup(e => e.OperationName).Returns("TestOperation");
        _mockResolver.Setup(r => r.Resolve("TestOperation")).Returns(_mockExecutor.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesOperations_InSequentialOrder()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1"),
            BatchOperationDefinition.Create("Op2"),
            BatchOperationDefinition.Create("Op3")
        };

        var executionOrder = new List<string>();

        var executor1 = CreateMockExecutor("Op1", () => executionOrder.Add("Op1"));
        var executor2 = CreateMockExecutor("Op2", () => executionOrder.Add("Op2"));
        var executor3 = CreateMockExecutor("Op3", () => executionOrder.Add("Op3"));

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);
        _mockResolver.Setup(r => r.Resolve("Op3")).Returns(executor3.Object);

        var strategy = new SerialExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert
        executionOrder.Should().Equal("Op1", "Op2", "Op3");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsDependentOperation_WhenDependencyFails()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
        {
            // Op1 is OPTIONAL - fails but doesn't throw
            BatchOperationDefinition.Create("Op1", isRequired: false),
        
            // Op2 is ALSO OPTIONAL - skipped when dependency fails, no throw
            BatchOperationDefinition.Create(
                "Op2",
                dependsOn: ["Op1"],
                isRequired: false)
        };

        var executor1 = CreateMockExecutor("Op1", shouldFail: true);
        var executor2 = CreateMockExecutor("Op2");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);

        var strategy = new SerialExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Should NOT throw because both operations are optional
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Op1 failed but didn't throw (optional)
        graphContext.HasResult("Op1").Should().BeTrue();
        graphContext.IsSuccess("Op1").Should().BeFalse();  // Op1 failed

        // Op2 was skipped (dependency failed) but didn't throw (optional)
        graphContext.HasResult("Op2").Should().BeTrue();
        graphContext.IsSuccess("Op2").Should().BeFalse();  // Op2 was skipped

        // Verify Op1 executor was called (it executed and failed)
        executor1.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify Op2 executor was NEVER called (skipped due to failed dependency)
        executor2.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify Op2's result mentions the dependency failure
        var op2Result = graphContext.GetResult("Op2");
        op2Result.Exception!.Message.Should().Contain("Dependency failed");
        op2Result.Exception!.Message.Should().Contain("Op1");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsOperation_WhenRequiredDependencyFails()
    {
        // Arrange - Both required, Op1 fails
        var operations = new List<BatchOperationDefinition>
        {
            BatchOperationDefinition.Create("Op1"),  // Required, will fail
            BatchOperationDefinition.Create("Op2", dependsOn: ["Op1"])  // Required, depends on Op1
        };

        var executor1 = CreateMockExecutor("Op1", shouldFail: true);
        var executor2 = CreateMockExecutor("Op2");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);

        var strategy = new SerialExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Will throw because Op1 is required and fails
        var act = () => strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Exception thrown, Op2 never processed
        await act.Should().ThrowAsync<BatchException>();

        graphContext.HasResult("Op1").Should().BeTrue();   // Op1 executed and failed
        graphContext.HasResult("Op2").Should().BeFalse();  // Op2 NEVER processed (thrown before)

        executor2.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Throws_WhenRequiredOperationWithFailedDependency()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
    {
        BatchOperationDefinition.Create("Op1"),
        BatchOperationDefinition.Create("Op2", dependsOn: ["Op1"])
    };

        var executor1 = CreateMockExecutor("Op1", shouldFail: true);
        var executor2 = CreateMockExecutor("Op2");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);

        var strategy = new SerialExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act & Assert - Should throw BatchException (wraps the original exception)
        var act = () => strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<BatchException>()
            .WithMessage("*Op1*failed*");  // Message includes operation name

        // Verify the inner exception is the original InvalidOperationException
        exception.And.InnerException.Should().BeOfType<InvalidOperationException>();
        exception.And.InnerException!.Message.Should().Be("Test failure");

        // Op1 should have a result (it executed and failed)
        graphContext.HasResult("Op1").Should().BeTrue();
        graphContext.IsSuccess("Op1").Should().BeFalse();

        // Op2 should NOT have a result (execution stopped before processing Op2)
        graphContext.HasResult("Op2").Should().BeFalse();

        // Op2 executor should never be called
        executor2.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Continues_WhenOptionalDependencyFails()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
    {
        // Op1 is OPTIONAL - can fail without throwing
        BatchOperationDefinition.Create("Op1", isRequired: false),
        
        // Op2 is ALSO OPTIONAL - can be skipped without throwing when dependency fails
        BatchOperationDefinition.Create(
            "Op2",
            dependsOn: ["Op1"],
            isRequired: false)  // ← FIX: Op2 must also be optional
    };

        var executor1 = CreateMockExecutor("Op1", shouldFail: true);
        var executor2 = CreateMockExecutor("Op2");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);

        var strategy = new SerialExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Should NOT throw because both operations are optional
        await strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Op1 failed but didn't throw (optional)
        graphContext.HasResult("Op1").Should().BeTrue();
        graphContext.IsSuccess("Op1").Should().BeFalse();  // Op1 failed

        // Op2 was skipped (dependency failed) but didn't throw (optional)
        graphContext.HasResult("Op2").Should().BeTrue();
        graphContext.IsSuccess("Op2").Should().BeFalse();  // Op2 was skipped

        // Verify Op1 executor was called (it executed and failed)
        executor1.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify Op2 executor was NEVER called (skipped due to failed dependency)
        executor2.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify Op2's result mentions the dependency failure
        var op2Result = graphContext.GetResult("Op2");
        op2Result.Exception!.Message.Should().Contain("Dependency failed");
        op2Result.Exception!.Message.Should().Contain("Op1");
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

        var strategy = new SerialExecutionStrategy(_mockResolver.Object);
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
            BatchOperationDefinition.Create("Op1")  // Required by default, will fail
        };

        var executor = CreateMockExecutor("Op1", shouldFail: true);
        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor.Object);

        Exception? capturedException = null;
        string? capturedName = null;

        var strategy = new SerialExecutionStrategy(
            _mockResolver.Object,
            onError: (name, ex) => { capturedName = name; capturedException = ex; });

        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Will throw because Op1 is required, but callback should still be invoked
        var act = () => strategy.ExecuteAsync(operations, graphContext, batchContext, CancellationToken.None);

        // Assert - Exception is thrown, but callback was invoked before throw
        await act.Should().ThrowAsync<BatchException>();

        // Verify callback was invoked (happens before exception propagates)
        capturedName.Should().Be("Op1");
        capturedException.Should().NotBeNull();
        capturedException.Should().BeOfType<BatchException>();
        capturedException!.Message.Should().Contain("Op1");
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

        string? capturedName = null;
        TimeSpan capturedDuration = TimeSpan.Zero;

        var strategy = new SerialExecutionStrategy(
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
    public async Task ExecuteAsync_RespectsCancellationToken()
    {
        // Arrange
        var operations = new List<BatchOperationDefinition>
    {
        BatchOperationDefinition.Create("Op1"),
        BatchOperationDefinition.Create("Op2"),
        BatchOperationDefinition.Create("Op3")
    };

        using var cts = new CancellationTokenSource();

        var executionOrder = new List<string>();

        // Op1 executes and cancels
        var executor1 = CreateMockExecutor("Op1", () =>
        {
            executionOrder.Add("Op1");
            cts.Cancel();  // Cancel after Op1 completes
        });

        // Op2 should check cancellation and throw
        var executor2 = new Mock<IOperationExecutor>();
        executor2.Setup(e => e.OperationName).Returns("Op2");
        executor2.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cts.Token.ThrowIfCancellationRequested();  // Will throw
                return OperationResult.Success("Op2", null);
            });

        // Op3 should never be reached
        var executor3 = new Mock<IOperationExecutor>();
        executor3.Setup(e => e.OperationName).Returns("Op3");

        _mockResolver.Setup(r => r.Resolve("Op1")).Returns(executor1.Object);
        _mockResolver.Setup(r => r.Resolve("Op2")).Returns(executor2.Object);
        _mockResolver.Setup(r => r.Resolve("Op3")).Returns(executor3.Object);

        var strategy = new SerialExecutionStrategy(_mockResolver.Object);
        var graphContext = new GraphContext();
        var batchContext = new BatchContext();

        // Act - Should throw OperationCanceledException
        var act = () => strategy.ExecuteAsync(operations, graphContext, batchContext, cts.Token);

        // Assert - Exception is thrown (expected behavior)
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Verify Op1 executed before cancellation
        graphContext.HasResult("Op1").Should().BeTrue();
        executionOrder.Should().Contain("Op1");

        // Verify Op2 and Op3 were NOT executed (cancelled)
        graphContext.HasResult("Op2").Should().BeFalse();
        graphContext.HasResult("Op3").Should().BeFalse();

        // Verify executors 2 and 3 were never called
        executor2.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
        executor3.Verify(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IOperationExecutor> CreateMockExecutor(
        string name,
        Action? onExecute = null,
        bool shouldFail = false)
    {
        var executor = new Mock<IOperationExecutor>();
        executor.Setup(e => e.OperationName).Returns(name);
        executor.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                onExecute?.Invoke();
                if (shouldFail)
                {
                    throw new InvalidOperationException("Test failure");
                }
                return OperationResult.Success(name, "result");
            });
        return executor;
    }
}