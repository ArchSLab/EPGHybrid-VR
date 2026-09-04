using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class TargetPositionExporter : MonoBehaviour
{
    [Header("导出配置")]
    public string saveFolderPath = "Assets/TargetPositions"; // 保存文件夹根路径
    public string fileNamePrefix = "TargetPositions_"; // 文件名前缀（最终格式：前缀+场景名.csv）

    void Start()
    {
        ExportTargetPositions();
    }

    void ExportTargetPositions()
    {
        // 1. 获取当前激活场景的名称并提取SceneID
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string sceneID = ExtractSceneIDFromSceneName(currentSceneName);

        if (string.IsNullOrEmpty(sceneID))
        {
            Debug.LogError($"场景名{currentSceneName}格式不合法（需包含Scene+数字，例如Scene1），无法提取SceneID");
            return;
        }

        // 2. 查找所有Tag为"Target"的物体
        GameObject[] targets = GameObject.FindGameObjectsWithTag("Target");
        if (targets.Length == 0)
        {
            Debug.LogWarning($"场景{currentSceneName}中未找到任何Tag为Target的物体");
            return;
        }

        // 3. 处理TargetID（提取名称中的纯数字）
        List<TargetData> targetDatas = new List<TargetData>();
        foreach (var target in targets)
        {
            if (int.TryParse(Regex.Match(target.name, @"\d+").Value, out int targetID))
            {
                targetDatas.Add(new TargetData(
                    sceneID,
                    targetID,
                    target.transform.position.x,
                    target.transform.position.y,
                    target.transform.position.z
                ));
            }
            else
            {
                Debug.LogWarning($"Target物体{target.name}的名称不含数字，无法提取TargetID，已跳过该物体");
            }
        }

        if (targetDatas.Count == 0)
        {
            Debug.LogError("没有可导出的Target数据（所有Target名称均无有效数字ID）");
            return;
        }

        // 4. 构建文件路径（文件名 = 前缀 + 场景名 + .csv）
        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }
        string fileName = $"{fileNamePrefix}{currentSceneName}.csv";
        string fullPath = Path.Combine(saveFolderPath, fileName);

        // 5. 写入CSV文件（处理浮点数格式，避免多语言环境下的逗号分隔问题）
        using (StreamWriter sw = new StreamWriter(fullPath))
        {
            // 写入表头
            sw.WriteLine("SceneID,TargetID,Position_x,Position_y,Position_z");

            // 写入数据行（强制使用点作为小数点分隔符）
            foreach (var data in targetDatas)
            {
                string posX = data.PosX.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                string posY = data.PosY.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                string posZ = data.PosZ.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                sw.WriteLine($"{data.SceneID},{data.TargetID},{posX},{posY},{posZ}");
            }
        }

        Debug.Log($"✅ CSV文件已成功导出！\n场景：{currentSceneName}\n路径：{fullPath}\n导出数据行数：{targetDatas.Count}");
    }

    /// <summary>
    /// 从场景名中提取Scene后的纯数字（例如Scene123→123，Scene_45→45，Scene-67→67）
    /// </summary>
    private string ExtractSceneIDFromSceneName(string sceneName)
    {
        // 正则匹配：先去掉所有非数字字符前的"Scene"相关字符，再提取纯数字
        Match match = Regex.Match(sceneName, @"Scene[^0-9]*(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    // 数据结构类
    private class TargetData
    {
        public string SceneID { get; }
        public int TargetID { get; }
        public float PosX { get; }
        public float PosY { get; }
        public float PosZ { get; }

        public TargetData(string sceneID, int targetID, float posX, float posY, float posZ)
        {
            SceneID = sceneID;
            TargetID = targetID;
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
        }
    }

    // 可选：在编辑器模式下直接点击按钮导出（无需运行游戏）
    [ContextMenu("一键导出当前场景Target位置")]
    public void ExportInEditor()
    {
        ExportTargetPositions();
    }
}