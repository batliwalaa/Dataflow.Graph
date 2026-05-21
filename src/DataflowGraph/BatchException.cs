#pragma warning disable SYSLIB0051 // Exception serialization is obsolete in .NET 6+

using System.Runtime.Serialization;

namespace DataflowGraph;

/// <summary>
/// Exception type used for operation failures during batch execution.
/// Contains operation-specific information for better error reporting and debugging.
/// Serializable for use with Hangfire, Quartz.NET, and cross-process scenarios.
/// 
/// Key features:
/// - Stores operation name that failed
/// - Preserves original inner exception
/// - JSON serializable (for HTTP APIs)
/// - Compatible with .NET serialization (for backward compatibility)
/// 
/// </summary>
[Serializable]
public class BatchException : Exception
{
    /// <summary>
    /// Gets the name of the operation that failed.
    /// Example: "FetchUsers", "ValidateOrder", "ProcessPayment"
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// Gets the original inner exception that caused the failure.
    /// This is the actual exception thrown by the operation (e.g., SqlException, HttpRequestException).
    /// </summary>
    public Exception? Inner => InnerException;

    /// <summary>
    /// Initializes a new instance of the BatchException class.
    /// </summary>
    public BatchException()
    {
        OperationName = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the BatchException class.
    /// </summary>
    /// <param name="operationName">The name of the failed operation</param>
    /// <param name="message">The error message</param>
    public BatchException(string operationName, string message)
        : base(message)
    {
        OperationName = operationName;
    }

    /// <summary>
    /// Initializes a new instance of the BatchException class.
    /// </summary>
    /// <param name="operationName">The name of the failed operation</param>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The original exception that caused the failure</param>
    public BatchException(string operationName, string message, Exception? innerException)
        : base(message, innerException)
    {
        OperationName = operationName;
    }

    /// <summary>
    /// Creates a BatchException from an operation failure.
    /// Convenience factory method for common usage pattern.
    /// </summary>
    /// <param name="operationName">The name of the failed operation</param>
    /// <param name="exception">The exception that occurred during operation execution</param>
    /// <returns>A new BatchException instance with operation name and inner exception</returns>
    public static BatchException FromStep(string operationName, Exception exception)
    {
        return new BatchException(
            operationName,
            $"Operation '{operationName}' failed: {exception.Message}",
            exception);
    }

    /// <summary>
    /// Creates a BatchException from an operation failure with custom message.
    /// Use when you need to provide additional context beyond the original exception message.
    /// </summary>
    /// <param name="operationName">The name of the failed operation</param>
    /// <param name="message">The custom error message</param>
    /// <param name="innerException">The original exception that occurred</param>
    /// <returns>A new BatchException instance</returns>
    public static BatchException FromStep(string operationName, string message, Exception innerException)
    {
        return new BatchException(operationName, message, innerException);
    }

    #region Serialization Support (.NET Framework Compatibility)

    /// <summary>
    /// Initializes a new instance of the BatchException class with serialized data.
    /// Included for .NET Framework and binary serialization compatibility.
    /// Not used in .NET 6/8 JSON serialization (Hangfire/Quartz use JSON now).
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
    protected BatchException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        OperationName = info.GetString(nameof(OperationName)) ?? string.Empty;
    }

    #endregion
}

#pragma warning restore SYSLIB0051