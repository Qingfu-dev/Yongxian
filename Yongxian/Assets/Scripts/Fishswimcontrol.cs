using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//默认注释

public class Fishswimcontrol : MonoBehaviour
{
    [Header("移动目标")]
    [Tooltip("鱼会朝这个 Transform 的位置移动")]
    public Transform target;

    [Header("移动参数")]
    [Tooltip("加速度（米/秒²）：按住 W 时每秒增加多少速度")]
    public float acceleration = 6f;

    [Tooltip("水阻力（1/秒）：越大减速越快，松开 W 后滑行越短；平衡速度 ≈ 加速度 ÷ 阻力")]
    public float waterDrag = 1.2f;

    [Header("活动范围（球形）")]
    [Tooltip("活动中心：留空 = 游戏开始时鱼所在的位置")]
    public Transform rangeCenter;

    [Tooltip("活动半径（米）：超出就会以活动中心为球心把鱼挡回球内；0 = 不限制")]
    public float rangeRadius = 10f;

    [Header("上下浮潜（空格 / Shift）")]
    [Tooltip("上浮键")]
    public KeyCode ascendKey = KeyCode.Space;

    [Tooltip("下潜键")]
    public KeyCode descendKey = KeyCode.LeftShift;

    [Tooltip("上浮 / 下潜速度（米/秒）")]
    public float verticalSpeed = 2.5f;

    [Header("转向（A / D）")]
    [Tooltip("左转键")]
    public KeyCode turnLeftKey = KeyCode.A;

    [Tooltip("右转键")]
    public KeyCode turnRightKey = KeyCode.D;

    [Tooltip("按住多久达到最大转向力度（秒）：1 秒 = 力度从 0 涨到满")]
    public float turnRampTime = 1f;

    [Tooltip("最大转向角速度（度/秒）：力度拉满时每秒转多少度")]
    public float maxTurnSpeed = 15f;

    [Header("头朝向（看向目标点）")]
    [Tooltip("头部位置的 Transform：用「头 → 目标点」的方向决定网格体朝向")]
    public Transform headTransform;

    [Tooltip("要旋转的网格体；留空 = 本物体。注意：Target 不要挂在这个物体的下级，否则头会转不停")]
    public Transform bodyTransform;

    [Tooltip("头朝向的转动速度（度/秒），越大转得越快")]
    public float headTurnSpeed = 180f;

    // 当前游动速度（米/秒）
    private Vector3 _velocity;

    // 当前转向力度：0 = 没转，-1 = 左满，+1 = 右满
    private float _turnInput;

    // 未指定活动中心时用的圆心（游戏开始时记录）
    private Vector3 _homeCenter;

    void Start()
    {
        // 记录初始位置：没有手动指定活动中心时，圆以这里为圆心
        _homeCenter = transform.position;

        Transform body = bodyTransform != null ? bodyTransform : transform;

        // 提示：Target 若挂在被旋转的网格体下面，头会绕着点转个不停（永远追不上）
        if (target != null && target.IsChildOf(body))
        {
            Debug.LogWarning("Fishswimcontrol：Target 是被头朝向旋转的网格体的子物体，头会一直转。请把 Target 移到不被旋转的层级（比如挂到它的父级）。", this);
        }

        // 提示：Head 必须是网格体的子物体，否则旋转网格体不会带动头，会一直转
        if (headTransform != null && !headTransform.IsChildOf(body))
        {
            Debug.LogWarning("Fishswimcontrol：Head Transform 不是被旋转网格体的子物体，旋转不会改变头的位置，头会一直转。请把 Head 挂到网格体下级。", this);
        }
    }

    void Update()
    {
        Move();
        VerticalSwim();
        Turn();
        LookAtTarget();
        ClampToRange();
    }

