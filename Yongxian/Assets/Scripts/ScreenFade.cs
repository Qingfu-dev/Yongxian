using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 屏幕黑幕渐隐特效：触发时立刻铺满一层黑色，然后在 fadeDuration 秒内把透明度慢慢减到 0。
/// 一般挂在主相机上；运行时会自动创建覆盖全屏的黑幕（Screen Space Overlay），不需要手动搭 Canvas。
/// 其他脚本调用 Play() 触发，比如鱼越界回到原点时，用黑幕遮住瞬移过程。
/// </summary>
[DisallowMultipleComponent]
public class ScreenFade : MonoBehaviour
{
    [Header("渐隐参数")]
    [Tooltip("从全黑减到完全透明的时间（秒）")]
    public float fadeDuration = 1.2f;

    [Tooltip("透明度曲线：横轴 0~1 是渐隐进度，纵轴是黑幕透明度（1 = 全黑，0 = 全透明）")]
    public AnimationCurve alphaCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    private Image _overlay;
    private GameObject _canvasGO;
    private float _timer;
    private bool _playing;

    void Awake()
    {
        CreateOverlay();
    }

    void OnDestroy()
    {
        // Canvas 是独立于本物体的根对象，跟着一起销毁，避免残留
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    void Update()
    {
        if (!_playing) return;

        _timer += Time.deltaTime;

        float progress = fadeDuration > 0f ? Mathf.Clamp01(_timer / fadeDuration) : 1f;
        SetAlpha(alphaCurve.Evaluate(progress));

        if (progress >= 1f) _playing = false;
    }

    /// <summary>触发一次黑幕渐隐：立刻变成全黑，再慢慢透明到 0</summary>
    public void Play()
    {
        if (_overlay == null) CreateOverlay();

        _timer = 0f;
        _playing = true;
        SetAlpha(1f);   // 立刻铺满黑色
    }

    // 运行时创建全屏黑幕：Canvas（最上层）+ 拉满屏幕的黑色 Image
    void CreateOverlay()
    {
        _canvasGO = new GameObject("ScreenFadeCanvas");
        Canvas canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;   // 盖在普通 UI 之上

        GameObject imageObject = new GameObject("Black");
        imageObject.transform.SetParent(_canvasGO.transform, false);

        _overlay = imageObject.AddComponent<Image>();
        _overlay.raycastTarget = false;   // 不挡鼠标交互

        // 四个锚点拉满全屏
        RectTransform rect = _overlay.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        SetAlpha(0f);
    }

    void SetAlpha(float alpha)
    {
        if (_overlay == null) return;

        Color color = _overlay.color;
        color.a = Mathf.Clamp01(alpha);
        _overlay.color = color;
    }
}