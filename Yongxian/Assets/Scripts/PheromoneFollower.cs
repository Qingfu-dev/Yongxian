using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 信息素接收者：挂在所有「会被信息素吸引」的物体上。
/// 收到信息素后：
///   1. 记住整条扩散链上最早的那份信息素，持续朝它的释放者靠近（永远优先跟随最早释放者）；
///   2. 同时在自己的位置释放一份信息素，通知周围其他接收者，把扩散链继续往外传。
/// 多个接收者靠近时互相排斥，最终在最早释放者周围堆成球状集合体。
/// 纯 Transform 驱动，不需要 Rigidbody；同一物体上不要再挂其他移动脚本，避免争抢位置。
/// </summary>
[DisallowMultipleComponent]
public class PheromoneFollower : MonoBehaviour
{
    [Header("信息素传播")]
    [Tooltip("自身被激活后释放的信息素能通知到的半径（米），链式扩散靠它一层层向外传")]
    public float pheromoneRadius = 5f;

    [Header("聚集移动")]
    [Tooltip("朝最早释放者靠近的速度（米/秒）")]
    public float moveSpeed = 3f;

    [Tooltip("与最早释放者保持的最近距离（米）：小于该值就不再往里挤，避免全部叠在一个点上")]
    public float stopDistance = 1.5f;

    [Tooltip("进入最近距离外的这段范围时平滑减速（米），越大越平缓，避免高速冲进集合体里抖动")]
    public float slowdownDistance = 4f;

    [Header("互相排斥")]
    [Tooltip("个体之间的排斥距离（米）：靠得比这更近就互相推开，最终自然形成球状集合体")]
    public float separationRadius = 1.5f;

    [Tooltip("排斥强度：越大集合体越松散")]
    public float separationStrength = 2f;

    [Header("转向")]
    [Tooltip("移动时仅绕世界 Y 轴逐渐转向前进方向（保持模型原有的俯仰姿态）")]
    public bool rotateToMoveDirection = true;

    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 360f;

    // 场景中所有接收者的登记表：既用于互相排斥，也用于信息素的范围通知
    static readonly List<PheromoneFollower> registry = new List<PheromoneFollower>();

    Pheromone current; // 当前追随的信息素（即已知最早的那份）；null 表示还没收到过信息素

    void OnEnable()
    {
        registry.Add(this);
    }

    void OnDisable()
    {
        registry.Remove(this);
    }

    /// <summary>收到一份信息素（由释放者调用）。只认更早的那份，避免来回通知成死循环。</summary>
    public void ReceivePheromone(Pheromone pheromone)
    {
        Pheromone root = pheromone.root;

        // 已经有更早（或同样早）的目标时忽略：保证永远优先靠近最早释放信息素的物体
        if (current != null && root.sequence >= current.sequence) return;

        current = root;

        // 控制台输出收到信息素的情况（链式反应时每个个体各打一条）
        string source = root.owner != null ? root.owner.name : "未知";
        Debug.Log($"{name} 收到信息素（最早序号 {root.sequence}），开始朝最早释放者 {source} 聚集", this);

        // 自己被激活的同时也释放一份信息素，把链继续往更远处扩散
        ReleasePheromone(root);
    }

    // 在自身位置释放一份信息素，并通知半径内的其他接收者
    void ReleasePheromone(Pheromone root)
    {
        Pheromone pheromone = new Pheromone(transform, pheromoneRadius, root);
        int count = NotifyNearby(transform.position, pheromoneRadius, pheromone, this);

        // 控制台输出自己向外扩散的情况
        Debug.Log($"{name} 释放信息素，通知到 {count} 个接收者", this);
    }

    /// <summary>把一份信息素通知给 position 周围 radius 内的所有接收者（exclude 为释放者自己），返回通知到的人数。</summary>
    public static int NotifyNearby(Vector3 position, float radius, Pheromone pheromone, PheromoneFollower exclude)
    {
        float sqrRadius = radius * radius;
        int count = 0;

        foreach (PheromoneFollower follower in registry)
        {
            if (follower == exclude) continue;

            // 用平方距离判断，省一次开方
            if ((follower.transform.position - position).sqrMagnitude > sqrRadius) continue;

            follower.ReceivePheromone(pheromone);
            count++;
        }

        return count;
    }

    void Update()
    {
        if (current == null) return;

        // 聚集目标：最早释放信息素者的当前位置（他会移动，集合体也会整体跟着走）
        Transform earliest = current.owner;
        Vector3 targetPosition = earliest != null ? earliest.position : current.origin;

        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;

        Vector3 velocity = Vector3.zero;

        // 1) 朝最早释放者靠近；距离小于 stopDistance 就不再靠近
        float approach = distance - stopDistance;
        if (approach > 0f)
        {
            float speed = moveSpeed;
            // 快贴上去时平滑减速，避免高速冲进集结区后来回抖动
            if (approach < slowdownDistance) speed *= approach / slowdownDistance;
            velocity += toTarget / distance * speed;
        }

        // 2) 与其他个体互相排斥，留出空隙，最终自然铺成球形
        velocity += ComputeSeparation();

        if (velocity.sqrMagnitude < 1e-6f) return;

        Vector3 move = velocity * Time.deltaTime;
        transform.position += move;

        if (rotateToMoveDirection) FaceMoveDirection(move);
    }

    // 与其他接收者互相排斥：离得越近推得越用力，用完整 3D 方向，便于堆成球形
    Vector3 ComputeSeparation()
    {
        Vector3 push = Vector3.zero;
        float sqrRadius = separationRadius * separationRadius;

        foreach (PheromoneFollower other in registry)
        {
            if (other == this) continue;

            Vector3 away = transform.position - other.transform.position;
            float sqrDistance = away.sqrMagnitude;
            if (sqrDistance > sqrRadius || sqrDistance < 1e-8f) continue;

            float dist = Mathf.Sqrt(sqrDistance);
            push += away / dist * (1f - dist / separationRadius); // 刚好到排斥距离时权重为 0
        }

        return push * separationStrength;
    }

    // 仅绕世界 Y 轴增量转向移动方向，不改变模型原有的俯仰/翻滚姿态
    void FaceMoveDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-6f) return;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return;

        float angle = Vector3.SignedAngle(forward, direction, Vector3.up);
        float maxStep = turnSpeed * Time.deltaTime;
        transform.Rotate(0f, Mathf.Clamp(angle, -maxStep, maxStep), 0f, Space.World);
    }

    void OnDrawGizmosSelected()
    {
        // 信息素传播半径
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 1f);
        Gizmos.DrawWireSphere(transform.position, pheromoneRadius);

        // 运行中：画一条线指向当前追随的最早释放者
        if (Application.isPlaying && current != null && current.owner != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, current.owner.position);
        }
    }
}