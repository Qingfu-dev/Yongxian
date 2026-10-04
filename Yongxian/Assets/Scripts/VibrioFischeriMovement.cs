using UnityEngine;

/// <summary>
/// 费氏弧菌式运动：模拟细菌在水中典型的 run-and-tumble（游动—翻滚）随机游走。
///   游动（Run）：鞭毛束推进，近似直线前进一小段；
///   翻滚（Tumble）：原地快速随机换向，然后进入下一段游动；
///   趋化性（Chemotaxis）：朝诱引物（光/营养源）方向游动时延长游动时间、
///     远离时缩短游动时间，宏观上形成朝向目标的净漂移；
///   布朗扰动：旋转扩散 + 微小位置抖动，模拟水体分子热运动；
///   鞭毛摆动：轻微的抬头摆尾，让轨迹更自然。
/// 纯 Transform 驱动，不需要 Rigidbody，直接挂在细菌物体上即可。
/// </summary>
[DisallowMultipleComponent]
public class VibrioFischeriMovement : MonoBehaviour
{
    enum SwimState { Run, Tumble }

    [Header("游动（Run）")]
    [Tooltip("游动速度（单位/秒）。真实费氏弧菌约 30~60 微米/秒，按场景比例缩放")]
    public float swimSpeed = 1.2f;

    [Tooltip("速度波动幅度（0~1），模拟鞭毛推进力的不均匀")]
    [Range(0f, 1f)] public float speedFluctuation = 0.15f;

    [Tooltip("单次游动持续时间范围（秒）")]
    public Vector2 runDuration = new Vector2(0.6f, 1.6f);

    [Header("翻滚（Tumble）")]
    [Tooltip("翻滚持续时间范围（秒），翻滚时减速并快速转向")]
    public Vector2 tumbleDuration = new Vector2(0.1f, 0.3f);

    [Tooltip("翻滚转向速度（度/秒）")]
    public float tumbleTurnSpeed = 480f;

    [Tooltip("翻滚时速度衰减到游动速度的比例")]
    [Range(0f, 1f)] public float tumbleSpeedFactor = 0.25f;

    [Tooltip("翻滚后的平均转向角（度）。费氏弧菌典型约 60~90 度")]
    public float meanTurnAngle = 75f;

    [Tooltip("转向角的随机浮动范围（度）")]
    public float turnAngleJitter = 40f;

    [Header("趋化性（Chemotaxis）")]
    [Tooltip("诱引物目标（光/营养源等），留空则为纯随机行走")]
    public Transform attractant;

    [Tooltip("趋化强度 0~1：越大，朝目标方向游动的时长加成越明显")]
    [Range(0f, 1f)] public float chemotaxisStrength = 0.7f;

    [Header("布朗扰动")]
    [Tooltip("旋转扩散（度/秒）：水体热运动造成的方向随机漂移")]
    public float rotationalDiffusion = 25f;

    [Tooltip("位置抖动幅度（单位/秒）：布朗运动的微小位移")]
    public float brownianJitter = 0.05f;

    [Tooltip("布朗噪声频率：越高扰动越急促")]
    public float brownianFrequency = 1.5f;

    [Header("鞭毛摆动")]
    [Tooltip("摆动幅度（度）：鞭毛推进造成的抬头摆尾")]
    public float wobbleAngle = 8f;

    [Tooltip("摆动频率（次/秒）")]
    public float wobbleFrequency = 5f;

    [Header("活动范围")]
    [Tooltip("活动中心，留空则用启用时的位置")]
    public Transform wanderCenter;

    [Tooltip("活动半径，0 表示不限制；越界后会被柔和地引导回来")]
    public float wanderRadius = 5f;

    [Tooltip("越界回拉强度 0~1")]
    [Range(0f, 1f)] public float boundarySteerStrength = 0.9f;

    [Header("其他")]
    [Tooltip("锁定在水平面内游动（贴近水面/表面的细菌）")]
    public bool planarMovement = false;

    SwimState _state;
    float _stateTimer;       // 当前状态剩余时间
    Vector3 _heading;        // 生物学前进方向（不含摆动）
    Vector3 _tumbleAxis;     // 本次翻滚的旋转轴
    float _turnRemaining;    // 本次翻滚剩余转角（度）
    Vector3 _homeCenter;     // 未指定 wanderCenter 时的活动中心
    float _noiseOffset;      // 每实例独立的噪声偏移，避免所有个体同步
    float _wobblePhase;
    float _speedOffset;

