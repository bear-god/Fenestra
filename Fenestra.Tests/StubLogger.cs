namespace Fenestra.Tests;

using System.Collections.Generic;
using Fenestra.Abstraction;

/// <summary>桩日志：记录错误与警告。</summary>
internal sealed class StubLogger : IVirtualListLogger
{
    /// <summary>错误列表。</summary>
    public List<string> Errors { get; } = new();

    /// <summary>警告列表。</summary>
    public List<string> Warnings { get; } = new();

    /// <inheritdoc />
    public void Error(string message)
    {
        Errors.Add(message);
    }

    /// <inheritdoc />
    public void Warning(string message)
    {
        Warnings.Add(message);
    }
}
