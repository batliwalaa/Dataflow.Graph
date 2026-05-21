using FluentAssertions;

namespace DataflowGraph.Tests;

public class BatchContextTests
{
    [Fact]
    public void Constructor_GeneratesBatchId_WhenNotProvided()
    {
        // Act
        var context = new BatchContext();

        // Assert
        context.BatchId.Should().NotBeNullOrEmpty();
        context.BatchId.Should().HaveLength(12);
    }

    [Fact]
    public void Constructor_UsesProvidedBatchId()
    {
        // Arrange
        var expectedId = "custom-batch-id";

        // Act
        var context = new BatchContext(batchId: expectedId);

        // Assert
        context.BatchId.Should().Be(expectedId);
    }

    [Fact]
    public void Constructor_SetsStartTime_ToUtcNow()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var context = new BatchContext();
        var after = DateTime.UtcNow;

        // Assert
        context.StartTime.Should().BeOnOrAfter(before);
        context.StartTime.Should().BeOnOrBefore(after);
        context.StartTime.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void SetData_GetData_RoundTrip_Works()
    {
        // Arrange
        var context = new BatchContext();
        var key = "TestKey";
        var value = "TestValue";

        // Act
        context.SetData(key, value);
        var retrieved = context.GetData<string>(key);

        // Assert
        retrieved.Should().Be(value);
    }

    [Fact]
    public void GetData_ReturnsDefault_WhenKeyNotFound()
    {
        // Arrange
        var context = new BatchContext();

        // Act
        var result = context.GetData<string>("NonExistentKey");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetData_ReturnsDefault_WhenTypeMismatch()
    {
        // Arrange
        var context = new BatchContext();
        context.SetData("NumberKey", 42);

        // Act
        var result = context.GetData<string>("NumberKey");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRequiredData_Throws_WhenKeyNotFound()
    {
        // Arrange
        var context = new BatchContext();

        // Act & Assert
        var act = () => context.GetRequiredData<string>("MissingKey");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void GetRequiredData_Throws_WhenTypeMismatch()
    {
        // Arrange
        var context = new BatchContext();
        context.SetData("NumberKey", 42);

        // Act & Assert
        var act = () => context.GetRequiredData<string>("NumberKey");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*type mismatch*");
    }

    [Fact]
    public void GetRequiredData_ReturnsValue_WhenKeyAndTypeMatch()
    {
        // Arrange
        var context = new BatchContext();
        context.SetData("TestKey", "TestValue");

        // Act
        var result = context.GetRequiredData<string>("TestKey");

        // Assert
        result.Should().Be("TestValue");
    }

    [Fact]
    public void UserId_TenantId_AreSettable()
    {
        // Arrange
        var context = new BatchContext
        {
            // Act
            UserId = "user-123",
            TenantId = "tenant-456"
        };

        // Assert
        context.UserId.Should().Be("user-123");
        context.TenantId.Should().Be("tenant-456");
    }

    [Fact]
    public void CancellationToken_Propagates_FromConstructor()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        var context = new BatchContext(cancellationToken: cts.Token);

        // Assert
        context.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void Clone_CopiesProperties_WithNewCancellationToken()
    {
        // Arrange
        var original = new BatchContext(
            batchId: "original-id",
            userId: "user-123",
            tenantId: "tenant-456");
        original.SetData("SharedKey", "SharedValue");
        using var cts = new CancellationTokenSource();

        // Act
        var clone = original.Clone(cts.Token);

        // Assert - Properties copied
        clone.BatchId.Should().Be(original.BatchId);
        clone.UserId.Should().Be(original.UserId);
        clone.TenantId.Should().Be(original.TenantId);
        clone.GetData<string>("SharedKey").Should().Be("SharedValue");

        // Assert - CancellationToken is new
        clone.CancellationToken.Should().Be(cts.Token);
        clone.CancellationToken.Should().NotBe(original.CancellationToken);
    }

    [Fact]
    public async Task Data_IsThreadSafe_ForConcurrentAccess()  // ← FIX: async Task
    {
        // Arrange
        var context = new BatchContext();
        var exceptions = new List<Exception>();
        var iterations = 100;

        // Act & Assert - Multiple threads writing/reading
        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(async () =>  // ← FIX: async lambda
            {
                try
                {
                    for (var j = 0; j < iterations; j++)
                    {
                        var key = $"Key-{i}-{j}";
                        context.SetData(key, j);
                        var value = context.GetData<int>(key);
                        value.Should().Be(j);

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
        context.Data.Count.Should().Be(10 * iterations);
    }
}