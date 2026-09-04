using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.IO;
using System;
using TMPro;
using System.Collections.Generic; // 新增：用于字典
using System.Linq; // 新增：用于处理数组操作

public class SceneManagerScript : MonoBehaviour
{
    // UI 组件引用
    public TMP_InputField testerIDInput;
    public TMP_InputField heightInput;
    public TMP_Dropdown ageDropdown;
    public TMP_Dropdown genderDropdown;
    public TMP_InputField taskOrderInput;
    public Toggle eyeTrackingToggle;
    public Toggle closePreloadToggle;
    public TMPro.TMP_Text errorText;
  

    public int taskOrderMin = 1;
    public int taskOrderMax = 100;

    // 静态变量（供后续程序读取）
    public static string testerID;
    public static int age;
    public static string gender;
    public static string height;
    public static float heightValue;
    public static int taskOrder;
    public static bool isEyeTrackingClosed;
    public static bool isPreloadClosed;
    // 仅保留必要的数据字典，供后续场景调用
    public static Dictionary<string, object> taskDataDict = new Dictionary<string, object>();

    private void Awake()
    {
        // 初始化错误提示
        if (errorText != null)
        {
            errorText.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogError("errorText未赋值，请在Inspector中绑定Text组件");
        }
        // 新增：加载默认TaskOrder
        LoadDefaultTaskOrder();
    }

    // 新增：加载默认TaskOrder的方法
    private void LoadDefaultTaskOrder()
    {
        try
        {
            // 构建CSV文件路径
            string filePath = Path.Combine(Application.streamingAssetsPath, "TaskRecorder.csv");

            // 检查文件是否存在
            if (File.Exists(filePath))
            {
                // 读取所有行，跳过空行
                string[] lines = File.ReadAllLines(filePath)
                                    .Where(line => !string.IsNullOrWhiteSpace(line))
                                    .ToArray();

                // 确保有数据行（至少包含标题行和一条数据行）
                if (lines.Length >= 2)
                {
                    // 获取最新一行数据（最后一行）
                    string lastLine = lines[lines.Length - 1];
                    string[] data = lastLine.Split(',');

                    // 假设TaskOrder是CSV中的某一列（根据实际CSV结构调整索引）
                    // TaskOrder在第2列（索引1，从0开始），请根据实际情况修改
                    if (data.Length > 1 && int.TryParse(data[1], out int lastTaskOrder))
                    {
                        // 计算默认值：+1并处理边界
                        int defaultOrder = lastTaskOrder + 1;
                        if (defaultOrder > taskOrderMax)
                        {
                            defaultOrder = taskOrderMin;
                        }

                        // 设置输入框默认值
                        taskOrderInput.text = defaultOrder.ToString();
                        return;
                    }
                }
            }

            // 如果文件不存在或读取失败，使用最小值作为默认值
            taskOrderInput.text = taskOrderMin.ToString();
        }
        catch (Exception ex)
        {
            Debug.LogError($"读取TaskOrder失败: {ex.Message}");
            // 异常情况下使用最小值作为默认值
            taskOrderInput.text = taskOrderMin.ToString();
        }
    }

    public void OnStartButtonClicked()
    {
        // 隐藏错误提示
        if (errorText != null)
        {
            errorText.gameObject.SetActive(false);
            errorText.text = "";
        }
        else
        {
            Debug.LogError("errorText未赋值");
            return;
        }

        // 数据验证
        // 1. 验证测试者ID
        if (string.IsNullOrEmpty(testerIDInput.text))
        {
            ShowError("请输入测试者ID");
            return;
        }

        // 2. 验证身高
        if (!float.TryParse(heightInput.text, out float parsedHeight))
        {
            ShowError("身高格式错误，请输入数字");
            return;
        }
        heightValue = parsedHeight;
        height = heightInput.text;

        // 3. 验证TaskOrder
        if (string.IsNullOrEmpty(taskOrderInput.text))
        {
            ShowError("请输入TaskOrder");
            return;
        }
        if (!int.TryParse(taskOrderInput.text, out int parsedOrder))
        {
            ShowError("TaskOrder必须是数字");
            return;
        }
        if (parsedOrder < taskOrderMin || parsedOrder > taskOrderMax)
        {
            ShowError($"TaskOrder必须在{taskOrderMin}-{taskOrderMax}之间");
            return;
        }
        taskOrder = parsedOrder;

        // 赋值静态变量
        testerID = testerIDInput.text;
        age = ageDropdown.value;
        gender = genderDropdown.options[genderDropdown.value].text;
        isEyeTrackingClosed = eyeTrackingToggle.isOn;
        // 通知GazeRay管理器更新状态
        if (GazeRayManager.Instance != null)
        {
            GazeRayManager.Instance.RefreshState();
        }
        isPreloadClosed = closePreloadToggle.isOn;
        taskDataDict.Add("IsPreloadClosed", isPreloadClosed);

        // 填充数据字典（仅保留必要字段）
        taskDataDict.Clear();
        taskDataDict.Add("TesterID", testerID);
        taskDataDict.Add("Age", age);
        taskDataDict.Add("Gender", gender);
        taskDataDict.Add("Height", heightValue);
        taskDataDict.Add("TaskOrder", taskOrder);
        taskDataDict.Add("EyeTrackingStatus", isEyeTrackingClosed ? "F" : "T");

        // 跳转到任务提示场景
        SceneManager.LoadScene("TaskTipScene");
    }

    // 错误提示显示方法
    private void ShowError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
            errorText.gameObject.SetActive(true);
        }
    }

    // 返回开始场景（如需保留）
    public void ReturnToStartScene()
    {
        SceneManager.LoadScene("StartScene");
    }
}
