using UnityEngine;

/// <summary>
/// 天空盒淡入特效：场景开始时天空盒是全黑的，然后在 fadeDuration 秒内把天空盒亮度从 0 淡入到 1
/// （也就是"纯黑世界 → 天空盒慢慢显现"）。
/// 依赖天空盒 Shader Graph 里的 float 属性 Fade（引用名 _Fade，0 = 全黑，1 = 完整天空盒），
/// 运行时修改的是 RenderSettings.skybox 材质的 _Fade 值。
/// 一般挂在场景里的任意常驻物体上（例如主相机），不需要在 Inspector 里指定材质。
/// </summary>
[DisallowMultipleComponent]
public class SkyboxFadeIn : MonoBehaviour
{
    [Header("淡入参数")]
    [Tooltip("开始淡入前的等待时间（秒），用来让世界先全黑停留一段时间")]
    public float startDelay = 0f;

    [Tooltip("天空盒从全黑淡入到完全显现的时间（秒）")]
    public float fadeDuration = 3f;

    [Tooltip("亮度曲线：横轴 0~1 是淡入进度，纵轴是天空盒亮度倍率（0 = 全黑，1 = 完整天空盒）")]
    public AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("是否在场景开始时自动播放淡入")]
    public bool playOnStart = true;

    private static readonly int FadePropertyID = Shader.PropertyToID("_Fade");

    private float _timer;    // 从开始播放起累计的时间
    private bool _playing;   // 是否正在淡入
    private bool _applied;   // 是否已经改过天空盒材质
    private bool _warned;    // 天空盒缺失/属性缺失时只警告一次

    void Awake()
    {
        if (!playOnStart) return;

        // 在第一帧渲染前就置黑，避免先闪一下完整天空盒
        _timer = 0f;
        _playing = true;
        ApplyFade(0f);
    }

    void Update()
    {
        if (!_playing) return;

        _timer += Time.deltaTime;

        if (_timer < startDelay) return;   // 等待阶段：保持全黑

        float progress = fadeDuration > 0f ? Mathf.Clamp01((_timer - startDelay) / fadeDuration) : 1f;
        ApplyFade(fadeCurve.Evaluate(progress));

        if (progress >= 1f) _playing = false;
    }

    void OnDestroy()
    {
        // 退出播放/销毁时恢复到完整天空盒，避免编辑器里天空盒一直黑着
        if (_applied) ApplyFade(1f);
    }

    /// <summary>触发一次从全黑开始的淡入（可被其它脚本调用，例如过场结束时）</summary>
    public void Play()
    {
        _warned = false;
        _timer = 0f;
        _playing = true;
        ApplyFade(0f);   // 立刻置黑
    }

    // 把 0~1 的亮度写入天空盒材质的 _Fade 属性
    void ApplyFade(float value)
    {
        Material skybox = RenderSettings.skybox;

        if (skybox == null || !skybox.HasProperty(FadePropertyID))
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning($"[{nameof(SkyboxFadeIn)}] 无法设置天空盒淡入：" +
                    (skybox == null ? "场景没有天空盒材质（RenderSettings.skybox 为空）。"
                                    : $"天空盒材质 {skybox.name} 上没有 _Fade 属性，请先给天空盒 Shader Graph 添加 Fade 属性。"));
            }
            _playing = false;
            return;
        }

        skybox.SetFloat(FadePropertyID, Mathf.Clamp01(value));
        _applied = true;
    }
}