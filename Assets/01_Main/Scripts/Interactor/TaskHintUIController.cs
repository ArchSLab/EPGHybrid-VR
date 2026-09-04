using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Valve.VR;
using System;
using System.IO;
using ViveSR.anipal.Eye;

public class TaskHintUIController : MonoBehaviour
{
    public static TaskHintUIController Instance { get; private set; }
    [Header("UI元素(GlobalUICanvas下)")]
    public GameObject globalUICanvas;
    private GameObject hintPanel;
    private Text hintText;
    private Button closeButton;
    private Button endTaskButton;
    private Text endTaskButtonText;  // 放弃按钮文本
    private Text timerText;          // 计时显示文本
    [Header("VR设置")]
    public float distanceFromCamera = 1.5f;
    public Vector3 offset = new Vector3(0, 0.1f, 0);
    private Transform vrCamera;
    [Header("SteamVR设置")]
    public SteamVR_Action_Boolean thumbPress;
    public SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.RightHand;
    [Header("放弃任务设置")]
    public float giveUpDelay = 60f;
    private float sceneLoadTime;
    private bool hasPassedDelay = false;
    private bool isTargetScene = false;
    private const string HINT_PANEL_NAME = "HintPanel";
    private const string UI_TAG = "GlobalUI";

    private void Awake()
    {
        Debug.Log("[TaskHint] Awake开始执行");

        // 场景判断逻辑保持不变
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == "EndScene" || currentScene == "StartScene" || currentScene == "EndSceneFailed")
        {
            Destroy(gameObject);
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
                Instance = null;
            }
            return;
        }

        // 单例模式逻辑保持不变
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        DontDestroyOnLoad(gameObject);
        if (globalUICanvas != null)
        {
            DontDestroyOnLoad(globalUICanvas);
            globalUICanvas.tag = UI_TAG;
        }

        AutoFindUIComponents();
        Debug.Log("[TaskHint] Awake执行完毕");
    }

    private void Start()
    {
        FindVRCamera();
        CheckCurrentSceneType();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        if (!isTargetScene) return;

        UpdateGiveUpButtonState();
        CheckThumbPressInput();
        UpdateTimerDisplay();  // 更新计时显示
    }

    private void LateUpdate()
    {
        if (isTargetScene && hintPanel?.activeSelf == true)
        {
            UpdateHintPanelPosition();
        }
    }

    private void CheckCurrentSceneType()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        isTargetScene = System.Text.RegularExpressions.Regex.IsMatch(currentSceneName, "^Scene\\d+$");
        //Debug.Log($"[TaskHint] 当前场景 名称={currentSceneName},是否目标场景={isTargetScene}");

        if (hintPanel != null)
        {
            hintPanel.SetActive(false);
        }

        if (isTargetScene)
        {
            sceneLoadTime = Time.time;
            hasPassedDelay = false;
            UpdateGiveUpButtonState();  // 初始化按钮状态
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        //Debug.Log($"[TaskHint] 场景加载完成: {scene.name}");

        if (scene.name == "EndScene" || scene.name == "StartScene" || scene.name == "EndSceneFailed")
        {
            Destroy(Instance.gameObject);
            Instance = null;
            return;
        }

        FindVRCamera();
        CheckCurrentSceneType();

        if (isTargetScene && hintText != null)
        {
            hintText.text = TaskTipSceneManager.CurrentTaskTip ?? "未获取到提示信息";
        }
    }

    private void AutoFindUIComponents()
    {
        if (globalUICanvas == null)
        {
            globalUICanvas = GameObject.FindGameObjectWithTag(UI_TAG);
        }

        if (globalUICanvas != null)
        {
            hintPanel = globalUICanvas.transform.Find(HINT_PANEL_NAME)?.gameObject;
            if (hintPanel != null)
            {
                hintPanel.SetActive(false); // 默认隐藏
            }
            hintText = hintPanel?.transform.Find("HintText")?.GetComponent<Text>();
            closeButton = hintPanel?.transform.Find("CloseButton")?.GetComponent<Button>();
            endTaskButton = hintPanel?.transform.Find("EndTaskButton")?.GetComponent<Button>();
            // 获取按钮文本组件
            endTaskButtonText = endTaskButton?.GetComponentInChildren<Text>();
            // 查找计时文本组件（需要在UI中添加一个名为TimerText的Text元素）
            timerText = hintPanel?.transform.Find("TimerText")?.GetComponent<Text>();

            BindButtonEvents();
        }
    }

    private void BindButtonEvents()
    {
        closeButton?.onClick.AddListener(() => hintPanel.SetActive(false));
        endTaskButton?.onClick.AddListener(() =>
        {
            // 1. 设置任务失败状态（会触发事件）
            GlobalTaskState.SetTaskFailed();

            // 2. 更新并记录实验数据
            if (TaskDataRecorder.Instance != null)
            {
                TaskDataRecorder.Instance.RecordExperimentData();
            }

            // 3. 处理眼动数据文件（新增部分）
            var eyeSaver = FindObjectOfType<ViveSR.anipal.Eye.EyeTrackingDataSaver>();
            if (eyeSaver != null)
            {
                eyeSaver.HandleAbandon();

                // 添加失败后缀
                string savePath = eyeSaver.SavePath; // 需要先在EyeTrackingDataSaver中添加SavePath属性
                if (!string.IsNullOrEmpty(savePath) && File.Exists(savePath) && !savePath.Contains("_failed"))
                {
                    string newPath = savePath.Replace(".csv", "_failed.csv");
                    if (!File.Exists(newPath))
                    {
                        File.Move(savePath, newPath);
                    }
                }
            }

            // 4. 跳转场景
            SceneManager.LoadScene("EndSceneFailed");
        });
    }

    private void CheckThumbPressInput()
    {
        if (thumbPress?.GetStateDown(inputSource) == true && hintPanel != null)
        {
            hintPanel.SetActive(true);
            UpdateGiveUpButtonState();  // 显示面板时更新按钮状态
        }
    }

    private void UpdateGiveUpButtonState()
    {
        if (endTaskButton == null || endTaskButtonText == null) return;

        // 始终显示按钮
        endTaskButton.gameObject.SetActive(true);

        if (!hasPassedDelay)
        {
            // 计时未到：不可点击，显示"继续寻找"
            hasPassedDelay = Time.time - sceneLoadTime >= giveUpDelay;
            endTaskButton.interactable = false;
            endTaskButtonText.text = "继续寻找";
        }
        else
        {
            // 计时已到：可点击，显示"放弃任务"
            endTaskButton.interactable = true;
            endTaskButtonText.text = "放弃任务";
        }
    }

    // 更新计时显示
    private void UpdateTimerDisplay()
    {
        if (timerText == null || !isTargetScene) return;

        float elapsedTime = Time.time - sceneLoadTime;
        TimeSpan timeSpan = TimeSpan.FromSeconds(elapsedTime);
        timerText.text = $"本次任务已进行{timeSpan.Minutes:D2}分{timeSpan.Seconds:D2}秒";
    }

    private void UpdateHintPanelPosition()
    {
        if (vrCamera == null) return;
        hintPanel.transform.position = vrCamera.position + vrCamera.forward * distanceFromCamera + offset;
        hintPanel.transform.LookAt(vrCamera);
        hintPanel.transform.Rotate(0, 180, 0);
    }

    private void FindVRCamera()
    {
        vrCamera = Camera.main?.transform;
    }

    public GameObject GetHintPanel()
    {
        return hintPanel;
    }
}