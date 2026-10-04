using UnityEngine;

/// <summary>
/// 第一人称相机控制器：直接挂在相机（Main Camera）上即可，
/// 会自动附带 CharacterController 组件。
///   鼠标移动 = 转动视角（Esc 解锁/锁定鼠标）
///   W/A/S/D  = 移动；按住 Shift = 加速跑；空格 = 跳跃
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

    [Header("移动")]
    [Tooltip("行走速度（米/秒）")]
    public float walkSpeed = 4f;

    [Tooltip("按住 Shift 时的奔跑速度")]
    public float runSpeed = 8f;

    [Tooltip("跳跃初速度，填 0 表示不能跳")]
    public float jumpSpeed = 5f;

    [Tooltip("重力加速度")]
    public float gravity = 20f;

    float _yaw;
    float _pitch;
    float _verticalVelocity;
    CharacterController _controller;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();

        // 用相机当前的朝向作为起点，避免开始时视角突然跳转
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x > 180f ? e.x - 360f : e.x;
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

        _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        _pitch += Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
        _pitch = Mathf.Clamp(_pitch, -89f, 89f);

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