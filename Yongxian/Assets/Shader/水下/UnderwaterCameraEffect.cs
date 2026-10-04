using UnityEngine;

/// <summary>
/// 水下滤镜：挂在相机上，让整个画面呈现出"相机在水下"的效果。
///
/// 用法：
///   1. 把本脚本挂到 Main Camera 上（会自动要求 Camera 组件）
///   2. 运行时脚本会自动创建 Hidden/Underwater 材质并开启相机深度纹理
///   3. Inspector 里调参数即可；enableEffect 取消勾选 = 一键关掉滤镜
///
/// 效果说明（对应 Underwater.shader）：
///   水面波动（折射晃动） + 散射模糊 + 水的吸收（越远越浑浊、红光先消失）
///   + 焦散光斑 + 暗角
///
/// 提示：
///   - 浑浊度越大，远处越快被水色吞没；把密度调 0 就是"清澈的水"
///   - 想模拟从水下浮出水面，可以在代码里切换 enableEffect
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Rendering/Underwater Camera Effect")]
public class UnderwaterCameraEffect : MonoBehaviour
{
    [Header("水色 / 浑浊度")]
    [Tooltip("远处景物融入的水色（建议青蓝调）")]
    public Color waterColor = new Color(0.05f, 0.32f, 0.38f, 1f);
    [Tooltip("浑浊度：越大可见距离越短、画面越快被水色吞没；0 = 完全清澈（只剩波动和焦散）")]
    [Range(0f, 0.5f)] public float density = 0.08f;
    [Tooltip("三通道吸收比例：红光吸收最快，所以水下偏青蓝；想更黄绿就把 R 调低")]
    public Color absorption = new Color(1f, 0.45f, 0.25f, 1f);

    [Header("水面波动（折射晃动）")]
    [Tooltip("波动强度：画面扭曲的程度，0 = 不晃动")]
    [Range(0f, 5f)] public float distortionStrength = 1f;
    [Tooltip("波动速度")]
    [Range(0f, 5f)] public float distortionSpeed = 1f;
    [Tooltip("波动密度：越大波纹越细越密")]
    [Range(1f, 40f)] public float waveScale = 9f;

    [Header("焦散（水面光斑）")]
    [Tooltip("焦散颜色")]
    public Color causticsColor = new Color(1f, 0.96f, 0.82f, 1f);
    [Tooltip("焦散强度：0 = 不显示光斑")]
    [Range(0f, 3f)] public float causticsIntensity = 0.4f;
    [Tooltip("焦散大小：越大光斑越密")]
    [Range(1f, 40f)] public float causticsScale = 12f;
    [Tooltip("焦散流动速度")]
    [Range(0f, 3f)] public float causticsSpeed = 0.7f;

    [Header("画面")]
    [Tooltip("散射模糊：水中悬浮颗粒让画面发糊；0 = 清晰，1 = 最糊")]
    [Range(0f, 1f)] public float scatterBlur = 0.35f;
    [Tooltip("色散：画面边缘的轻微 RGB 分离，模拟水的折射")]
    [Range(0f, 1f)] public float chromaticAberration = 0.15f;
    [Tooltip("暗角强度：画面四周变暗的程度")]
    [Range(0f, 1f)] public float vignetteIntensity = 0.35f;
    [Tooltip("暗角范围：越大暗角越集中在四周")]
    [Range(0.5f, 6f)] public float vignettePower = 2.5f;

    [Header("总开关")]
    [Tooltip("取消勾选 = 临时关掉滤镜（比如角色浮出水面时）")]
    public bool enableEffect = true;

    [Tooltip("水下滤镜 Shader；留空会自动找 Hidden/Underwater")]
    public Shader underwaterShader;

    Material _material;

    // 懒加载：第一次渲染时再创建材质
    Material Material
    {
        get
        {
            if (_material != null) return _material;

            Shader shader = underwaterShader != null ? underwaterShader : Shader.Find("Hidden/Underwater");
            if (shader == null)
            {
                Debug.LogWarning("[UnderwaterCameraEffect] 找不到 Hidden/Underwater 着色器，" +
                                 "请把 Underwater.shader 拖到脚本的 Underwater Shader 上", this);
                return null;
            }

            _material = new Material(shader);
            _material.hideFlags = HideFlags.HideAndDontSave;   // 不显示在 Project 里、不进存档
            return _material;
        }
    }

    void OnEnable()
    {
        // 着色器要用深度图计算"越远越浑浊"，必须让相机渲染一张深度纹理
        var cam = GetComponent<Camera>();
        if (cam != null) cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    void OnDisable()
    {
        if (_material != null)
        {
            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
            _material = null;
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        var mat = Material;
        if (!enableEffect || mat == null)
        {
            Graphics.Blit(source, destination);   // 关掉滤镜时直接输出原画面
            return;
        }

        mat.SetColor("_WaterColor", waterColor);
        mat.SetFloat("_Density", density);
        mat.SetColor("_Absorption", absorption);

        mat.SetFloat("_DistortionStrength", distortionStrength);
        mat.SetFloat("_DistortionSpeed", distortionSpeed);
        mat.SetFloat("_WaveScale", waveScale);

        mat.SetColor("_CausticsColor", causticsColor);
        mat.SetFloat("_CausticsIntensity", causticsIntensity);
        mat.SetFloat("_CausticsScale", causticsScale);
        mat.SetFloat("_CausticsSpeed", causticsSpeed);

        mat.SetFloat("_ScatterBlur", scatterBlur);
        mat.SetFloat("_ChromaticAberration", chromaticAberration);
        mat.SetFloat("_VignetteIntensity", vignetteIntensity);
        mat.SetFloat("_VignettePower", vignettePower);

        Graphics.Blit(source, destination, mat);
    }
}