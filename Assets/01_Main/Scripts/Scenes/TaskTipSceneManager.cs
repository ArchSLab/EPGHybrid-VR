using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;
using System.Text;
using System;
using System.Collections;       // 用于 IEnumerator（协程）
using System.Collections.Generic; // 用于 List<>（泛型列表）
using System.Linq; // 新增这一行，引入LINQ扩展方法

public class TaskTipSceneManager : MonoBehaviour
{
    public static TaskTipSceneManager Instance { get; private set; }
    //public FadeEffectManager fadeEffectMgr;

    [Header("提示设置")]
    public GameObject taskTipText;
    public float tipShowTime = 10f;
    [Header("进度条设置")]
    public Slider loadProgressBar;
    public Text progressText;
    [Header("加载完成提示")]
    public Text completeText;
    [Header("实验按钮设置")] // 新增：按钮引用
    public Button startExperimentBtn;   // 开始实验（左）
    public Button exitExperimentBtn;    // 退出实验（右）
    public static bool needPreload = true;
    private string targetSceneName;
    private bool isPreloadComplete = false;
    private int totalNodesToLoad = 0;
    private int loadedNodesCount = 0;
    private TaskListParser taskListParser;
    private string sceneFolderName;
    private int currentTaskOrder; // 缓存当前TaskOrder
    private string lastSceneName; // 新增：记录上一个场景名称
    public static float sceneStartTime;
    public static string CurrentTaskTip { get; private set; }

    [Header("预加载设置")]
    public int preloadLayer = 2; // 默认值设为2，与原逻辑一致

    private void Awake()
    {
        // 单例初始化（保留）
        if (Instance == null)
        {
            Instance = this;
            //DontDestroyOnLoad(gameObject); // 如需跨场景保留可启用
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        taskListParser = new TaskListParser();
        LoadTaskList();
        currentTaskOrder = SceneManagerScript.taskOrder;
        needPreload = !SceneManagerScript.isPreloadClosed;
        InitButtons();
        InitProgressUI();

        // 检查当前场景是否与上一个相同
        int sceneID = taskListParser.GetSceneIDByTaskOrder(currentTaskOrder);
        string currentSceneName = $"Scene{sceneID}";
        if (lastSceneName == currentSceneName)
        {
            isPreloadComplete = true;
            EnableStartButton();
            loadProgressBar.value = 1f;
            progressText.text = "100%";
        }
        else
        {
            if (!InitSceneInfoByTaskOrder()) return;
            ShowTaskTipWithNewFormat();
            if (needPreload)
            {
                // 如果场景不同，先卸载之前加载的资源
                if (lastSceneName != null)
                {
                    StartCoroutine(UnloadPreviousSceneResources(lastSceneName));
                }
                StartCoroutine(PreloadAllScenePanoramas());
            }
            else
            {
                isPreloadComplete = true;
                EnableStartButton();
            }
        }
        lastSceneName = currentSceneName; // 更新上一个场景名称
    }

    // 新增：卸载之前加载的资源
    private IEnumerator UnloadPreviousSceneResources(string sceneName)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (scene.isLoaded)
        {
            AsyncOperation asyncOp = SceneManager.UnloadSceneAsync(scene);
            while (!asyncOp.isDone)
            {
                yield return null;
            }
            Resources.UnloadUnusedAssets();
        }

        // 清理旧场景全景图缓存（核心修正部分）
        if (PanoramaMaterialManager.Instance != null)
        {
            // 提取旧场景ID（根据你的缓存Key格式调整，示例："Scene1" → "1"）
            string oldSceneID = sceneName.Replace("Scene", "");

            // 调用清理接口（内部已完成：收集Key→销毁纹理→移除缓存）
            PanoramaMaterialManager.Instance.ClearSceneCache(oldSceneID);

            // 可选：输出清理日志（如需统计清理数量，可修改ClearSceneCache返回int）
            Debug.Log($"清理旧场景[{sceneName}]全景图缓存完成（场景ID：{oldSceneID}）");
        }

        // 强制GC释放内存（可选，根据项目内存情况调整）
        System.GC.Collect();
        yield return new WaitForEndOfFrame();

        // 原清理纹理缓存代码下方添加
        if (PanoramaMaterialManager.Instance != null)
        {
            string oldSceneID = sceneName.Replace("Scene", "");
            PanoramaMaterialManager.Instance.ClearSceneABCache(oldSceneID); // 新增AB包清理
            Debug.Log($"清理旧场景[{sceneName}]的AB包缓存完成");
        }

    }

