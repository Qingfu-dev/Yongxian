using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 扫描显形控制器：让隐形材质（Unlit/Yingxing）的物体在"被扫描线扫到"时显形，
/// 一段时间没有再被扫到后，不透明度缓慢回到 0（重新隐形）。
///
/// 用法：
///   1. 把 Assets/Shader/yingxing/yingxing.mat 挂到要隐形的物体上（平时完全看不见）
///   2. 把这个脚本挂到场景里随便一个物体上（扫描球留空会自动找）
///
/// 说明：
///   - 可以用 Target Tag / Target Layer 只控制一部分物体（比如只让道具隐形，地面保持可见）
///   - 打开 Auto Replace Material 后，脚本会自己把筛选到的物体换成隐形材质（会复制原贴图/颜色）
/// </summary>
public class ScanReveal : MonoBehaviour
{
    [Header("扫描球")]
    [Tooltip("扫描球（挂了 saomiao 材质的球）。留空会自动在场景里找")]
    public Transform scanSphere;

    [Header("目标筛选（Tag / Layer）")]
    [Tooltip("只控制这个 Tag 的物体；留空 = 不按 Tag 过滤")]
    public string targetTag = "";
    [Tooltip("只控制在这个 Layer 上的物体")]
    public LayerMask targetLayer = ~0;

    [Header("隐形材质")]
    [Tooltip("隐形材质（Assets/Shader/yingxing/yingxing.mat）")]
    public Material invisibleMaterial;
    [Tooltip("自动给筛选到的物体换成隐形材质（会复制原来的贴图和颜色）；关闭时只驱动已经用了隐形材质的物体")]
    public bool autoReplaceMaterial = false;

    [Header("显形 / 变回隐形")]
    [Tooltip("被扫到后保持完全显形的时间（秒）")]
    public float stayDuration = 3f;
    [Tooltip("显形速度（不透明度每秒增加多少），越大越快")]
    public float fadeInSpeed = 6f;
    [Tooltip("变回隐形的速度（不透明度每秒减少多少），越小越慢")]
    public float fadeOutSpeed = 0.35f;

    class Target
    {
        public Renderer renderer;
        public Material originalMaterial;
        public float opacity;
        public float lastTouchTime = float.NegativeInfinity;
    }

    readonly List<Target> _targets = new List<Target>();
    MaterialPropertyBlock _mpb;
    float _sphereMeshRadius = 0.5f;
    float _scanWidth = 0.5f;

    void Awake()
    {
        // MaterialPropertyBlock 不能在字段初始化里 new（Unity 会报 CreateImpl 错误），放到 Awake 里创建
        _mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        if (scanSphere == null) scanSphere = FindScanSphere();

        if (scanSphere != null)
        {
            // 从扫描球材质读参数，保证"判定范围"和"画出来的线"一致
            var sphereRenderer = scanSphere.GetComponent<Renderer>();
            var sphereMat = sphereRenderer != null ? sphereRenderer.sharedMaterial : null;
            if (sphereMat != null)
            {
                if (sphereMat.HasProperty("_MeshRadius")) _sphereMeshRadius = sphereMat.GetFloat("_MeshRadius");
                if (sphereMat.HasProperty("_ScanWidth")) _scanWidth = sphereMat.GetFloat("_ScanWidth");
            }
        }
        else
        {
            Debug.LogWarning("[ScanReveal] 没找到扫描球，请手动把扫描球拖到 Scan Sphere 上", this);
        }

        RefreshTargets();
    }

