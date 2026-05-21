using DataflowGraph.Abstractions;

namespace DataflowGraph.IntegrationTests.TestApp.TestOperations;

/// <summary>
/// Test operation that simulates fetching users.
/// Returns a list of user names based on input arguments.
/// </summary>
public class TestFetchUsersOperation : BaseOperation<List<string>>
{
    public override string OperationName => "TestFetchUsers";

    protected override async Task<List<string>> ExecuteCoreAsync(
        IBatchContext batchContext,
        IGraphContext graphContext,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        // Simulate async I/O
        await Task.Delay(10, cancellationToken);

        string? filter = null;
        if (arguments != null && arguments.TryGetValue("filter", out var filterValue))
        {
            filter = filterValue?.ToString();
        }

        var users = new List<string> { "Alex", "Bob", "Charlie", "Diana" };

        // Apply filter if specified
        if (!string.IsNullOrEmpty(filter))
        {
            users = [.. users.Where(u => u.Contains(filter, StringComparison.OrdinalIgnoreCase))];
        }

        return users;
    }
}