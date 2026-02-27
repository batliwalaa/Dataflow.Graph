# DataflowGraph

[![NuGet](https://img.shields.io/nuget/v/DataflowGraph.svg)](https://www.nuget.org/packages/DataflowGraph)
[![Downloads](https://img.shields.io/nuget/dt/DataflowGraph)](https://www.nuget.org/packages/DataflowGraph)
[![License](https://img.shields.io/github/license/yourusername/DataflowGraph)](https://github.com/batliwalaa/Dataflow.Graph/blob/main/LICENSE)

**Declarative dependency-graph orchestration built on System.Threading.Tasks.Dataflow**

DataflowGraph provides a fluent, code-first API for orchestrating complex batch operations with explicit dependencies. Built on top of Microsoft's battle-tested TPL Dataflow library, it adds intelligent dependency resolution while maintaining the performance and reliability you expect from .NET.

> **Perfect for**: CLI tools • Serverless functions • Microservices • Desktop applications • Any scenario requiring lightweight, in-process orchestration without external databases.

## 🚀 Quick Start

```csharp
// Install-Package DataflowGraph

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
