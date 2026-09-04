using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class TaskDataRecorder : MonoBehaviour
{
    public static TaskDataRecorder Instance { get; private set; }
    private TaskListParser taskListParser;
    private bool hasRecorded = false;
    public static long CurrentTaskUnixTimestamp { get; private set; }

    private string saveBasePath;
    private string _taskValidity = "Y"; // 默认有效
    public string TaskValidity => _taskValidity; // 公开属性，供外部访问



    private void Awake()
    {
        saveBasePath = ProjectPathConfig.RecordingSaveRoot;

        if (!Directory.Exists(saveBasePath))
        {
            Directory.CreateDirectory(saveBasePath);
        }

        if (Instance == null)
        {
            Instance = this;
            taskListParser = new TaskListParser();
            LoadTaskList();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (!hasRecorded)
        {
            RecordExperimentData();
            hasRecorded = true;
        }
    }

    private void LoadTaskList()
    {
        string taskListPath = Path.Combine(Application.streamingAssetsPath, "ReadData/TaskList.csv");
        if (!taskListParser.ParseCSV(taskListPath))
            Debug.LogError("任务列表解析失败，可能影响数据记录");
    }

    // 新增方法：设置任务为无效
    public void SetTaskInvalid()
    {
        _taskValidity = "N";
    }


    public void RecordExperimentData()
    {
        Debug.Log("开始执行数据记录逻辑");
        try
        {
            string csvPath = Path.Combine(Application.streamingAssetsPath, "TaskRecorder.csv");
            string backupPath = Path.Combine(Application.streamingAssetsPath, "TaskRecorder_Backup.csv");

            // 备份原文件（仅首次记录时有效）
            if (File.Exists(csvPath))
                File.Copy(csvPath, backupPath, overwrite: true);

            // 计算任务数据（保持原有逻辑）
            int taskCount = GetNextTaskCount(csvPath);
            int taskOrder = SceneManagerScript.taskOrder;
            int sceneID = taskListParser.GetSceneIDByTaskOrder(taskOrder);
            int taskID = taskListParser.GetTaskIDByTaskOrder(taskOrder);
            string testerID = SceneManagerScript.testerID;
            int age = SceneManagerScript.age;
            string gender = SceneManagerScript.gender;
            float height = SceneManagerScript.heightValue;
            string eyetracking = SceneManagerScript.isEyeTrackingClosed ? "F" : "T";
            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            long unixTimestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

            // 首次记录时赋值时间戳，后续不再修改（确保始终指向原文件）
            if (CurrentTaskUnixTimestamp == 0)
            {
                CurrentTaskUnixTimestamp = unixTimestamp;
            }

            // 拼接数据行（保持原有逻辑）
            string dataLine = $"{taskCount}," +
                              $"{taskOrder}," +
                              $"{sceneID}," +
                              $"{taskID}," +
                              $"{WrapWithQuotes(testerID)}," +
                              $"{age}," +
                              $"{WrapWithQuotes(gender)}," +
                              $"{height}," +
                              $"{eyetracking}," +
                              $"{WrapWithQuotes(timeStamp)}," +
                              $"{CurrentTaskUnixTimestamp}," + // 关键：始终用首次的时间戳
                              $"{(GlobalTaskState.IsTaskFailed ? "N" : "Y")}";

            // 仅首次记录时写入新数据，后续失败时不新增（避免重复文件）
            if (!hasRecorded)
            {
                // 写入主CSV（首次记录）
                using (StreamWriter sw = File.AppendText(csvPath))
                {
                    if (new FileInfo(csvPath).Length == 0)
                    {
                        string header = "TaskCount,TaskOrder,SceneID,TaskID,TesterID,Age,Gender,Height,Eyetracking,TimeStamp,UnixTimestamp,TaskValidity";
                        sw.WriteLine(header);
                    }
                    sw.WriteLine(dataLine);
                }

                // 写入单独文件（首次记录，无后缀）
                string recordingsDir = saveBasePath;
                if (!Directory.Exists(recordingsDir))
                {
                    Directory.CreateDirectory(recordingsDir);
                }
                string originalFileName = $"TestInfo_ID{testerID}_Task{taskOrder}_{CurrentTaskUnixTimestamp}.csv";
                string originalFilePath = Path.Combine(recordingsDir, originalFileName);
                using (StreamWriter sw = File.CreateText(originalFilePath))
                {
                    string header = "TaskCount,TaskOrder,SceneID,TaskID,TesterID,Age,Gender,Height,Eyetracking,TimeStamp,UnixTimestamp,TaskValidity";
                    sw.WriteLine(header);
                    sw.WriteLine(dataLine);
                }

                hasRecorded = true;
                //Debug.Log($"首次记录完成：\n{csvPath}\n{originalFilePath}");
            }
            else
            {
                // 非首次记录（即放弃任务时）：仅更新内容+重命名，不新建文件
                if (GlobalTaskState.IsTaskFailed)
                {
                    UpdateTaskValidityInFile(CurrentTaskUnixTimestamp); // 更新值为N
                    RenameFileWithSuffix(CurrentTaskUnixTimestamp);    // 加_failed后缀
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"记录实验数据失败：{ex.Message}");
        }
    }

   

    private int GetNextTaskCount(string csvPath)
    {
        if (!File.Exists(csvPath))
            return 1;

        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length <= 1)
            return 1;

        string lastLine = lines[lines.Length - 1];
        string[] lastLineData = lastLine.Split(',');
        if (int.TryParse(lastLineData[0], out int lastCount))
            return lastCount + 1;
        else
            return 1;
    }

    private string WrapWithQuotes(string value)
    {
        return value.Contains(",") ? $"\"{value}\"" : value;
    }

    /// <summary>
    /// 更新原文件中的TaskValidity值（从Y改为N）
    /// </summary>
    /// <param name="targetUnixTimestamp">原文件的时间戳（确保找到对应文件）</param>
    private void UpdateTaskValidityInFile(long targetUnixTimestamp)
    {
        try
        {
            // 1. 处理主CSV文件（TaskRecorder.csv）
            string mainCsvPath = Path.Combine(Application.streamingAssetsPath, "TaskRecorder.csv");
            if (File.Exists(mainCsvPath))
            {
                string[] allLines = File.ReadAllLines(mainCsvPath);
                if (allLines.Length <= 1) return; // 无数据行，无需更新

                // 遍历所有数据行，找到当前任务（匹配时间戳）的行
                for (int i = 1; i < allLines.Length; i++)
                {
                    string line = allLines[i];
                    string[] columns = line.Split(',');
                    if (columns.Length < 12) continue; // 数据格式异常，跳过

                    // 匹配时间戳（第11列是UnixTimestamp）
                    if (long.TryParse(columns[10], out long lineTimestamp) && lineTimestamp == targetUnixTimestamp)
                    {
                        columns[11] = "N"; // 将TaskValidity列（第12列）改为N
                        allLines[i] = string.Join(",", columns);
                        break; // 找到对应行，更新后退出循环
                    }
                }

                // 写回修改后的内容到主CSV
                File.WriteAllLines(mainCsvPath, allLines);
                Debug.Log($"主CSV文件已更新：{mainCsvPath}");
            }

            // 2. 处理记录目录的单独文件（如TestInfo_ID1_Task1_1764169955064.csv）
            string recordingsDir = saveBasePath;
            string originalFileName = $"TestInfo_ID{SceneManagerScript.testerID}_Task{SceneManagerScript.taskOrder}_{targetUnixTimestamp}.csv";
            string originalFilePath = Path.Combine(recordingsDir, originalFileName);

            if (File.Exists(originalFilePath))
            {
                string[] fileLines = File.ReadAllLines(originalFilePath);
                if (fileLines.Length <= 1) return;

                // 修改该文件的数据行（仅一行数据）
                string[] dataColumns = fileLines[1].Split(',');
                if (dataColumns.Length >= 12)
                {
                    dataColumns[11] = "N"; // TaskValidity列改为N
                    fileLines[1] = string.Join(",", dataColumns);
                    File.WriteAllLines(originalFilePath, fileLines);
                    //Debug.Log($"单独文件内容已更新：{originalFilePath}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"更新TaskValidity失败：{ex.Message}");
        }
    }

    private void RenameFileWithSuffix(long targetUnixTimestamp)
    {
        try
        {
            string recordingsDir = saveBasePath;
            // 原文件名（无后缀）
            string originalFileName = $"TestInfo_ID{SceneManagerScript.testerID}_Task{SceneManagerScript.taskOrder}_{targetUnixTimestamp}.csv";
            string originalFilePath = Path.Combine(recordingsDir, originalFileName);

            // 新文件名（仅加_failed后缀）
            string newFileName = $"TestInfo_ID{SceneManagerScript.testerID}_Task{SceneManagerScript.taskOrder}_{targetUnixTimestamp}_failed.csv";
            string newFilePath = Path.Combine(recordingsDir, newFileName);

            // 重命名（确保原文件存在，且新文件不存在）
            if (File.Exists(originalFilePath) && !File.Exists(newFilePath))
            {
                File.Move(originalFilePath, newFilePath);
                Debug.Log($"文件已重命名：{originalFileName} → {newFileName}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"文件重命名失败：{ex.Message}");
        }
    }
}