using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class FadeEffectManager : MonoBehaviour
{
    [Header("淡入淡出设置")]
    public float fadeDuration = 1f;
    public Color fadeColor = Color.black;

    [Header("VR相机引用")]
    [Tooltip("拖入VR场景中的主相机（如VRCamera）")]
    public Camera vrCamera; // 手动指定VR相机，避免自动查找失败

    private Image fadeImage;
    private Canvas fadeCanvas;

    private void Awake()
    {
        CreateFadeUI();
    }

    private void Start()
    {
        // 自动查找VR相机（如果未手动指定）
        if (vrCamera == null)
        {
            vrCamera = FindObjectOfType<Camera>();
            if (vrCamera == null)
            {
                Debug.LogError("未找到VR相机，请手动指定！");
            }
        }

        // 确保Canvas正确关联相机并处于视野中
        UpdateWorldSpaceCanvasPosition();
    }

    /// <summary>
    /// 创建WorldSpace模式的Canvas（VR兼容）
    /// </summary>
    private void CreateFadeUI()
    {
        // 创建Canvas
        GameObject canvasObj = new GameObject("FadeCanvas");
        fadeCanvas = canvasObj.AddComponent<Canvas>();
        fadeCanvas.renderMode = RenderMode.WorldSpace; // 使用世界空间模式（VR兼容）
        fadeCanvas.sortingOrder = int.MaxValue;

        // 关键：设置Canvas尺寸为“超宽超高”，确保覆盖视野
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(100f, 100f); // 大幅增大尺寸（可根据需求调整，比如200x200）
        canvasRect.localScale = new Vector3(0.1f, 0.1f, 0.1f); // 配合距离，避免过大

        // 添加CanvasScaler（确保UI缩放正确）
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPhysicalSize;
        scaler.physicalUnit = CanvasScaler.Unit.Points;
        scaler.fallbackScreenDPI = 96;

        // 添加全屏Image
        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform);
        fadeImage = imageObj.AddComponent<Image>();
        fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, 0); // 初始透明

        // 设置Image铺满Canvas
        RectTransform imageRect = imageObj.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 将Canvas定位到相机正前方（确保覆盖视野）
    /// </summary>
    private void UpdateWorldSpaceCanvasPosition()
    {
        if (vrCamera != null && fadeCanvas != null)
        {
            // 将Canvas放在相机正前方2米处（距离可调整，比如改为1.5f更贴近）
            fadeCanvas.transform.position = vrCamera.transform.position + vrCamera.transform.forward * 0.5f;
            // 让Canvas严格面向相机（避免角度偏差导致显示不全）
            fadeCanvas.transform.rotation = vrCamera.transform.rotation;
            // 可选：若Canvas尺寸仍不够，可微调localScale进一步放大
            // fadeCanvas.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
        }
    }

    /// <summary>
    /// 淡出效果（透明→不透明）
    /// </summary>
    public IEnumerator FadeOut()
    {
        float elapsed = 0;
        float startAlpha = fadeImage.color.a; // 记录起始透明度
        float targetAlpha = 1f;

        while (elapsed < fadeDuration)
        {
            float t = elapsed / fadeDuration;
            // 仅插值透明度，保持RGB为目标颜色（黑色）
            float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, currentAlpha);
            elapsed += Time.deltaTime;
            yield return null;
        }
        // 确保最终完全不透明
        fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, targetAlpha);
    }

    /// <summary>
    /// 淡入效果：从黑色逐渐透明
    /// </summary>
    public IEnumerator FadeIn()
    {
        float elapsed = 0;
        float startAlpha = fadeImage.color.a; // 记录起始透明度
        float targetAlpha = 0f;

        while (elapsed < fadeDuration)
        {
            float t = elapsed / fadeDuration;
            // 仅插值透明度
            float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, currentAlpha);
            elapsed += Time.deltaTime;
            yield return null;
        }
        // 确保最终完全透明
        fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, targetAlpha);
    }

    // 每帧更新Canvas位置（防止相机移动导致Canvas偏离视野）
    private void LateUpdate()
    {
        UpdateWorldSpaceCanvasPosition();
    }
}
