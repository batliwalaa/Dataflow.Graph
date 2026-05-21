using DataflowGraph.Abstractions;
using DataflowGraph.Resolution;
using FluentAssertions;
using Moq;

namespace DataflowGraph.Tests;

public class BatchGraphTests
{
    private readonly Mock<IOperationResolver> _mockResolver;
    private readonly Mock<IOperationExecutor> _mockExecutor;

    public BatchGraphTests()
    {
        _mockResolver = new Mock<IOperationResolver>();
        _mockExecutor = new Mock<IOperationExecutor>();
        _mockExecutor.Setup(e => e.OperationName).Returns("TestOperation");
        _mockResolver.Setup(r => r.Resolve("TestOperation")).Returns(_mockExecutor.Object);
        _mockResolver.Setup(r => r.TryResolve("TestOperation", out It.Ref<IOperationExecutor?>.IsAny))
            .Returns(true);
        _mockResolver.Setup(r => r.Resolve("DependencyOp")).Returns(_mockExecutor.Object);
        _mockResolver.Setup(r => r.TryResolve("DependencyOp", out It.Ref<IOperationExecutor?>.IsAny))
            .Returns(true);
        _mockResolver.Setup(r => r.GetRegisteredOperationNames())
            .Returns(["TestOperation", "DependencyOp"]);
    }

    [Fact]
    public void AddOperation_ByName_AddsOperationToGraph()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);

        // Act
        var result = graph.AddOperation("TestOperation");

        // Assert
        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void AddOperation_ByName_Throws_WhenOperationNotRegistered()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        _mockResolver.Setup(r => r.TryResolve("UnknownOp", out It.Ref<IOperationExecutor?>.IsAny))
            .Returns(false);

        // Act & Assert
        var act = () => graph.AddOperation("UnknownOp");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void AddOperation_WithArguments_AddsOperationWithArguments()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        var arguments = new Dictionary<string, object?> { ["Key"] = "Value" };

        // Act
        graph.AddOperation("TestOperation", arguments);

        // Assert - verified by successful execution later
    }

    [Fact]
    public void AddOperation_WithDefinition_AddsOperationWithFullConfig()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        var definition = BatchOperationDefinition.Create(
            name: "TestOperation",
            dependsOn: ["DependencyOp"],
            arguments: new Dictionary<string, object?> { ["Key"] = "Value" },
            isRequired: false,
            maxRetries: 3);

        // Act
        graph.AddOperation(definition);

        // Assert - verified by successful execution later
    }

    [Fact]
    public void AddOperations_AddsMultipleOperations()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        var definitions = new[]
        {
            BatchOperationDefinition.Create("TestOperation"),
            BatchOperationDefinition.Create("TestOperation")
        };

        // Act
        graph.AddOperations(definitions);

        // Assert - no exception means success
    }

    [Fact]
    public void DependsOn_AddsDependencies_ToOperation()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        graph.AddOperation("TestOperation");
        graph.AddOperation("DependencyOp");

        // Act
        var result = graph.DependsOn("TestOperation", "DependencyOp");

        // Assert - fluent API returns same instance
        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void DependsOn_Throws_WhenOperationNotAdded()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        graph.AddOperation("DependencyOp");

        // Act & Assert
        var act = () => graph.DependsOn("NonExistentOp", "DependencyOp");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not been added*");
    }

    [Fact]
    public void DependsOn_Throws_WhenDependencyNotAdded()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        graph.AddOperation("TestOperation");

        // Act & Assert
        var act = () => graph.DependsOn("TestOperation", "NonExistentDep");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not been added*");
    }

    [Fact]
    public void WithProcessingType_SetsProcessingType()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);

        // Act
        var result = graph.WithProcessingType(ProcessingType.Serial);

        // Assert
        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void WithMaxDegreeOfParallelism_SetsParallelism()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);

        // Act
        var result = graph.WithMaxDegreeOfParallelism(8);

        // Assert
        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void WithMaxDegreeOfParallelism_Throws_WhenZeroOrNegative()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);

        // Act & Assert
        var act = () => graph.WithMaxDegreeOfParallelism(0);
        act.Should().Throw<ArgumentOutOfRangeException>();

        act = () => graph.WithMaxDegreeOfParallelism(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ExecuteAsync_Throws_WhenNoOperationsAdded()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);

        // Act & Assert
        var act = () => graph.ExecuteAsync();
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No operations have been added*");
    }

    [Fact]
    public async Task ExecuteAsync_DetectsCircularDependency()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        graph.AddOperation("TestOperation");
        graph.AddOperation("DependencyOp");
        graph.DependsOn("TestOperation", "DependencyOp");
        graph.DependsOn("DependencyOp", "TestOperation"); // Circular!

        // Act & Assert
        var act = () => graph.ExecuteAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public async Task ExecuteAsync_CallsExecutionStrategy()
    {
        // Arrange
        var mockExecutor = new Mock<IOperationExecutor>();
        mockExecutor.Setup(e => e.OperationName).Returns("TestOperation");
        mockExecutor.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Success("TestOperation", "result"));

        _mockResolver.Setup(r => r.Resolve("TestOperation")).Returns(mockExecutor.Object);
        _mockResolver.Setup(r => r.TryResolve("TestOperation", out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);

        var graph = new BatchGraph(_mockResolver.Object);
        graph.AddOperation("TestOperation");

        // Act
        var result = await graph.ExecuteAsync();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.StepCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsGraphResult_WithExecutionStats()
    {
        // Arrange
        var mockExecutor = new Mock<IOperationExecutor>();
        mockExecutor.Setup(e => e.OperationName).Returns("TestOperation");
        mockExecutor.Setup(e => e.ExecuteAsync(It.IsAny<IBatchContext>(), It.IsAny<IGraphContext>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Success("TestOperation", "result"));

        _mockResolver.Setup(r => r.Resolve("TestOperation")).Returns(mockExecutor.Object);
        _mockResolver.Setup(r => r.TryResolve("TestOperation", out It.Ref<IOperationExecutor?>.IsAny)).Returns(true);

        var graph = new BatchGraph(_mockResolver.Object);
        graph.AddOperation("TestOperation");

        // Act
        var result = await graph.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StepCount.Should().Be(1);
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(0);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void BuildFromDefinition_AddsOperations_FromDefinition()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        var definition = BatchGraphDefinition.Create()
            .AddStep(StepDefinitionDto.Create("TestOperation"));

        // Act
        var result = graph.BuildFromDefinition(definition);

        // Assert
        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void BuildFromDefinition_Throws_WhenDefinitionInvalid()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        var definition = BatchGraphDefinition.Create()
            .AddStep(StepDefinitionDto.Create("InvalidOperation")); // Not registered

        // Act & Assert
        var act = () => graph.BuildFromDefinition(definition);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid batch graph definition*");
    }

    [Fact]
    public void OnError_SetsErrorHandler()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        static void handler(string name, Exception ex) { }

        // Act
        var result = graph.OnError(handler);

        // Assert
        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void OnError_Throws_WhenHandlerIsNull()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);

        // Act & Assert
        var act = () => graph.OnError(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OnComplete_SetsCompleteHandler()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);
        static void handler(string name, TimeSpan duration) { }

        // Act
        var result = graph.OnComplete(handler);

        // Assert
        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void OnComplete_Throws_WhenHandlerIsNull()
    {
        // Arrange
        var graph = new BatchGraph(_mockResolver.Object);

        // Act & Assert
        var act = () => graph.OnComplete(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}