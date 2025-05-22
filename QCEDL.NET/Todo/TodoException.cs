namespace QCEDL.NET.Todo;

/// <summary>
/// This exception is to suppress CA2201: Do not raise reserved exception types
/// </summary>
public sealed class TodoException(string message) : Exception(message);