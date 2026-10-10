using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 剧情对话 UI：运行时自动生成界面（底部半透明对话框 + 说话人 + 台词 + 选项按钮），
/// 不需要手动搭 Canvas，也不需要手动摆控件。
///   台词 = 打字机逐字显示；点屏幕/空格 = 推进（正在打字时先显示完这一句）
///   有选项时在屏幕中间生成按钮，点按钮完成选择
/// 一般和 StoryPlayer 挂在同一个物体上（或场景里任意常驻物体上），由 StoryPlayer 驱动。
/// 注意：中文需要带中文字形的字体 —— 把字体拖到 font 字段；留空则用内置字体（中文可能显示成方块）。
/// </summary>
[DisallowMultipleComponent]
public class StoryUI : MonoBehaviour
{
    [Header("外观")]
    [Tooltip("对话框背景色")]
    public Color panelColor = new Color(0f, 0f, 0f, 0.75f);

    [Tooltip("台词文字颜色")]
    public Color textColor = Color.white;

    [Tooltip("说话人文字颜色")]
    public Color speakerColor = new Color(1f, 0.9f, 0.5f, 1f);

    [Tooltip("对话框高度占屏幕的比例（0~1）")]
    [Range(0.1f, 0.6f)] public float panelHeightRatio = 0.3f;

    [Tooltip("对话框左右留白占屏幕宽度的比例（0~0.3）")]
    [Range(0f, 0.3f)] public float sideMarginRatio = 0.06f;

    [Tooltip("字体；留空则用内置字体（中文需要指定中文字体）")]
    public Font font;

    [Tooltip("台词字号")]
    public int dialogueFontSize = 30;

    [Tooltip("说话人字号")]
    public int speakerFontSize = 26;

    [Header("打字机")]
    [Tooltip("每秒显示的字数；<= 0 = 瞬间显示整句")]
    public float charactersPerSecond = 30f;

    [Header("选项")]
    [Tooltip("选项按钮高度（像素）")]
    public float choiceButtonHeight = 64f;

    [Tooltip("选项按钮之间的间距（像素）")]
    public float choiceSpacing = 8f;

    [Tooltip("选项按钮宽度（像素）")]
    public float choiceButtonWidth = 720f;

    [Tooltip("选项按钮背景色")]
    public Color choiceColor = new Color(0.12f, 0.12f, 0.16f, 0.92f);

    [Tooltip("选项按钮悬停色")]
    public Color choiceHoverColor = new Color(0.25f, 0.45f, 0.75f, 1f);

    /// <summary>玩家点击屏幕/按空格推进时触发</summary>
    public event Action OnAdvanceClicked;

    /// <summary>玩家选择选项时触发，参数是选项下标</summary>
    public event Action<int> OnChoiceSelected;

    /// <summary>是否正在逐字显示</summary>
    public bool IsTyping { get; private set; }

    Canvas _canvas;
    Text _speakerText;
    Text _dialogueText;
    RectTransform _choiceRoot;
    readonly List<GameObject> _choiceButtons = new List<GameObject>();

    StoryAsset.Node _node;
    Coroutine _typing;
    string _fullText = "";
    bool _visible;

    void Awake()
    {
        Build();
        Hide();
    }

