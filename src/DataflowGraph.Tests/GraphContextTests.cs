using FluentAssertions;

namespace DataflowGraph.Tests;

public class GraphContextTests
{
    [Fact]
    public void SetResult_GetResult_RoundTrip_Works()
    {
        // Arrange
        var context = new GraphContext();
        var result = OperationResult.Success("TestOp", "value");

        // Act
        context.SetResult(result);
        var retrieved = context.GetResult("TestOp");

        // Assert
        retrieved.Should().Be(result);
        retrieved.OperationName.Should().Be("TestOp");
        retrieved.Value.Should().Be("value");
    }

    [Fact]
    public void GetResult_Throws_WhenOperationNotFound()
    {
        // Arrange
        var context = new GraphContext();

        // Act & Assert
        var act = () => context.GetResult("NonExistentOp");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No result found*");
    }

    [Fact]
    public void GetValue_ReturnsTypedValue_WhenSuccess()
    {
        // Arrange
        var context = new GraphContext();
        var expectedValue = new List<string> { "a", "b" };
        context.SetResult(OperationResult.Success("FetchItems", expectedValue));

        // Act
        var actualValue = context.GetValue<List<string>>("FetchItems");

        // Assert
        actualValue.Should().BeEquivalentTo(expectedValue);
    }

    [Fact]
    public void GetValue_Throws_WhenOperationFailed()
    {
        // Arrange
        var context = new GraphContext();
        context.SetResult(OperationResult.Failure("FetchItems", new Exception("Failed")));

        // Act & Assert
        var act = () => context.GetValue<List<string>>("FetchItems");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryGetValue_ReturnsTrue_WhenSuccessAndTypeMatches()
    {
        // Arrange
        var context = new GraphContext();
        context.SetResult(OperationResult.Success("GetNumber", 42));

        // Act
        var success = context.TryGetValue<int>("GetNumber", out var value);

        // Assert
        success.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryGetValue_ReturnsFalse_WhenOperationNotFound()
    {
        // Arrange
        var context = new GraphContext();

        // Act
        var success = context.TryGetValue<int>("NonExistent", out var value);

        // Assert
        success.Should().BeFalse();
        value.Should().Be(default);
    }

    [Fact]
    public void HasResult_ReturnsTrue_WhenOperationCompleted()
    {
        // Arrange
        var context = new GraphContext();
        context.SetResult(OperationResult.Success("TestOp", null));

        // Act
        var hasResult = context.HasResult("TestOp");

        // Assert
        hasResult.Should().BeTrue();
    }

    [Fact]
    public void HasResult_ReturnsFalse_WhenOperationNotCompleted()
    {
        // Arrange
        var context = new GraphContext();

        // Act
        var hasResult = context.HasResult("TestOp");

        // Assert
        hasResult.Should().BeFalse();
    }

    [Fact]
    public void IsSuccess_ReturnsTrue_WhenOperationSucceeded()
    {
        // Arrange
        var context = new GraphContext();
        context.SetResult(OperationResult.Success("TestOp", null));

        // Act
        var success = context.IsSuccess("TestOp");

        // Assert
        success.Should().BeTrue();
    }

    [Fact]
    public void IsSuccess_ReturnsFalse_WhenOperationFailed()
    {
        // Arrange
        var context = new GraphContext();
        context.SetResult(OperationResult.Failure("TestOp", new Exception()));

        // Act
        var success = context.IsSuccess("TestOp");

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void IsSuccess_ReturnsFalse_WhenOperationNotCompleted()
    {
        // Arrange
        var context = new GraphContext();

        // Act
        var success = context.IsSuccess("TestOp");

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void AllResults_ReturnsReadOnlyDictionary()
    {
        // Arrange
        var context = new GraphContext();
        context.SetResult(OperationResult.Success("Op1", "value1"));
        context.SetResult(OperationResult.Success("Op2", "value2"));

        // Act
        var allResults = context.AllResults;

        // Assert
        allResults.Should().HaveCount(2);
        allResults.Should().ContainKey("Op1");
        allResults.Should().ContainKey("Op2");
        allResults.Should().BeAssignableTo<IReadOnlyDictionary<string, OperationResult>>();
    }

    [Fact]
    public async Task Results_AreThreadSafe_ForConcurrentAccess()  // ← FIX: async Task
    {
        // Arrange
        var context = new GraphContext();
        var exceptions = new List<Exception>();
        var operationCount = 50;

        // Act & Assert - Multiple threads writing/reading
        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(async () =>  // ← FIX: async lambda
            {
                try
                {
                    for (var j = 0; j < operationCount; j++)
                    {
                        var opName = $"Op-{i}-{j}";
                        var result = OperationResult.Success(opName, j);
                        context.SetResult(result);

                        var retrieved = context.GetResult(opName);
                        retrieved.Value.Should().Be(j);

                        // Small delay to increase contention
                        await Task.Delay(1);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

        // FIX: Use await Task.WhenAll instead of Task.WaitAll
        await Task.WhenAll(tasks);

        // Assert - No exceptions during concurrent access
        exceptions.Should().BeEmpty();
        context.AllResults.Count.Should().Be(10 * operationCount);
    }
}