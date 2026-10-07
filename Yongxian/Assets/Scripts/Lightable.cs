using UnityEngine;

/// <summary>
/// 被点亮脚本：挂在所有「可以被点亮」的物体上。
/// 主角按下鼠标左键点亮自己时，一定范围内带本脚本的物体会各收到一次 OnLit 通知。
/// </summary>
[DisallowMultipleComponent]
public class Lightable : MonoBehaviour
{
    /// <summary>
    /// 收到点亮信息时执行一次。
    /// 具体的点亮表现（发光材质 / 灯光 / 粒子等）先留空，后续在这里实现。
    /// </summary>
    public void OnLit()
    {
        // 点亮实现先留空，先用日志表示本物体收到了点亮信息
        Debug.Log($"收到点亮信息：{name}", this);
    }
}