using DataflowGraph.Abstractions;

namespace DataflowGraph.IntegrationTests.TestApp.TestOperations;

/// <summary>
/// Test operation that validates input data.
/// Fails if "invalid" argument is true.
/// </summary>
public class TestValidateOperation : BaseOperation
{
    public override string OperationName => "TestValidate";

    protected override async Task ExecuteCoreAsync(
        IBatchContext batchContext,
        IGraphContext graphContext,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        // Simulate async validation
        await Task.Delay(5, cancellationToken);

        bool shouldFail = false;
        if (arguments != null && arguments.TryGetValue("invalid", out var invalidValue))
        {
            shouldFail = invalidValue is true;
        }

        if (shouldFail)
        {
            throw new InvalidOperationException("Validation failed: invalid input");
        }
    }
}