    // 初始化按钮（绑定事件+设置初始交互性）
    private void InitButtons()
    {
        // 退出按钮：随时可点击，触发退出
        if (exitExperimentBtn != null)
        {
            exitExperimentBtn.interactable = true;
            exitExperimentBtn.onClick.RemoveAllListeners();
            exitExperimentBtn.onClick.AddListener(ExitExperiment);
        }
        // 开始按钮：默认禁用，预加载完成后启用
        if (startExperimentBtn != null)
        {
            startExperimentBtn.interactable = false;
            startExperimentBtn.onClick.RemoveAllListeners();
            startExperimentBtn.onClick.AddListener(StartExperiment);
        }
    }

    // 启用开始实验按钮
    private void EnableStartButton()
    {
        if (startExperimentBtn != null)
        {
            startExperimentBtn.interactable = true;
            if (completeText != null)
                completeText.text = "预加载完成，点击【开始实验】进入场景";
        }
    }

    // 初始化场景信息
    private bool InitSceneInfoByTaskOrder()
    {
        // 获取TaskOrder
        if (!SceneManagerScript.taskDataDict.TryGetValue("TaskOrder", out object orderObj) || !(orderObj is int))
        {
            ShowError("无法获取TaskOrder，请检查StartScene数据");
            return false;
        }


        // 获取SceneID和目标场景名
        int sceneID = taskListParser.GetSceneIDByTaskOrder(currentTaskOrder);
        sceneFolderName = "Scene" + sceneID;
        targetSceneName = $"Scene{sceneID}";
        Debug.Log($"当前任务：TaskOrder={currentTaskOrder} → 目标场景：{targetSceneName}");
        return true;
    }

    // 显示欢迎文案
    private void ShowTaskTipWithNewFormat()
    {
        if (taskTipText == null)
        {
            Debug.LogError("请先赋值TaskTipText引用");
            return;
        }

        // 从解析器获取所需字段
        string sceneName = taskListParser.GetSceneNameByTaskOrder(currentTaskOrder);
        string sceneType = taskListParser.GetSceneTypeByTaskOrder(currentTaskOrder);
        string levelName = taskListParser.GetLevelNameByTaskOrder(currentTaskOrder);
        string startInfo = taskListParser.GetStartInfoByTaskOrder(currentTaskOrder);
        string taskInfo = taskListParser.GetTaskInfoByTaskOrder(currentTaskOrder);
        string backGround = taskListParser.GetBackgroundByTaskOrder(currentTaskOrder);

        // 拼接文案（按需求格式）
        //string tipContent = $"你现在正位于{sceneName}{sceneType}{levelName}，从{startInfo}进入本区域，\n现在你的任务是：{taskInfo}。";
        string tipContent = $"你现在正位于{sceneName}{sceneType}{levelName}，起点为{startInfo}，{backGround}：\n<color=#FFA500><b><size=40%>{taskInfo}</size></b></color>\n";
        CurrentTaskTip = $"现在你的任务是{backGround}：\n<color=#FFA500><b>{taskInfo}</b></color>";

        // 赋值并显示
        Text tipText = taskTipText.GetComponent<Text>();
        if (tipText != null)
        {
            tipText.text = tipContent;
            tipText.fontSize = 38; // 可根据UI调整字体大小
        }
        taskTipText.SetActive(true);
    }

    // 加载任务列表CSV
    private void LoadTaskList()
    {
        string taskListPath = Path.Combine(Application.streamingAssetsPath, "ReadData/TaskList.csv");
        if (!taskListParser.ParseCSV(taskListPath))
            Debug.LogError("任务列表加载失败，可能影响场景跳转和任务显示");
    }

    // 修改1：带层级参数的私有方法
    private List<string> GetRelatedNodesByVisibleNodes(int sceneID, string startPoint, int levels)
    {
        if (!int.TryParse(startPoint.Replace("Node", ""), out int startNodeID))
        {
            Debug.LogError($"StartPoint格式错误：{startPoint}");
            return new List<string>();
        }

        HashSet<int> relatedNodeIDs = new HashSet<int>();
        relatedNodeIDs.Add(startNodeID);

        List<int> currentLevelNodes = new List<int> { startNodeID };

        for (int i = 0; i < levels; i++)
        {
            List<int> nextLevelNodes = new List<int>();
            foreach (int nodeID in currentLevelNodes)
            {
                NodeVisibleCSVReader.NodeVisibleData nodeData = NodeVisibleCSVReader.Instance.GetVisibleData(sceneID, nodeID);
                if (nodeData != null)
                {
                    foreach (int visibleNodeID in nodeData.VisibleNodeIDs)
                    {
                        if (relatedNodeIDs.Add(visibleNodeID))
                        {
                            nextLevelNodes.Add(visibleNodeID);
                        }
                    }
                }
            }
            currentLevelNodes = nextLevelNodes;
        }

        List<string> resultNodeIDs = new List<string>();
        foreach (int nodeID in relatedNodeIDs)
        {
            resultNodeIDs.Add($"Node{nodeID}");
        }

        // 新增：按节点ID与起始节点的差值排序（模拟距离排序）
        resultNodeIDs.Sort((a, b) =>
        {
            // 提取节点ID的数字部分（比如"Node123"→123）
            int aId = int.Parse(a.Replace("Node", ""));
            int bId = int.Parse(b.Replace("Node", ""));
            // 按与起始节点的差值绝对值排序（模拟距离由近到远）
            return Mathf.Abs(aId - startNodeID).CompareTo(Mathf.Abs(bId - startNodeID));
        });

        // 确保return语句在Sort之后，且没有被提前阻断
        return resultNodeIDs;
    }

