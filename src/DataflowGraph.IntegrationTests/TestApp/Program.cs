using DataflowGraph.AspNetCore;
using DataflowGraph.IntegrationTests.TestApp.TestOperations;

var builder = WebApplication.CreateBuilder(args);

// Register DataflowGraph with test operations
builder.Services.AddDataflowGraph()
    .AddOperation<TestFetchUsersOperation>()
    .AddOperation<TestValidateOperation>();

builder.Services.AddRouting();

var app = builder.Build();

// Map the batch endpoint for testing
app.MapBatchEndpoint("/batch");

app.Run();

// Required for WebApplicationFactory - must be public partial class
public partial class Program { }