using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 宇宙大爆炸 · 生成演出：不再预先铺满星球，而是在爆炸那一刻让所有星球在爆点附近诞生，
/// 然后被炸向四面八方 —— 宇宙从爆炸中生成。自带 FOV 猛冲与镜头震动，一个脚本完成整场演出。
///
/// 用法：
///   1. 把本脚本挂到星球生成器所在的物体上（留空会自动找场景里的 RandomPrefabSpawner；
///      星球数量与预制体沿用生成器的 count / prefabs 配置）
///   2. 场景里把旧的 UniverseExpansion 组件移除或禁用，避免两个演出互相打架
///   3. 运行后等待 startDelay 秒自动爆炸一次；调试重播按 replayKey（默认 F，
///      每次重播会在爆点再生成一批新星球）
///
/// 原理：
///   触发时在爆点周围 seedRadius 的小球里一次性生成全部星球（"致密奇点"），
///   每颗星球随机取一个朝外的初速（"被炸飞"），再叠加随距离增长的额外加速
///   （"越远越快、越来越猛"）。飞行分两段：
///     第一段：快速爆炸飞行（duration 秒），同时 FOV 猛冲拉宽 + 镜头震动；
///     第二段：缓慢飞行（driftDuration 秒内平滑降到「缓行速度比例」，之后永远保持）——
///             星球一直向外缓慢飞，不会停下来。
///   「中心残留」效果预制体（可选，例如扭曲空间的 Quad）会在爆点生成，星球炸开后留在正中间。
/// </summary>
[DisallowMultipleComponent]
public class BigBangExplosion : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("星球生成器；留空则自动找本物体 / 场景里的 RandomPrefabSpawner。数量与预制体沿用它的配置")]
    public RandomPrefabSpawner spawner;

    [Tooltip("负责 FOV 扩张与震动的相机；留空则自动用 Camera.main")]
    public Camera targetCamera;

    [Header("开场演出")]
    [Tooltip("运行时自动播放一次大爆炸")]
    public bool playOnStart = true;

    [Tooltip("开场等待时间（秒）：先安静一小会儿，再爆炸")]
    public float startDelay = 2f;

    [Header("爆炸位置")]
    [Tooltip("爆点相对相机的偏移（相机局部坐标）：z = 正前方距离，x = 左右，y = 上下。\n" +
             "在触发瞬间按当时的相机位置算出来；选中本物体可看到黄色爆点标注")]
    public Vector3 explosionOffset = new Vector3(0f, 0f, 60f);

    [Header("奇点生成")]
    [Tooltip("星球诞生的球体半径（米）：越小越像从一个点炸出来，越大出生时越分散")]
    public float seedRadius = 3f;

    [Header("黑洞")]
    [Tooltip("爆炸后留在爆点正中间的效果预制体（例如「扭曲空间」的 Quad 预制体；留空 = 不生成）\n" +
             "重播时会销毁旧的那个，在新的爆点重新生成")]
    public GameObject blackHolePrefab;

    [Header("爆炸飞散（两段飞行）")]
    [Tooltip("第一段：快速爆炸飞行时长（秒）")]
    public float duration = 4f;

    [Tooltip("第二段：缓慢飞行时长（秒）：速度从爆炸末速平滑降到「缓行速度比例」，\n" +
             "这段走完后仍会以缓行速度一直飞下去（永不停下）")]
    public float driftDuration = 10f;

    [Tooltip("缓行速度比例（相对每颗星球自己的初速，0~1）：\n" +
             "缓行段最终保留的速度；大于 0 = 永远缓慢向外飞、不停下来")]
    [Range(0f, 1f)] public float driftSpeedFloor = 0.08f;

    [Tooltip("爆炸曲线（横轴 0~1 = 第一段进度，纵轴 = 速度强度）：\n" +
             "默认「瞬间拉满 → 长时间保持 → 降到中速交给缓行段」。\n" +
             "注意结尾别设成 0：结尾值就是缓行段的起始速度，设 0 会在两段之间停顿一下")]
    public AnimationCurve blastCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.05f, 1f),
        new Keyframe(0.7f, 0.95f),
        new Keyframe(1f, 0.45f));

    [Tooltip("星球出生初速范围（米/秒）：每颗星球随机取一个朝外速度，快慢不一才像爆炸")]
    public float minSpeed = 20f;

    [Tooltip("初速上限（米/秒）")]
    public float maxSpeed = 45f;

    [Tooltip("随距离附加的加速（每秒）：距离越远额外越快，让膨胀越来越猛；0 = 纯爆炸初速")]
    public float hubbleBoost = 0.2f;

    [Header("视野与震动")]
    [Tooltip("FOV 曲线（横轴 0~1 进度，纵轴 0~1 扩张比例）：\n" +
             "默认几帧内猛冲到最宽、保持大广角、最后收回")]
    public AnimationCurve fovCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.06f, 1f),
        new Keyframe(0.7f, 0.95f),
        new Keyframe(1f, 0f));

    [Tooltip("速度峰值时的相机视野：从演出开始时的 FOV 猛冲到这个值，保持后收回原值")]
    public float peakFov = 75f;

    [Tooltip("震动幅度（米）：爆炸瞬间最猛、随演出减弱；0 = 关闭")]
    public float shakeAmplitude = 0.2f;

    [Tooltip("震动频率（每秒抖动次数）")]
    public float shakeFrequency = 22f;

    [Header("调试")]
    [Tooltip("重播快捷键（每次重播会在爆点再生成一批星球）；None = 关闭")]
    public KeyCode replayKey = KeyCode.F;

    // 本次爆炸这一批星球（spawner.spawnedObjects 里从 _firstIndex 开始）的飞散数据
    List<Vector3> _dirs = new List<Vector3>();     // 每颗星球的朝外方向（单位向量）
    List<float> _speeds = new List<float>();       // 每颗星球的初速
    int _firstIndex;
    GameObject _blackHole;   // 留在爆点的黑洞实例（重播时销毁旧的、重新生成）

    float _baseFov;          // 演出前的相机 FOV，结束时回正到它
    float _delayTimer;       // 开场等待倒计时
    float _elapsed;          // 演出已进行的时间
    bool _playing;           // 第一段（爆炸）进行中：FOV 猛冲 + 震动只在这段里
    bool _moving;            // 触发后一直为真：星球持续飞行（缓行段结束后也不停）
    float _shakeIntensity;   // 本帧震动强度（第一段里大于 0，之后归零）
    Vector3 _shakeOffset;    // 已施加到相机上的震动偏移，下一帧先撤掉它
    Vector3 _center;         // 本次爆炸的爆点

    void Awake()
    {
        if (spawner == null) spawner = GetComponent<RandomPrefabSpawner>();
        if (spawner == null) spawner = FindObjectOfType<RandomPrefabSpawner>();

        // 生成改由本脚本接管：关掉生成器的开场自动铺满，避免开场先铺一遍、爆炸又生一遍
        if (spawner != null) spawner.spawnOnStart = false;
    }

    void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) _baseFov = targetCamera.fieldOfView;

        _delayTimer = Mathf.Max(0f, startDelay);
    }

    void Update()
    {
        // 快捷键重播：会再生成一批星球
        if (replayKey != KeyCode.None && Input.GetKeyDown(replayKey)) Trigger();

        // 开场倒计时
        if (!_playing && _delayTimer > 0f)
        {
            _delayTimer -= Time.deltaTime;
            if (_delayTimer <= 0f) Trigger();
        }

        if (!_moving)
        {
            _shakeIntensity = 0f;
            return;
        }

        _elapsed += Time.deltaTime;

        // 星球持续飞行：第一段爆炸冲刺 → 第二段缓行 → 之后以最低速一直巡航（永不停）
        BlastPlanets(SpeedMultiplier());

        // FOV 猛冲与震动只在第一段里生效
        if (_playing)
        {
            float t = duration > 0.01f ? Mathf.Clamp01(_elapsed / duration) : 1f;
            ApplyFov(fovCurve.Evaluate(t));
            _shakeIntensity = blastCurve.Evaluate(t);

            if (t >= 1f) _playing = false;   // 进入缓行段：FOV 收回原值、震动停止
        }
        else
        {
            _shakeIntensity = 0f;
        }
    }

    void LateUpdate()
    {
        // 先撤掉上一帧的震动偏移，避免和 CharacterController 的移动叠加造成漂移
        if (targetCamera == null) { _shakeOffset = Vector3.zero; return; }
        targetCamera.transform.position -= _shakeOffset;
        _shakeOffset = Vector3.zero;

        if (_shakeIntensity <= 0f || shakeAmplitude <= 0f) return;

        // 用 Perlin 噪声取三轴平滑随机位移，在相机自身坐标系里抖
        float n = Time.time * shakeFrequency;
        Vector3 noise = new Vector3(
            Mathf.PerlinNoise(n, 0.11f) - 0.5f,
            Mathf.PerlinNoise(n, 5.71f) - 0.5f,
            Mathf.PerlinNoise(n, 9.38f) - 0.5f) * 2f;

        _shakeOffset = targetCamera.transform.rotation * (noise * (shakeAmplitude * _shakeIntensity));
        targetCamera.transform.position += _shakeOffset;
    }

    void OnDisable()
    {
        // 中途被禁用时把震动偏移还回去
        if (_shakeOffset != Vector3.zero && targetCamera != null)
            targetCamera.transform.position -= _shakeOffset;
        _shakeOffset = Vector3.zero;
    }

    /// <summary>播放一次大爆炸：在爆点生成星球并炸开（也可由事件 / Timeline 调用）</summary>
    public void Trigger()
    {
        _center = ComputeCenter();   // 爆点：按触发这一刻的相机位置算，之后固定
        SpawnSeed();
        SpawnBlackHole();

        _elapsed = 0f;
        _playing = true;
        _moving = true;
        _delayTimer = 0f;
    }

    // 爆点：相机前方按偏移取点；没有相机时退回生成器 / 本物体位置
    Vector3 ComputeCenter()
    {
        if (targetCamera != null)
            return targetCamera.transform.position + targetCamera.transform.rotation * explosionOffset;
        if (spawner != null) return spawner.transform.position;
        return transform.position;
    }

    // 在爆点周围生成一批星球（"致密奇点"），并记录每颗的朝外方向与初速
    void SpawnSeed()
    {
        if (spawner == null)
        {
            Debug.LogWarning("[BigBangExplosion] 找不到 RandomPrefabSpawner，没有星球可生成", this);
            return;
        }

        _dirs.Clear();
        _speeds.Clear();
        _firstIndex = spawner.spawnedObjects.Count;

        spawner.SpawnAt(_center, seedRadius);

        // 每生成一颗就记录一条方向与初速，下标与 spawnedObjects 一一对应
        var planets = spawner.spawnedObjects;
        for (int i = _firstIndex; i < planets.Count; i++)
        {
            GameObject planet = planets[i];

            Vector3 dir = Random.onUnitSphere;   // 兜底：正好生在爆点上的用随机方向
            if (planet != null)
            {
                Vector3 v = planet.transform.position - _center;
                if (v.sqrMagnitude > 0.0001f) dir = v.normalized;
            }

            _dirs.Add(dir);
            _speeds.Add(Random.Range(minSpeed, maxSpeed));
        }
    }

    // 在爆点生成黑洞星球：爆炸后留在正中间的那一个
    void SpawnBlackHole()
    {
        if (blackHolePrefab == null) return;

        if (_blackHole != null) Destroy(_blackHole);
        _blackHole = Instantiate(blackHolePrefab, _center, Quaternion.identity);
    }

    // 两段式速度乘子：
    //   第一段：爆炸曲线（快速冲刺）
    //   第二段：从爆炸末速指数衰减到缓行比例（前段掉得快、后段拖着长尾慢慢趋近），
    //            driftDuration 走完后一直保持缓行比例 —— 永远不会是 0
    float SpeedMultiplier()
    {
        if (duration > 0.01f && _elapsed < duration)
            return blastCurve.Evaluate(_elapsed / duration);

        float start = blastCurve.Evaluate(1f);   // 爆炸段结尾的速度强度（两段衔接值）
        float tau = driftDuration > 0.01f
            ? Mathf.Clamp01((_elapsed - duration) / driftDuration)
            : 1f;

        // exp(-4τ)：先把爆炸的余速收掉，再拖着长尾慢慢磨到缓行比例
        return Mathf.Lerp(driftSpeedFloor, start, Mathf.Exp(-4f * tau));
    }

    // 每帧把这一批星球朝外推：速度 = （初速 + 距离 × 附加加速）× 两段速度乘子
    void BlastPlanets(float intensity)
    {
        if (intensity <= 0f || spawner == null) return;

        var planets = spawner.spawnedObjects;

        for (int i = 0; i < _dirs.Count; i++)
        {
            int index = _firstIndex + i;
            if (index >= planets.Count) break;

            GameObject planet = planets[index];
            if (planet == null) continue;

            Transform tf = planet.transform;
            float speed = (_speeds[i] + hubbleBoost * Vector3.Distance(tf.position, _center)) * intensity;
            tf.position += _dirs[i] * (speed * Time.deltaTime);
        }
    }

    void ApplyFov(float k)
    {
        if (targetCamera == null) return;
        targetCamera.fieldOfView = Mathf.Lerp(_baseFov, peakFov, k);
    }

    // 在场景里标出「爆炸会发生在哪」：黄色小球 + 相机到爆点的连线，外加奇点生成范围
    void OnDrawGizmosSelected()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        Vector3 point = cam.transform.position + cam.transform.rotation * explosionOffset;

        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(point, seedRadius);   // 奇点生成范围

        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(point, 1.5f);
        Gizmos.DrawLine(cam.transform.position, point);
    }
}
