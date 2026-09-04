using UnityEngine;

public class FloatScript : MonoBehaviour
{
    // 悬浮的高度范围
    public float floatHeight = 0.2f;
    // 悬浮的速度
    public float floatSpeed = 1f;

    // 初始位置（用于计算相对位移）
    private Vector3 initialPosition;

    void Start()
    {
        // 记录初始位置
        initialPosition = transform.position;
    }

    void Update()
    {
        // 用正弦函数计算Y轴的偏移，实现周期性上下浮动
        float yOffset = Mathf.Sin(Time.time * floatSpeed) * floatHeight;
        // 更新物体位置
        transform.position = initialPosition + new Vector3(0, yOffset, 0);
    }
}