    void OnDisable()
    {
        // 恢复原材质、清掉属性块，避免退出播放后物体还保持隐形/显形状态
        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _targets[i];
            if (t.renderer == null) continue;
            if (t.originalMaterial != null) t.renderer.sharedMaterial = t.originalMaterial;
            t.renderer.SetPropertyBlock(null);
        }
        _targets.Clear();
    }

    /// <summary>重新收集一次目标（运行中新生成的物体可以调用这个）</summary>
    public void RefreshTargets()
    {
        _targets.Clear();

        var renderers = FindObjectsOfType<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!IsMatch(r.gameObject)) continue;

            var t = new Target { renderer = r, originalMaterial = r.sharedMaterial };

            if (autoReplaceMaterial)
            {
                var mat = CreateInvisibleMaterial(r);
                if (mat == null) continue;
                r.sharedMaterial = mat;
            }
            else if (r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_Opacity"))
            {
                continue;   // 没挂隐形材质的物体不控制
            }

            _targets.Add(t);
        }

        if (_targets.Count == 0)
        {
            Debug.LogWarning("[ScanReveal] 没找到可控目标：请把 yingxing.mat 挂到要隐形的物体上，" +
                             "或打开 Auto Replace Material；如果用 Tag/Layer 筛选，检查设置是否正确", this);
        }
        else
        {
            Debug.Log($"[ScanReveal] 找到 {_targets.Count} 个可控目标");
        }
    }

    void Update()
    {
        if (scanSphere == null || _targets.Count == 0) return;

        // 与扫描 shader 相同的半径公式：网格半径 × 最大缩放
        Vector3 s = scanSphere.lossyScale;
        float radius = _sphereMeshRadius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        Vector3 center = scanSphere.position;
        float now = Time.time;
        float dt = Time.deltaTime;

        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _targets[i];
            if (t.renderer == null) continue;

            // ---- 扫描线有没有碰到这个物体：球壳（半径 radius）和物体包围盒相交判定 ----
            Bounds bounds = t.renderer.bounds;
            float nearest = Mathf.Sqrt(bounds.SqrDistance(center));                                   // 球心到物体最近点
            float farthest = (bounds.center - center).magnitude + bounds.extents.magnitude;           // 球心到物体最远点（近似）
            if (radius >= nearest - _scanWidth && radius <= farthest + _scanWidth)
            {
                t.lastTouchTime = now;
            }

            // ---- 不透明度：被扫到后快速拉到 1，超过保持时间后缓慢回到 0 ----
            float goal = (now - t.lastTouchTime < stayDuration) ? 1f : 0f;
            float speed = goal > t.opacity ? fadeInSpeed : fadeOutSpeed;
            t.opacity = Mathf.MoveTowards(t.opacity, goal, speed * dt);

            t.renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Opacity", t.opacity);
            t.renderer.SetPropertyBlock(_mpb);
        }
    }

    bool IsMatch(GameObject go)
    {
        if (!string.IsNullOrEmpty(targetTag) && !HasTag(go, targetTag)) return false;
        if ((targetLayer.value & (1 << go.layer)) == 0) return false;
        return true;
    }

    static bool HasTag(GameObject go, string tag)
    {
        try { return go.CompareTag(tag); }
        catch { return false; }   // Tag 还没在 Tag Manager 里添加时
    }

    // 用隐形材质生成一份物体专属的材质，并复制原来的贴图 / 颜色
    Material CreateInvisibleMaterial(Renderer r)
    {
        Material source = invisibleMaterial;
        if (source == null)
        {
            var shader = Shader.Find("Unlit/Yingxing");
            if (shader == null)
            {
                Debug.LogWarning("[ScanReveal] 找不到 Unlit/Yingxing，请把 yingxing.mat 拖到 Invisible Material 上", this);
                return null;
            }
            source = new Material(shader);
        }

        var mat = new Material(source);
        var original = r.sharedMaterial;
        if (original != null)
        {
            if (original.HasProperty("_MainTex") && mat.HasProperty("_MainTex"))
            {
                Texture tex = original.GetTexture("_MainTex");
                if (tex != null) mat.SetTexture("_MainTex", tex);
            }
            if (original.HasProperty("_Color") && mat.HasProperty("_BaseColor"))
            {
                Color c = original.GetColor("_Color");
                c.a = 1f;
                mat.SetColor("_BaseColor", c);
            }
        }

        mat.SetFloat("_Opacity", 0f);   // 从隐形开始
        return mat;
    }

    // 自动找场景里用了扫描 shader（Unlit/Saomiao）的球
    Transform FindScanSphere()
    {
        var renderers = FindObjectsOfType<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var mat = renderers[i].sharedMaterial;
            if (mat != null && mat.shader != null && mat.shader.name == "Unlit/Saomiao")
            {
                return renderers[i].transform;
            }
        }
        return null;
    }
}