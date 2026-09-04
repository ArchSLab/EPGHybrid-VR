using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections; // 关键：补充此命名空间
using System.Collections.Generic; // 补充此命名空间

public class EndSceneManager : MonoBehaviour
{
    // 继续下一个任务
    private void Awake()
    {
        Debug.Log("[EndSceneManager] 组件已初始化！当前激活场景：" + SceneManager.GetActiveScene().name);
    }

    private void Start()
    {
        Debug.Log("[EndSceneManager] 组件已初始化！当前激活场景：" + SceneManager.GetActiveScene().name);
        Debug.Log("[EndSceneManager] 开始执行强制缓存重置");

        // 直接调用PanoramaMaterialManager的强制清空方法
        if (PanoramaMaterialManager.Instance != null)
        {
            PanoramaMaterialManager.Instance.ForceClearAllCache();
        }
        else
        {
            Debug.LogError("[EndSceneManager] PanoramaMaterialManager实例不存在！");
        }

        // 强制释放资源
        StartCoroutine(ForceUnloadAssets());
    }

    private IEnumerator ForceUnloadAssets()
    {
        Debug.Log("[强制清理] 开始释放未使用资源...");
        AsyncOperation op = Resources.UnloadUnusedAssets();
        while (!op.isDone) yield return null;

        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        Debug.Log("[强制清理] 资源释放完成！");
    }

    public void OnContinueButtonClicked()
    {
        // 1. 卸载上一个场景资源（关键新增逻辑）
        UnloadPreviousSceneResources();

        // 增加TaskOrder，超过最大值则重置为1
        int maxTaskID = GetMaxTaskOrder();
        SceneManagerScript.taskOrder++;
        if (SceneManagerScript.taskOrder > maxTaskID)  // 使用maxTaskID替换TaskOrder
        {
            SceneManagerScript.taskOrder = 1;
        }

        // 标记不需要重新预加载全景图（根据新逻辑调整，这里可以先不设置，在TaskTipSceneManager中处理）
        // TaskTipSceneManager.needPreload = false;

        // 跳转到TaskTipScene
        SceneManager.LoadScene("TaskTipScene");
    }

    // 结束实验
    public void OnExitButtonClicked()
    {
        // 退出游戏
        Application.Quit();
        // 在编辑器中停止播放
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // 新增：卸载上一个场景资源
    // 在 EndSceneManager.cs 的 UnloadPreviousSceneResources 方法中修改
    private void UnloadPreviousSceneResources()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        List<Scene> scenesToUnload = new List<Scene>();

        // 收集所有需要卸载的场景（排除当前场景）
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.name != currentScene.name && scene.isLoaded)
            {
                scenesToUnload.Add(scene);
            }
        }

        // 逐个卸载场景并清理资源（关键：等待异步卸载完成）
        foreach (var scene in scenesToUnload)
        {
            string sceneID = scene.name.Replace("Scene", "");
            // 等待场景完全卸载后再清理资源
            StartCoroutine(UnloadSceneAndClearResources(scene, sceneID));
        }
    }

    // 新增协程：同步卸载场景和清理资源
    private IEnumerator UnloadSceneAndClearResources(Scene scene, string sceneID)
    {
        AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(scene);
        while (!unloadOp.isDone)
        {
            yield return null;
        }

        if (PanoramaMaterialManager.Instance != null)
        {
            int clearedTextures = PanoramaMaterialManager.Instance.ClearSceneCache(sceneID);
            PanoramaMaterialManager.Instance.ClearSceneABCache(sceneID);
            Debug.Log($"场景[{scene.name}]卸载完成，清理纹理{clearedTextures}个，AB包1个");
        }

        Resources.UnloadUnusedAssets();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }

    // 从TaskList.csv获取最大TaskID
    private int GetMaxTaskOrder()
    {
        TaskListParser parser = new TaskListParser();
        string taskListPath = Path.Combine(Application.streamingAssetsPath, "ReadData/TaskList.csv");
        if (parser.ParseCSV(taskListPath))
        {
            return parser.GetMaxTaskOrder();
        }
        return 1; // 默认最大值为1
    }
}