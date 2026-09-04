using UnityEngine;
using System.Collections;
using System;
using System.IO;
using ViveSR.anipal.Eye;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class VRGameManager : MonoBehaviour
{
    [Header("初始化设置")]
    public string initialNode;
    public string csvPath = "ReadData/NodeInfo.csv";
    [Header("依赖引用")]
    public PanoramaMaterialManager panoramaMgr;
    public Transform cameraRig;
    public FadeEffectManager fadeEffectMgr;
    [Header("VR相机设置")]
    public Transform vrCamera;
    [Header("相机高度设置")]
    public float fixedCameraY;
    private bool lastEyeTrackingState = false;
    private NodeData currentNode; // 新增：用于跟踪当前节点的成员变量  <-- 添加这一行

    private TargetValidator targetValidator;


    private void Start()
    {
        // 自动获取未赋值的依赖引用
        if (panoramaMgr == null)
            panoramaMgr = FindObjectOfType<PanoramaMaterialManager>();
        if (cameraRig == null)
            cameraRig = GameObject.Find("CameraRig").transform;
        if (fadeEffectMgr == null)
            fadeEffectMgr = FindObjectOfType<FadeEffectManager>();
        if (vrCamera == null)
            vrCamera = cameraRig.Find("Camera");



        // 1. 从SceneManagerScript获取TaskOrder（静态变量或静态字典，二选一即可)
        int taskOrder = SceneManagerScript.taskOrder;

        // 2. 加载TaskList.csv并通过TaskOrder获取StartPoint（移除多余的int关键字）
        string taskListPath = Path.Combine(Application.streamingAssetsPath, "ReadData/TaskList.csv");
        TaskListParser parser = new TaskListParser();
        if (parser.ParseCSV(taskListPath))
        {
            string startPoint = parser.GetStartPointByTaskOrder(taskOrder); // 修正：移除int，传递变量
            if (!string.IsNullOrEmpty(startPoint))
            {
                initialNode = "Node" + startPoint; // 拼接成NodeX格式（如Node1）
            }
            else
            {
                Debug.LogError($"未找到TaskOrder为{taskOrder}对应的StartPoint，使用默认初始节点Node1");
                initialNode = "Node1";
            }
        }
        else
        {
            Debug.LogError($"TaskList.csv解析失败，使用默认初始节点Node1");
            initialNode = "Node1";
        }

        Debug.Log($"初始节点设置为: {initialNode}");
        StartCoroutine(CSVReader.LoadCSV(csvPath, OnCSVLoaded));

    }

    private void OnCSVLoaded()
    {
        int sceneID = TaskTipSceneManager.Instance.GetSceneIDByTaskOrder();
        string compositeKey = $"{sceneID}_{initialNode.Substring(4)}";
        if (CSVReader.AllNodeData.ContainsKey(compositeKey))
        {
            NodeData node = CSVReader.AllNodeData[compositeKey];

            // 新增：从当前节点获取TranslateY并取反，计算相机高度
            float heightFromStartScene = SceneManagerScript.heightValue;
            fixedCameraY = -node.TranslateY - heightFromStartScene / 100f; // 核心修改
            //Debug.Log($"相机固定高度: {fixedCameraY} (来源Node{node.NodeID}的TranslateY取反: {-node.TranslateY}，StartScene身高: {heightFromStartScene})");

            cameraRig.position = new Vector3(node.Pos_X, fixedCameraY, node.Pos_Z);
            currentNode = node; // 初始化当前节点  <-- 添加这一行
            //Debug.Log($"初始位置设置: {initialNode}");
            StartCoroutine(TransitionToNode(node, () =>
            {
                if (NodeManager.Instance != null)
                {
                    if (int.TryParse(initialNode.Substring(4), out int initialNodeID))
                    {
                        NodeManager.Instance.ShowVisibleObjectsByCSV(initialNodeID);
                    }
                    else
                    {
                        Debug.LogError($"初始节点ID解析失败: {initialNode}，格式应为NodeX（如Node1）");
                    }
                }
            }));
        }
        else
        {
            Debug.LogError($"初始节点{initialNode}不存在");
        }

        targetValidator = FindObjectOfType<TargetValidator>();
        if (targetValidator != null)
        {
            // 假设你有默认的目标对象或需要打印当前任务的目标列表
            List<int> validTargets = targetValidator.GetCurrentTaskValidTargets();
            if (validTargets.Count > 0)
            {
                Debug.Log($"当前任务[{targetValidator.currentTaskOrder}]的有效目标列表：{string.Join(", ", validTargets)}");
            }
        }

    }

    // 新增：初始化第一个节点时先加载全景图
    private IEnumerator InitializeFirstNode(NodeData node)
    {
        // 初始节点需要同步加载全景图，避免黑屏
        int currentSceneID = TaskTipSceneManager.Instance.GetSceneIDByTaskOrder();
        yield return panoramaMgr.UpdatePanorama(node.NodeID, currentSceneID.ToString());

        // 再执行过渡动画
        StartCoroutine(TransitionToNode(node, () =>
        {
            if (NodeManager.Instance != null)
            {
                if (int.TryParse(initialNode.Substring(4), out int initialNodeID))
                {
                    NodeManager.Instance.ShowVisibleObjectsByCSV(initialNodeID);
                }
                else
                {
                    Debug.LogError($"初始节点ID解析失败: {initialNode}，格式应为NodeX（如Node1）");
                }
            }
        }));
    }


    public void SwitchToNode(string sceneId, string nodeId, Action onComplete)
    {
        // 从 nodeId 中提取纯数字部分（比如 Node1 → 1）
        if (int.TryParse(nodeId, out int nodeNumber))
        {
            string compositeKey = $"{sceneId}_{nodeNumber}";
            if (CSVReader.AllNodeData.TryGetValue(compositeKey, out NodeData node))
            {
                StartCoroutine(TransitionToNode(node, onComplete));
                return;
            }
            else
            {
                Debug.LogError($"复合键为 {compositeKey} 的节点不存在");
                return;
            }
        }
        else
        {
            Debug.LogError($"节点ID {nodeId} 格式不正确，应为纯数字（如 1, 2, 3）");
            return;
        }
    }

    public IEnumerator TransitionToNode(NodeData targetNode, Action onComplete)
    {
        // 1. 等待淡出完全完成（确保画面已完全黑屏）
        yield return fadeEffectMgr.FadeOut();
        //// 额外等待一帧，确保渲染管线同步（可选但更稳妥）
        //yield return null;

        // 2. 在完全黑屏环境下执行所有可能导致画面异常的操作
        if (NodeManager.Instance != null)
        {
            NodeManager.Instance.HideAllNodesAndTargets();
        }

        // 切换节点时更新相机高度（可选，根据需求决定是否启用）
        float heightFromStartScene = SceneManagerScript.heightValue;
        fixedCameraY = -targetNode.TranslateY - heightFromStartScene / 100f;
        //Debug.Log($"切换到节点{targetNode.NodeID}，相机高度更新为: {fixedCameraY}");

        // 设置相机位置（可能导致拉伸，在黑屏中执行）
        cameraRig.position = new Vector3(targetNode.Pos_X, fixedCameraY, targetNode.Pos_Z);
        currentNode = targetNode; // 更新当前节点  <-- 添加这一行

        // 加载全景图（最耗时操作，在黑屏中等待完成）
        int currentSceneID = TaskTipSceneManager.Instance.GetSceneIDByTaskOrder();
        yield return panoramaMgr.UpdatePanorama(targetNode.NodeID, currentSceneID.ToString());

        // 3. 所有加载和设置完成后，执行淡入（显示正常画面）
        yield return fadeEffectMgr.FadeIn();

        // 4. 淡入完成后，在后台加载全景图和显示节点（核心改动：从淡入前移到淡入后）
        yield return StartCoroutine(LoadPanoramaAndShowObjects(targetNode));

        // 5. 执行回调
        onComplete?.Invoke();

        //// 4. 淡入完成后，执行后续逻辑（不影响画面显示）
        //if (NodeManager.Instance != null)
        //{
        //    if (int.TryParse(targetNode.NodeID, out int nodeID))
        //    {
        //        NodeManager.Instance.ShowVisibleObjectsByCSV(nodeID);
        //    }
        //    else
        //    {
        //        Debug.LogError("Failed to parse NodeID to int: " + targetNode.NodeID);
        //    }

        //    var eyeTracker = FindObjectOfType<EyeTrackingDataSaver>();
        //    if (eyeTracker != null)
        //    {
        //        // 移除条件判断，始终启动记录（即使眼动追踪关闭）
        //        eyeTracker.StartRecording();

        //        // 仅更新状态日志
        //        if (!SceneManagerScript.isEyeTrackingClosed)
        //        {
        //            if (lastEyeTrackingState != SceneManagerScript.isEyeTrackingClosed)
        //            {
        //                Debug.Log("眼动追踪记录已开启");
        //                lastEyeTrackingState = SceneManagerScript.isEyeTrackingClosed;
        //            }
        //        }
        //        else
        //        {
        //            if (lastEyeTrackingState != SceneManagerScript.isEyeTrackingClosed)
        //            {
        //                Debug.Log("眼动追踪记录已关闭（数据将记为0）");
        //                lastEyeTrackingState = SceneManagerScript.isEyeTrackingClosed;
        //            }
        //        }
        //    }
        //}

        //if (PathRecording.Instance != null)
        //{
        //    PathRecording.Instance.RecordNode(targetNode.NodeID, cameraRig.position);
        //}
        //Debug.Log($"已切换到节点: {targetNode.NodeID}");
        //onComplete?.Invoke();
    }

    public string GetCurrentNodeID()
    {

        if (currentNode != null)
        {
            return "Node" + currentNode.NodeID; // 修正返回格式，与节点命名一致
        }
        return "Unknown";
    }


    // 监听场景加载完成事件（双重保险）
    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoadedComplete;
    }

    private void OnSceneLoadedComplete(Scene scene, LoadSceneMode mode)
    {
        // 仅在目标场景执行控制（根据场景名称筛选）
        if (scene.name.Contains("TargetScene") || scene.buildIndex > 1)
        {
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedComplete;
    }

    /// <summary>
    /// 后台加载全景图并更新显示（在淡入后执行）
    /// </summary>
    private IEnumerator LoadPanoramaAndShowObjects(NodeData targetNode)
    {
        int currentSceneID = TaskTipSceneManager.Instance.GetSceneIDByTaskOrder();
        // 后台加载全景图
        yield return panoramaMgr.UpdatePanorama(targetNode.NodeID, currentSceneID.ToString());

        // 加载完成后显示节点物体
        if (NodeManager.Instance != null)
        {
            if (int.TryParse(targetNode.NodeID, out int nodeID))
            {
                NodeManager.Instance.ShowVisibleObjectsByCSV(nodeID);
            }
            else
            {
                Debug.LogError("Failed to parse NodeID to int: " + targetNode.NodeID);
            }

            var eyeTracker = FindObjectOfType<EyeTrackingDataSaver>();
            if (eyeTracker != null)
            {
                eyeTracker.StartRecording();

                if (!SceneManagerScript.isEyeTrackingClosed)
                {
                    if (lastEyeTrackingState != SceneManagerScript.isEyeTrackingClosed)
                    {
                        Debug.Log("眼动追踪记录已开启");
                        lastEyeTrackingState = SceneManagerScript.isEyeTrackingClosed;
                    }
                }
                else
                {
                    if (lastEyeTrackingState != SceneManagerScript.isEyeTrackingClosed)
                    {
                        Debug.Log("眼动追踪记录已关闭（数据将记为0）");
                        lastEyeTrackingState = SceneManagerScript.isEyeTrackingClosed;
                    }
                }
            }
        }

        if (PathRecording.Instance != null)
        {
            PathRecording.Instance.RecordNode(targetNode.NodeID, cameraRig.position);
        }
        Debug.Log($"已切换到节点: {targetNode.NodeID}");
    }
}