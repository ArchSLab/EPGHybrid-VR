using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class EndSceneFailedManager : MonoBehaviour
{
    private void Start()
    {
        if (PanoramaMaterialManager.Instance != null)
        {
            PanoramaMaterialManager.Instance.ForceClearAllCache();
        }
        StartCoroutine(ForceUnloadAssets());
    }

    // 补充缺失的协程方法
    private IEnumerator ForceUnloadAssets()
    {
        Debug.Log("[强制清理] 开始释放未使用资源...");
        AsyncOperation op = Resources.UnloadUnusedAssets();
        while (!op.isDone) yield return null;

        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        Debug.Log("[强制清理] 资源释放完成！");
    }

    // 返回到开始场景（或任务提示场景）
    public void OnBackButtonClicked()
    {
        UnloadPreviousSceneResources();
        SceneManager.LoadScene("StartScene"); // 或根据需求跳转到TaskTipScene
    }

    // 卸载上一场景资源（复用EndSceneManager的逻辑）
    private void UnloadPreviousSceneResources()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        List<Scene> scenesToUnload = new List<Scene>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.name != currentScene.name && scene.isLoaded)
            {
                scenesToUnload.Add(scene);
            }
        }

        foreach (var scene in scenesToUnload)
        {
            string sceneID = scene.name.Replace("Scene", "");
            StartCoroutine(UnloadSceneAndClearResources(scene, sceneID));
        }
    }

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
            Debug.Log($"失败场景[{scene.name}]卸载完成，清理纹理{clearedTextures}个");
        }

        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }
}