    // 修改2：带层级参数的公共方法
    public static List<string> GetRelatedNodesPublic(int sceneID, int currentNodeID, int levels)
    {
        HashSet<int> relatedNodeIDs = new HashSet<int>();
        relatedNodeIDs.Add(currentNodeID);

        List<int> currentLevelNodes = new List<int> { currentNodeID };

        for (int i = 0; i < levels; i++)
        {
            List<int> nextLevelNodes = new List<int>();
            foreach (int nodeID in currentLevelNodes)
            {
                NodeVisibleCSVReader.NodeVisibleData nodeData = NodeVisibleCSVReader.Instance.GetVisibleData(sceneID, nodeID);
                if (nodeData != null)
                {
                    foreach (int visibleNodeID in nodeData.VisibleNodeIDs)
                    {
                        if (relatedNodeIDs.Add(visibleNodeID))
                        {
                            nextLevelNodes.Add(visibleNodeID);
                        }
                    }
                }
            }
            currentLevelNodes = nextLevelNodes;
        }

        List<string> resultNodeIDs = new List<string>();
        foreach (int nodeID in relatedNodeIDs)
        {
            resultNodeIDs.Add($"Node{nodeID}");
        }

        // 新增：按与当前节点的距离由近到远排序（和私有方法逻辑一致）
        resultNodeIDs.Sort((a, b) =>
        {
            int aId = 0;
            int bId = 0;
            // 用TryParse避免格式异常，更健壮
            int.TryParse(a.Replace("Node", ""), out aId);
            int.TryParse(b.Replace("Node", ""), out bId);
            return Mathf.Abs(aId - currentNodeID).CompareTo(Mathf.Abs(bId - currentNodeID));
        });

        return resultNodeIDs;
    }


    // 预加载全景图
    private IEnumerator PreloadAllScenePanoramas()
    {
        if (!needPreload)
        {
            isPreloadComplete = true;
            EnableStartButton();
            yield break;
        }
        yield return WaitForCSVLoad();
        List<string> allNodeIDs = GetAllNodeIDsFromCSV();
        if (allNodeIDs.Count == 0)
        {
            Debug.LogWarning($"目标场景{targetSceneName}未找到任何节点，跳过预加载");
            isPreloadComplete = true;
            EnableStartButton();
            yield break;
        }
        totalNodesToLoad = allNodeIDs.Count;
        loadedNodesCount = 0;
        UpdateProgressUI();
        if (PanoramaMaterialManager.Instance == null)
        {
            Debug.LogError("PanoramaMaterialManager实例未找到，无法预加载全景图");
            isPreloadComplete = true;
            EnableStartButton();
            yield break;
        }

        int targetSceneID = int.Parse(targetSceneName.Replace("Scene", "")); // 提取纯场景ID（1）
        foreach (string nodeID in allNodeIDs)
        {
            // 原错误：yield return PanoramaMaterialManager.Instance.PreloadPanorama(nodeID, targetSceneName);
            yield return PanoramaMaterialManager.Instance.PreloadPanorama(nodeID, targetSceneID.ToString()); // 传递纯场景ID（"1"）
            loadedNodesCount++;
            UpdateProgressUI();
            yield return null;
        }
        Debug.Log($"目标场景{targetSceneName}全景图预加载完成，共加载{allNodeIDs.Count}张");
        isPreloadComplete = true;
        EnableStartButton();

    }



    // 点击【开始实验】触发（数据记录+场景跳转）
    public void StartExperiment()
    {
        // 点击后立即禁用按钮，防止重复点击
        if (startExperimentBtn != null)
        {
            startExperimentBtn.interactable = false;
        }

        StartCoroutine(JumpToTargetSceneAfterDelay());

    }


    // 点击【退出实验】触发（直接退出，不保存数据）
    public void ExitExperiment()
    {
        Debug.Log("用户点击退出实验，不保存数据");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // 编辑器中停止运行
#else
        Application.Quit(); // 打包后退出程序
#endif
    }

