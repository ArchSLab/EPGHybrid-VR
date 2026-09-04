using UnityEngine;

public class TransparentBreath : MonoBehaviour
{
    [Header("呼吸效果参数")]
    public float breathPeriod = 3f;
    public Vector2 alphaRange = new Vector2(0.5f, 1f); // 透明度范围

    private Material targetMaterial;
    private float timer = 0f;

    void Start()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            targetMaterial = renderer.material;
            // 确保材质支持透明度（以Standard着色器为例）
            targetMaterial.SetFloat("_Mode", 3f); // 设置为Transparent模式
            targetMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            targetMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            targetMaterial.SetInt("_ZWrite", 0);
            targetMaterial.DisableKeyword("_ALPHATEST_ON");
            targetMaterial.EnableKeyword("_ALPHABLEND_ON");
            targetMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
    }

    void Update()
    {
        if (targetMaterial == null) return;

        timer += Time.deltaTime;
        float t = Mathf.Sin(timer / breathPeriod * Mathf.PI * 2f) * 0.5f + 0.5f;
        float currentAlpha = Mathf.Lerp(alphaRange.x, alphaRange.y, t);

        Color color = targetMaterial.color;
        color.a = currentAlpha;
        targetMaterial.color = color;
    }
}