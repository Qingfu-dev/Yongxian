using UnityEngine;

/// <summary>
/// 点亮控制器：挂在主角上。
/// 按下鼠标左键时：
///   1. 把自己点亮（具体实现先留空）
///   2. 通知周围一定范围内所有带 Lightable 的物体，各执行一次它们的被点亮脚本
/// </summary>
[DisallowMultipleComponent]
public class LightUpController : MonoBehaviour
{
    [Header("点亮范围")]
    [Tooltip("按下左键点亮自己时，能通知到的周围半径（米）")]
    public float lightRadius = 5f;

    void Update()
    {
        // GetMouseButtonDown：只在按下的那一帧触发一次，按住不会连续触发
        if (Input.GetMouseButtonDown(0))
        {
            LightUpSelf();
            LightUpNearby();
        }
    }

    // 把自己点亮。具体表现先留空。
    void LightUpSelf()
    {
        // 主角自身点亮的实现（发光、灯光、特效等）先留空，后续在这里补充
        Debug.Log("主角点亮了自己", this);
    }

    // 通知范围内所有带 Lightable 的物体：各执行一次被点亮脚本
    void LightUpNearby()
    {
        Lightable[] all = FindObjectsByType<Lightable>(FindObjectsSortMode.None);

        float sqrRadius = lightRadius * lightRadius;
        int count = 0;
        foreach (Lightable lightable in all)
        {
            // 用平方距离判断，省一次开方
            float sqrDistance = (lightable.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance > sqrRadius) continue;

            lightable.OnLit();
            count++;
        }

        Debug.Log($"点亮范围（{lightRadius} 米）内共通知 {count} 个物体", this);
    }

    // 在 Scene 视图选中主角时画出点亮范围，方便调参
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 1f);
        Gizmos.DrawWireSphere(transform.position, lightRadius);
    }
}
