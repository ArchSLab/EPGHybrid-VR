using UnityEngine;
public class FloatScriptLeave : MonoBehaviour
{
    public float floatHeight = 0.2f; // 浮动幅度（可保留原名，实际是前后偏移范围）
    public float floatSpeed = 1f;
    private Vector3 initialPosition;

    void Start()
    {
        initialPosition = transform.position;
    }

    void Update()
    {
        // 1. 计算偏移量（逻辑不变，仅变量名语义调整）
        float forwardOffset = Mathf.Sin(Time.time * floatSpeed) * floatHeight;
        // 2. 关键修改：用transform.forward替代世界Z轴，贴合箭头方向
        transform.position = initialPosition + transform.forward * forwardOffset;
    }
}