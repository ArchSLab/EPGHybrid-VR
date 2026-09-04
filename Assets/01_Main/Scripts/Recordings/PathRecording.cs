using System;
using System.IO;
using UnityEngine;
using System.Text.RegularExpressions;

/// <summary>
/// 记录节点的路径信息：NodeID、位置、时间等
/// 支持在Inspector中设置存储路径，停留时长=下节点时间戳-当前节点时间戳（单位：秒），最后一个节点为0
/// </summary>
public class PathRecording : MonoBehaviour
{
    // 单例实例
    public static PathRecording Instance { get; private set; }

    [Header("存储路径设置")]
    [Tooltip("存储文件夹名称，会创建在PersistentDataPath下")]
    [SerializeField] private string _saveFolderName = "NodePathRecords";

    [Tooltip("文件名称前缀")]
    [SerializeField] private string _filePrefix = "NodePath_";

    // 记录文件的完整路径
    private string _recordFilePath;

    // 标记文件是否已经初始化（在场景切换时重置）
    private bool _isFileInitialized = false;

    // 存储上一个节点的完整记录（用于后续计算停留时长，单位：秒）
    private NodeRecord? _lastNodeRecord = null;

    // 记录数据结构（DecTime改为float类型，单位：秒）
    private struct NodeRecord
    {
        public string nodeID;
        public Vector3 position;
        public DateTime timestamp;
        public long unixTimestamp; // 记录Unix时间戳（毫秒级，用于计算差值）
        public float recordingTime; // 记录Unity的运行时间（秒）
        public string targetID; // 记录Target标识，可为空或默认值
        public float decTime; // 【修改1】决策时间：当前节点的停留时长（单位：秒），由下节点计算
        public float sceneTime; // 新增：场景运行时间（秒）
    }

    private void Awake()
    {
        // 单例模式：如果已有实例则直接销毁当前实例并不执行任何初始化
        if (Instance != null)
        {
            DestroyImmediate(gameObject); // 立即销毁，不执行任何销毁回调
            return;
        }
        // 设为唯一实例并标记为场景切换不销毁
        Instance = this;
        DontDestroyOnLoad(gameObject); // 确保场景切换时保留实例
    }

    // 在类中添加以下代码
    private void OnEnable()
    {
        GlobalTaskState.OnTaskFailed += RenameFileOnTaskFailed;
    }

    private void OnDisable()
    {
        GlobalTaskState.OnTaskFailed -= RenameFileOnTaskFailed;
    }

    private void RenameFileOnTaskFailed()
    {
        if (!string.IsNullOrEmpty(_recordFilePath) && File.Exists(_recordFilePath))
        {
            string directory = Path.GetDirectoryName(_recordFilePath);
            string fileName = Path.GetFileNameWithoutExtension(_recordFilePath);
            string extension = Path.GetExtension(_recordFilePath);

            // 如果还没有添加失败后缀，则添加
            if (!fileName.Contains("_failed"))
            {
                string newFileName = $"{fileName}_failed{extension}";
                string newFilePath = Path.Combine(directory, newFileName);

                File.Move(_recordFilePath, newFilePath);
                _recordFilePath = newFilePath;
                Debug.Log($"路径记录文件已重命名为: {newFilePath}");
            }
        }
    }

    // 场景切换时，补充最后一个节点的停留时长（设为0，单位：秒）并写入
    public void ResetForNewScene()
    {
        // 处理最后一个节点，手动设停留时长为0（秒）
        if (_lastNodeRecord.HasValue && _isFileInitialized && !string.IsNullOrEmpty(_recordFilePath))
        {
            NodeRecord lastNode = _lastNodeRecord.Value;
            lastNode.decTime = 0f; // 最后一个节点停留时长设为0（点击后跳转场景，单位：秒）

            // 写入最后一个节点的完整数据（含停留时长0，保留4位小数）
            string lastCsvLine = $"{lastNode.timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                                 $"{lastNode.unixTimestamp}," +
                                 $"{lastNode.recordingTime:F4}," +
                                 $"{lastNode.sceneTime:F6}," + // 新增
                                 $"{lastNode.nodeID}," +
                                 $"{lastNode.targetID}," +
                                 $"{lastNode.position.x:F4}," +
                                 $"{lastNode.position.y:F4}," +
                                 $"{lastNode.position.z:F4}," +
                                 $"{lastNode.decTime:F4}\n"; // 【修改2】保留4位小数，单位：秒
            File.AppendAllText(_recordFilePath, lastCsvLine);
            Debug.Log($"场景切换，最后一个节点（{lastNode.nodeID}）停留时长设为0秒并记录");
        }

        // 重置状态，准备新场景
        _recordFilePath = null;
        _isFileInitialized = false;
        _lastNodeRecord = null; // 清空上一节点记录
        Debug.Log("已重置路径记录状态，准备记录新场景数据");
    }

