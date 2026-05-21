using DataflowGraph.Abstractions;
using FluentAssertions;

namespace DataflowGraph.Tests;

public class BaseOperationTests
{
    [Fact]
    public async Task ExecuteAsync_WrapsSuccessResult_InOperationResult()
    {
        // Arrange
        var operation = new TestOperationWithResult();
        var batchContext = new BatchContext();
        var graphContext = new GraphContext();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await operation.ExecuteAsync(batchContext, graphContext, null, cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFaulted.Should().BeFalse();
        result.Value.Should().Be("test-result");
        result.OperationName.Should().Be("TestOperation");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WrapsException_InFailureResult()
    {
        // Arrange
        var operation = new TestOperationThatThrows();
        var batchContext = new BatchContext();
        var graphContext = new GraphContext();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await operation.ExecuteAsync(batchContext, graphContext, null, cancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFaulted.Should().BeTrue();
        result.Value.Should().BeNull();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Be("Operation 'TestOperation' failed: Test exception");
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesOperationCanceledException()
    {
        // Arrange
        var operation = new TestOperationThatCancels();
        var batchContext = new BatchContext();
        var graphContext = new GraphContext();
        using var cts = new CancellationTokenSource();

        // FIX: Actually cancel the token before calling ExecuteAsync
        cts.Cancel();

        // Act & Assert
        var act = () => operation.ExecuteAsync(batchContext, graphContext, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_PassesArguments_ToExecuteCore()
    {
        // Arrange
        var operation = new TestOperationWithArguments();
        var batchContext = new BatchContext();
        var graphContext = new GraphContext();
        var arguments = new Dictionary<string, object?>
        {
            ["Key1"] = "Value1",
            ["Key2"] = 42
        };

        // Act
        await operation.ExecuteAsync(batchContext, graphContext, arguments, CancellationToken.None);

        // Assert
        operation.ReceivedArguments.Should().BeEquivalentTo(arguments);
    }

    [Fact]
    public async Task ExecuteAsync_PassesBatchContext_ToExecuteCore()
    {
        // Arrange
        var operation = new TestOperationWithContext();
        var batchContext = new BatchContext(batchId: "test-batch", userId: "user-123");
        var graphContext = new GraphContext();

        // Act
        await operation.ExecuteAsync(batchContext, graphContext, null, CancellationToken.None);

        // Assert
        operation.ReceivedBatchId.Should().Be("test-batch");
        operation.ReceivedUserId.Should().Be("user-123");
    }

    [Fact]
    public async Task ExecuteAsync_PassesGraphContext_ToExecuteCore()
    {
        // Arrange
        var operation = new TestOperationWithGraphContext();
        var batchContext = new BatchContext();
        var graphContext = new GraphContext();
        graphContext.SetResult(OperationResult.Success("DependencyOp", "dep-value"));

        // Act
        await operation.ExecuteAsync(batchContext, graphContext, null, CancellationToken.None);

        // Assert
        operation.ReceivedDependencyValue.Should().Be("dep-value");
    }

    [Fact]
    public async Task ExecuteAsync_VoidOperation_ReturnsSuccessWithNullValue()
    {
        // Arrange
        var operation = new TestVoidOperation();
        var batchContext = new BatchContext();
        var graphContext = new GraphContext();

        // Act
        var result = await operation.ExecuteAsync(batchContext, graphContext, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_VoidOperationThatThrows_ReturnsFailure()
    {
        // Arrange
        var operation = new TestVoidOperationThatThrows();
        var batchContext = new BatchContext();
        var graphContext = new GraphContext();

        // Act
        var result = await operation.ExecuteAsync(batchContext, graphContext, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFaulted.Should().BeTrue();
        result.Exception.Should().NotBeNull();
    }

    // Test Operation Implementations

    private class TestOperationWithResult : BaseOperation<string>
    {
        public override string OperationName => "TestOperation";

        protected override Task<string> ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult("test-result");
        }
    }

    private class TestOperationThatThrows : BaseOperation<string>
    {
        public override string OperationName => "TestOperation";

        protected override Task<string> ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Test exception");
        }
    }

    private class TestOperationThatCancels : BaseOperation<string>
    {
        public override string OperationName => "TestOperation";

        protected override Task<string> ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("should-not-reach");
        }
    }

    private class TestOperationWithArguments : BaseOperation<string>
    {
        public override string OperationName => "TestOperation";
        public IDictionary<string, object?>? ReceivedArguments { get; private set; }

        protected override Task<string> ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            ReceivedArguments = arguments;
            return Task.FromResult("ok");
        }
    }

    private class TestOperationWithContext : BaseOperation<string>
    {
        public override string OperationName => "TestOperation";
        public string? ReceivedBatchId { get; private set; }
        public string? ReceivedUserId { get; private set; }

        protected override Task<string> ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            ReceivedBatchId = batchContext.BatchId;
            ReceivedUserId = batchContext.UserId;
            return Task.FromResult("ok");
        }
    }

    private class TestOperationWithGraphContext : BaseOperation<string>
    {
        public override string OperationName => "TestOperation";
        public string? ReceivedDependencyValue { get; private set; }

        protected override Task<string> ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            ReceivedDependencyValue = graphContext.GetValue<string>("DependencyOp");
            return Task.FromResult("ok");
        }
    }

    private class TestVoidOperation : BaseOperation
    {
        public override string OperationName => "TestVoidOperation";

        protected override Task ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private class TestVoidOperationThatThrows : BaseOperation
    {
        public override string OperationName => "TestVoidOperation";

        protected override Task ExecuteCoreAsync(
            IBatchContext batchContext,
            IGraphContext graphContext,
            IDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Test exception");
        }
    }
}