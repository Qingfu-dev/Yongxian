using UnityEngine;

/// <summary>
/// 最基础的位移脚本：朝自身正前方持续前进一段时间后自动停止。
/// 直接挂在需要移动的物体上即可，不需要任何输入。
/// </summary>
public class SimpleMove : MonoBehaviour
{
    [Tooltip("前进速度（米/秒）")]
    public float speed = 5f;

    [Tooltip("持续时间（秒）：走满这段时间后停止")]
    public float duration = 3f;

    private Transform _transform;
    private float _timer;

    void Start()
    {
        _transform = transform;
    }

    void Update()
    {
        // 时间到就停下
        _timer += Time.deltaTime;
        if (_timer >= duration) return;

        // 沿自身正前方匀速前进
        // 想改为固定世界方向的话，把 transform.forward 换成 Vector3.forward 即可
        _transform.position += _transform.forward * speed * Time.deltaTime;
    }
}
