using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 随机区域生成器：在指定的 3D 区域内以随机位置、随机朝向生成一批预制体。
///   区域形状支持盒体（Box）与球体（Sphere），以本物体为中心，
///   并跟随本物体的位置与旋转（旋转挂脚本的物体即可旋转整个区域）。
///   生成后的物体会一直保留在场景中，不做回收或销毁。
/// 可以勾选 spawnOnStart 自动生成，也可以在代码中调用 Spawn() 手动生成。
///   也可以调用 SpawnAt(中心, 半径) 在指定位置爆炸式生成一批（供宇宙大爆炸演出用）。
/// </summary>
[DisallowMultipleComponent]
public class RandomPrefabSpawner : MonoBehaviour
{
    public enum RegionShape { Box, Sphere }

    [Header("生成内容")]
    [Tooltip("要生成的预制体，可放多个，每次随机挑一个")]
    public GameObject[] prefabs;

    [Tooltip("生成数量")]
    public int count = 10;

    [Tooltip("启用时自动生成一次；关闭则只能调用 Spawn() 手动生成")]
    public bool spawnOnStart = true;

    [Tooltip("运行时自动记录的已生成实例（供其它脚本读取，例如宇宙膨胀演出）")]
    public List<GameObject> spawnedObjects = new List<GameObject>();

    [Header("生成区域")]
    [Tooltip("区域形状：盒体 / 球体")]
    public RegionShape shape = RegionShape.Box;

    [Tooltip("盒体区域尺寸（长/高/宽，世界单位）")]
    public Vector3 boxSize = new Vector3(10f, 5f, 10f);

    [Tooltip("球体区域半径（世界单位）")]
    public float sphereRadius = 5f;

    [Header("随机朝向")]
    [Tooltip("生成时使用随机朝向；关闭则保持预制体自身朝向")]
    public bool randomRotation = true;

    void Start()
    {
        if (spawnOnStart) Spawn();
    }

    /// <summary>按设置的 count 在区域内随机生成一批预制体</summary>
    public void Spawn()
    {
        Spawn(count);
    }

    /// <summary>在区域内随机生成指定数量的预制体（生成后不会销毁）</summary>
    public void Spawn(int amount)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning("RandomPrefabSpawner：没有指定预制体", this);
            return;
        }

        for (int i = 0; i < amount; i++)
        {
            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab == null) continue;

            Vector3 position = transform.TransformPoint(RandomLocalPoint());
            Quaternion rotation = randomRotation ? Random.rotation : prefab.transform.rotation;
            spawnedObjects.Add(Instantiate(prefab, position, rotation));
        }
    }

    /// <summary>
    /// 在指定世界位置、指定半径的球形区域内爆炸式生成一批（数量沿用 count）。
    /// 供宇宙大爆炸等特殊演出调用，不影响也不修改本物体自身的区域设置。
    /// </summary>
    public void SpawnAt(Vector3 worldCenter, float radius)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning("RandomPrefabSpawner：没有指定预制体", this);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab == null) continue;

            Vector3 position = worldCenter + Random.insideUnitSphere * radius;
            Quaternion rotation = randomRotation ? Random.rotation : prefab.transform.rotation;
            spawnedObjects.Add(Instantiate(prefab, position, rotation));
        }
    }

    // 在本地空间取随机点：球体用 insideUnitSphere（体积均匀分布），盒体逐轴独立随机
    Vector3 RandomLocalPoint()
    {
        if (shape == RegionShape.Sphere)
            return Random.insideUnitSphere * sphereRadius;

        return new Vector3(
            Random.Range(-boxSize.x * 0.5f, boxSize.x * 0.5f),
            Random.Range(-boxSize.y * 0.5f, boxSize.y * 0.5f),
            Random.Range(-boxSize.z * 0.5f, boxSize.z * 0.5f));
    }

    void OnDrawGizmosSelected()
    {
        // 用 gizmo 显示生成区域（会跟随本物体的旋转）
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.5f);

        if (shape == RegionShape.Sphere)
            Gizmos.DrawWireSphere(Vector3.zero, sphereRadius);
        else
            Gizmos.DrawWireCube(Vector3.zero, boxSize);

        Gizmos.matrix = Matrix4x4.identity;
    }
}
