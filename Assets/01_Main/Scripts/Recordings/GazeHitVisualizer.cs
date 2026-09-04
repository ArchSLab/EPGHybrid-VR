using UnityEngine;
using ViveSR.anipal.Eye;

public class GazeHitVisualizer : MonoBehaviour
{
    [Tooltip("用于显示交点的小球预制体")]
    public GameObject hitMarkerPrefab;

    [Tooltip("小球跟随交点的平滑度（0=立即更新，值越大越平滑）")]
    [Range(0, 0.5f)] public float smoothDampTime = 0.1f;

    private GameObject currentMarker; // 当前显示的小球
    private Vector3 velocity = Vector3.zero; // 平滑移动用的速度变量
    private Vector3 targetPosition; // 目标位置（交点）

    private void Start()
    {
        // 初始化小球（如果未赋值预制体，尝试从Resources加载）
        if (hitMarkerPrefab == null)
        {
            hitMarkerPrefab = Resources.Load<GameObject>("HitPointMarker");
            if (hitMarkerPrefab == null)
            {
                Debug.LogError("未找到HitPointMarker预制体，请检查Resources文件夹");
                enabled = false;
                return;
            }
        }

        // 创建小球实例并隐藏（初始无交点时不显示）
        currentMarker = Instantiate(hitMarkerPrefab);
        currentMarker.SetActive(false);
        currentMarker.name = "ActiveHitMarker"; // 重命名实例
    }

    /// <summary>
    /// 外部调用：更新交点位置（从眼动追踪脚本传入）
    /// </summary>
    public void UpdateHitPoint(Vector3 newHitPoint, bool isHit)
    {
        if (currentMarker == null) return;

        if (isHit)
        {
            // 有交点：更新目标位置并显示小球
            targetPosition = newHitPoint;
            currentMarker.SetActive(true);
        }
        else
        {
            // 无交点：隐藏小球
            currentMarker.SetActive(false);
            return;
        }

        // 平滑移动小球到目标位置（避免闪烁）
        currentMarker.transform.position = Vector3.SmoothDamp(
            currentMarker.transform.position,
            targetPosition,
            ref velocity,
            smoothDampTime
        );
    }
}