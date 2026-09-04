using UnityEngine;
using Valve.VR;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;

/// <summary>
/// 手柄射线检测与节点跳转逻辑（仅加载点击节点，取消关联节点批量加载）
/// </summary>
public class VRControllerRay : MonoBehaviour
{
    public TargetValidator targetValidator;

    [Header("VR输入配置")]
    public SteamVR_Action_Boolean triggerPress; // 扳机键按下动作
    public SteamVR_Action_Boolean thumbPress;  // 拇指键输入（用于NodeMarker）
    public SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.RightHand; // 右手柄

    [Header("射线配置")]
    public LineRenderer rayRenderer; // 射线渲染组件
    public float rayLength = 15f;    // 射线最大长度
    public Color rayNormal = Color.red;   // 默认射线色
    public Color rayHover = Color.green;  // 对准节点时射线色
    public Color rayTargetColor = Color.blue; // 检测到Target时的射线颜色

    [Header("依赖引用")]
    public Transform cameraRig; // 引用CameraRig（控制相机位置）
    private Transform hoverNode; // 当前对准的节点
    private bool rayActive = true; // 射线是否激活
    private VRGameManager gameManager;

    // 新增：引用UI射线组件
    public VRRayInteractor uiRayInteractor;

    private void Awake()
    {
        // 初始化射线渲染器
        if (rayRenderer == null)
            rayRenderer = gameObject.AddComponent<LineRenderer>();

        rayRenderer.startWidth = 0.01f;
        rayRenderer.endWidth = 0.01f;
        rayRenderer.positionCount = 2; // 射线由2个点组成
        rayRenderer.enabled = rayActive;

        // 初始化VRGameManager
        gameManager = FindObjectOfType<VRGameManager>();
        if (gameManager == null)
            Debug.LogError("未找到VRGameManager实例！");
    }

    private void Start()
    {
        // 自动查找CameraRig（若未手动引用）
        if (cameraRig == null)
            cameraRig = GameObject.Find("CameraRig").transform;

        // 检查扳机键动作引用
        if (triggerPress == null)
            Debug.LogError("请引用SteamVR扳机键动作！");
        if (thumbPress == null)
            Debug.LogError("请引用SteamVR拇指键动作！");
    }

    private void Update()
    {
        if (!rayActive) return;

        // 新增：如果UI射线击中了UI，场景射线不执行任何交互
        if (uiRayInteractor != null && uiRayInteractor.IsHittingUI)
        {
            // 仅更新射线显示，但不处理交互
            UpdateRayVisualsWithoutInteraction();
            return;
        }

        Vector3 rayStart = transform.position + transform.forward * 0.1f;
        Vector3 rayDir = transform.forward;
        Vector3 rayEnd = rayStart + rayDir * rayLength;

        RaycastHit hit;
        bool hasHit = Physics.Raycast(rayStart, rayDir, out hit, rayLength, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);

        if (hasHit)
        {
            // 优先检测Target标签
            if (hit.collider.CompareTag("Target"))
            {
                rayEnd = hit.point;
                rayRenderer.startColor = rayTargetColor;
                rayRenderer.endColor = rayTargetColor;
            }
            // 原有NodeMarker检测逻辑
            else if (hit.collider.CompareTag("NodeMarker"))
            {
                rayEnd = hit.point;
                rayRenderer.startColor = rayHover;
                rayRenderer.endColor = rayHover;
                hoverNode = hit.collider.transform;
            }
        }
        else
        {
            // 未命中任何物体时的默认状态
            rayRenderer.startColor = rayNormal;
            rayRenderer.endColor = rayNormal;
        }

        rayRenderer.SetPosition(0, rayStart);
        rayRenderer.SetPosition(1, rayEnd);

        if (triggerPress.GetStateDown(inputSource) && hit.collider != null)
        {
            // 优先判断是否命中Target
            if (hit.collider.CompareTag("Target"))
            {
                if (targetValidator == null)
                {
                    Debug.LogError("请在Inspector中指定TargetValidator组件");
                    return;
                }

                if (targetValidator.ValidateTarget(hit.collider.gameObject))
                {
                    TargetClickHandler targetHandler = hit.collider.GetComponent<TargetClickHandler>();
                    if (targetHandler != null)
                    {
                        targetHandler.OnTargetClicked();
                    }
                    else
                    {
                        RecordTargetAndDelayJump(hit.collider.gameObject);
                    }
                }
            }
            // 再判断是否命中NodeMarker（跳转点）
            else if (hit.collider.CompareTag("NodeMarker"))
            {
                // 触发节点跳转（仅加载当前点击节点）
                JumpToNode(hit.collider.name);
            }
        }
    }

    // 仅更新射线显示，不处理交互（当UI被击中时）
    private void UpdateRayVisualsWithoutInteraction()
    {
        Vector3 rayStart = transform.position + transform.forward * 0.1f;
        Vector3 rayDir = transform.forward;
        Vector3 rayEnd = rayStart + rayDir * rayLength;

        rayRenderer.startColor = rayNormal;
        rayRenderer.endColor = rayNormal;
        rayRenderer.SetPosition(0, rayStart);
        rayRenderer.SetPosition(1, rayEnd);
    }

    /// <summary>
    /// 手动记录Target并延迟跳转（备用）
    /// </summary>
    private void RecordTargetAndDelayJump(GameObject targetObj)
    {
        if (PathRecording.Instance == null) return;

        // 解析TargetID
        string targetName = targetObj.name;
        string targetID = Regex.Replace(targetName, "[^0-9]", "");
        if (string.IsNullOrEmpty(targetID))
            targetID = "0";

        // 记录Target数据
        PathRecording.Instance.RecordNode(
            nodeID: "0",
            position: targetObj.transform.position,
            targetID: targetID
        );
        Debug.Log($"手动记录Target命中数据：TargetID={targetID}");

        // 延迟跳转
        StartCoroutine(DelayLoadEndScene(0.1f));
    }

    /// <summary>
    /// 延迟加载EndScene的协程
    /// </summary>
    private IEnumerator DelayLoadEndScene(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene("EndScene");
    }

    /// <summary>
    /// 跳转到目标节点（仅加载当前点击节点，取消关联节点批量加载）
    /// </summary>
    private void JumpToNode(string targetNodeID)
    {
        // 解析场景ID和节点ID
        string currentSceneName = SceneManager.GetActiveScene().name;
        string sceneIDStr = currentSceneName.Replace("Scene", "");
        int sceneID = int.Parse(sceneIDStr);
        string nodeNumber = targetNodeID.Replace("Node", "");
        int targetNodeIDNum = int.Parse(nodeNumber);

        // 核心修改：仅加载当前点击的节点（取消关联节点批量加载）
        if (PanoramaMaterialManager.Instance != null)
        {
            // 仅加载点击的单个节点，而非关联节点列表
            StartCoroutine(PanoramaMaterialManager.Instance.PreloadPanorama(targetNodeID, sceneID.ToString()));
        }

        // 原有节点跳转逻辑（保持不变）
        string compositeKey = $"{sceneIDStr}_{nodeNumber}";
        if (!CSVReader.AllNodeData.ContainsKey(compositeKey))
        {
            Debug.LogError($"目标节点不存在: {compositeKey}");
            return;
        }
        Transform nodeToHide = hoverNode;
        gameManager.SwitchToNode(sceneIDStr, nodeNumber, () => { });
    }
}