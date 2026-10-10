using UnityEngine;

/// <summary>
/// 信息素释放控制器：挂在主角上。
/// 按下鼠标右键时：
///   1. 在自身位置释放一份信息素，它就是整条扩散链里最早的那份；
///   2. 通知周围一定范围内所有带 PheromoneFollower 的物体：
///      它们会朝自己聚集，并各自再释放信息素向外扩散，形成链式反应。
/// 所有个体永远优先靠近最早释放信息素的物体（也就是主角），最终聚集成球状集合体。
/// </summary>
[DisallowMultipleComponent]
public class PheromoneController : MonoBehaviour
{
    [Header("信息素扩散范围")]
    [Tooltip("按下右键释放信息素时，能直接通知到的周围半径（米）")]
    public float pheromoneRadius = 5f;

    void Update()
    {
        // GetMouseButtonDown：只在按下的那一帧触发一次，按住不会连续触发
        // 参数 1 = 鼠标右键（0 是左键，2 是中键）
        if (Input.GetMouseButtonDown(1))
        {
            ReleasePheromone();
        }
    }

    /// <summary>在自身位置释放一份信息素，并通知范围内的接收者（也可从代码里主动调用）</summary>
    public void ReleasePheromone()
    {
        Pheromone pheromone = new Pheromone(transform, pheromoneRadius);
        int count = PheromoneFollower.NotifyNearby(transform.position, pheromoneRadius, pheromone, null);
        Debug.Log($"释放信息素（传播半径 {pheromoneRadius} 米），直接通知 {count} 个接收者", this);
    }

    // 在 Scene 视图选中主角时画出传播范围，方便调参
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, pheromoneRadius);
    }
}