    void OnDestroy()
    {
        // Canvas 是独立根对象，跟着销毁避免残留
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    void Update()
    {
        if (!_visible) return;

        // 空格 / 回车 也能推进
        if (Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnAdvanceClicked?.Invoke();
        }
    }

    /// <summary>显示一个节点：更新说话人与台词，并开始打字机效果</summary>
    public void ShowNode(StoryAsset.Node node)
    {
        if (node == null) return;

        _node = node;

        if (_canvas != null) _canvas.gameObject.SetActive(true);
        _visible = true;

        bool hasSpeaker = !string.IsNullOrEmpty(node.speaker);
        _speakerText.text = hasSpeaker ? node.speaker : "";
        _speakerText.gameObject.SetActive(hasSpeaker);

        _fullText = node.text ?? "";
        if (_typing != null) StopCoroutine(_typing);
        _typing = StartCoroutine(TypeRoutine(_fullText));
    }

    /// <summary>把当前这句立刻显示完（跳过打字机）</summary>
    public void CompleteTyping()
    {
        if (_typing != null)
        {
            StopCoroutine(_typing);
            _typing = null;
        }

        IsTyping = false;
        _dialogueText.text = _fullText;
        BuildChoices();
    }

    /// <summary>隐藏整个对话界面</summary>
    public void Hide()
    {
        _visible = false;
        IsTyping = false;
        _node = null;

        if (_typing != null)
        {
            StopCoroutine(_typing);
            _typing = null;
        }

        ClearChoices();
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    // 逐字显示；打完后才生成选项按钮
    IEnumerator TypeRoutine(string fullText)
    {
        IsTyping = true;
        ClearChoices();
        _dialogueText.text = "";

        if (charactersPerSecond > 0f && fullText.Length > 0)
        {
            float shown = 0f;
            while (shown < fullText.Length)
            {
                shown += charactersPerSecond * Time.deltaTime;
                int count = Mathf.Min(fullText.Length, Mathf.FloorToInt(shown));
                _dialogueText.text = fullText.Substring(0, count);
                yield return null;
            }
        }

        _dialogueText.text = fullText;
        _typing = null;
        IsTyping = false;
        BuildChoices();
    }

    // ---------- 构建界面 ----------

    void Build()
    {
        EnsureEventSystem();

        // 全屏 Canvas
        GameObject canvasGO = new GameObject("StoryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        _canvas = canvasGO.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 900;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildClickArea(canvasGO.transform);
        BuildPanel(canvasGO.transform);
        BuildChoiceRoot(canvasGO.transform);
    }

    // 全屏透明按钮：点屏幕任意位置 = 推进（选项按钮在它上面，会优先吃掉点击）
    void BuildClickArea(Transform parent)
    {
        GameObject go = new GameObject("ClickArea", typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);

        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        go.GetComponent<Button>().onClick.AddListener(() => OnAdvanceClicked?.Invoke());
    }

    // 底部对话框：背景 + 说话人 + 台词
    void BuildPanel(Transform parent)
    {
        GameObject panelGO = new GameObject("DialoguePanel", typeof(Image));
        panelGO.transform.SetParent(parent, false);
        panelGO.GetComponent<Image>().color = panelColor;

        RectTransform rect = panelGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(sideMarginRatio, 0.04f);
        rect.anchorMax = new Vector2(1f - sideMarginRatio, 0.04f + panelHeightRatio);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _speakerText = CreateText(panelGO.transform, "Speaker", speakerFontSize, speakerColor, TextAnchor.MiddleLeft);
        RectTransform srt = _speakerText.rectTransform;
        srt.anchorMin = new Vector2(0.02f, 0.62f);
        srt.anchorMax = new Vector2(0.98f, 1f);
        srt.offsetMin = new Vector2(0f, 0f);
        srt.offsetMax = new Vector2(0f, -6f);

        _dialogueText = CreateText(panelGO.transform, "Dialogue", dialogueFontSize, textColor, TextAnchor.UpperLeft);
        RectTransform drt = _dialogueText.rectTransform;
        drt.anchorMin = new Vector2(0.02f, 0.06f);
        drt.anchorMax = new Vector2(0.98f, 0.62f);
        drt.offsetMin = new Vector2(0f, 0f);
        drt.offsetMax = new Vector2(0f, 0f);
    }

    // 屏幕中间的选项容器（竖向排列，自动撑高）
    void BuildChoiceRoot(Transform parent)
    {
        GameObject go = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);

        _choiceRoot = go.GetComponent<RectTransform>();
        _choiceRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _choiceRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _choiceRoot.pivot = new Vector2(0.5f, 0.5f);
        _choiceRoot.sizeDelta = new Vector2(choiceButtonWidth, 0f);

        VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = choiceSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    Text CreateText(Transform parent, string name, int size, Color color, TextAnchor alignment)
    {
        GameObject go = new GameObject(name, typeof(Text));
        go.transform.SetParent(parent, false);

        Text text = go.GetComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    Font ResolveFont()
    {
        if (font != null) return font;

        // 内置字体兜底：只保证能显示，中文字形缺失时会显示成方块
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // ---------- 选项 ----------

    void BuildChoices()
    {
        ClearChoices();

        if (_node == null || _node.choices == null || _node.choices.Count == 0) return;

        for (int i = 0; i < _node.choices.Count; i++)
        {
            _choiceButtons.Add(CreateChoiceButton(_node.choices[i].text, i));
        }
    }

    GameObject CreateChoiceButton(string label, int index)
    {
        GameObject go = new GameObject($"Choice{index}", typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(_choiceRoot, false);

        Image image = go.GetComponent<Image>();
        image.color = choiceColor;

        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = choiceHoverColor;
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            ClearChoices();                 // 先收掉按钮，避免连点
            OnChoiceSelected?.Invoke(index);
        });

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.preferredHeight = choiceButtonHeight;
        element.preferredWidth = choiceButtonWidth;

        Text text = CreateText(go.transform, "Text", dialogueFontSize, textColor, TextAnchor.MiddleCenter);
        RectTransform trt = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12f, 4f);
        trt.offsetMax = new Vector2(-12f, -4f);
        text.text = label ?? "";

        return go;
    }

    void ClearChoices()
    {
        for (int i = 0; i < _choiceButtons.Count; i++)
        {
            if (_choiceButtons[i] != null) Destroy(_choiceButtons[i]);
        }
        _choiceButtons.Clear();
    }

    void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;

        GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.transform.SetParent(transform, false);
    }
}
