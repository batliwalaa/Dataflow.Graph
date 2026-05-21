# DataflowGraph

[![NuGet](https://img.shields.io/nuget/v/DataflowGraph.svg)](https://www.nuget.org/packages/DataflowGraph)
[![Downloads](https://img.shields.io/nuget/dt/DataflowGraph)](https://www.nuget.org/packages/DataflowGraph)
[![License](https://img.shields.io/github/license/yourusername/DataflowGraph)](LICENSE)
[![Build Status](https://img.shields.io/github/actions/workflow/status/yourusername/DataflowGraph/dotnet.yml?branch=main)](https://github.com/yourusername/DataflowGraph/actions)

> **Declarative dependency-graph orchestration built on System.Threading.Tasks.Dataflow**

DataflowGraph provides a fluent, code-first API for orchestrating complex batch operations with explicit dependencies. Built on top of Microsoft's battle-tested TPL Dataflow library, it adds intelligent dependency resolution while maintaining performance and reliability.

**Perfect for**: Complex background jobs • Serverless functions • Microservices • CLI tools • Any scenario requiring lightweight, in-process orchestration.

---

## 🚀 Quick Start

```bash
dotnet add package DataflowGraph
```

```csharp
using DataflowGraph;

var result = await BatchGraph.Create()
    .AddStep("LoadData", async (context, ct) =>
        await DataLoader.LoadAsync(ct))
    .AddStep("Validate", async (context, ct) =>
        await Validator.ValidateAsync(context.GetResult<string>("LoadData"), ct))
    .AddStep("Transform", async (context, ct) =>
        await Transformer.ProcessAsync(context.GetResult<Data>("Validate"), ct))

    // Define dependencies explicitly
    .DependsOn("Validate", "LoadData")
    .DependsOn("Transform", "Validate")

    .ExecuteAsync(cancellationToken);

Console.WriteLine($"Transform result: {result.GetResult<ProcessedData>("Transform")}");
```

---

## 🔥 Integration with Background Job Schedulers

DataflowGraph is **scheduler-agnostic**. Use your favorite job scheduler for _when_ to run, and DataflowGraph for _how_ to orchestrate complex steps.

### With Hangfire

```csharp
public class ReportJobs
{
    private readonly IBatchGraphFactory _graphFactory;
    private readonly ILogger<ReportJobs> _logger;

    public ReportJobs(IBatchGraphFactory graphFactory, ILogger<ReportJobs> logger)
    {
        _graphFactory = graphFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task GenerateDailyReportAsync(string date, CancellationToken ct)
    {
        var graph = _graphFactory.Create()
            .AddStep("FetchSales", async (_, ct) => await _repo.GetSalesAsync(date, ct))
            .AddStep("FetchUsers", async (_, ct) => await _repo.GetUsersAsync(ct))
            .AddStep("Aggregate", async (ctx, ct) =>
            {
                var sales = ctx.GetResult<List<Sale>>("FetchSales");
                var users = ctx.GetResult<List<User>>("FetchUsers");
                return await _service.AggregateAsync(sales, users, ct);
            })
            .DependsOn("Aggregate", "FetchSales", "FetchUsers")
            .AddStep("Upload", async (ctx, ct) =>
                await _storage.UploadAsync(ctx.GetResult<Report>("Aggregate"), ct))
            .DependsOn("Upload", "Aggregate")
            .OnError((step, ex) => _logger.LogError(ex, "Step {Step} failed", step));

        await graph.ExecuteAsync(ct);
    }
}

// Trigger with Hangfire
BackgroundJob.Enqueue<ReportJobs>(x => x.GenerateDailyReportAsync("2026-01-15", CancellationToken.None));
```

### With Quartz.NET

```csharp
public class ReportGenerationJob : IJob
{
    private readonly IBatchGraphFactory _graphFactory;
    private readonly ILogger<ReportGenerationJob> _logger;
    private readonly IDataService _dataService;

    public ReportGenerationJob(
        IBatchGraphFactory graphFactory,
        ILogger<ReportGenerationJob> logger,
        IDataService dataService)
    {
        _graphFactory = graphFactory;
        _logger = logger;
        _dataService = dataService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var reportDate = context.JobDetail.JobDataMap.GetString("ReportDate");

        var graph = _graphFactory.Create()
            .AddStep("FetchOrders", async (_, ct) =>
                await _dataService.GetOrdersAsync(reportDate, ct))
            .AddStep("FetchCustomers", async (_, ct) =>
                await _dataService.GetCustomersAsync(ct))
            .AddStep("Aggregate", async (ctx, ct) =>
            {
                var orders = ctx.GetResult<List<Order>>("FetchOrders");
                var customers = ctx.GetResult<List<Customer>>("FetchCustomers");
                return await _dataService.AggregateReportAsync(orders, customers, ct);
            })
            .DependsOn("Aggregate", "FetchOrders", "FetchCustomers")
            .AddStep("Export", async (ctx, ct) =>
                await _dataService.ExportToStorageAsync(
                    ctx.GetResult<Report>("Aggregate"), ct))
            .DependsOn("Export", "Aggregate")
            .OnError((step, ex) =>
                _logger.LogError(ex, "Step '{Step}' failed", step));

        await graph.ExecuteAsync(cancellationToken);
    }
}

// Register with Quartz (Program.cs)
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    var jobKey = new JobKey("DailyReportJob");
    q.AddJob<ReportGenerationJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithCronSchedule("0 0 2 * * ?")); // Daily at 2 AM
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
builder.Services.AddDataflowGraph();
```

### Why Combine Them?

| Benefit                       | Explanation                                           |
| ----------------------------- | ----------------------------------------------------- |
| **Separation of Concerns**    | Scheduler handles _when_; DataflowGraph handles _how_ |
| **Reduced Job Proliferation** | One scheduled job can orchestrate 10+ dependent steps |
| **Better Error Handling**     | Step-level errors vs. job-level retries               |
| **In-Process Efficiency**     | No database round-trips between dependent steps       |
| **Context Sharing**           | Pass results between steps without serialization      |

### Which Scheduler Should You Use?

- **Choose Hangfire** if you want: fire-and-forget jobs, beautiful dashboard, minimal setup
- **Choose Quartz.NET** if you need: complex cron expressions, enterprise clustering, fine-grained control
- **Both work perfectly** with DataflowGraph — the orchestration logic remains identical

---

## ✨ Key Features

- **Declarative Dependencies**: Express task relationships clearly with `.DependsOn()`
- **Scheduler-Agnostic**: Works with Hangfire, Quartz.NET, Azure Functions, or custom schedulers
- **Built on TPL Dataflow**: Leverages Microsoft's proven concurrency primitives
- **Lightweight**: No database required for orchestration logic; minimal dependencies
- **Modern .NET**: Full support for `CancellationToken`, `IAsyncEnumerable<T>`, and `async/await`
- **Error Isolation**: Failed steps don't block independent parallel operations
- **Dependency Injection Ready**: Integrates seamlessly with `Microsoft.Extensions.DependencyInjection`
- **Multi-target**: Supports .NET 6.0, .NET 8.0, and .NET Standard 2.1

---

## 📊 When to Use DataflowGraph

| Scenario                                 | Recommendation                                   |
| ---------------------------------------- | ------------------------------------------------ |
| Simple parallel operations               | Use `Task.WhenAll()`                             |
| Long-running jobs needing persistence    | **Use Scheduler + DataflowGraph**                |
| Low-level data pipelines                 | Use **System.Threading.Tasks.Dataflow** directly |
| **Dependency-aware batch orchestration** | **✅ Use DataflowGraph**                         |

---

## 🎯 Advanced Example: Parallel Execution with Dependencies

```csharp
var graph = BatchGraph.Create()
    // Independent steps run in parallel
    .AddStep("FetchUsers", FetchUsersAsync)
    .AddStep("FetchProducts", FetchProductsAsync)
    .AddStep("FetchInventory", FetchInventoryAsync)

    // Dependent step waits for all three
    .AddStep("GenerateReport", async (ctx, ct) =>
    {
        var users = ctx.GetResult<List<User>>("FetchUsers");
        var products = ctx.GetResult<List<Product>>("FetchProducts");
        var inventory = ctx.GetResult<Inventory>("FetchInventory");

        return await ReportGenerator.CreateAsync(users, products, inventory, ct);
    })
    .DependsOn("GenerateReport", "FetchUsers", "FetchProducts", "FetchInventory")

    // Optional: conditional execution
    .AddStep("SendNotification", async (ctx, ct) =>
        await Notifier.SendAsync(ctx.GetResult<Report>("GenerateReport"), ct))
    .DependsOn("SendNotification", "GenerateReport")
    .When(() => config.EnableNotifications)

    // Error handling
    .OnError((stepName, exception) =>
        _logger.LogError(exception, "Step {Step} failed", stepName))
    .OnComplete((stepName, duration) =>
        _metrics.RecordStepDuration(stepName, duration));

var results = await graph.ExecuteAsync(cancellationToken);
var report = results.GetResult<Report>("GenerateReport");
```

---

## 🔧 Integration with Dependency Injection

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register DataflowGraph services
builder.Services.AddDataflowGraph(options =>
{
    options.DefaultMaxDegreeOfParallelism = 4;
    options.EnableStepMetrics = true;
});

// Register your application services
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IDataLoader, DataLoader>();

var app = builder.Build();
app.Run();
```

```csharp
// Your service
public class ReportService
{
    private readonly IBatchGraphFactory _graphFactory;
    private readonly IDataLoader _dataLoader;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IBatchGraphFactory graphFactory,
        IDataLoader dataLoader,
        ILogger<ReportService> logger)
    {
        _graphFactory = graphFactory;
        _dataLoader = dataLoader;
        _logger = logger;
    }

    public async Task<Report> GenerateReportAsync(string reportId, CancellationToken ct)
    {
        var graph = _graphFactory.Create()
            .AddStep("LoadData", async (_, ct) =>
                await _dataLoader.LoadAsync(reportId, ct))
            .AddStep("Process", async (ctx, ct) =>
                await ProcessAsync(ctx.GetResult<RawData>("LoadData"), ct))
            .AddStep("Validate", async (ctx, ct) =>
                await ValidateAsync(ctx.GetResult<ProcessedData>("Process"), ct))
            .DependsOn("Process", "LoadData")
            .DependsOn("Validate", "Process");

        var result = await graph.ExecuteAsync(ct);
        return result.GetResult<Report>("Validate");
    }
}
```

---

## 🆚 Comparison with Alternatives

| Feature                   | DataflowGraph            | Hangfire                | Quartz.NET            | Raw TPL Dataflow      |
| ------------------------- | ------------------------ | ----------------------- | --------------------- | --------------------- |
| **Database Required**     | ❌ No                    | ✅ Yes                  | ✅ Yes                | ❌ No                 |
| **Dependency Resolution** | ✅ Built-in              | ⚠️ Manual job chains    | ⚠️ Manual job chains  | ❌ None               |
| **Learning Curve**        | Low                      | Low-Medium              | Medium-High           | High                  |
| **Overhead**              | Minimal                  | Medium-High             | Medium                | Low                   |
| **Best For**              | In-process orchestration | Background jobs with UI | Enterprise scheduling | Custom data pipelines |
| **Works With**            | Any scheduler            | Standalone              | Standalone            | Standalone            |

> 💡 **Pro Tip**: Use DataflowGraph _inside_ Hangfire or Quartz.NET jobs to get the best of both worlds.

---

## 📦 Installation

### Via .NET CLI

```bash
dotnet add package DataflowGraph
```

### Via Package Manager Console

```powershell
Install-Package DataflowGraph
```

### Via PackageReference

```xml
<PackageReference Include="DataflowGraph" Version="1.0.0" />
```

---

## 📚 Documentation

- [Getting Started Guide](docs/getting-started.md)
- [Hangfire Integration](docs/integrations/hangfire.md)
- [Quartz.NET Integration](docs/integrations/quartz-net.md)
- [Azure Functions Integration](docs/integrations/azure-functions.md)
- [API Reference](docs/api-reference.md)
- [Performance Best Practices](docs/performance.md)
- [Migration from Legacy Libraries](docs/migration-guide.md)

---

## 🧪 Testing

DataflowGraph is designed for testability:

```csharp
public class ReportServiceTests
{
    [Fact]
    public async Task GenerateReport_ExecutesStepsInOrder()
    {
        // Arrange
        var mockFactory = new Mock<IBatchGraphFactory>();
        var service = new ReportService(mockFactory.Object, ...);

        // Act
        var result = await service.GenerateReportAsync("test-123", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        mockFactory.Verify(x => x.Create(), Times.Once);
    }
}
```

---

## 📄 License

Distributed under the MIT License. See [LICENSE](LICENSE) for more information.

---
