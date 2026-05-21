using FluentAssertions;

namespace DataflowGraph.Tests;

public class OperationResultTests
{
    [Fact]
    public void Success_CreatesResultWithSuccessStatus()
    {
        // Arrange
        var operationName = "TestOperation";
        var value = "test-value";

        // Act
        var result = OperationResult.Success(operationName, value);

        // Assert
        result.OperationName.Should().Be(operationName);
        result.Value.Should().Be(value);
        result.IsSuccess.Should().BeTrue();
        result.IsFaulted.Should().BeFalse();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void Failure_CreatesResultWithFailureStatus()
    {
        // Arrange
        var operationName = "TestOperation";
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = OperationResult.Failure(operationName, exception);

        // Assert
        result.OperationName.Should().Be(operationName);
        result.Value.Should().BeNull();
        result.IsSuccess.Should().BeFalse();
        result.IsFaulted.Should().BeTrue();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Contain("Test error");
    }

    [Fact]
    public void GetValue_ReturnsTypedValue_WhenSuccess()
    {
        // Arrange
        var expectedValue = new List<string> { "a", "b", "c" };
        var result = OperationResult.Success("FetchItems", expectedValue);

        // Act
        var actualValue = result.GetValue<List<string>>();

        // Assert
        actualValue.Should().BeEquivalentTo(expectedValue);
    }

    [Fact]
    public void GetValue_Throws_WhenFaulted()
    {
        // Arrange
        var result = OperationResult.Failure("FetchItems", new Exception("Failed"));

        // Act & Assert
        var act = () => result.GetValue<List<string>>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot get value from failed operation*");
    }

    [Fact]
    public void GetValue_Throws_WhenTypeMismatch()
    {
        // Arrange
        var result = OperationResult.Success("FetchItems", "string-value");

        // Act & Assert
        var act = () => result.GetValue<List<string>>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*type mismatch*");
    }

    [Fact]
    public void TryGetValue_ReturnsTrue_WhenSuccessAndTypeMatches()
    {
        // Arrange
        var expectedValue = 42;
        var result = OperationResult.Success("GetNumber", expectedValue);

        // Act
        var success = result.TryGetValue<int>(out var actualValue);

        // Assert
        success.Should().BeTrue();
        actualValue.Should().Be(expectedValue);
    }

    [Fact]
    public void TryGetValue_ReturnsFalse_WhenFaulted()
    {
        // Arrange
        var result = OperationResult.Failure("GetNumber", new Exception("Failed"));

        // Act
        var success = result.TryGetValue<int>(out var actualValue);

        // Assert
        success.Should().BeFalse();
        actualValue.Should().Be(default);
    }

    [Fact]
    public void TryGetValue_ReturnsFalse_WhenTypeMismatch()
    {
        // Arrange
        var result = OperationResult.Success("GetNumber", "not-a-number");

        // Act
        var success = result.TryGetValue<int>(out var actualValue);

        // Assert
        success.Should().BeFalse();
        actualValue.Should().Be(default);
    }

    [Fact]
    public void Failure_WrapsNonBatchException_AsBatchException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var result = OperationResult.Failure("TestOp", innerException);

        // Assert
        result.Exception.Should().BeOfType<BatchException>();
        result.Exception!.InnerException.Should().Be(innerException);
        result.Exception!.OperationName.Should().Be("TestOp");
    }

    [Fact]
    public void Failure_KeepsBatchException_AsIs()
    {
        // Arrange
        var batchException = new BatchException("TestOp", "Already batched");

        // Act
        var result = OperationResult.Failure("TestOp", batchException);

        // Assert
        result.Exception.Should().Be(batchException);
    }
}