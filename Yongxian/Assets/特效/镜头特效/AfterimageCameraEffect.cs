using UnityEngine;

/// <summary>
/// 残影（视觉暂留）特效 · 相机版：挂在主相机上，只对指定 Layer 上的物体生效。
///
/// 用法：
///   1. 把本脚本挂到 Main Camera 上（会自动要求 Camera 组件）
///   2. 在 Inspector 的「生效的层」里勾选要产生残影的层，其它层完全不受影响
///   3. 运行即可，脚本会自动创建一台隐藏的"残影相机"，不需要手动搭任何东西
///
/// 原理（和物体数量无关，每帧只多 2 次全屏 Blit）：
///   残影相机（只渲染指定层、黑底）先把这一层画到一张贴图上 →
///   累积进残影缓冲（乒乓两张 RT）：历史 × 衰减，再叠加一点点扩散（spread），
///   然后和本帧画面取较大值 → 主相机最后把残影按发光颜色加到画面上
///
/// 提示：
///   - 残影在屏幕空间，所以转动相机时残影会被"拉花"，这正是视觉暂留想要的感觉
///   - 残影相机只画指定层，看不到墙/地形等其它层，所以残影会"透墙"显示
///   - 静止的物体不会留下残影，也不会被叠加提亮（用 max 累积 + 合成时扣除本体画面）
///   - 场景物体很多时把「残影分辨率」调到 0.5，画面更糊但更省
///   - 觉得残影"一截一截不连续"：先确认 fadeRush = 0，然后调大 spread（扩散）
///     和 trailDuration（存活时间），或把残影分辨率降到 0.75 让放大时的双线性过滤帮忙填缝
/// </summary>
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Effects/Afterimage Camera Effect (按层生效)")]
[DisallowMultipleComponent]
public class AfterimageCameraEffect : MonoBehaviour
{
    [Header("生效的层（只有这些层上的物体会留下残影）")]
    [Tooltip("勾选需要产生残影的层；没勾的层上的物体完全不受影响")]
    public LayerMask targetLayers = ~0;

    [Header("总开关")]
    [Tooltip("取消勾选 = 一键关掉残影（残影相机也会一起停掉，省性能）")]
    public bool enableEffect = true;

    [Header("残影")]
    [Tooltip("残影持续多久后淡到几乎看不见（秒）：越小拖尾越短")]
    [Range(0.05f, 3f)] public float trailDuration = 0.6f;

    [Tooltip("本帧画面的权重：越大，新出现的画面在残影里越亮")]
    [Range(0f, 2f)] public float currentWeight = 1f;

    [Tooltip("每帧对残影做的扩散量（像素）：把一帧一个的鬼影抹成连续光带。" +
             "觉得残影「一截一截」就调大，0 = 清晰的离散鬼影")]
    [Range(0f, 6f)] public float spread = 1f;

    [Tooltip("每秒额外削减的亮度：让残影尾端更快消失；它会造成尾端突然断掉，" +
             "感觉不连续就先把它设成 0，改用 trailDuration 控制长度")]
    [Range(0f, 4f)] public float fadeRush = 0f;

    [Header("外观（发光叠加）")]
    [Tooltip("残影的发光颜色")]
    public Color ghostColor = new Color(0.4f, 0.9f, 1f, 1f);

    [Tooltip("发光强度：越大越亮，配合 Bloom 后处理效果更好")]
    [Range(0f, 4f)] public float intensity = 1.2f;

    [Tooltip("1 = 只在物体离开的位置叠加残影（推荐，本体和静止物体不会被提亮）；0 = 整层纯叠加")]
    [Range(0f, 1f)] public float subtractSource = 1f;

    [Header("性能")]
    [Tooltip("残影缓冲的分辨率倍率：1 = 全分辨率最清晰，0.5 = 半分辨率（更省、更糊）")]
    [Range(0.25f, 1f)] public float resolutionScale = 1f;

    [Header("Shader")]
    [Tooltip("残影 Shader；留空会自动找 Hidden/Afterimage")]
    public Shader afterimageShader;

    Camera _camera;
    Camera _trailCamera;
    Material _material;

    RenderTexture _layerRT;    // 残影相机这一帧画出来的该层画面
    RenderTexture _accumA;     // 残影缓冲（乒乓）
    RenderTexture _accumB;
    bool _accumMainIsA = true; // _accumA 里是不是最新的历史

    const float EndRatio = 0.05f;   // 经过 trailDuration 秒后残影只剩 5%

    Material Material
    {
        get
        {
            if (_material != null) return _material;

            Shader shader = afterimageShader != null ? afterimageShader : Shader.Find("Hidden/Afterimage");
            if (shader == null)
            {
                Debug.LogWarning("[AfterimageCameraEffect] 找不到 Hidden/Afterimage 着色器，" +
                                 "请把 Afterimage.shader 拖到脚本的 Afterimage Shader 上", this);
                return null;
            }

            _material = new Material(shader);
            _material.hideFlags = HideFlags.HideAndDontSave;   // 不显示在 Project 里、不进存档
            return _material;
        }
    }

    void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    void OnEnable()
    {
        if (!Application.isPlaying) return;   // 编辑模式不生成相机，免得弄脏场景
        if (_camera == null) _camera = GetComponent<Camera>();
        CreateTrailCamera();
    }

    void OnDisable()
    {
        ReleaseBuffers();

        if (_trailCamera != null)
        {
            if (Application.isPlaying) Destroy(_trailCamera.gameObject);
            else DestroyImmediate(_trailCamera.gameObject);
            _trailCamera = null;
        }

        if (_material != null)
        {
            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
            _material = null;
        }
    }

