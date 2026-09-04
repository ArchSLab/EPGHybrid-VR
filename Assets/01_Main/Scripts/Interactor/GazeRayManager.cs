using UnityEngine;
using UnityEngine.SceneManagement;

public class GazeRayManager : MonoBehaviour
{
    public static GazeRayManager Instance { get; private set; }

    private GameObject gazeRayObject;
    private LineRenderer gazeRayLine;

    private void Awake()
    {
        // 单例模式确保全局唯一
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // 场景加载完成后初始化GazeRay
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 只在名称以"Scene"开头的场景（如Scene1、Scene2）中初始化GazeRay
        // 自动排除StartScene、TaskTipScene、EndScene
        if (scene.name.StartsWith("Scene"))
        {
            StartCoroutine(InitializeGazeRayDelayed());
        }
        else
        {
            // 非目标场景，重置GazeRay引用（避免残留无效引用）
            gazeRayObject = null;
            gazeRayLine = null;
        }
    }

    // 延迟初始化确保场景完全加载
    private System.Collections.IEnumerator InitializeGazeRayDelayed()
    {
        yield return null; // 等待1帧
        FindGazeRayObject();

        // 首次未找到则1秒后重试
        if (gazeRayObject == null)
        {
            Debug.LogWarning("GazeRay初始化失败，1秒后重试...");
            yield return new WaitForSeconds(1f);
            FindGazeRayObject();
        }

        // 初始化时同步状态
        UpdateGazeRayVisibility();
    }

    // 查找场景中的GazeRay对象（仅目标场景执行）
    private void FindGazeRayObject()
    {
        gazeRayObject = GameObject.Find("GazeRay");
        if (gazeRayObject != null)
        {
            gazeRayLine = gazeRayObject.GetComponent<LineRenderer>();
            //Debug.Log($"当前场景{SceneManager.GetActiveScene().name}：GazeRay对象找到并初始化");
        }
        else
        {
            //Debug.LogError($"当前场景{SceneManager.GetActiveScene().name}：未找到GazeRay对象，请检查场景中是否存在名称为'GazeRay'的物体");
        }
    }

    // 更新GazeRay可见性（核心控制逻辑）
    public void UpdateGazeRayVisibility()
    {
        if (gazeRayObject == null) return;

        // 根据眼动追踪状态设置可见性
        bool isVisible = !SceneManagerScript.isEyeTrackingClosed;

        // 同时控制对象激活状态、LineRenderer和子对象
        gazeRayObject.SetActive(isVisible);
        if (gazeRayLine != null) gazeRayLine.enabled = isVisible;
        foreach (Transform child in gazeRayObject.transform)
        {
            child.gameObject.SetActive(isVisible);
        }

        //Debug.Log($"当前场景{SceneManager.GetActiveScene().name}：GazeRay可见性更新为{(isVisible ? "显示" : "隐藏")}");
    }

    // 外部调用强制刷新状态
    public void RefreshState()
    {
        UpdateGazeRayVisibility();
    }
}