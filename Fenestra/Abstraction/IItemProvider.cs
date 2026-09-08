namespace Fenestra.Abstraction;

using System.Threading;
using System.Threading.Tasks;

/// <summary>元素获取抽象。生产实现（宿主适配层）包装对象池；测试用桩实现。</summary>
public interface IItemProvider
{
    /// <summary>取一个元素视图。约定：装配时预热后，运行期同步完成（返回已完成 ValueTask，见设计 D2）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>元素视图。</returns>
    ValueTask<IItemView> GetAsync(CancellationToken ct);

    /// <summary>归还元素视图（编排器已先调用 OnUnbind）。</summary>
    /// <param name="view">元素视图。</param>
    void Return(IItemView view);
}
