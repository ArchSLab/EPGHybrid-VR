using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine.Networking;
using System.Text; // 新增的命名空间引用，用于 StringBuilder

/// <summary>
/// 解析NodeVisible.csv，支持列内数据用双引号包裹（含逗号），无需额外配置
/// </summary>
public class NodeVisibleCSVReader : MonoBehaviour
{
    public static NodeVisibleCSVReader Instance { get; private set; }
    //private Dictionary<int, NodeVisibleData> _nodeVisibleDict = new Dictionary<int, NodeVisibleData>();
    // 用Tuple<int, int>（SceneID, NodeID）作为组合键，确保唯一
    private Dictionary<Tuple<int, int>, NodeVisibleData> _nodeVisibleDict = new Dictionary<Tuple<int, int>, NodeVisibleData>();
    public string csvFilePath = "ReadData/NodeVisible.csv";
    public bool IsReady { get; private set; } = false; // 解析状态标识

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        //DontDestroyOnLoad(gameObject);
        StartCoroutine(ParseCSV());
    }

    private void OnDestroy()
    {
        // 场景销毁时释放实例引用
        if (Instance == this)
        {
            Instance = null;
            IsReady = false;
            _nodeVisibleDict.Clear(); // 清理数据，避免内存泄漏
        }
    }

    /// <summary>
    /// 手动解析CSV，处理双引号包裹的列内数据
    /// </summary>
    private IEnumerator ParseCSV()
    {
        IsReady = false; // 开始解析时重置状态
        string fullCsvPath = System.IO.Path.Combine(Application.streamingAssetsPath, csvFilePath);
        //Debug.Log($"开始解析CSV：{fullCsvPath}");

        // 用UnityWebRequest读取文件（兼容所有平台）
        UnityWebRequest req = UnityWebRequest.Get(fullCsvPath);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"CSV读取失败：{req.error}，路径：{fullCsvPath}");
            yield break;
        }

        // 按行分割内容（过滤空行）
        string csvContent = req.downloadHandler.text;
        string[] lines = csvContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        int validLineCount = 0;

        // 从第1行开始（跳过表头第0行）
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            List<string> columns = ParseLine(line);
            // 关键修改1：将列数判断从3改为4（匹配你的CSV实际列数）
            if (columns.Count != 4)
            {
                Debug.LogWarning($"行格式错误（跳过）：第{lineIndex + 1}行，列数={columns.Count}，内容：{line}");
                continue;
            }

            // 新增：先解析第0列作为SceneID
            if (!int.TryParse(columns[0], out int sceneID))  // 第0列是场景ID（如1）
            {
                Debug.LogWarning($"SceneID错误（跳过）：第{lineIndex + 1}行，值={columns[0]}");
                continue;  // 解析失败则跳过当前行
            }

            // 修改：解析第1列作为NodeID（你的CSV中第1列才是节点ID）
            if (!int.TryParse(columns[1], out int nodeID))   // 第1列是节点ID（如1）
            {
                Debug.LogWarning($"NodeID错误（跳过）：第{lineIndex + 1}行，值={columns[1]}");
                continue;
            }

            // 关键修改3：提取第2列作为VisibleNodeIDs，第3列作为VisibleEndnodeIDs
            List<int> visibleNodes = ParseIDList(columns[2]);
            List<int> visibleEndnodes = ParseIDList(columns[3]);

            // （后续存储数据的逻辑不变）
            //if (_nodeVisibleDict.ContainsKey(nodeID))
            //{
            //    Debug.LogWarning($"NodeID重复（覆盖）：{nodeID}");
            //    // 新增sceneID参数传给构造方法
            //    _nodeVisibleDict[nodeID] = new NodeVisibleData(sceneID, nodeID, visibleNodes, visibleEndnodes);
            //}
            //else
            //{
            //    // 新增sceneID参数传给构造方法
            //    _nodeVisibleDict.Add(nodeID, new NodeVisibleData(sceneID, nodeID, visibleNodes, visibleEndnodes));
            //}

            // 1. 创建组合键（SceneID + NodeID）
            var dataKey = Tuple.Create(sceneID, nodeID);

            // 2. 用组合键判断是否重复
            if (_nodeVisibleDict.ContainsKey(dataKey))
            {
                // 日志包含SceneID，明确重复场景
                Debug.LogWarning($"SceneID={sceneID}下的NodeID={nodeID}重复（覆盖）");
                _nodeVisibleDict[dataKey] = new NodeVisibleData(sceneID, nodeID, visibleNodes, visibleEndnodes);
            }
            else
            {
                _nodeVisibleDict.Add(dataKey, new NodeVisibleData(sceneID, nodeID, visibleNodes, visibleEndnodes));
            }

            validLineCount++;
        }

        // 解析结果统计
        if (validLineCount == 0)
        {
            Debug.LogError("CSV无有效数据");
        }
        else
        {
            //Debug.Log($"CSV解析完成，加载{_nodeVisibleDict.Count}个节点数据（有效行：{validLineCount}）");
        }

        IsReady = true;
    }

    /// <summary>
    /// 核心方法：手动解析一行CSV，处理双引号包裹的列
    /// </summary>
    private List<string> ParseLine(string line)
    {
        List<string> columns = new List<string>();
        StringBuilder currentColumn = new StringBuilder();
        bool inQuotes = false; // 是否处于双引号内
        int index = 0;

        while (index < line.Length)
        {
            char c = line[index];

            // 处理双引号
            if (c == '"')
            {
                // 连续两个双引号视为转义（"" → "）
                if (index + 1 < line.Length && line[index + 1] == '"')
                {
                    currentColumn.Append('"');
                    index += 2; // 跳过两个引号
                    continue;
                }
                // 切换引号状态
                inQuotes = !inQuotes;
                index++;
            }
            // 处理逗号（仅当不在引号内时视为列分隔符）
            else if (c == ',' && !inQuotes)
            {
                columns.Add(currentColumn.ToString().Trim());
                currentColumn.Clear();
                index++;
            }
            // 普通字符
            else
            {
                currentColumn.Append(c);
                index++;
            }
        }

        // 添加最后一列
        columns.Add(currentColumn.ToString().Trim());

        // 处理未闭合的引号（容错）
        if (inQuotes)
        {
            Debug.LogWarning($"未闭合的双引号：{line}");
        }

        return columns;
    }

    /// <summary>
    /// 将"1,2,3"或"none"转换为整数列表
    /// </summary>
    private List<int> ParseIDList(string idStr)
    {
        List<int> ids = new List<int>();
        if (string.IsNullOrEmpty(idStr) || idStr.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return ids;
        }

        string normalizedStr = idStr.Replace('.', ',');
        foreach (string s in normalizedStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(s.Trim(), out int id))
            {
                ids.Add(id);
            }
            else
            {
                Debug.LogWarning($"无效ID格式：{s}");
            }
        }
        return ids;
    }

    /// <summary>
    /// 获取节点可见性数据
    /// </summary>
    //public NodeVisibleData GetVisibleData(int currentNodeID)
    //{
    //    if (_nodeVisibleDict.TryGetValue(currentNodeID, out var data))
    //    {
    //        return data;
    //    }
    //    Debug.LogError($"未找到节点数据：{currentNodeID}");
    //    return null;
    //}

    /// <summary>
    /// 获取指定场景下的节点可见性数据（新增SceneID参数）
    /// </summary>
    /// <param name="sceneID">目标场景ID</param>
    /// <param name="currentNodeID">目标节点ID</param>
    /// <returns>节点可见性数据</returns>
    public NodeVisibleData GetVisibleData(int sceneID, int currentNodeID)
    {
        // 用组合键查询
        var dataKey = Tuple.Create(sceneID, currentNodeID);
        if (_nodeVisibleDict.TryGetValue(dataKey, out var data))
        {
            return data;
        }
        // 错误日志包含SceneID，便于定位问题
        Debug.LogError($"未找到SceneID={sceneID}下的节点数据：NodeID={currentNodeID}");
        return null;
    }

    /// <summary>
    /// 节点可见性数据结构
    /// </summary>
    public class NodeVisibleData
    {
        public int SceneID { get; }  // 新增：存储场景ID
        public int CurrentNodeID { get; }
        public List<int> VisibleNodeIDs { get; }
        public List<int> VisibleEndnodeIDs { get; }

        // 构造方法增加sceneID参数
        public NodeVisibleData(int sceneID, int currentNodeID, List<int> visibleNodeIDs, List<int> visibleEndnodeIDs)
        {
            SceneID = sceneID;  // 赋值场景ID
            CurrentNodeID = currentNodeID;
            VisibleNodeIDs = visibleNodeIDs ?? new List<int>();
            VisibleEndnodeIDs = visibleEndnodeIDs ?? new List<int>();
        }
    }

    // 添加等待解析完成的方法
    public IEnumerator WaitUntilReady()
    {
        while (!IsReady)
            yield return null;
    }
}
