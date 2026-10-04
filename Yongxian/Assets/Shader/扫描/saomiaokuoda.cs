using UnityEngine;

/// <summary>
/// 扫描球扩大：挂在挂了扫描材质（Unlit/Saomiao）的球上，
/// 让球随时间从 0 放大到最大半径，球扫过物体时就会扫出轮廓线。
/// </summary>
public class saomiaokuoda : MonoBehaviour
{
    [Tooltip("最大半径（世界单位），球缩放 = 半径 × 2")]
    public float maxRadius = 10f;

    [Tooltip("从 0 放大到最大半径需要的时间（秒）")]
    public float duration = 2f;

    [Tooltip("是否循环放大；关闭则只放一次，停在最大半径")]
    public bool loop = true;

    float _time;

    void OnEnable()
    {
        EnableCameraDepthTexture();
        _time = 0f;
        ApplyRadius(0f);
    }

    void Start()
    {
        EnableCameraDepthTexture();   // 兜底再设一次，确保开始渲染前深度纹理已开启
    }

    void Update()
    {
        _time += Time.deltaTime;

        float phase;
        if (loop)
            phase = duration > 0f ? Mathf.Repeat(_time, duration) / duration : 1f;  // 循环：到最大后立刻重新开始
        else
            phase = duration > 0f ? Mathf.Clamp01(_time / duration) : 1f;           // 单次：停在最大半径

        ApplyRadius(maxRadius * phase);
    }

    void ApplyRadius(float radius)
    {
        // Sphere 网格半径是 0.5，所以缩放 = 世界半径 × 2
        transform.localScale = Vector3.one * (radius * 2f);
    }

    // 内置管线需要相机开启深度纹理，扫描线才能显示
    void EnableCameraDepthTexture()
    {
        var cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i].depthTextureMode |= DepthTextureMode.Depth;
        }

        // 兜底：万一前面这一下没拿到相机
        if (Camera.main != null)
        {
            Camera.main.depthTextureMode |= DepthTextureMode.Depth;
        }
    }
}