using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

[RequireComponent(typeof(LineRenderer))]
public class VRRayInteractor : MonoBehaviour
{
    // 射线相关参数
    public float rayLength = 10f; // 射线长度
    public LayerMask interactLayer; // 可交互层（建议将按钮放在单独层，如"UI_Interact"）
    private LineRenderer lineRenderer; // 射线视觉效果
    private RaycastHit hitInfo; // 碰撞信息
    private GameObject currentHitObject; // 当前碰撞的物体

    // SteamVR 输入动作（拇指键触发）
    public SteamVR_Action_Boolean triggerClickAction;

    // 新增：是否击中UI（供场景射线判断是否被阻挡）
    public bool IsHittingUI => currentHitObject != null;

    void Awake()
    {
        // 初始化射线渲染器
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2; // 射线由两个点组成（起点和终点）
        lineRenderer.enabled = true;
    }

    void Update()
    {
        // 更新射线位置
        UpdateRay();

        // 检测拇指键按下，且当前有碰撞到可交互物体
        if (triggerClickAction.GetStateDown(SteamVR_Input_Sources.Any) && currentHitObject != null)
        {
            // 触发按钮点击事件
            TriggerButtonClick(currentHitObject);
        }
    }

    // 更新射线的位置和视觉效果
    private void UpdateRay()
    {
        // 射线起点：控制器位置
        Vector3 rayOrigin = transform.position;
        // 射线方向：控制器正前方
        Vector3 rayDirection = transform.forward;

        // 设置射线起点
        lineRenderer.SetPosition(0, rayOrigin);

        // 射线检测
        if (Physics.Raycast(rayOrigin, rayDirection, out hitInfo, rayLength, interactLayer))
        {
            // 射线命中物体
            lineRenderer.SetPosition(1, hitInfo.point); // 终点为碰撞点
            currentHitObject = hitInfo.collider.gameObject;
            Debug.Log($"[VRRay] Hit: {currentHitObject.name}");
        }
        else
        {
            // 未命中物体，射线终点为最大长度
            lineRenderer.SetPosition(1, rayOrigin + rayDirection * rayLength);
            currentHitObject = null;
        }
    }

    // 触发按钮的点击事件
    private void TriggerButtonClick(GameObject target)
    {
        // 检测目标是否是UI按钮
        UnityEngine.UI.Button button = target.GetComponent<UnityEngine.UI.Button>();
        if (button != null && button.interactable)
        {
            button.onClick.Invoke(); // 触发按钮的OnClick事件
            // 可选：添加手柄震动反馈
            Hand hand = GetComponentInParent<Hand>();
            if (hand != null)
            {
                hand.TriggerHapticPulse(1000); // 震动强度（0-3999）
            }
        }
    }
}