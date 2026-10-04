using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 坐标轴方向指示器（调试用）：从物体原点画出本地 X / Y / Z 的正方向彩色线条。
///   X = 红、Y = 绿、Z = 蓝（Unity 坐标轴的颜色约定），可选画出负方向（暗色）和 +X/+Y/+Z 文字标签。
///
/// 用法：
///   1. 把本脚本挂到要观察的物体上（比如菌子模型）
///   2. Scene 视图里任何时候都能看到（编辑 / 播放都行，不需要选中物体）
///   3. 想在 Game 视图里也看到：保持 Draw In Game View 打开，并打开 Game 视图右上角的 Gizmos 开关
///
/// 用途：确认"菌子的正面到底朝哪" —— 线条长度忽略物体缩放，看的时候不会被缩放干扰
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Debug/Axis Direction Gizmos")]
public class AxisDirectionGizmos : MonoBehaviour
{
    [Header("线条")]
    [Tooltip("坐标轴线条长度（世界单位，不受物体缩放影响）")]
    public float axisLength = 1.5f;

    [Tooltip("X 轴（红）")]
    public Color xColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Tooltip("Y 轴（绿）")]
    public Color yColor = new Color(0.3f, 1f, 0.3f, 1f);

    [Tooltip("Z 轴（蓝）")]
    public Color zColor = new Color(0.35f, 0.55f, 1f, 1f);

    [Tooltip("同时画出三个轴的负方向（暗色，长度一半）")]
    public bool drawNegative = true;

    [Header("显示")]
    [Tooltip("在 Scene 视图里显示 +X / +Y / +Z 文字标签")]
    public bool showLabels = true;

    [Tooltip("在 Game 视图里也画这些线（需要打开 Game 视图右上角的 Gizmos 开关）")]
    public bool drawInGameView = true;

    void OnDrawGizmos()
    {
        // 只用位置和旋转、忽略缩放：物体被缩放后线条长度依然好读
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

        DrawAxis(Vector3.right, xColor, "+X");
        DrawAxis(Vector3.up, yColor, "+Y");
        DrawAxis(Vector3.forward, zColor, "+Z");

        Gizmos.matrix = Matrix4x4.identity;
    }

    void DrawAxis(Vector3 direction, Color color, string label)
    {
        Vector3 tip = direction * axisLength;

        Gizmos.color = color;
        Gizmos.DrawLine(Vector3.zero, tip);
        Gizmos.DrawSphere(tip, axisLength * 0.04f);   // 端点画个小球，一眼能看出哪边是正方向

        if (drawNegative)
        {
            Color dim = color;
            dim.a *= 0.35f;
            Gizmos.color = dim;
            Gizmos.DrawLine(Vector3.zero, -tip * 0.5f);
        }

#if UNITY_EDITOR
        if (showLabels)
        {
            Handles.color = color;
            Handles.Label(transform.position + transform.rotation * (tip * 1.08f), label);
        }
#endif
    }

    void Update()
    {
        if (!drawInGameView || !Application.isPlaying) return;

        // Game 视图（Gizmos 开关打开时）也能看到，方便边玩边确认方向
        Vector3 origin = transform.position;
        Debug.DrawLine(origin, origin + transform.right * axisLength, xColor);
        Debug.DrawLine(origin, origin + transform.up * axisLength, yColor);
        Debug.DrawLine(origin, origin + transform.forward * axisLength, zColor);
    }
}