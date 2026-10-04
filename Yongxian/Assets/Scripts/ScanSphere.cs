using UnityEngine;

/// <summary>
/// 扫描球控制器：挂在「挂了扫描材质（Unlit/Saomiao）的球」上。
/// 作用：
///   1. 让球从 0 平滑扩大到最大半径（球变大时，材质会自动在球面和物体的交界处画出扫描线）
///   2. 自动为场景相机开启深度纹理（内置管线必须开启，否则扫不出线）
/// 也可以不用这个脚本的扩大功能，自己用动画 / Timeline 缩放球体，
/// 但相机深度纹理仍然要开启（否则扫描线不显示）。
/// </summary>
[DisallowMultipleComponent]
public class ScanSphere : MonoBehaviour
{
    [Header("扫描范围")]
    [Tooltip("球的最大半径（世界单位）。球使用 Unity 自带 Sphere 网格（半径 0.5），所以缩放 = 半径 × 2")]
    public float maxRadius = 10f;

    [Header("扫描节奏")]
    [Tooltip("半径从 0 扩大到最大值的时间（秒）")]
    public float expandDuration = 2f;

    [Tooltip("两轮扫描之间的停顿时长（秒），循环扫描时才生效")]
    public float interval = 0.5f;

    [Tooltip("是否循环扫描；关闭则只扫一轮，停在最大半径")]
    public bool loop = true;

    [Tooltip("半径变化曲线：横轴 0~1 是进度，纵轴是半径比例，可做缓入缓出或带一点回弹")]
    public AnimationCurve radiusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("启动")]
    [Tooltip("组件启用时自动开始扫描")]
    public bool playOnEnable = true;

    float _time;
    bool _playing;

    void OnEnable()
    {
        EnableCameraDepthTexture();

        if (playOnEnable) Play();
        else ApplyRadius(0f);
    }

    void Update()
    {
        if (!_playing) return;

        _time += Time.deltaTime;

        if (!loop)
        {
            ApplyRadius(ExpandRadius(_time));
            if (_time >= expandDuration) _playing = false;   // 单次：停在最大半径
            return;
        }

        float cycle = expandDuration + interval;
        if (cycle <= 0f) return;

        // 一轮：前 expandDuration 秒扩大，之后 interval 秒保持在最大半径，然后重新开始
        float t = Mathf.Repeat(_time, cycle);
        ApplyRadius(ExpandRadius(t));
    }

    /// <summary>重新开始一轮扫描（从半径 0 开始）</summary>
    public void Play()
    {
        _time = 0f;
        _playing = true;
        ApplyRadius(0f);
    }

    /// <summary>停止扫描，保持在当前大小</summary>
    public void Stop()
    {
        _playing = false;
    }

    float ExpandRadius(float t)
    {
        float phase = expandDuration > 0f ? Mathf.Clamp01(t / expandDuration) : 1f;
        // 只限制不小于 0，允许曲线超过 1（曲线带一点回弹超出去也没问题）
        return maxRadius * Mathf.Max(0f, radiusCurve.Evaluate(phase));
    }

    void ApplyRadius(float radius)
    {
        // Sphere 网格半径是 0.5，所以缩放 = 世界半径 × 2
        transform.localScale = Vector3.one * (radius * 2f);
    }

    // 内置管线不会自动生成 _CameraDepthTexture，必须给相机开启 Depth 深度纹理
    void EnableCameraDepthTexture()
    {
        var cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i].depthTextureMode |= DepthTextureMode.Depth;
        }
    }
}