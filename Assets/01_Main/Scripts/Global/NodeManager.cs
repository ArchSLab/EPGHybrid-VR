using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class NodeManager : MonoBehaviour
{
    public static NodeManager Instance;

    [Header("节点显示设置")]
    [Tooltip("跳转完成后显示节点/目标物的延迟时间（秒）")]
    public float showDelay = 0.5f; // 保留延迟，仅删除距离判断

    // 管理两类对象：NodeMarker（节点）和Target（目标物）
    private List<GameObject> _allNodes = new List<GameObject>(); // Tag=NodeMarker的物体
    private List<GameObject> _allTargets = new List<GameObject>(); // Tag=Target的物体

    // 引用CSV解析器（需在Inspector拖拽赋值，或自动查找）
    public NodeVisibleCSVReader csvReader;

    private void Awake()
    {
        // 单例模式（不变）
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        // 自动查找CSV解析器（避免手动赋值遗漏）
        if (csvReader == null)
        {
            csvReader = FindObjectOfType<NodeVisibleCSVReader>();
            if (csvReader == null)
            {
                Debug.LogError("场景中未找到NodeVisibleCSVReader脚本，请先添加该脚本！");
            }
        }
    }

    private void Start()
    {
        // 1. 收集场景中所有NodeMarker（节点）和Target（目标物）
        CollectAllNodes();
        CollectAllTargets();
        // 2. 初始隐藏所有节点和目标物（避免初始状态混乱）
        HideAllNodesAndTargets();
    }

    #region 第一步：收集场景中的节点和目标物
    /// <summary>
    /// 收集所有Tag=NodeMarker的节点（原逻辑不变，新增ID验证）
    /// </summary>
    private void CollectAllNodes()
    {
        GameObject[] nodes = GameObject.FindGameObjectsWithTag("NodeMarker");
        foreach (var node in nodes)
        {
            // 排除父物体"Nodes"，只添加符合格式的子节点
            if (node.name != "Nodes")
            {
                _allNodes.Add(node);
            }
        }


        // 验证节点名称格式（需为"NodeX"，如Node2、Node37，否则无法提取ID）
        foreach (var node in _allNodes)
        {
            if (!TryGetIDFromName(node.name, "Node", out int nodeID))
            {
                Debug.LogWarning($"节点名称格式错误（无法提取ID）：{node.name}，需为'NodeX'格式（如Node2）");
            }
        }

        //Debug.Log($"已收集 {_allNodes.Count} 个NodeMarker节点");
    }

    /// <summary>
    /// 新增：收集所有Tag=Target的目标物
    /// </summary>
    private void CollectAllTargets()
    {
        GameObject[] targets = GameObject.FindGameObjectsWithTag("Target");
        foreach (var target in targets)
        {
            // 排除父物体"TargetObjects"，只添加符合格式的子节点
            if (target.name != "TargetObjects")
            {
                _allTargets.Add(target);
            }
        }

        // 验证目标物名称格式（需为"TargetX"，如Target55、Target67）
        foreach (var target in _allTargets)
        {
            if (!TryGetIDFromName(target.name, "Target", out int targetID))
            {
                Debug.LogWarning($"目标物名称格式错误（无法提取ID）：{target.name}，需为'TargetX'格式（如Target55）");
            }
        }

        //Debug.Log($"已收集 {_allTargets.Count} 个Target目标物");
    }
    #endregion

    #region 第二步：核心逻辑——按CSV数据显示/隐藏
    /// <summary>
    /// 对外方法：传入当前NodeID，按CSV显示对应节点和目标物（替换原ShowNearbyNodes）
    /// </summary>
    /// <param name="currentNodeID">当前所在的节点ID（如Node1的ID=1）</param>
    public void ShowVisibleObjectsByCSV(int currentNodeID)
    {
        // 检查CSV解析器是否就绪
        if (csvReader == null)
        {
            Debug.LogError("CSV解析器未就绪，无法显示节点/目标物！");
            return;
        }

        // 延迟显示（保留原延迟逻辑）
        StartCoroutine(ShowObjectsWithDelay(currentNodeID));
    }

    /// <summary>
    /// 延迟显示逻辑（替换原ShowNodesWithDelay）
    /// </summary>
    private IEnumerator ShowObjectsWithDelay(int currentNodeID)
    {
        yield return new WaitForSeconds(showDelay);

        //// 1. 从CSV获取当前节点的可见性数据
        //NodeVisibleCSVReader.NodeVisibleData visibleData = csvReader.GetVisibleData(currentNodeID);

        // 获取当前场景ID（需要先获取场景ID，这里假设从TaskTipSceneManager获取）
        int currentSceneID = TaskTipSceneManager.Instance.GetSceneIDByTaskOrder();
        NodeVisibleCSVReader.NodeVisibleData visibleData = csvReader.GetVisibleData(currentSceneID, currentNodeID);

        if (visibleData == null)
        {
            yield break; // 无数据则不处理
        }

        // 2. 处理NodeMarker节点：仅显示CSV中VisibleNodes列表的节点
        UpdateNodeVisibility(visibleData.VisibleNodeIDs);
        // 3. 处理Target目标物：仅显示CSV中VisibleEndnodes列表的目标物
        UpdateTargetVisibility(visibleData.VisibleEndnodeIDs);

        //Debug.Log($"按CSV显示完成：当前NodeID={currentNodeID}，显示节点{visibleData.VisibleNodeIDs.Count}个，显示目标物{visibleData.VisibleEndnodeIDs.Count}个");
    }

    /// <summary>
    /// 辅助：更新NodeMarker节点的显示/隐藏
    /// </summary>
    private void UpdateNodeVisibility(List<int> visibleNodeIDs)
    {
        foreach (var node in _allNodes)
        {
            if (node == null) continue;

            // 提取节点ID（如"Node2"提取ID=2）
            if (TryGetIDFromName(node.name, "Node", out int nodeID))
            {
                // 判断ID是否在CSV的VisibleNodes列表中
                bool shouldShow = visibleNodeIDs.Contains(nodeID);
                node.SetActive(shouldShow);

                // 激活节点的父对象（避免父对象隐藏导致节点不显示，保留原逻辑）
                if (shouldShow && node.transform.parent != null)
                {
                    node.transform.parent.gameObject.SetActive(true);
                }
            }
        }
    }

    /// <summary>
    /// 辅助：更新Target目标物的显示/隐藏
    /// </summary>
    private void UpdateTargetVisibility(List<int> visibleTargetIDs)
    {
        foreach (var target in _allTargets)
        {
            if (target == null) continue;

            // 提取目标物ID（如"Target55"提取ID=55）
            if (TryGetIDFromName(target.name, "Target", out int targetID))
            {
                // 判断ID是否在CSV的VisibleEndnodes列表中
                bool shouldShow = visibleTargetIDs.Contains(targetID);
                target.SetActive(shouldShow);

                // 激活目标物的父对象（同理，避免父对象隐藏）
                if (shouldShow && target.transform.parent != null)
                {
                    target.transform.parent.gameObject.SetActive(true);
                }
            }
        }
    }
    #endregion

    #region 辅助方法：隐藏所有对象 + 提取ID
    /// <summary>
    /// 新增：隐藏所有节点和目标物（替代原HideAllNodes）
    /// </summary>
    public void HideAllNodesAndTargets()
    {
        // 隐藏所有NodeMarker
        foreach (var node in _allNodes)
        {
            if (node != null) node.SetActive(false);
        }
        // 隐藏所有Target
        foreach (var target in _allTargets)
        {
            if (target != null) target.SetActive(false);
        }
        //Debug.Log("已隐藏所有节点和目标物");
    }

    /// <summary>
    /// 辅助：从物体名称中提取ID（如"NodeX"→X，"TargetY"→Y）
    /// </summary>
    /// <param name="objName">物体名称（如Node2、Target55）</param>
    /// <param name="prefix">前缀（如"Node"、"Target"）</param>
    /// <param name="id">提取出的ID（失败则为-1）</param>
    /// <returns>提取成功返回true</returns>
    private bool TryGetIDFromName(string objName, string prefix, out int id)
    {
        id = -1;
        // 忽略父物体名称（Nodes或TargetObjects）
        if (objName == "Nodes" || objName == "TargetObjects")
        {
            return false; // 父物体不参与ID校验
        }
        if (!objName.StartsWith(prefix))
        {
            return false; // 名称不包含指定前缀（Node或Target）
        }

        // 提取前缀后的字符串并尝试转换为数字
        string idStr = objName.Substring(prefix.Length);
        return int.TryParse(idStr, out id);
    }
    #endregion
}