    // 更新进度UI
    private void UpdateProgressUI()
    {
        if (loadProgressBar != null && totalNodesToLoad > 0)
        {
            float progress = (float)loadedNodesCount / totalNodesToLoad;
            loadProgressBar.value = progress;
            if (progressText != null)
                progressText.text = $"{Mathf.RoundToInt(progress * 100)}%";
        }
    }

    // 等待CSV加载
    private IEnumerator WaitForCSVLoad()
    {
        if (CSVReader.AllNodeData == null || CSVReader.AllNodeData.Count == 0)
        {
            string csvPath = "ReadData/NodeInfo.csv";
            yield return CSVReader.LoadCSV(csvPath, () => { });
        }
        else
        {
            Debug.Log("已加载NodeInfo.csv，直接使用现有数据");
            yield return null;
        }
    }

    // 从CSV获取当前目标场景的所有节点ID（新增场景筛选）
    private List<string> GetAllNodeIDsFromCSV()
    {
        List<string> preloadNodeIDs = new List<string>();
        int targetSceneID = int.Parse(targetSceneName.Replace("Scene", "")); // 解析当前场景ID（如"Scene1"→1）

        // 关键步骤1：从TaskListParser获取当前任务的StartPoint（起始节点，需确保格式为"NodeX"）
        string startPoint = taskListParser.GetStartPointByTaskOrder(currentTaskOrder);
        if (string.IsNullOrEmpty(startPoint))
        {
            Debug.LogError("未从TaskList中获取到StartPoint，无法筛选预加载节点");
            return preloadNodeIDs;
        }
        Debug.Log($"当前任务：TaskOrder={currentTaskOrder}，场景ID={targetSceneID}，起始节点={startPoint}");

        preloadNodeIDs = GetRelatedNodesByVisibleNodes(targetSceneID, startPoint, preloadLayer);

        // 日志输出筛选结果，便于调试验证
        Debug.Log($"当前场景{targetSceneName}，基于StartPoint={startPoint}的{preloadLayer}层VisibleNodes筛选完成，共{preloadNodeIDs.Count}个预加载节点：{string.Join(",", preloadNodeIDs)}");

        return preloadNodeIDs;
    }

    // 场景跳转
    private IEnumerator JumpToTargetSceneAfterDelay()
    {
        while (!isPreloadComplete)
            yield return null;

        if (completeText != null)
        {
            completeText.text = "加载完成，即将进入场景...";
            completeText.gameObject.SetActive(true);
        }

        // 新增：跳转前重置PathRecording状态，确保新场景生成新文件
        if (PathRecording.Instance != null)
        {
            PathRecording.Instance.ResetForNewScene();
        }

        GlobalTaskState.Reset(); // 重置任务状态

        yield return new WaitForSeconds(1f);

 
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetSceneName);
        asyncLoad.allowSceneActivation = false;
        while (!asyncLoad.isDone)
        {
            if (asyncLoad.progress >= 0.9f)
            {
                asyncLoad.allowSceneActivation = true;
                // 记录场景开始时间（在场景即将激活时）
                sceneStartTime = Time.time;
            }
            yield return null;
        }
    }

    // 获取场景文件夹名
    public string GetSceneFolderName() => sceneFolderName;

    // 对外提供获取SceneID的公共方法（供VRGameManager等外部类调用）
    public int GetSceneIDByTaskOrder()
    {
        // 优先使用缓存的currentTaskOrder（Start方法中已赋值）
        if (currentTaskOrder > 0 && taskListParser != null)
        {
            return taskListParser.GetSceneIDByTaskOrder(currentTaskOrder);
        }
        // 降级逻辑：若缓存无效，从SceneManagerScript重新获取TaskOrder
        int taskOrder = SceneManagerScript.taskOrder;
        if (taskListParser != null)
        {
            return taskListParser.GetSceneIDByTaskOrder(taskOrder);
        }
        // 异常情况：返回默认值1（避免空引用）
        Debug.LogWarning("获取SceneID失败，返回默认值1");
        return 1;
    }

    // 初始化进度UI
    private void InitProgressUI()
    {
        if (loadProgressBar != null)
        {
            loadProgressBar.value = 0;
            loadProgressBar.gameObject.SetActive(true);
        }
        if (progressText != null)
            progressText.text = "0%";
        if (completeText != null)
            completeText.gameObject.SetActive(false);
    }

    // 显示错误
    private void ShowError(string errorMsg)
    {
        Debug.LogError(errorMsg);
        if (taskTipText != null)
        {
            Text tipText = taskTipText.GetComponent<Text>();
            if (tipText != null)
            {
                tipText.text = $"错误：{errorMsg}";
                tipText.color = Color.red;
            }
            taskTipText.SetActive(true);
        }
    }

}