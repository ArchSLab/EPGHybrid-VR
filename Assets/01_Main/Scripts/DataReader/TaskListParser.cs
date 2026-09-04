using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;

public class TaskListParser
{
    // 核心映射：TaskOrder为唯一键关联所有任务信息
    private Dictionary<int, int> taskOrderToSceneID = new Dictionary<int, int>();         // TaskOrder -> SceneID
    private Dictionary<int, int> taskOrderToTaskID = new Dictionary<int, int>();       // TaskOrder -> TaskID
    private Dictionary<int, string> taskOrderToStartPoint = new Dictionary<int, string>(); // TaskOrder -> StartPoint
    private Dictionary<int, string> taskOrderToEndPoint = new Dictionary<int, string>();  // TaskOrder -> EndPoint
    // 关键修改：存储Endpoint_All的数字数组（TaskOrder -> 数字集合）
    private Dictionary<int, List<int>> taskOrderToEndpointAll = new Dictionary<int, List<int>>();
    private Dictionary<int, string> taskOrderToSceneName = new Dictionary<int, string>();   // TaskOrder -> SceneName
    private Dictionary<int, string> taskOrderToSceneType = new Dictionary<int, string>();   // TaskOrder -> SceneType
    private Dictionary<int, string> taskOrderToLevelName = new Dictionary<int, string>();  // TaskOrder -> LevelName
    private Dictionary<int, string> taskOrderToStartInfo = new Dictionary<int, string>();   // TaskOrder -> StartInfo
    private Dictionary<int, string> taskOrderToTaskInfo = new Dictionary<int, string>();   // TaskOrder -> TaskInfo
    private Dictionary<int, string> taskOrderToPath = new Dictionary<int, string>();      // TaskOrder -> Path
    private Dictionary<int, string> taskOrderToEndPointType = new Dictionary<int, string>(); // TaskOrder -> EndPointType（第12列）
    private Dictionary<int, string> taskOrderToNotes = new Dictionary<int, string>();       // TaskOrder -> Notes（第13列）
    private Dictionary<int, string> taskOrderToMapAid = new Dictionary<int, string>();      // TaskOrder -> MapAid（第14列）

    private Dictionary<int, string> taskOrderToBackground = new Dictionary<int, string>(); // TaskOrder -> Background（新增列）
    private Dictionary<int, string> taskOrderToGender = new Dictionary<int, string>();     // TaskOrder -> Gender（新增列）
    private Dictionary<int, string> taskOrderToAge = new Dictionary<int, string>();       // TaskOrder -> Age（新增列）
    private Dictionary<int, int> taskOrderToTaskOrderOriginal = new Dictionary<int, int>();  // TaskOrder -> TaskOrderOriginal



