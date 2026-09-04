using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
public class TargetValidator : MonoBehaviour
{
    [Header("提示文本组件")]
    public Text hintText;
    private TaskListParser taskListParser;
    public int currentTaskOrder;

    private void Awake()
    {
        taskListParser = new TaskListParser();
        string taskListPath = System.IO.Path.Combine(Application.streamingAssetsPath, "ReadData/TaskList.csv");
        taskListParser.ParseCSV(taskListPath);
    }

    private void Start()
    {
        currentTaskOrder = SceneManagerScript.taskOrder;
        if (hintText != null)
        {
            hintText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 统一验证逻辑：所有任务通过EndPoint_All数组判断
    /// </summary>
    public bool ValidateTarget(GameObject target)
    {
        // 基础校验：目标不为空且带有Target标签
        if (target == null || !target.CompareTag("Target"))
        {
            ShowHint("无效的目标");
            return false;
        }

        // 提取目标名称中的数字部分（如Target1→1，Target23→23）
        string targetName = target.name;
        string numberPart = System.Text.RegularExpressions.Regex.Replace(targetName, "[^0-9]", "");
        if (string.IsNullOrEmpty(numberPart) || !int.TryParse(numberPart, out int targetNum))
        {
            ShowHint("目标名称格式错误，无法识别");
            return false;
        }

        // 添加后台日志，显示当前验证的目标
        Debug.Log($"本次验证的目标为：Target{targetNum}");

        // 获取当前任务的EndPoint_All数组
        List<int> validTargetNums = taskListParser.GetEndpointAllByTaskOrder(currentTaskOrder);

        // 校验EndPoint_All数组是否有效
        if (validTargetNums.Count == 0)
        {
            ShowHint("当前任务无有效目标列表");
            return false;
        }

        // 验证目标是否在有效数组中
        if (validTargetNums.Contains(targetNum))
        {
            HideHint();
            return true;
        }
        else
        {
            ShowHint("未找到正确终点，请重新寻找");
            return false;
        }
    }

    // 提示相关方法保持不变
    private void ShowHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
            hintText.gameObject.SetActive(true);
            StartCoroutine(HideHintAfterDelay(3f));
        }
    }

    private void HideHint()
    {
        if (hintText != null)
        {
            hintText.gameObject.SetActive(false);
        }
    }

    private System.Collections.IEnumerator HideHintAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideHint();
    }

    // 在TargetValidator.cs中添加
    public int? GetTargetNumber(GameObject target)
    {
        string targetName = target.name;
        string numberPart = System.Text.RegularExpressions.Regex.Replace(targetName, "[^0-9]", "");
        if (int.TryParse(numberPart, out int targetNum))
        {
            return targetNum;
        }
        return null;
    }

    public List<int> GetCurrentTaskValidTargets()
    {
        // 调用 taskListParser 获取当前任务的有效目标列表
        return taskListParser.GetEndpointAllByTaskOrder(currentTaskOrder);
    }
}