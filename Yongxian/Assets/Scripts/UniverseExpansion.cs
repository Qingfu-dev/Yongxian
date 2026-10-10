using UnityEngine;

/// <summary>
/// 宇宙膨胀 · 大爆炸开场演绎：星球按「哈勃式」向外飞散（离中心越远飞得越快），
/// 起步瞬间爆发、速度随膨胀不断加大，配合相机 FOV 猛冲拉宽和镜头震动，
/// 做出「大爆炸之后那一下猛烈膨胀」的观感。
///
/// 用法：
///   1. 把本脚本挂到星球生成器所在的物体上（挂别处也行，留空会自动找场景里的生成器）
///   2. 确认「开场演出」勾选，运行后等待 startDelay 秒自动播放一次
///   3. 调试重播：运行中按 replayKey（默认 F），不必反复重启播放模式
///
/// 原理：
///   星球速度 = 膨胀率 × 星球到中心的距离，距离随膨胀越来越大，
///   所以速度会自己越滚越快（整场越来越猛），再由「膨胀速度曲线」控制收尾。
///   膨胀中心 = 触发那一刻、相机前方指定偏移处（也就是「爆炸发生在你面前」，
///   你就在爆炸半径内，四周星球向爆点外炸开），演出结束后星球停在新位置上
///   （配合拖尾后处理会自然拉出光带）。
/// </summary>
[DisallowMultipleComponent]
public class UniverseExpansion : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("星球生成器；留空则自动找场景里的 RandomPrefabSpawner")]
    public RandomPrefabSpawner spawner;

    [Tooltip("负责 FOV 扩张与震动的相机；留空则自动用 Camera.main")]
    public Camera targetCamera;

    [Header("开场演出")]
    [Tooltip("运行时自动播放一次演出")]
    public bool playOnStart = true;

    [Tooltip("开场等待时间（秒）：先安静一小会儿，再开始膨胀")]
    public float startDelay = 2f;

    [Header("爆炸位置")]
    [Tooltip("爆炸点相对相机的偏移（相机局部坐标）：z = 正前方距离，x = 左右，y = 上下。\n" +
             "在触发瞬间按当时的相机位置算出来，之后固定不动；选中本物体可看到黄色爆点标注")]
    public Vector3 explosionOffset = new Vector3(0f, 0f, 60f);

    [Header("演出参数")]
    [Tooltip("演出总时长（秒）")]
    public float duration = 4f;

    [Tooltip("膨胀速度曲线（横轴 0~1 演出进度，纵轴 0~1 强度）：\n" +
             "默认「瞬间拉满 → 长时间保持 → 平滑收住」，就是大爆炸的爆发节奏")]
    public AnimationCurve expansionCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.05f, 1f),
        new Keyframe(0.7f, 0.95f),
        new Keyframe(1f, 0f));

    [Tooltip("FOV 曲线（横轴 0~1 进度，纵轴 0~1 扩张比例）：\n" +
             "默认几帧内猛冲到最宽、保持大广角、最后收回")]
    public AnimationCurve fovCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.06f, 1f),
        new Keyframe(0.7f, 0.95f),
        new Keyframe(1f, 0f));

    [Tooltip("峰值膨胀率（每秒）：星球飞散速度 = 该值 × 星球到膨胀中心的距离。\n" +
             "距离随膨胀不断变大、速度也跟着越来越快；0.4 ≈ 整场约膨胀到 3.5 倍，0.6 约 6 倍")]
    public float expansionRate = 0.4f;

    [Tooltip("速度峰值时的相机视野：从演出开始时的 FOV 猛冲到这个值，保持后收回原值")]
    public float peakFov = 75f;

    [Header("镜头震动")]
    [Tooltip("震动幅度（米）：爆发时最猛、随膨胀减弱；0 = 关闭")]
    public float shakeAmplitude = 0.2f;

    [Tooltip("震动频率（每秒抖动次数）")]
    public float shakeFrequency = 22f;

    [Header("调试")]
    [Tooltip("重播快捷键（运行中随时重来一遍）；None = 关闭快捷键")]
    public KeyCode replayKey = KeyCode.F;

    float _baseFov;          // 演出前的相机 FOV，结束时回正到它
    float _delayTimer;       // 开场等待倒计时
    float _elapsed;          // 演出已进行的时间
    bool _playing;
    float _intensity;        // 本帧膨胀强度（镜头震动用）
    Vector3 _shakeOffset;    // 已施加到相机上的震动偏移，下一帧先撤掉它
    Vector3 _center;         // 本次演出的膨胀中心（触发瞬间由相机前方算得）

    void Start()
    {
        if (spawner == null) spawner = FindObjectOfType<RandomPrefabSpawner>();
        if (targetCamera == null) targetCamera = Camera.main;

        if (targetCamera != null) _baseFov = targetCamera.fieldOfView;

        _delayTimer = Mathf.Max(0f, startDelay);
    }

    void Update()
    {
        // 快捷键重播：方便在编辑器里反复调参
        if (replayKey != KeyCode.None && Input.GetKeyDown(replayKey)) Trigger();

        // 开场倒计时
        if (!_playing && _delayTimer > 0f)
        {
            _delayTimer -= Time.deltaTime;
            if (_delayTimer <= 0f) Trigger();
        }

        if (!_playing)
        {
            _intensity = 0f;
            return;
        }

        _elapsed += Time.deltaTime;
        float t = duration > 0.01f ? Mathf.Clamp01(_elapsed / duration) : 1f;
        _intensity = expansionCurve.Evaluate(t);

        ExpandPlanets(_intensity);
        ApplyFov(fovCurve.Evaluate(t));

        if (t >= 1f) _playing = false;   // 演出结束：星球留在新的位置上
    }

    void LateUpdate()
    {
        // 先撤掉上一帧的震动偏移，避免和 CharacterController 的移动叠加造成漂移
        if (targetCamera == null) { _shakeOffset = Vector3.zero; return; }
        targetCamera.transform.position -= _shakeOffset;
        _shakeOffset = Vector3.zero;

        if (_intensity <= 0f || shakeAmplitude <= 0f) return;

        // 用 Perlin 噪声取三轴平滑随机位移，在相机自身坐标系里抖
        float n = Time.time * shakeFrequency;
        Vector3 noise = new Vector3(
            Mathf.PerlinNoise(n, 0.11f) - 0.5f,
            Mathf.PerlinNoise(n, 5.71f) - 0.5f,
            Mathf.PerlinNoise(n, 9.38f) - 0.5f) * 2f;

        _shakeOffset = targetCamera.transform.rotation * (noise * (shakeAmplitude * _intensity));
        targetCamera.transform.position += _shakeOffset;
    }

    void OnDisable()
    {
        // 中途被禁用时把震动偏移还回去
        if (_shakeOffset != Vector3.zero && targetCamera != null)
            targetCamera.transform.position -= _shakeOffset;
        _shakeOffset = Vector3.zero;
    }

    /// <summary>播放一次膨胀演出（也可由事件 / Timeline 调用）</summary>
    public void Trigger()
    {
        _center = ComputeCenter();   // 爆炸点：按触发这一刻的相机位置算，之后固定
        _elapsed = 0f;
        _playing = true;
        _delayTimer = 0f;
    }

    // 爆炸中心：相机前方按偏移取点；没有相机时退回生成器 / 本物体位置
    Vector3 ComputeCenter()
    {
        if (targetCamera != null)
            return targetCamera.transform.position + targetCamera.transform.rotation * explosionOffset;
        if (spawner != null) return spawner.transform.position;
        return transform.position;
    }

    // 按哈勃定律移动星球：速度大小 = expansionRate × 星球到膨胀中心的距离，方向朝外。
    // 因此位移 = （星球位置 - 中心）× 常数 × dt，不需要归一化，也不必开方求距离。
    void ExpandPlanets(float intensity)
    {
        if (spawner == null) return;

        float h = expansionRate * intensity;
        if (h <= 0f) return;

        var planets = spawner.spawnedObjects;

        for (int i = 0; i < planets.Count; i++)
        {
            GameObject planet = planets[i];
            if (planet == null) continue;

            Transform tf = planet.transform;
            tf.position += (tf.position - _center) * (h * Time.deltaTime);
        }
    }

    void ApplyFov(float k)
    {
        if (targetCamera == null) return;
        targetCamera.fieldOfView = Mathf.Lerp(_baseFov, peakFov, k);
    }

    // 在场景里标出「爆炸会发生在哪」：黄色小球 + 相机到爆点的连线
    void OnDrawGizmosSelected()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        Vector3 point = cam.transform.position + cam.transform.rotation * explosionOffset;
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(point, 1.5f);
        Gizmos.DrawLine(cam.transform.position, point);
    }
}