    public bool ParseCSV(string csvFilePath)
    {
        if (!File.Exists(csvFilePath))
        {
            Debug.LogError($"TaskList.csv 文件不存在: {csvFilePath}");
            return false;
        }

        try
        {
            string[] lines = File.ReadAllLines(csvFilePath);
            ClearAllDictionaries();

            // 跳过表头，从第2行开始解析
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // 关键：使用自定义方法分割CSV行，处理带引号字段
                List<string> columns = SplitCsvLine(line);

                if (columns.Count < 19)
                {
                    Debug.LogWarning($"第 {i + 1} 行数据格式不正确（至少19列，当前{columns.Count}列）: {line}");
                    continue;
                }

                // 转换为数组（后续逻辑不变）
                string[] columnsArr = columns.ToArray();
                if (!int.TryParse(columnsArr[0], out int taskOrder))
                {
                    Debug.LogWarning($"第 {i + 1} 行的TaskOrder格式不正确（第0列）: {columnsArr[0]}");
                    continue;
                }

                ParseAllFields(columnsArr, taskOrder);
            }

            //Debug.Log($"TaskList.csv 解析成功，共{taskOrderToTaskID.Count}条任务数据");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"解析TaskList.csv时发生异常: {ex.Message}\n堆栈信息: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 自定义CSV行分割方法，处理带引号的字段
    /// </summary>
    private List<string> SplitCsvLine(string line)
    {
        List<string> fields = new List<string>();
        StringBuilder currentField = new StringBuilder();
        bool inQuotes = false; // 是否处于引号包裹状态

        foreach (char c in line)
        {
            if (c == '"')
            {
                // 遇到引号：切换「包裹状态」，不将引号加入字段内容
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                // 遇到逗号且不在引号内：分割字段
                fields.Add(currentField.ToString().Trim());
                currentField.Clear();
            }
            else
            {
                // 其他字符：加入当前字段
                currentField.Append(c);
            }
        }

        // 添加最后一个字段
        fields.Add(currentField.ToString().Trim());
        return fields;
    }

    /// <summary>
    /// 清空所有任务数据字典，避免数据残留
    /// </summary>
    private void ClearAllDictionaries()
    {
        taskOrderToSceneID.Clear();
        taskOrderToTaskID.Clear();
        taskOrderToStartPoint.Clear();
        taskOrderToEndPoint.Clear();
        taskOrderToEndpointAll.Clear(); // 清空Endpoint_All数组字典
        taskOrderToSceneName.Clear();
        taskOrderToSceneType.Clear();
        taskOrderToLevelName.Clear();
        taskOrderToStartInfo.Clear();
        taskOrderToTaskInfo.Clear();
        taskOrderToPath.Clear();
        taskOrderToEndPointType.Clear();
        taskOrderToNotes.Clear();
        taskOrderToMapAid.Clear();
        taskOrderToBackground.Clear(); // 清空Background字典
        taskOrderToGender.Clear();     // 清空Gender字典
        taskOrderToAge.Clear();        // 清空Age字典
        taskOrderToTaskOrderOriginal.Clear();  // 清空新增字典
    }

    /// <summary>
    /// 解析CSV行的所有字段，映射到对应字典
    /// </summary>
    /// <param name="columns">CSV行分割后的字段数组</param>
    /// <param name="taskOrder">当前任务的TaskOrder（用于字典键）</param>
    private void ParseAllFields(string[] columns, int taskOrder)
    {
        // 1. 解析SceneID（第1列，数字类型）
        if (int.TryParse(columns[1], out int sceneID))
            taskOrderToSceneID[taskOrder] = sceneID;
        else
        {
            Debug.LogWarning($"TaskOrder={taskOrder}的SceneID格式不正确（第1列）: {columns[1]}，默认赋值-1");
            taskOrderToSceneID[taskOrder] = -1;
        }


        // 2. 解析TaskID（第2列，数字类型）
        if (int.TryParse(columns[2], out int taskID))
            taskOrderToTaskID[taskOrder] = taskID;
        else
            Debug.LogWarning($"TaskOrder={taskOrder}的TaskID格式不正确（第2列）: {columns[2]}");

        // 3. 解析StartPoint（第3列，字符串类型）
        taskOrderToStartPoint[taskOrder] = columns[3].Trim();
        // 4. 解析EndPoint（第4列，字符串类型）
        taskOrderToEndPoint[taskOrder] = columns[4].Trim();
        // 5. 关键解析：Endpoint_All（第5列，逗号分隔数字数组）
        ParseEndpointAll(columns[5], taskOrder);

        // 6. 解析后续字段（因新增Endpoint_All列，索引均向后顺延1位）
        taskOrderToSceneName[taskOrder] = columns[6].Trim();    // 原第5列 → 现第6列
        taskOrderToSceneType[taskOrder] = columns[7].Trim();    // 原第6列 → 现第7列
        taskOrderToLevelName[taskOrder] = columns[8].Trim();   // 原第7列 → 现第8列
        taskOrderToStartInfo[taskOrder] = columns[9].Trim();    // 原第8列 → 现第9列
        taskOrderToBackground[taskOrder] = columns[10].Trim();  // Background（第11列，原TaskInfo列索引顺延）
        taskOrderToTaskInfo[taskOrder] = columns[11].Trim();   // 原第10列 → 现第11列
        taskOrderToPath[taskOrder] = columns.Length >= 13 ? columns[12].Trim() : ""; // 原第11列 → 现第12列
        taskOrderToEndPointType[taskOrder] = columns[13].Trim(); // 原第12列 → 现第13列
        taskOrderToNotes[taskOrder] = columns[14].Trim();       // 原第13列 → 现第14列
        taskOrderToMapAid[taskOrder] = columns[15].Trim();      // 原第14列 → 现第15列
        taskOrderToGender[taskOrder] = columns[16].Trim();
        taskOrderToAge[taskOrder] = columns[17].Trim();

        // 解析TaskOrderOriginal（新增列，第18列）
        if (int.TryParse(columns[18], out int taskOrderOriginal))
            taskOrderToTaskOrderOriginal[taskOrder] = taskOrderOriginal;
        else
            Debug.LogWarning($"TaskOrder={taskOrder}的TaskOrderOriginal格式不正确（第18列）: {columns[18]}");
    }

    /// <summary>
    /// 专门解析Endpoint_All列（逗号分隔数字数组）
    /// </summary>
    /// <param name="endpointAllStr">CSV中Endpoint_All列的原始字符串</param>
    /// <param name="taskOrder">当前任务的TaskOrder</param>
    private void ParseEndpointAll(string endpointAllStr, int taskOrder)
    {
        List<int> endpointAllList = new List<int>();
        // 关键优化：先过滤所有非数字和非逗号的字符（保留数字和半角逗号）
        string cleanedStr = System.Text.RegularExpressions.Regex.Replace(endpointAllStr, @"[^0-9,]", "");
        // 再按半角逗号分割
        string[] endpointItems = cleanedStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string item in endpointItems)
        {
            string cleanItem = item.Trim();
            if (int.TryParse(cleanItem, out int endpointNum))
            {
                endpointAllList.Add(endpointNum);
            }
            else
            {
                Debug.LogWarning($"TaskOrder={taskOrder}的Endpoint_All包含无效数字: {cleanItem}（已跳过）");
            }
        }
        taskOrderToEndpointAll[taskOrder] = endpointAllList;
    }