    void Update()
    {
        if (_camera == null) _camera = GetComponent<Camera>();
        if (_camera == null) return;

        if (_trailCamera == null) CreateTrailCamera();

        // 每帧同步，支持运行中改层 / 改相机参数
        _trailCamera.cullingMask = targetLayers;
        _trailCamera.depth = _camera.depth - 1f;   // 比主相机先渲染，这样合成时拿到的是"这一帧"的层画面
        _trailCamera.clearFlags = CameraClearFlags.SolidColor;
        _trailCamera.backgroundColor = Color.clear;  // 黑底：累积时用 max，黑底不会污染缓冲
        _trailCamera.orthographic = _camera.orthographic;
        _trailCamera.orthographicSize = _camera.orthographicSize;
        _trailCamera.fieldOfView = _camera.fieldOfView;
        _trailCamera.nearClipPlane = _camera.nearClipPlane;
        _trailCamera.farClipPlane = _camera.farClipPlane;
        _trailCamera.aspect = _camera.aspect;

        // 关掉残影时把残影相机也停掉；重新打开时清空缓冲，避免闪出上一帧的旧残影
        if (_trailCamera.enabled != enableEffect)
        {
            _trailCamera.enabled = enableEffect;
            if (enableEffect) ClearAccum();
        }

        EnsureBuffers();
    }

    void CreateTrailCamera()
    {
        var go = new GameObject("AfterimageTrailCamera");
        go.transform.SetParent(transform, false);   // 跟着主相机走，视角永远一致

        _trailCamera = go.AddComponent<Camera>();
        _trailCamera.clearFlags = CameraClearFlags.SolidColor;
        _trailCamera.backgroundColor = Color.clear;
        _trailCamera.cullingMask = targetLayers;
        _trailCamera.depth = _camera != null ? _camera.depth - 1f : -1f;
        _trailCamera.allowHDR = false;
        _trailCamera.allowMSAA = false;
        _trailCamera.useOcclusionCulling = false;
    }

    // 缓冲尺寸跟着屏幕走，分辨率或倍率变了就重建
    void EnsureBuffers()
    {
        int w = Mathf.Max(1, Mathf.RoundToInt(_camera.pixelWidth * resolutionScale));
        int h = Mathf.Max(1, Mathf.RoundToInt(_camera.pixelHeight * resolutionScale));
        if (_layerRT != null && _layerRT.width == w && _layerRT.height == h) return;

        ReleaseBuffers();

        // 用半浮点：每帧乘一次衰减，8 位缓冲会有明显的色阶
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf
            : RenderTextureFormat.Default;

        _layerRT = new RenderTexture(w, h, 16, format) { name = "AfterimageLayerRT", filterMode = FilterMode.Bilinear };
        _accumA  = new RenderTexture(w, h, 0, format) { name = "AfterimageAccumA", filterMode = FilterMode.Bilinear };
        _accumB  = new RenderTexture(w, h, 0, format) { name = "AfterimageAccumB", filterMode = FilterMode.Bilinear };

        _trailCamera.targetTexture = _layerRT;
        ClearAccum();
    }

    void ClearAccum()
    {
        if (_accumA == null || _accumB == null) return;

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = _accumA;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = _accumB;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = prev;
    }

    void ReleaseBuffers()
    {
        if (_trailCamera != null) _trailCamera.targetTexture = null;

        Release(ref _layerRT);
        Release(ref _accumA);
        Release(ref _accumB);
    }

    static void Release(ref RenderTexture rt)
    {
        if (rt == null) return;
        rt.Release();
        if (Application.isPlaying) Destroy(rt);
        else DestroyImmediate(rt);
        rt = null;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        var mat = Material;
        if (!enableEffect || mat == null || _trailCamera == null || _layerRT == null)
        {
            Graphics.Blit(source, destination);   // 关掉时直接输出原画面
            return;
        }

        float dt = Time.deltaTime;

        // 把「持续多少秒」换算成每帧的保留比例：经过 trailDuration 秒后只剩 EndRatio
        float decay = 0f;
        if (trailDuration > 0.01f)
        {
            decay = Mathf.Clamp01(Mathf.Exp(Mathf.Log(EndRatio) * dt / trailDuration));
        }

        RenderTexture read = _accumMainIsA ? _accumA : _accumB;
        RenderTexture write = _accumMainIsA ? _accumB : _accumA;

        // ---------- 累积：残影缓冲 = max(历史 × 衰减 - 快消, 本帧该层画面) ----------
        mat.SetTexture("_LayerTex", _layerRT);
        mat.SetFloat("_Decay", decay);
        mat.SetFloat("_CurrentWeight", currentWeight);
        mat.SetFloat("_FadeRush", fadeRush);
        mat.SetFloat("_Spread", spread);
        mat.SetFloat("_DeltaTime", dt);
        Graphics.Blit(read, write, mat, 0);
        _accumMainIsA = !_accumMainIsA;   // 交换乒乓缓冲

        // ---------- 合成：主画面 + 残影 × 发光颜色 × 强度 ----------
        mat.SetTexture("_TrailTex", write);
        mat.SetColor("_GhostColor", ghostColor);
        mat.SetFloat("_Intensity", intensity);
        mat.SetFloat("_SubtractSource", subtractSource);
        Graphics.Blit(source, destination, mat, 1);
    }
}
