using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public static class CSVReader
{
    // 存储所有节点数据，Key: SceneID+NodeID的组合键，Value: 节点信息
    public static Dictionary<string, NodeData> AllNodeData = new Dictionary<string, NodeData>();

    /// <summary>
    /// 异步加载并解析CSV文件（适配Unity 2020.3）
    /// </summary>
    public static IEnumerator LoadCSV(string csvRelativePath, Action onLoadComplete)
    {
        // 1. 拼接CSV文件路径（StreamingAssets目录下）
        string csvPath = Path.Combine(Application.streamingAssetsPath, csvRelativePath);
        //Debug.Log($"尝试加载CSV：{csvPath}");

        // 2. 创建网络请求（Unity 2020.3推荐使用UnityWebRequest.Get）
        UnityWebRequest request = UnityWebRequest.Get(csvPath);

        // 3. 发送请求并等待完成（协程关键：必须yield return）
        yield return request.SendWebRequest();

        // 4. 处理请求结果（Unity 2020.3的Result枚举兼容）
        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"CSV加载失败：{request.error}，路径：{csvPath}");
            yield break; // 加载失败则终止协程
        }

        // 5. 解析CSV内容（按行分割，跳过表头）
        string[] allLines = request.downloadHandler.text.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );

        // 清空旧数据，避免重复加载
        AllNodeData.Clear();

        // 从第2行开始解析（第1行为表头）
        for (int i = 1; i < allLines.Length; i++)
        {
            string[] csvRow = allLines[i].Split(','); // 假设CSV用逗号分隔

            // 校验行格式（至少13列数据）
            if (csvRow.Length < 14)
            {
                Debug.LogWarning($"CSV行{i + 1}格式错误（列数不足）：{allLines[i]}");
                continue;
            }

            // 创建节点数据并添加到字典
            try
            {
                NodeData nodeData = new NodeData(csvRow);
                string compositeKey = $"{nodeData.SceneID}_{nodeData.NodeID}";

                if (!AllNodeData.ContainsKey(compositeKey))
                {
                    AllNodeData.Add(compositeKey, nodeData);
                }
                else
                {
                    Debug.LogWarning($"重复的SceneID+NodeID组合: {compositeKey}，行号: {i + 1}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"解析行{i + 1}失败：{e.Message}");
            }
        }

        // 6. 加载完成，触发回调
        Debug.Log($"CSV加载成功！共解析{AllNodeData.Count}个节点");
        onLoadComplete?.Invoke();
    }
}