    #region 对外提供的任务数据获取方法
    /// <summary>通过TaskOrder获取SceneID</summary>
    public int GetSceneIDByTaskOrder(int taskOrder) =>
        taskOrderToSceneID.TryGetValue(taskOrder, out int sid) ? sid : 0;

    /// <summary>通过TaskOrder获取TaskID</summary>
    public int GetTaskIDByTaskOrder(int taskOrder) =>
        taskOrderToTaskID.TryGetValue(taskOrder, out int id) ? id : 0;

    /// <summary>通过TaskOrder获取StartPoint</summary>
    public string GetStartPointByTaskOrder(int taskOrder) =>
        taskOrderToStartPoint.TryGetValue(taskOrder, out string sp) ? sp : "";

    /// <summary>通过TaskOrder获取EndPoint</summary>
    public string GetEndPointByTaskOrder(int taskOrder) =>
        taskOrderToEndPoint.TryGetValue(taskOrder, out string ep) ? ep : "";

    /// <summary>
    /// 关键方法：通过TaskOrder获取Endpoint_All的数字数组
    /// </summary>
    /// <param name="taskOrder">任务序号</param>
    /// <returns>数字集合（无数据时返回空集合，非null）</returns>
    public List<int> GetEndpointAllByTaskOrder(int taskOrder)
    {
        if (taskOrderToEndpointAll.TryGetValue(taskOrder, out List<int> endpointAllList))
        {
            return endpointAllList;
        }
        // 默认返回空集合，避免外部调用时出现NullReferenceException
        return new List<int>();
    }

    /// <summary>通过TaskOrder获取SceneName（默认"未知场景"）</summary>
    public string GetSceneNameByTaskOrder(int taskOrder) =>
        taskOrderToSceneName.TryGetValue(taskOrder, out string sn) ? sn : "未知场景";

    /// <summary>通过TaskOrder获取SceneType（默认"未知类型"）</summary>
    public string GetSceneTypeByTaskOrder(int taskOrder) =>
        taskOrderToSceneType.TryGetValue(taskOrder, out string st) ? st : "未知类型";

    /// <summary>通过TaskOrder获取LevelName（默认"未知楼层"）</summary>
    public string GetLevelNameByTaskOrder(int taskOrder) =>
        taskOrderToLevelName.TryGetValue(taskOrder, out string ln) ? ln : "未知楼层";

    /// <summary>通过TaskOrder获取StartInfo（默认"未知入口"）</summary>
    public string GetStartInfoByTaskOrder(int taskOrder) =>
        taskOrderToStartInfo.TryGetValue(taskOrder, out string si) ? si : "未知入口";

    /// <summary>通过TaskOrder获取TaskInfo</summary>
    public string GetTaskInfoByTaskOrder(int taskOrder) =>
        taskOrderToTaskInfo.TryGetValue(taskOrder, out string ti) ? ti : "";

    /// <summary>通过TaskOrder获取Path</summary>
    public string GetPathByTaskOrder(int taskOrder) =>
        taskOrderToPath.TryGetValue(taskOrder, out string p) ? p : "";

    /// <summary>通过TaskOrder获取EndPointType（默认"未知类型"）</summary>
    public string GetEndPointTypeByTaskOrder(int taskOrder)
    {
        taskOrderToEndPointType.TryGetValue(taskOrder, out string endPointType);
        return endPointType ?? "未知类型";
    }

    /// <summary>通过TaskOrder获取Notes（默认"无备注"）</summary>
    public string GetNotesByTaskOrder(int taskOrder)
    {
        taskOrderToNotes.TryGetValue(taskOrder, out string notes);
        return notes ?? "无备注";
    }

    /// <summary>通过TaskOrder获取MapAid（默认"未知"）</summary>
    public string GetMapAidByTaskOrder(int taskOrder)
    {
        taskOrderToMapAid.TryGetValue(taskOrder, out string mapAid);
        return mapAid ?? "未知";
    }

    /// <summary>通过TaskOrder获取Background（默认空字符串）</summary>
    public string GetBackgroundByTaskOrder(int taskOrder) =>
        taskOrderToBackground.TryGetValue(taskOrder, out string bg) ? bg : "";

    /// <summary>通过TaskOrder获取Gender（默认"未知"）</summary>
    public string GetGenderByTaskOrder(int taskOrder) =>
        taskOrderToGender.TryGetValue(taskOrder, out string g) ? g : "未知";

    /// <summary>通过TaskOrder获取Age（默认"未知"）</summary>
    public string GetAgeByTaskOrder(int taskOrder) =>
        taskOrderToAge.TryGetValue(taskOrder, out string a) ? a : "未知";

    /// <summary>通过TaskOrder获取TaskOrderOriginal</summary>
    public int GetTaskOrderOriginalByTaskOrder(int taskOrder) =>
        taskOrderToTaskOrderOriginal.TryGetValue(taskOrder, out int orig) ? orig : 0;


    /// <summary>获取当前解析的最大TaskOrder</summary>
    public int GetMaxTaskOrder()
    {
        int max = 0;
        foreach (int key in taskOrderToTaskID.Keys)
            if (key > max) max = key;
        return max;
    }
    #endregion
}