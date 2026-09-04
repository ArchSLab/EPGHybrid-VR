using UnityEngine;

public class FluorescentBreath : MonoBehaviour
{
    [Header("呼吸效果参数")]
    [Tooltip("呼吸周期（秒）")]
    public float breathPeriod = 3f;
    [Tooltip("自发光强度范围")]
    public Vector2 emissionIntensityRange = new Vector2(1f, 3f);

    private Material targetMaterial; // 目标材质
    private float timer = 0f;       // 时间计数器

    void Start()
    {
        // 获取物体的材质（假设预制体只有一个材质）
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            targetMaterial = renderer.material;
        }
        else
        {
            Debug.LogError("物体没有Renderer组件，无法实现呼吸效果！");
        }
    }

    void Update()
    {
        if (targetMaterial == null) return;

        // 计算当前自发光强度（基于正弦曲线，实现缓入缓出）
        timer += Time.deltaTime;
        float t = Mathf.Sin(timer / breathPeriod * Mathf.PI * 2f) * 0.5f + 0.5f; // t在0~1之间循环
        float currentIntensity = Mathf.Lerp(emissionIntensityRange.x, emissionIntensityRange.y, t);

        // 设置材质的自发光强度
        Color emissionColor = targetMaterial.GetColor("_EmissionColor");
        emissionColor *= currentIntensity;
        targetMaterial.SetColor("_EmissionColor", emissionColor);
    }
}