using UnityEngine;

/// <summary>
/// 冲刺体力条 HUD：运行时自动创建，不需要手动搭 UI，默认显示在屏幕左下角。
/// 挂在任意物体上即可（不用挂在菌子上），会自动在场景里找 MushroomSwimController 读取体力。
///
/// 说明：
///   - 纯显示 UI，所以不建 GraphicRaycaster、Image 也关掉 Raycast Target，不参与点击检测
///   - 想换成自己的界面时，删掉本脚本即可；控制器上的 StaminaRatio / IsExhausted 可以直接给别的 UI 用
///   - 体力充足显示 normalColor，低于 lowThreshold 或已耗尽显示 lowColor
/// </summary>
[DisallowMultipleComponent]
public class StaminaBarUI : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("要显示体力的控制器；留空会自动在场景里找")]
    public MushroomSwimController controller;

    [Header("外观")]
    [Tooltip("体力条尺寸（像素，基于 1920×1080 参考分辨率）")]
    public Vector2 barSize = new Vector2(260f, 18f);

    [Tooltip("距离屏幕左下角的边距（像素）")]
    public Vector2 screenMargin = new Vector2(40f, 40f);

    [Tooltip("体力条底色")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);

    [Tooltip("体力充足时的填充色")]
    public Color normalColor = new Color(0.35f, 0.85f, 0.95f, 0.95f);

    [Tooltip("体力过低 / 耗尽时的填充色")]
    public Color lowColor = new Color(1f, 0.55f, 0.2f, 0.95f);

    [Tooltip("低于这个比例就显示低体力颜色")]
    [Range(0f, 1f)] public float lowThreshold = 0.25f;

    [Tooltip("体力条在屏幕上的层级，越大越靠前")]
    public int sortingOrder = 100;

    GameObject _canvasGO;
    UnityEngine.UI.Image _fill;
    float _displayed = 1f;

    void Awake()
    {
        if (controller == null) controller = FindObjectOfType<MushroomSwimController>();
        if (controller == null)
        {
            Debug.LogWarning("[StaminaBarUI] 场景里没有找到 MushroomSwimController，体力条不会显示", this);
            return;
        }

        BuildUI();
    }

    void OnDestroy()
    {
        // Canvas 是独立于本物体的根对象，跟着一起销毁，避免残留
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    void BuildUI()
    {
        // ---- Canvas：Screen Space - Overlay + 按屏幕缩放 ----
        _canvasGO = new GameObject("StaminaBarCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        var canvas = _canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = _canvasGO.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // ---- 背景条：锚定屏幕左下角 ----
        var backgroundGO = new GameObject("Background", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        backgroundGO.transform.SetParent(_canvasGO.transform, false);

        var backgroundRect = backgroundGO.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.zero;
        backgroundRect.pivot = Vector2.zero;             // 从左下角往外长，边距就是屏幕距离
        backgroundRect.anchoredPosition = screenMargin;
        backgroundRect.sizeDelta = barSize;

        var backgroundImage = backgroundGO.GetComponent<UnityEngine.UI.Image>();
        backgroundImage.color = backgroundColor;
        backgroundImage.raycastTarget = false;

        // ---- 填充条：贴着背景内部，靠 offsetMax.x 控制显示比例（不会出现负尺寸）----
        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        fillGO.transform.SetParent(backgroundGO.transform, false);

        var fillRect = fillGO.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);        // 留 2px 内边距
        fillRect.offsetMax = new Vector2(-2f, -2f);

        _fill = fillGO.GetComponent<UnityEngine.UI.Image>();
        _fill.color = normalColor;
        _fill.raycastTarget = false;
    }

    void Update()
    {
        if (_fill == null || controller == null) return;

        float target = Mathf.Clamp01(controller.StaminaRatio);
        _displayed = Mathf.Lerp(_displayed, target, 1f - Mathf.Exp(-12f * Time.deltaTime));

        // 右边缘往里收：(1 - 显示比例) 的部分全部收掉
        float innerWidth = Mathf.Max(barSize.x - 4f, 1f);
        _fill.rectTransform.offsetMax = new Vector2(-2f - innerWidth * (1f - _displayed), -2f);

        _fill.color = (controller.IsExhausted || target <= lowThreshold) ? lowColor : normalColor;
    }
}