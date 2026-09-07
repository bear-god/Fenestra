namespace Fenestra.Abstraction;

/// <summary>核心日志抽象（依赖倒置：核心不依赖 Game.Logging，由适配层实现转接）。</summary>
public interface IVirtualListLogger
{
    /// <summary>记录错误。</summary>
    /// <param name="message">错误信息。</param>
    void Error(string message);

    /// <summary>记录警告。</summary>
    /// <param name="message">警告信息。</param>
    void Warning(string message);
}

/// <summary>
/// 空日志实现：未注入日志时静默降级，保证核心可独立运行（headless）且不依赖 Game.Logging。
/// </summary>
public sealed class NullVirtualListLogger : IVirtualListLogger
{
    /// <summary>共享实例。</summary>
    public static readonly NullVirtualListLogger Instance = new();

    /// <inheritdoc />
    public void Error(string message)
    {
    }

    /// <inheritdoc />
    public void Warning(string message)
    {
    }
}
