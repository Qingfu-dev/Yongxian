using System;
using UnityEngine;

/// <summary>
/// 剧情播放核心：只负责"播放哪一段、下一步去哪"，不碰任何 UI 细节。
/// 显示交给 StoryUI，本脚本通过它的事件接收"点击推进 / 选择选项"。
///
/// 用法：
///   1. 把本脚本挂到场景里任意常驻物体上
///   2. 把做好的剧本资产（StoryAsset）拖到 story 字段
///   3. 场景里再挂一个 StoryUI（留空会自动找），运行时就能对话了
///
/// 外部也可以直接调用 Play() / Advance() / Choose(index) / Stop() 来驱动剧情，
/// 并订阅 OnNodeChanged、OnStoryFinished 事件做额外处理（比如触发演出、存档）。
/// </summary>
[DisallowMultipleComponent]
public class StoryPlayer : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("要播放的剧本资产")]
    public StoryAsset story;

    [Tooltip("负责显示的对话 UI；留空则自动找场景里的 StoryUI")]
    public StoryUI ui;

    [Header("播放")]
    [Tooltip("运行时自动从头播放")]
    public bool playOnStart = true;

    /// <summary>切换到新节点时触发（参数是当前节点）</summary>
    public event Action<StoryAsset.Node> OnNodeChanged;

    /// <summary>剧情走到结尾时触发一次</summary>
    public event Action OnStoryFinished;

    /// <summary>是否正在播放剧情</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>当前节点；没在播放时为 null</summary>
    public StoryAsset.Node CurrentNode { get; private set; }

    void Awake()
    {
        if (ui == null) ui = FindObjectOfType<StoryUI>();

        if (ui != null)
        {
            ui.OnAdvanceClicked += Advance;
            ui.OnChoiceSelected += Choose;
        }
    }

    void OnDestroy()
    {
        if (ui != null)
        {
            ui.OnAdvanceClicked -= Advance;
            ui.OnChoiceSelected -= Choose;
        }
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>从剧本的起始节点开始播放</summary>
    public void Play()
    {
        if (story == null)
        {
            Debug.LogWarning("[StoryPlayer] 没有指定剧本资产", this);
            return;
        }

        Play(story.startId);
    }

    /// <summary>从指定节点开始播放</summary>
    public void Play(string nodeId)
    {
        if (story == null)
        {
            Debug.LogWarning("[StoryPlayer] 没有指定剧本资产", this);
            return;
        }

        IsPlaying = true;
        GoTo(nodeId);
    }

    /// <summary>
    /// 推进剧情：
    ///   正在逐字显示 → 先把当前这句瞬间显示完；
    ///   有选项        → 不推进（等玩家点选项）；
    ///   有 nextId     → 跳下一节点；否则剧情结束。
    /// </summary>
    public void Advance()
    {
        if (!IsPlaying || CurrentNode == null) return;

        if (ui != null && ui.IsTyping)
        {
            ui.CompleteTyping();
            return;
        }

        if (CurrentNode.choices != null && CurrentNode.choices.Count > 0) return;

        if (string.IsNullOrEmpty(CurrentNode.nextId)) Finish();
        else GoTo(CurrentNode.nextId);
    }

    /// <summary>选择当前节点的第 index 个选项</summary>
    public void Choose(int index)
    {
        if (!IsPlaying || CurrentNode == null) return;
        if (CurrentNode.choices == null || index < 0 || index >= CurrentNode.choices.Count) return;

        string nextId = CurrentNode.choices[index].nextId;
        if (string.IsNullOrEmpty(nextId)) Finish();
        else GoTo(nextId);
    }

    /// <summary>提前结束剧情并隐藏 UI</summary>
    public void Stop()
    {
        IsPlaying = false;
        CurrentNode = null;
        if (ui != null) ui.Hide();
    }

    // 跳到指定节点：找不到就当剧情结束
    void GoTo(string nodeId)
    {
        StoryAsset.Node node = story.GetNode(nodeId);
        if (node == null)
        {
            Debug.LogWarning($"[StoryPlayer] 找不到节点 \"{nodeId}\"，剧情结束", this);
            Finish();
            return;
        }

        CurrentNode = node;
        if (ui != null) ui.ShowNode(node);
        OnNodeChanged?.Invoke(node);
    }

    void Finish()
    {
        IsPlaying = false;
        CurrentNode = null;
        if (ui != null) ui.Hide();
        OnStoryFinished?.Invoke();
    }
}