    /// <summary>
    /// 初始化存储路径和文件（包括创建文件夹）
    /// </summary>
    private void InitializeSavePath()
    {
        try
        {
            string basePath = Path.Combine(Application.persistentDataPath, _saveFolderName);
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
                Debug.Log($"已创建存储文件夹：{basePath}");
            }
            long taskUnixTimestamp = TaskDataRecorder.CurrentTaskUnixTimestamp;
            string suffix = GlobalTaskState.IsTaskFailed ? "_failed" : "";
            string fileName = $"Path_ID{SceneManagerScript.testerID}_Task{SceneManagerScript.taskOrder}_{taskUnixTimestamp}{suffix}.csv";
            _recordFilePath = Path.Combine(basePath, fileName);
            WriteCsvHeader();
        }
        catch (Exception e)
        {
            Debug.LogError($"初始化存储路径失败：{e.Message}");
        }
    }

    /// <summary>
    /// 写入CSV文件头部（补充DecTime单位说明）
    /// </summary>
    private void WriteCsvHeader()
    {
        try
        {
            // 【修改3】表头添加DecTime单位说明（秒）
            string header = "Timestamp,UnixTimestamp,RecordingTime(s),SceneTime(s),NodeID,TargetID,PositionX,PositionY,PositionZ,DecTime(s)\n";
            File.WriteAllText(_recordFilePath, header);
            Debug.Log($"路径记录文件已创建：{_recordFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"创建记录文件失败：{e.Message}");
        }
    }

    /// <summary>
    /// 记录节点数据（核心逻辑：毫秒转秒换算）
    /// 规则：当前节点的停留时长 = (下一个节点时间戳 - 当前节点时间戳) / 1000（单位：秒），最后一个节点为0
    /// </summary>
    public void RecordNode(string nodeID, Vector3 position, string targetID = "0")
    {
        if (!_isFileInitialized)
        {
            InitializeSavePath();
            _isFileInitialized = true;
            if (string.IsNullOrEmpty(_recordFilePath))
            {
                Debug.LogError("文件初始化失败，无法记录数据");
                return;
            }
        }

        try
        {
            string processedNodeID = nodeID.Replace("Node", string.Empty);
            // 1. 计算当前节点的基础数据（Unix时间戳为毫秒级）
            long currentUnixTimestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            float recordingTime = Time.time;
            DateTime currentTimestamp = DateTime.Now;
            // 计算场景运行时间（当前时间 - 场景开始时间）
            float sceneTime = Time.time - TaskTipSceneManager.sceneStartTime;

            // 2. 创建当前节点的记录（停留时长先设为0，后续由下节点补充，单位：秒）
            NodeRecord currentNode = new NodeRecord
            {
                nodeID = processedNodeID,
                position = position,
                timestamp = currentTimestamp,
                unixTimestamp = currentUnixTimestamp,
                recordingTime = recordingTime,
                targetID = targetID,
                decTime = 0f, // 临时值，下一个节点会修改上一节点的此值（单位：秒）
                sceneTime = sceneTime // 赋值场景运行时间
            };

            // 3. 核心换算：如果存在上一节点，计算上一节点的停留时长（毫秒转秒）
            if (_lastNodeRecord.HasValue)
            {
                NodeRecord lastNode = _lastNodeRecord.Value;
                // 【修改4】毫秒差值 ÷ 1000f = 秒级停留时长，保留4位小数
                lastNode.decTime = Mathf.Round((currentUnixTimestamp - lastNode.unixTimestamp) / 1000f * 10000f) / 10000f;

                // 写入上一节点的完整数据（含秒级停留时长，保留4位小数）
                string lastCsvLine = $"{lastNode.timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                                     $"{lastNode.unixTimestamp}," +
                                     $"{lastNode.recordingTime:F4}," +
                                     $"{lastNode.sceneTime:F6}," + // 新增：保留6位小数
                                     $"{lastNode.nodeID}," +
                                     $"{lastNode.targetID}," +
                                     $"{lastNode.position.x:F4}," +
                                     $"{lastNode.position.y:F4}," +
                                     $"{lastNode.position.z:F4}," +
                                     $"{lastNode.decTime:F4}\n"; // 【修改5】输出秒级数据，保留4位小数
                File.AppendAllText(_recordFilePath, lastCsvLine);
                Debug.Log($"记录上一节点（{lastNode.nodeID}）停留时长：{lastNode.decTime:F4}秒");
            }

            // 4. 更新上一节点记录为当前节点，供下一次计算
            _lastNodeRecord = currentNode;

        }
        catch (Exception e)
        {
            Debug.LogError($"记录节点失败：{e.Message}");
        }
    }

    private void OnGUI()
    {
        GUILayout.Label($"记录文件路径：{_recordFilePath}");
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(_saveFolderName))
            _saveFolderName = "NodePathRecords";
        if (string.IsNullOrWhiteSpace(_filePrefix))
            _filePrefix = "NodePath_";
    }

    // 程序退出时，处理最后一个节点（设为0秒，避免异常退出丢失）
    private void OnDestroy()
    {
        if (_lastNodeRecord.HasValue && _isFileInitialized && !string.IsNullOrEmpty(_recordFilePath))
        {
            NodeRecord lastNode = _lastNodeRecord.Value;
            lastNode.decTime = 0f; // 最后一个节点停留时长设为0秒
            string lastCsvLine = $"{lastNode.timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                                 $"{lastNode.unixTimestamp}," +
                                 $"{lastNode.recordingTime:F4}," +
                                 $"{lastNode.sceneTime:F6}," + // 新增
                                 $"{lastNode.nodeID}," +
                                 $"{lastNode.targetID}," +
                                 $"{lastNode.position.x:F4}," +
                                 $"{lastNode.position.y:F4}," +
                                 $"{lastNode.position.z:F4}," +
                                 $"{lastNode.decTime:F4}\n";
            File.AppendAllText(_recordFilePath, lastCsvLine);
            Debug.Log($"程序退出，最后一个节点（{lastNode.nodeID}）停留时长设为0秒并记录");
        }
    }

}