    // 按住 W：朝目标方向加速；水阻力持续衰减速度（松开后滑行一段再停）
    void Move()
    {
        if (target != null && Input.GetKey(KeyCode.W))
        {
            Vector3 direction = (target.position - transform.position).normalized;
            _velocity += direction * acceleration * Time.deltaTime;
        }

        // 水阻力：速度按指数衰减
        _velocity *= Mathf.Exp(-waterDrag * Time.deltaTime);

        transform.position += _velocity * Time.deltaTime;
    }

    // 空格 / Shift：上浮 / 下潜（垂直方向直接位移，不参与水阻力滑行）
    void VerticalSwim()
    {
        float vertical = 0f;
        if (Input.GetKey(ascendKey)) vertical += 1f;
        if (Input.GetKey(descendKey)) vertical -= 1f;

        transform.position += Vector3.up * (vertical * verticalSpeed * Time.deltaTime);
    }

    // 按住 A / D：转向力度随时间增长，角速度 = 当前力度 × 最大角速度
    void Turn()
    {
        float rawInput = 0f;
        if (Input.GetKey(turnLeftKey)) rawInput -= 1f;
        if (Input.GetKey(turnRightKey)) rawInput += 1f;

        // 力度按 turnRampTime 逼近目标值：按住约 1 秒到满；松开后同样速率回中
        _turnInput = Mathf.MoveTowards(_turnInput, rawInput, Time.deltaTime / turnRampTime);

        // 以当前力度对应的角速度绕 Y 轴旋转（度/秒）
        transform.Rotate(0f, _turnInput * maxTurnSpeed * Time.deltaTime, 0f, Space.World);
    }

    // 网格体的头朝向目标点：只绕世界 Y 轴微调角度，保留模型原有的仰俯/侧倾姿态
    void LookAtTarget()
    {
        if (headTransform == null || target == null)
        {
            return;
        }

        // 手动转向优先：按住 A/D 时不自动回正
        if (Input.GetKey(turnLeftKey) || Input.GetKey(turnRightKey))
        {
            return;
        }

        Transform body = bodyTransform != null ? bodyTransform : transform;

        // 身体当前的前方 =「身体 → 头」的水平方向（不依赖模型的前向轴）
        Vector3 bodyForward = headTransform.position - body.position;
        bodyForward.y = 0f;

        // 期望方向 =「头 → 目标点」的水平方向
        Vector3 toTarget = target.position - headTransform.position;
        toTarget.y = 0f;

        if (bodyForward.sqrMagnitude < 0.001f || toTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        // 算出还差多少度，按 headTurnSpeed 限速，只绕世界 Y 轴施加旋转（不动模型的仰俯/侧倾）
        float yawDelta = Vector3.SignedAngle(bodyForward, toTarget, Vector3.up);
        float step = Mathf.MoveTowards(0f, yawDelta, headTurnSpeed * Time.deltaTime);
        body.Rotate(0f, step, 0f, Space.World);
    }

    // 球形活动范围：超出就把鱼挡回球面上，并消掉速度里朝外的分量
    void ClampToRange()
    {
        if (rangeRadius <= 0f) return;

        Vector3 center = rangeCenter != null ? rangeCenter.position : _homeCenter;

        Vector3 offset = transform.position - center;   // 到球心的距离（含上下方向）
        float dist = offset.magnitude;
        if (dist <= rangeRadius) return;

        Vector3 dir = offset / dist;   // 球心 → 鱼的径向

        // 位置贴回球面
        transform.position = center + dir * rangeRadius;

        // 消掉朝外的速度分量：贴着球面滑动，而不是顶着边界抖动
        float outward = Vector3.Dot(_velocity, dir);
        if (outward > 0f) _velocity -= dir * outward;
    }

    // 在 Scene 视图选中鱼时画出球形活动范围，方便调半径
    void OnDrawGizmosSelected()
    {
        if (rangeRadius <= 0f) return;

        Vector3 center = rangeCenter != null
            ? rangeCenter.position
            : (Application.isPlaying ? _homeCenter : transform.position);

        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.9f);
        Gizmos.DrawWireSphere(center, rangeRadius);
    }
}