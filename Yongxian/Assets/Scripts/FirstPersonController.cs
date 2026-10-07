using UnityEngine;

/// <summary>
/// 第一人称相机控制器：直接挂在相机（Main Camera）上即可，
/// 会自动附带 CharacterController 组件。
///   鼠标移动 = 转动视角（Esc 解锁/锁定鼠标）
///   W/A/S/D  = 移动；按住 Shift = 加速跑；空格 = 跳跃
///   视角弹簧 = 始终有一股力把视角拉回正位置：顶着它拖鼠标就是「阻力」，松开鼠标就被拉回去（可开关）
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class FirstPersonController : MonoBehaviour
{
    [Header("视角")]
    [Tooltip("鼠标灵敏度")]
    public float mouseSensitivity = 2.5f;

    [Tooltip("Y 轴反转")]
    public bool invertY = false;

    [Header("视角弹簧（阻力 + 回正）")]
    [Tooltip("总开关：关掉后视角完全跟手，也没有任何回正")]
    public bool enableViewSpring = true;

    [Tooltip("弹簧力度：始终把视角拉向回正位置。顶着它拖鼠标就是「阻力」，松开鼠标就被拉回去。\n" +
             "0 = 不拉（只剩阻力）；3 = 柔和；6 = 明显；12 以上 = 很强")]
    [Range(0f, 20f)] public float springStrength = 3f;

    [Tooltip("拖动阻力（秒）：实际视角追上目标角度所需的时间，带来惯性和松手后的滑行。\n" +
             "0 = 完全跟手（只剩回正）")]
    [Range(0f, 0.5f)] public float dragResistance = 0.12f;

    [Tooltip("回正的目标俯仰角：0 = 水平，负数 = 稍微低头，正数 = 稍微抬头")]
    [Range(-89f, 89f)] public float recenterTargetPitch = 0f;

    [Tooltip("水平是否也一起回正（关掉 = 只有抬/低头回正，左右朝向保持不变）")]
    public bool enableRecenterYaw = true;

    [Tooltip("水平回正的目标：勾选 = 转回脚本开始时面朝的方向；取消勾选 = 转回下面填的角度")]
    public bool recenterYawToInitial = true;

    [Tooltip("水平回正的目标角度（度），仅在取消勾选「回到初始朝向」时生效")]
    public float recenterTargetYaw = 0f;

    [Header("移动")]
    [Tooltip("行走速度（米/秒）")]
    public float walkSpeed = 4f;

    [Tooltip("按住 Shift 时的奔跑速度")]
    public float runSpeed = 8f;

    [Tooltip("跳跃初速度，填 0 表示不能跳")]
    public float jumpSpeed = 5f;

    [Tooltip("重力加速度")]
    public float gravity = 20f;

    float _yaw;                  // 实际视角角度（会跟着 _targetYaw 带阻尼地滑动）
    float _pitch;
    float _targetYaw;            // 鼠标输入直接改这个，实际角度再追它
    float _targetPitch;
    float _yawVelocity;          // SmoothDamp 用的速度缓存
    float _pitchVelocity;
    float _initialYaw;           // 脚本开始时的朝向，水平回正的默认目标
    float _verticalVelocity;
    CharacterController _controller;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();

        // 用相机当前的朝向作为起点，避免开始时视角突然跳转
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x > 180f ? e.x - 360f : e.x;
        _targetYaw = _yaw;
        _targetPitch = _pitch;
        _initialYaw = _yaw;
    }

    void OnEnable()
    {
        SetCursorLocked(true);
    }

    void OnDisable()
    {
        SetCursorLocked(false);
    }

    void Update()
    {
        Look();
        Move();
    }

    void Look()
    {
        // Esc 切换鼠标锁定，方便在编辑器里操作
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
        }

        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        // 1. 鼠标输入：直接加到目标角度上
        _targetYaw += mouseX * mouseSensitivity;
        _targetPitch = Mathf.Clamp(_targetPitch + mouseY * mouseSensitivity * (invertY ? 1f : -1f), -89f, 89f);

        // 2. 弹簧：每帧按偏移比例把目标角度拉回正位置（指数衰减 → 偏得越远拉得越猛，像橡胶筋）。
        //    鼠标一直在顶着它，所以这股力在手上就是「阻力」；松开鼠标，同一个力就负责把视角拉回来。
        if (enableViewSpring && springStrength > 0f)
        {
            float k = 1f - Mathf.Exp(-springStrength * Time.deltaTime);

            _targetPitch = Mathf.Lerp(_targetPitch, recenterTargetPitch, k);

            if (enableRecenterYaw)
            {
                // DeltaAngle 取两个朝向之间的最小夹角：不管转过几圈，都按最近的方向回去
                float restYaw = recenterYawToInitial ? _initialYaw : recenterTargetYaw;
                _targetYaw += Mathf.DeltaAngle(_targetYaw, restYaw) * k;
            }
        }

        // 3. 阻力：实际角度带惯性、带一点滞后地追目标角度（松手后会滑行一小段）
        if (enableViewSpring && dragResistance > 0f)
        {
            // _yaw / _targetYaw 是持续累加的连续角度（不是 0~360 的绕圈角度），两者差值始终很小，
            // 所以直接用 SmoothDamp 即可，不需要 SmoothDampAngle 那套绕圈处理
            _yaw = Mathf.SmoothDamp(_yaw, _targetYaw, ref _yawVelocity, dragResistance);
            _pitch = Mathf.Clamp(Mathf.SmoothDamp(_pitch, _targetPitch, ref _pitchVelocity, dragResistance), -89f, 89f);
        }
        else
        {
            _yaw = _targetYaw;
            _pitch = _targetPitch;
            _yawVelocity = 0f;
            _pitchVelocity = 0f;
        }

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void Move()
    {
        // 水平移动方向：跟随视角朝向（只取水平方向）
        Vector3 dir = Quaternion.Euler(0f, _yaw, 0f)
                    * new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        float speed = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? runSpeed : walkSpeed;

        // 竖直方向：落地后贴地，按空格起跳
        if (_controller.isGrounded)
        {
            if (_verticalVelocity < 0f) _verticalVelocity = -2f;
            if (jumpSpeed > 0f && Input.GetButtonDown("Jump")) _verticalVelocity = jumpSpeed;
        }
        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 motion = dir * speed + Vector3.up * _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}