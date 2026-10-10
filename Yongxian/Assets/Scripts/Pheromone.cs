using UnityEngine;

/// <summary>
/// 一份被释放出来的信息素。
/// 每份信息素都有全局递增的顺序号（越小表示释放得越早），并记录整条扩散链最早的那份（root）。
/// 所有接收者都以 root 的释放者为聚集目标，所以最终全都聚到最早释放信息素的那个物体身边。
/// </summary>
public class Pheromone
{
    // 全局递增的顺序号：用来判断谁释放得更早
    static int nextSequence;

    public readonly int sequence;    // 释放顺序号，越小越早
    public readonly Transform owner; // 释放者：聚集时向它的当前位置靠近
    public readonly Vector3 origin;  // 释放瞬间的位置（释放者被销毁时作为备用目标点）
    public readonly float radius;    // 这份信息素的传播半径
    public readonly Pheromone root;  // 整条链上最早的那份信息素；自己就是最早时指向自己

    /// <param name="owner">释放者</param>
    /// <param name="radius">传播半径</param>
    /// <param name="root">上一层的 root；传 null 表示自己就是最早的那份</param>
    public Pheromone(Transform owner, float radius, Pheromone root = null)
    {
        sequence = nextSequence++;
        this.owner = owner;
        this.radius = radius;
        origin = owner != null ? owner.position : Vector3.zero;
        this.root = root != null ? root : this;
    }
}