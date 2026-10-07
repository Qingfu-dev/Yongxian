using UnityEngine;

/// <summary>
/// 环绕相机：围着目标物体旋转观察（第三人称轨道视角）
///   鼠标左右 = 绕目标水平环绕；鼠标上下 = 改变俯仰视角
///   相机与目标距离固定（distance），且始终看向目标
///   Esc = 解锁 / 重新锁定鼠标指针（方便在编辑器里操作）
/// 用法：把本脚本挂到相机物体上，并把要环绕的物体拖到 Target 字段
/// </summary>
public class FreeLookController : MonoBehaviour
{
    [Header("环绕目标")]
    [Tooltip("相机环绕的物体；留空时相机保持不动")]
    public Transform target;

    [Tooltip("相机与目标的距离（米）")]
    public float distance = 5f;

    [Header("鼠标视角")]
    [Tooltip("鼠标灵敏度")]
    public float mouseSensitivity = 2.5f;

    [Tooltip("Y 轴反转")]
    public bool invertY = false;

    [Tooltip("俯仰角下限（度）：负值 = 相机可以转到目标下方仰视")]
    public float minPitch = -80f;

    [Tooltip("俯仰角上限（度）：正值 = 相机可以转到目标上方俯视")]
    public float maxPitch = 80f;

    [Header("鼠标锁定")]
    [Tooltip("运行时锁定并隐藏鼠标指针；按 Esc 解锁/重新锁定")]
    public bool lockCursor = true;

    private float _yaw;
    private float _pitch;

    void Start()
    {
        InitializeAngles();

        if (lockCursor)
        {
            SetCursorLocked(true);
        }
    }

    void LateUpdate()
    {
        // Esc 切换鼠标锁定，方便在编辑器里操作
        if (lockCursor && Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
        }

        if (target == null)
        {
            return;
        }

        // 鼠标移动 → 环绕角度
        if (!lockCursor || Cursor.lockState == CursorLockMode.Locked)
        {
            _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            _pitch += Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        // 相机停在目标周围的轨道上，并始终看向目标
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.position = target.position - rotation * Vector3.forward * distance;
        transform.rotation = rotation;
    }

    // 用相机当前的位置/朝向初始化角度，避免运行时跳变
    void InitializeAngles()
    {
        if (target == null)
        {
            return;
        }

        Vector3 lookDirection = target.position - transform.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion lookRotation = Quaternion.LookRotation(lookDirection);
        _yaw = lookRotation.eulerAngles.y;
        _pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, lookRotation.eulerAngles.x), minPitch, maxPitch);
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}