using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 剧本资产：一个剧情（或一章）的所有对话节点都存在这里，用 ScriptableObject 承载，
/// 可以直接在 Inspector 里可视化编辑。
///
/// 一个节点的结构：
///   id       —— 节点唯一标识，跳转时按它查找
///   speaker  —— 说话人名字，留空表示旁白
///   text     —— 台词内容
///   nextId   —— 没有选项时，点击后跳转到的节点；留空 = 剧情结束
///   choices  —— 有选项时显示按钮，每个选项自带要跳转的 nextId（留空 = 结束）
///
/// 创建方式：Project 右键 → Create → 剧情 → 剧本资产
/// 编辑时会自动做一次校验（空 ID / 重复 ID / 跳转断裂），有问题会在 Console 报警告。
/// </summary>
[CreateAssetMenu(fileName = "NewStory", menuName = "剧情/剧本资产", order = 0)]
public class StoryAsset : ScriptableObject
{
    /// <summary>一个选项：显示的文字 + 选择后跳转到的节点 ID</summary>
    [System.Serializable]
    public class Choice
    {
        [Tooltip("选项按钮上显示的文字")]
        public string text = "选项";

        [Tooltip("选择后跳转到的节点 ID；留空 = 剧情结束")]
        public string nextId;
    }

    /// <summary>一个剧情节点（一段台词 + 可选的选项）</summary>
    [System.Serializable]
    public class Node
    {
        [Tooltip("节点唯一 ID，跳转时靠它查找，例如 start / end")]
        public string id = "start";

        [Tooltip("说话人名字；留空 = 旁白（不显示名字）")]
        public string speaker;

        [TextArea(2, 6)]
        [Tooltip("台词内容")]
        public string text;

        [Tooltip("选项列表；为空 = 普通台词，点一下进入 nextId")]
        public List<Choice> choices = new List<Choice>();

        [Tooltip("没有选项时，点击后跳转到的节点 ID；留空 = 剧情结束")]
        public string nextId;
    }

    [Tooltip("起始节点 ID：Play() 时从这里开始")]
    public string startId = "start";

    [Tooltip("所有剧情节点")]
    public List<Node> nodes = new List<Node>();

    /// <summary>按 ID 找节点；找不到返回 null</summary>
    public Node GetNode(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null && nodes[i].id == id) return nodes[i];
        }
        return null;
    }

    void OnValidate()
    {
        // 新建的空资产不校验，避免一创建就报警告
        if (nodes == null || nodes.Count == 0) return;
        Validate();
    }

    /// <summary>
    /// 校验剧本：空 ID、重复 ID、起始节点无效、跳转目标不存在。
    /// 返回 true 表示全部通过；问题会以警告形式输出到 Console。
    /// </summary>
    public bool Validate()
    {
        bool ok = true;

        // 第一遍：收集所有 ID，找出空 ID / 重复 ID
        HashSet<string> ids = new HashSet<string>();
        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];
            if (node == null)
            {
                Warn($"第 {i + 1} 个节点是空的");
                ok = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.id))
            {
                Warn($"第 {i + 1} 个节点的 ID 为空");
                ok = false;
                continue;
            }

            if (!ids.Add(node.id))
            {
                Warn($"节点 ID 重复：\"{node.id}\"");
                ok = false;
            }
        }

        // 起始节点必须存在
        if (string.IsNullOrWhiteSpace(startId) || !ids.Contains(startId))
        {
            Warn($"起始节点 ID 无效：\"{startId}\"（找不到对应节点）");
            ok = false;
        }

        // 第二遍：检查所有跳转目标
        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];
            if (node == null) continue;

            if (!string.IsNullOrEmpty(node.nextId) && !ids.Contains(node.nextId))
            {
                Warn($"节点 \"{node.id}\" 的 nextId 断裂：\"{node.nextId}\" 不存在");
                ok = false;
            }

            if (node.choices == null) continue;

            for (int c = 0; c < node.choices.Count; c++)
            {
                Choice choice = node.choices[c];
                if (choice == null)
                {
                    Warn($"节点 \"{node.id}\" 的第 {c + 1} 个选项是空的");
                    ok = false;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(choice.text))
                {
                    Warn($"节点 \"{node.id}\" 的第 {c + 1} 个选项文字为空");
                    ok = false;
                }

                if (!string.IsNullOrEmpty(choice.nextId) && !ids.Contains(choice.nextId))
                {
                    Warn($"节点 \"{node.id}\" 的第 {c + 1} 个选项跳转断裂：\"{choice.nextId}\" 不存在");
                    ok = false;
                }
            }
        }

        return ok;
    }

    void Warn(string message)
    {
        Debug.LogWarning($"[StoryAsset] {name}：{message}", this);
    }
}
