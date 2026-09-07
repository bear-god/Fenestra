namespace Fenestra.Unity.Editor;

using Fenestra.Entity;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="VirtualListMono" /> 的自定义编辑器：在默认 Inspector 基础上追加布局预览区。
/// 开启预览后于 Scene 视图按当前配置把元素网格以线框叠加在列表节点上（不实例化元素、不改场景层级），
/// 随 Inspector 配置实时刷新，用于预览元素间距 / Grid 分列 / 反向排列 / 轴向等布局效果。
/// 预览仅按布局数学示意（变高未测量元素以配置主轴尺寸近似，缺省回退预制件主轴尺寸 / 100），不驱动核心。
/// 不依赖 Odin Inspector，基于原生 <see cref="Editor" />（Fenestra 的配置字段均为原生序列化，默认绘制即可）。
/// </summary>
[CustomEditor(typeof(VirtualListMono))]
public sealed class VirtualListMonoEditor : Editor
{
    private const string PreviewPrefKey = "Fenestra.VirtualListMono.Preview.Enabled";
    private const string SampleCountPrefKey = "Fenestra.VirtualListMono.Preview.SampleCount";
    private const int DefaultSampleCount = 20;
    private const int MinSampleCount = 1;
    private const int MaxSampleCount = 100;
    private const float FallbackItemSize = 100f;

    private static readonly Color InViewFill = new(0.2f, 0.8f, 1f, 0.35f);
    private static readonly Color OutViewFill = new(0.45f, 0.45f, 0.45f, 0.12f);
    private static readonly Color ItemOutline = new(0f, 0f, 0f, 0.9f);
    private static readonly Color ViewportOutline = new(1f, 0.85f, 0.2f, 1f);

    private bool _previewEnabled;
    private int _sampleCount;

