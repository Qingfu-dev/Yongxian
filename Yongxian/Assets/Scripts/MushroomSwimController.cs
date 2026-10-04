using UnityEngine;

/// <summary>
/// 主角菌子水下移动控制器：鼠标转向，W/S 控制速度，空格 / 左Ctrl 浮潜，左 Shift 冲刺（消耗体力）。
///
/// 操作（按键都可以在 Inspector 里改）：
///   鼠标      = 转向（默认同时控制抬头/低头，可关；Esc 解锁/锁定鼠标）
///   W / S     = 加速前进 / 减速·后退（油门式，沿相机看向的方向游，松开后借惯性滑行慢慢停下）
///   空格      = 上浮；左 Ctrl = 下潜
///   左 Shift  = 冲刺：速度 × 冲刺倍率，消耗体力；体力耗光会掉回普通速度，
///               要等体力回到"恢复阈值"以上才能再次冲刺
///
/// 移动手感：
///   惯性滑行   ：速度按指数曲线逼近期望速度，松开 W 后会滑一段再停
///   朝向完全由鼠标决定，不会再有任何自发旋转（没有低头、侧倾这类自动姿态和回正）
///
/// 移动方向：
///   前进方向取相机看向的方向（看到哪就游向哪），相机的俯仰也算在内 —— 低头看下方 + W 就会往下潜
///   挂到菌子模型、用外部相机观察时，把那个相机拖到 Camera Transform 上（留空自动用 Camera.main）
///   挂到相机自己身上时（第一人称），相机就是自己，前进方向等于自身朝向
///
/// 用法：
///   1. 把本脚本挂到菌子模型物体上（会自动要求 CharacterController 组件），模型正面朝 +Z
///   2. 相机不要再让别的脚本用鼠标转（会和本脚本的转向打架）
///   3. 体力条：把 StaminaBarUI 挂到任意物体上即可显示，会自动找到本脚本
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class MushroomSwimController : MonoBehaviour
{
    [Header("总开关")]
    [Tooltip("取消勾选 = 暂时不接受玩家输入（比如过场动画时），惯性滑行仍然保留")]
    public bool controlEnabled = true;

    [Header("移动方向（W / S 的参考）")]
    [Tooltip("前进方向跟着这个相机的朝向走（看到哪就游向哪，含相机俯仰）；留空会自动用 Camera.main。" +
             "找不到相机时退回使用菌子自身朝向")]
    public Transform cameraTransform;

    [Header("鼠标转向")]
    [Tooltip("鼠标灵敏度")]
    public float mouseSensitivity = 2.5f;

    [Tooltip("Y 轴反转")]
    public bool invertY = false;

    [Tooltip("运行时自动锁定鼠标指针（Esc 解锁/锁定）")]
    public bool lockCursor = true;

    [Tooltip("鼠标是否同时控制抬头/低头；关掉 = 只能左右转向，上下全靠空格 / Ctrl")]
    public bool mouseControlsPitch = true;

    [Tooltip("抬头 / 低头角度限制（度）")]
    [Range(0f, 89f)] public float pitchLimit = 70f;

    [Header("速度 / 油门（W S）")]
    [Tooltip("巡航速度（米/秒）：油门推满时的速度")]
    public float swimSpeed = 3.5f;

    [Tooltip("冲刺速度倍率：按住冲刺键时速度 × 这个值")]
    public float boostMultiplier = 1.9f;

    [Tooltip("油门响应（1/秒）：越大加速/减速越干脆")]
    public float throttleRamp = 1.2f;

    [Tooltip("松开 W/S 后油门回中的速度（1/秒）：越小滑行越久")]
    public float throttleRelease = 0.5f;

    [Tooltip("S 键最多减速到多少（相对巡航速度的比例，负值 = 可以倒退）")]
    [Range(-1f, 0f)] public float reverseRatio = -0.35f;

    [Header("浮潜（空格 / Ctrl）")]
    [Tooltip("上浮 / 下潜速度（米/秒）")]
    public float verticalSpeed = 2.5f;

    [Header("体力（冲刺限制）")]
    [Tooltip("体力上限")]
    public float maxStamina = 100f;

    [Tooltip("冲刺时每秒消耗的体力")]
    public float staminaDrain = 22f;

    [Tooltip("不冲刺时每秒恢复的体力")]
    public float staminaRegen = 14f;

    [Tooltip("停止冲刺后，隔多久才开始回体力（秒）")]
    public float staminaRegenDelay = 0.6f;

    [Tooltip("体力耗光后，要回到最大体力的这个比例才能再次冲刺（防止一格一格地抽搐）")]
    [Range(0f, 1f)] public float staminaResumeRatio = 0.2f;

    [Header("水的阻力")]
    [Tooltip("水的阻力响应（1/秒）：越大起步/刹车越干脆，越小越像在糖浆里滑")]
    [Range(0.2f, 8f)] public float responsiveness = 1.8f;

    [Header("按键")]
    public KeyCode ascendKey = KeyCode.Space;
    public KeyCode descendKey = KeyCode.LeftControl;
    public KeyCode boostKey = KeyCode.LeftShift;

    [Header("碰撞体自动适配")]
    [Tooltip("启用时在 Awake 按子物体渲染器的包围盒自动设置 CharacterController 的 Center / Height / Radius")]
    public bool autoFitCollider = true;

    CharacterController _controller;
    Vector3 _velocity;        // 当前游动速度
    float _yaw;
    float _pitch;
    float _throttle;          // 油门：1 = 巡航速度前进，负值 = 倒退
    float _verticalKeyInput;  // 上下键输入：+1 上浮 / -1 下潜 / 0 没按
    float _stamina;
    float _regenDelayTimer;
    bool _exhausted;
    bool _isBoosting;

    /// <summary>当前速度（米/秒），可供跟随相机、音效等脚本使用</summary>
    public Vector3 CurrentVelocity => _velocity;

    /// <summary>体力比例 0~1（给 HUD 用）</summary>
    public float StaminaRatio => maxStamina > 0f ? Mathf.Clamp01(_stamina / maxStamina) : 0f;

    /// <summary>当前是否正在冲刺</summary>
    public bool IsBoosting => _isBoosting;

    /// <summary>体力是否已耗尽（耗尽后要恢复到阈值才能再冲）</summary>
    public bool IsExhausted => _exhausted;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();

        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x > 180f ? transform.eulerAngles.x - 360f : transform.eulerAngles.x;
        _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);
        _stamina = maxStamina;

        if (autoFitCollider) FitColliderToModel();
    }

    void OnEnable()
    {
        if (lockCursor) SetCursorLocked(true);
    }

    void OnDisable()
    {
        if (lockCursor) SetCursorLocked(false);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Look();
        ReadVerticalInput();
        UpdateThrottle(dt);
        UpdateStamina(dt);
        UpdateVelocity(dt);
        Move(dt);
    }

    // ---------- 鼠标转向 ----------

    void Look()
    {
        // Esc 切换鼠标锁定，方便在编辑器里操作
        if (lockCursor && Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
        }

        if (!controlEnabled || Cursor.lockState != CursorLockMode.Locked) return;

        _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;

        if (mouseControlsPitch)
        {
            _pitch += Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
            _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);
        }

        // 朝向完全由鼠标决定：不叠加任何自动姿态，也不会自动回正
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    // ---------- 油门（W / S）----------

    void UpdateThrottle(float dt)
    {
        float v = controlEnabled ? Input.GetAxisRaw("Vertical") : 0f;

        if (v > 0.01f)         _throttle = Mathf.MoveTowards(_throttle, 1f, throttleRamp * dt);
        else if (v < -0.01f)   _throttle = Mathf.MoveTowards(_throttle, reverseRatio, throttleRamp * dt);
        else                   _throttle = Mathf.MoveTowards(_throttle, 0f, throttleRelease * dt);   // 松开 → 缓慢收油，借惯性滑行
    }

    void ReadVerticalInput()
    {
        _verticalKeyInput = 0f;
        if (!controlEnabled) return;

        if (Input.GetKey(ascendKey)) _verticalKeyInput += 1f;
        if (Input.GetKey(descendKey)) _verticalKeyInput -= 1f;
    }

    // ---------- 体力 ----------

    void UpdateStamina(float dt)
    {
        bool wantsBoost = controlEnabled && Input.GetKey(boostKey);
        bool hasMovement = Mathf.Abs(_throttle) > 0.05f || !Mathf.Approximately(_verticalKeyInput, 0f);

        _isBoosting = wantsBoost && hasMovement && !_exhausted && _stamina > 0f;

        if (_isBoosting)
        {
            _stamina -= staminaDrain * dt;
            _regenDelayTimer = staminaRegenDelay;

            if (_stamina <= 0f)
            {
                // 体力耗光：立刻掉回普通速度，并且要恢复到阈值以上才能再冲
                _stamina = 0f;
                _exhausted = true;
                _isBoosting = false;
            }
        }
        else
        {
            if (_regenDelayTimer > 0f) _regenDelayTimer -= dt;
            else _stamina = Mathf.Min(maxStamina, _stamina + staminaRegen * dt);

            if (_exhausted && _stamina >= maxStamina * staminaResumeRatio) _exhausted = false;
        }
    }

    // ---------- 速度：油门 / 浮潜 / 水的阻力 ----------

    void UpdateVelocity(float dt)
    {
        float boostFactor = _isBoosting ? boostMultiplier : 1f;

        // 前进 / 倒退方向 = 相机看向的方向（看到哪就游向哪，含相机俯仰）
        Vector3 forward = GetThrustReference().forward;

        Vector3 targetVelocity = forward * (_throttle * swimSpeed * boostFactor)
                               + Vector3.up * (_verticalKeyInput * verticalSpeed * boostFactor);

        // 水的阻力模型：速度按指数曲线逼近期望值
        // → 起步、刹车都带惯性，松开按键后还会滑行一段（responsiveness 越小滑得越远）
        float k = 1f - Mathf.Exp(-responsiveness * dt);
        _velocity = Vector3.Lerp(_velocity, targetVelocity, k);

        float maxSpeed = Mathf.Abs(swimSpeed) * Mathf.Max(1f, boostMultiplier) * 1.5f
                       + Mathf.Abs(verticalSpeed);
        if (_velocity.sqrMagnitude > maxSpeed * maxSpeed) _velocity = _velocity.normalized * maxSpeed;
    }

    // ---------- 位移 ----------

    void Move(float dt)
    {
        _controller.Move(_velocity * dt);
    }

    // ---------- 工具 ----------

    // W/S 的参考方向：优先用指定的相机，留空自动找 Camera.main；都没有就退回菌子自身朝向
    Transform GetThrustReference()
    {
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        return cameraTransform != null ? cameraTransform : transform;
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // 按所有子物体渲染器的世界包围盒，把 CharacterController 调成和模型一样大
    void FitColliderToModel()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[MushroomSwimController] 没有找到任何渲染器，无法自动适配碰撞体，" +
                             "请手动设置 CharacterController 的 Center / Height / Radius", this);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        // 世界包围盒的 8 个角点转到本地空间，得到与缩放/旋转无关的本地包围盒
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        for (int c = 0; c < 8; c++)
        {
            Vector3 corner = new Vector3(
                (c & 1) == 0 ? bounds.min.x : bounds.max.x,
                (c & 2) == 0 ? bounds.min.y : bounds.max.y,
                (c & 4) == 0 ? bounds.min.z : bounds.max.z);
            Vector3 p = transform.InverseTransformPoint(corner);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 size = max - min;
        _controller.center = (min + max) * 0.5f;
        _controller.radius = Mathf.Max(Mathf.Min(size.x, size.z) * 0.5f, 0.01f);
        _controller.height = Mathf.Max(size.y, _controller.radius * 2f + 0.01f);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawRay(transform.position, _velocity);
    }
}