    void OnEnable()
    {
        _homeCenter = transform.position;
        _noiseOffset = Random.value * 100f;
        _wobblePhase = Random.value * Mathf.PI * 2f;
        _speedOffset = Random.value * 100f;

        // 初始朝向随机
        _heading = Random.onUnitSphere;
        if (planarMovement) _heading = Flatten(_heading);

        EnterRun();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 noise = SampleNoise();

        UpdateSwimState(dt);
        ApplyRotationalDiffusion(noise, dt);
        ApplyBoundarySteering(dt);
        if (planarMovement) _heading = Flatten(_heading);

        // 视觉方向 = 前进方向 + 鞭毛摆动（摆动只影响表现，不污染航行方向）
        Vector3 dir = ApplyWobble(_heading);

        // 速度带缓慢波动，翻滚时大幅减速
        float speed = swimSpeed * (1f + speedFluctuation * (Mathf.PerlinNoise(_speedOffset, Time.time * 0.7f) * 2f - 1f));
        if (_state == SwimState.Tumble) speed *= tumbleSpeedFactor;

        // 位移 = 定向游动 + 布朗微小抖动
        Vector3 jitter = planarMovement ? new Vector3(noise.x, 0f, noise.z) : noise;
        transform.position += (dir * speed + jitter * brownianJitter) * dt;

        if (dir.sqrMagnitude > 1e-6f)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            transform.rotation = Quaternion.LookRotation(dir, up);
        }
    }

    // ---------- 状态机：游动 ⇄ 翻滚 ----------

    void UpdateSwimState(float dt)
    {
        _stateTimer -= dt;

        if (_state == SwimState.Run)
        {
            if (_stateTimer <= 0f) EnterTumble();
        }
        else
        {
            // 翻滚：绕随机轴快速转到目标转角
            float step = Mathf.Min(tumbleTurnSpeed * dt, _turnRemaining);
            _heading = Quaternion.AngleAxis(step, _tumbleAxis) * _heading;
            _turnRemaining -= step;

            if (_stateTimer <= 0f) EnterRun();
        }
    }

    void EnterRun()
    {
        _state = SwimState.Run;

        float duration = Random.Range(runDuration.x, runDuration.y);

        // 趋化性（时间感知近似）：朝诱引物游动时抑制翻滚 → 延长本次游动，
        // 远离时更频繁翻滚，宏观上形成朝向目标的净漂移
        if (attractant != null)
        {
            Vector3 toTarget = attractant.position - transform.position;
            if (planarMovement) toTarget.y = 0f;

            if (toTarget.sqrMagnitude > 1e-6f)
            {
                float alignment = Vector3.Dot(_heading, toTarget.normalized); // -1 ~ 1
                duration *= 1f + chemotaxisStrength * alignment * 0.8f;
            }
        }

        _stateTimer = Mathf.Max(0.05f, duration);
    }

    void EnterTumble()
    {
        _state = SwimState.Tumble;
        _stateTimer = Random.Range(tumbleDuration.x, tumbleDuration.y);

        // 绕与前进方向垂直的随机轴转动，保证转向角真实生效
        _tumbleAxis = Vector3.Cross(_heading, Random.onUnitSphere);
        if (_tumbleAxis.sqrMagnitude < 1e-6f) _tumbleAxis = Vector3.Cross(_heading, Vector3.up);
        if (_tumbleAxis.sqrMagnitude < 1e-6f) _tumbleAxis = Vector3.right;
        _tumbleAxis.Normalize();

        float angle = meanTurnAngle + Random.Range(-turnAngleJitter, turnAngleJitter);
        _turnRemaining = Mathf.Clamp(angle, 0f, 180f);
    }

    // ---------- 布朗扰动 ----------

    void ApplyRotationalDiffusion(Vector3 noise, float dt)
    {
        Vector3 n = planarMovement ? new Vector3(noise.x, 0f, noise.z) : noise;

        // 噪声向量给出偏转轴与偏转幅度 → 方向做连续平滑的小幅随机游走
        Vector3 axis = Vector3.Cross(_heading, n);
        if (axis.sqrMagnitude < 1e-8f) return;

        _heading = Quaternion.AngleAxis(rotationalDiffusion * dt * n.magnitude * 2f, axis.normalized) * _heading;
    }

    Vector3 SampleNoise()
    {
        // 三个不同偏移的 Perlin 通道 → 连续平滑的随机扰动（比逐帧 Random 更自然）
        float t = Time.time * brownianFrequency;
        return new Vector3(
            Mathf.PerlinNoise(_noiseOffset, t) - 0.5f,
            Mathf.PerlinNoise(_noiseOffset + 17.31f, t) - 0.5f,
            Mathf.PerlinNoise(_noiseOffset + 43.77f, t) - 0.5f);
    }

    // ---------- 活动范围 & 表现 ----------

    void ApplyBoundarySteering(float dt)
    {
        if (wanderRadius <= 0f) return;

        Vector3 center = wanderCenter != null ? wanderCenter.position : _homeCenter;
        Vector3 toCenter = center - transform.position;
        if (planarMovement) toCenter.y = 0f;

        float dist = toCenter.magnitude;
        if (dist <= wanderRadius || dist < 1e-4f) return;

        // 越界越远，回拉越强（最大转向速率 90 度/秒）
        float over = Mathf.Clamp01((dist - wanderRadius) / wanderRadius);
        _heading = Vector3.RotateTowards(_heading, toCenter / dist,
            boundarySteerStrength * over * Mathf.Deg2Rad * 90f * dt, 0f);
    }

    Vector3 ApplyWobble(Vector3 heading)
    {
        if (wobbleAngle <= 0f || wobbleFrequency <= 0f) return heading;

        float angle = wobbleAngle * Mathf.Sin(Time.time * wobbleFrequency * 2f * Mathf.PI + _wobblePhase);

        Vector3 axis = Vector3.Cross(heading, Vector3.up);
        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
        axis.Normalize();

        return Quaternion.AngleAxis(angle, axis) * heading;
    }

    Vector3 Flatten(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f)
        {
            // 方向已竖直时给一个随机水平方向兜底
            float a = Random.value * Mathf.PI * 2f;
            dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }
        return dir.normalized;
    }

    void OnDrawGizmosSelected()
    {
        if (wanderRadius <= 0f) return;

        Vector3 center = wanderCenter != null
            ? wanderCenter.position
            : (Application.isPlaying ? _homeCenter : transform.position);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(center, wanderRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, (Application.isPlaying ? _heading : transform.forward) * 0.5f);
    }
}