    /// <inheritdoc/>
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        DrawPreviewControls();
    }

    /// <inheritdoc/>
    protected override void OnEnable()
    {
        base.OnEnable();

        // Editor 实例是 ScriptableObject，构造函数/字段初始化器禁止调用 EditorPrefs，必须在 OnEnable 读取。
        _previewEnabled = EditorPrefs.GetBool(PreviewPrefKey, true);
        _sampleCount = Mathf.Clamp(EditorPrefs.GetInt(SampleCountPrefKey, DefaultSampleCount), MinSampleCount, MaxSampleCount);
    }

    /// <summary>按当前配置计算并绘制预览线框。</summary>
    /// <param name="viewport">列表节点 RectTransform（视口）。</param>
    /// <param name="config">核心配置。</param>
    /// <param name="prefabMainSize">预制件主轴尺寸。</param>
    /// <param name="count">预览元素数量。</param>
    private static void DrawPreview(RectTransform viewport, VirtualListConfig config, float prefabMainSize, int count)
    {
        var rect = viewport.rect;
        var vertical = config.Axis == VirtualListAxis.Vertical;
        var viewportMain = vertical ? rect.height : rect.width;
        var viewportCross = vertical ? rect.width : rect.height;

        // 元素主轴尺寸：定高 / Grid 用配置值；变高未测量，以配置尺寸为近似，缺省回退预制件 / 常量。
        var mainSize = config.FixedItemSize;
        if (mainSize <= 0f)
        {
            mainSize = prefabMainSize > 0f ? prefabMainSize : FallbackItemSize;
        }

        var columns = ComputeColumnCount(config, viewportCross);
        var rows = Mathf.Max(0, Mathf.CeilToInt((float)count / columns));
        var crossSize = columns <= 1
            ? viewportCross
            : (viewportCross - ((columns - 1) * config.Grid.CrossSpacing)) / columns;
        crossSize = Mathf.Max(0f, crossSize);

        // 行主轴偏移（累加尺寸与间距，等价核心 RowMainOffset）。
        var rowOffsets = new float[rows];
        var cursor = 0f;
        for (var r = 0; r < rows; r++)
        {
            rowOffsets[r] = cursor;
            cursor += mainSize + config.Spacing;
        }

        var contentSize = rows > 0 ? cursor - config.Spacing : 0f;
        var maxScroll = Mathf.Max(0f, contentSize - viewportMain);
        var initialOffset = config.InitialAnchor == VirtualListInitialAnchor.OffsetMax ? maxScroll : 0f;

        var topLeft = viewport.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
        var z = topLeft.z;

        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var mainOffset = rowOffsets[row];
            if (config.Reversed)
            {
                mainOffset = contentSize - mainOffset - mainSize;
            }

            var crossOffset = columns <= 1 ? 0f : col * (crossSize + config.Grid.CrossSpacing);
            var inView = (mainOffset + mainSize > initialOffset) && (mainOffset < initialOffset + viewportMain);

            Vector3 min;
            Vector3 max;
            if (vertical)
            {
                min = new Vector3(topLeft.x + crossOffset, topLeft.y - mainOffset - mainSize, z);
                max = new Vector3(topLeft.x + crossOffset + crossSize, topLeft.y - mainOffset, z);
            }
            else
            {
                min = new Vector3(topLeft.x + mainOffset, topLeft.y - crossOffset - crossSize, z);
                max = new Vector3(topLeft.x + mainOffset + mainSize, topLeft.y - crossOffset, z);
            }

            DrawQuad(min, max, inView ? InViewFill : OutViewFill, ItemOutline);
        }

        // 视口边界框（黄）。
        var v0 = viewport.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f));
        var v1 = viewport.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
        var v2 = viewport.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f));
        var v3 = viewport.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f));
        Handles.DrawSolidRectangleWithOutline(new[] { v0, v1, v2, v3 }, new Color(0f, 0f, 0f, 0f), ViewportOutline);
    }

    /// <summary>Grid 交叉轴元素数（非 Grid 为 1；AutoFit 按视口交叉轴尺寸推断）。</summary>
    /// <param name="config">核心配置。</param>
    /// <param name="viewportCross">视口交叉轴尺寸。</param>
    /// <returns>交叉轴元素数（≥1）。</returns>
    private static int ComputeColumnCount(VirtualListConfig config, float viewportCross)
    {
        if (!config.Grid.Enabled)
        {
            return 1;
        }

        if (config.Grid.AutoFit)
        {
            var cell = Mathf.Max(1f, config.Grid.CrossSize);
            var spacing = config.Grid.CrossSpacing;
            var cols = Mathf.FloorToInt((viewportCross + spacing) / (cell + spacing));
            return Mathf.Max(1, cols);
        }

        return Mathf.Max(1, config.Grid.CrossCount);
    }

    /// <summary>以两对角点绘制实心矩形（含描边）。</summary>
    /// <param name="min">最小角（世界坐标）。</param>
    /// <param name="max">最大角（世界坐标）。</param>
    /// <param name="fill">填充色。</param>
    /// <param name="outline">描边色。</param>
    private static void DrawQuad(Vector3 min, Vector3 max, Color fill, Color outline)
    {
        var verts = new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, max.y, min.z),
        };
        Handles.DrawSolidRectangleWithOutline(verts, fill, outline);
    }

    /// <summary>Scene 视图叠加绘制预览线框（仅选中该列表时调用）。</summary>
    protected override void OnSceneGUI()
    {
        if (Application.isPlaying || !_previewEnabled)
        {
            return;
        }

        var mono = target as VirtualListMono;
        if (mono == null)
        {
            return;
        }

        // 预制件资源（非场景实例 / 非预制件编辑模式）没有场景，跳过避免在空白处绘制。
        if (!mono.gameObject.scene.IsValid())
        {
            return;
        }

        var viewport = mono.transform as RectTransform;
        if (viewport == null)
        {
            return;
        }

        var config = mono.Config?.ToCore() ?? default;
        var prefabMainSize = ResolvePrefabMainSize(mono, config.Axis);
        DrawPreview(viewport, config, prefabMainSize, _sampleCount);
    }

    /// <summary>预览控制区：开关 + 采样数量（状态经 EditorPrefs 持久化）。</summary>
    private void DrawPreviewControls()
    {
        EditorGUILayout.Space(6f);
        GUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("布局预览（Scene 视图）", EditorStyles.boldLabel);

        var enabled = EditorGUILayout.Toggle("启用预览", _previewEnabled);
        if (enabled != _previewEnabled)
        {
            _previewEnabled = enabled;
            EditorPrefs.SetBool(PreviewPrefKey, enabled);
        }

        var count = EditorGUILayout.IntSlider("预览元素数", _sampleCount, MinSampleCount, MaxSampleCount);
        if (count != _sampleCount)
        {
            _sampleCount = count;
            EditorPrefs.SetInt(SampleCountPrefKey, count);
        }

        EditorGUILayout.HelpBox(
            "青色=初始可视窗口，灰色=窗口外；黄色框=视口边界。仅示意布局，不实例化元素。",
            MessageType.None);
        GUILayout.EndVertical();

        // 保证 Inspector 改配后 Scene 视图立即刷新（实时预览）。
        SceneView.RepaintAll();
    }

    /// <summary>读取 item 预制件主轴尺寸（变高 / 未配置定高尺寸时作预览近似）。</summary>
    /// <param name="mono">目标列表。</param>
    /// <param name="axis">滚动主轴。</param>
    /// <returns>预制件主轴尺寸；无预制件时为 0。</returns>
    private float ResolvePrefabMainSize(VirtualListMono mono, VirtualListAxis axis)
    {
        var prefabProp = serializedObject.FindProperty("_itemPrefab");
        var prefab = prefabProp != null ? prefabProp.objectReferenceValue as GameObject : null;
        if (prefab == null)
        {
            return 0f;
        }

        var prefabRect = prefab.GetComponent<RectTransform>();
        if (prefabRect == null)
        {
            return 0f;
        }

        return axis == VirtualListAxis.Vertical ? prefabRect.rect.height : prefabRect.rect.width;
    }
}
