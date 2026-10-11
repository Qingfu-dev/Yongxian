using UnityEngine;

/// <summary>
/// 第三人称控制器：一个脚本同时管角色移动和跟随相机，直接挂到角色物体上即可。
/// 会自动附带 CharacterController 组件；相机不用做任何设置（留空时自动使用 Main Camera）。
///   W/A/S/D  = 相对相机方向移动，角色平滑转向移动方向
///   按住 Shift = 加速跑；空格 = 跳跃
///   鼠标移动 = 环绕视角；Esc = 解锁 / 重新锁定鼠标指针
///   动画：站立播放 Idle 状态，移动播放 Action_002 状态，自动淡入淡出；奔跑时移动动画会加速
/// 注意：相机上不要再挂 FreeLookController，否则两个脚本会互相抢控制权。
/// 动画要求：角色上要有 Animator，且控制器里存在与下面两个「状态名」完全同名的状态；
///           用脚本驱动位移，所以 Animator 的 Apply Root Motion 不要勾。
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class ThirdPersonController : MonoBehaviour
{
    [Header("相机")]
    [Tooltip("跟随相机：留空时自动使用场景里的 Main Camera")]
    public Transform cameraTransform;

    [Tooltip("相机与角色看向点的距离（米）")]
    public float distance = 5f;

    [Tooltip("看向点的高度：相机始终看向角色这个高度的位置，一般填胸口或头部高度")]
    public float pivotHeight = 1.5f;

    [Tooltip("鼠标灵敏度")]
    public float mouseSensitivity = 2.5f;

    [Tooltip("Y 轴反转")]
    public bool invertY = false;

    [Tooltip("俯仰角下限（度）：负值 = 相机可以降到角色下方仰视")]
    public float minPitch = -30f;

    [Tooltip("俯仰角上限（度）：正值 = 相机可以升到角色上方俯视")]
    public float maxPitch = 80f;

    [Tooltip("相机跟随的平滑时间（秒）：0 = 硬跟随，越大越松软")]
    [Range(0f, 0.5f)] public float followSmoothTime = 0.08f;

    [Tooltip("相机防穿墙：碰到墙壁/地面时自动把相机拉近，避免钻进物体内部")]
    public bool preventClipping = true;

    [Tooltip("防穿墙检测的球体半径（米）")]
    public float clipCheckRadius = 0.25f;

    [Header("鼠标锁定")]
    [Tooltip("运行时锁定并隐藏鼠标指针；按 Esc 解锁 / 重新锁定")]
    public bool lockCursor = true;

    [Header("移动")]
    [Tooltip("行走速度（米/秒）")]
    public float walkSpeed = 4f;

    [Tooltip("按住 Shift 时的奔跑速度")]
    public float runSpeed = 8f;

    [Tooltip("转身速度（度/秒）：越大转向越快")]
    public float turnSpeed = 720f;

    [Tooltip("跳跃初速度，填 0 表示不能跳")]
    public float jumpSpeed = 5f;

    [Tooltip("重力加速度")]
    public float gravity = 20f;

    [Header("动画")]
    [Tooltip("角色的 Animator：留空时自动在自身和子物体里查找")]
    public Animator animator;

    [Tooltip("站立状态名：必须和 Animator 控制器里的状态名完全一致（大小写、竖线都不能差）")]
    public string idleStateName = "rig|Idle";

    [Tooltip("移动状态名：必须和 Animator 控制器里的状态名完全一致（大小写、竖线都不能差）")]
    public string moveStateName = "rig|Action_002";

    [Tooltip("站立 / 移动之间的淡入淡出时间（秒）")]
    [Range(0f, 1f)] public float animationFadeTime = 0.2f;

    [Tooltip("奔跑时移动动画的播放倍率：跑得比走快，动画也要放快一点才不滑步")]
    public float runAnimationSpeed = 1.5f;

    // 防穿墙检测复用的结果数组，避免每帧产生垃圾
    static readonly RaycastHit[] ClipHits = new RaycastHit[8];

    float _yaw;                   // 相机水平环绕角
    float _pitch;                 // 相机俯仰角
    float _verticalVelocity;      // 竖直速度（跳跃 / 重力）
    Vector3 _cameraVelocity;      // 相机位置 SmoothDamp 的速度缓存
    bool _isMoving;               // 当前是否在播放移动动画
    bool _warnedNoCamera;
    CharacterController _controller;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    void Start()
    {
        InitializeAngles();

        if (lockCursor)
        {
            SetCursorLocked(true);
        }
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

    void LateUpdate()
    {
        UpdateCamera();
    }

    void Look()
    {
        // Esc 切换鼠标锁定，方便在编辑器里操作
        if (lockCursor && Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
        }

        if (lockCursor && Cursor.lockState != CursorLockMode.Locked) return;

        _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        _pitch = Mathf.Clamp(_pitch + Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f), minPitch, maxPitch);
    }

    void Move()
    {
        // 水平移动方向：以相机朝向为基准（只取水平方向）
        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 dir = Quaternion.Euler(0f, _yaw, 0f) * input;

        bool moving = dir.sqrMagnitude > 0.0001f;
        bool running = moving && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        float speed = running ? runSpeed : walkSpeed;

        UpdateAnimation(moving, running);

        // 朝移动方向平滑转身
        if (moving)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }

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

    void UpdateAnimation(bool moving, bool running)
    {
        // 没挂 Animator、或还没给 Animator 指定控制器时，静默跳过
        if (animator == null || animator.runtimeAnimatorController == null) return;

        // 只在「站立 / 移动」真正切换的那一帧做淡入淡出，避免每帧重复切换
        if (moving != _isMoving)
        {
            _isMoving = moving;
            // 用 CrossFadeInFixedTime：淡入淡出时长按「秒」算，和上面的参数字段一致
            animator.CrossFadeInFixedTime(moving ? moveStateName : idleStateName, animationFadeTime);
        }

        // 奔跑时把移动动画放快，避免滑步；站立 / 行走保持原速
        animator.speed = running ? runAnimationSpeed : 1f;
    }

    void UpdateCamera()
    {
        if (cameraTransform == null)
        {
            if (!_warnedNoCamera)
            {
                _warnedNoCamera = true;
                Debug.LogWarning("[ThirdPersonController] 没有找到相机：请把相机拖到 Camera Transform 字段，或确认场景里有带 MainCamera 标签的相机。", this);
            }
            return;
        }

        Vector3 pivot = transform.position + Vector3.up * pivotHeight;
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 rawTarget = pivot - rotation * Vector3.forward * distance;

        // 平滑跟随：角色移动时相机带一点滞后，避免硬绑在角色身上
        Vector3 smoothedTarget = followSmoothTime > 0f
            ? Vector3.SmoothDamp(cameraTransform.position, rawTarget, ref _cameraVelocity, followSmoothTime)
            : rawTarget;

        // 防穿墙：从看向点朝相机方向做球体检测，碰到东西就把相机拉到障碍物前面
        Vector3 toCamera = smoothedTarget - pivot;
        float targetDistance = toCamera.magnitude;

        if (preventClipping && targetDistance > 0.0001f)
        {
            Vector3 castDirection = toCamera / targetDistance;
            int count = Physics.SphereCastNonAlloc(pivot, clipCheckRadius, castDirection, ClipHits, targetDistance, ~0, QueryTriggerInteraction.Ignore);

            float nearest = targetDistance;
            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = ClipHits[i].transform;
                // 忽略角色自身：看向点在角色胶囊里面，否则每次都会被自己挡住
                if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
                if (ClipHits[i].distance < nearest) nearest = ClipHits[i].distance;
            }

            smoothedTarget = pivot + castDirection * Mathf.Max(nearest, 0.1f);
        }

        cameraTransform.position = smoothedTarget;
        cameraTransform.rotation = rotation;
    }

    // 用相机当前的角度作为起点，避免开始时视角突然跳转
    void InitializeAngles()
    {
        Vector3 pivot = transform.position + Vector3.up * pivotHeight;

        if (cameraTransform == null || (pivot - cameraTransform.position).sqrMagnitude < 0.0001f)
        {
            // 没有相机、或相机正好在看向点上时，直接用角色当前朝向
            _yaw = transform.eulerAngles.y;
            _pitch = Mathf.Clamp(0f, minPitch, maxPitch);
            return;
        }

        Vector3 e = Quaternion.LookRotation(pivot - cameraTransform.position).eulerAngles;
        _yaw = e.y;
        _pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), minPitch, maxPitch);
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}