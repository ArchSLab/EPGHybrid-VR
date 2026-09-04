using UnityEngine;
using UnityEngine.SceneManagement;
using ViveSR.anipal.Eye;
using System.Text.RegularExpressions; // 新增这一行，引入Regex所在命名空间
using System.Collections;

public class TargetClickHandler : MonoBehaviour
{
    private void OnMouseDown()
    {
        // 检查是否点击了Target
        if (gameObject.CompareTag("Target"))
        {
            OnTargetClicked();
        }
    }

    // 也可以添加VR手柄射线检测
    public void OnRaycastHit()
    {
        if (gameObject.CompareTag("Target"))
        {
            OnTargetClicked();
        }
    }

    public void OnTargetClicked()
    {
        // 1. 先停止眼动记录（原有逻辑保留）
        EyeTrackingDataSaver eyeTracker = FindObjectOfType<EyeTrackingDataSaver>();
        if (eyeTracker != null)
        {
            eyeTracker.StopRecording();
        }

        // 2. 执行Target记录逻辑
        RecordTargetHit();

        // 3. 启动协程：等待记录完成后再跳转（核心修改）
        StartCoroutine(DelayLoadEndScene(0.1f)); // 0.1秒延迟，确保数据写入
    }

    /// <summary>
    /// 延迟加载EndScene的协程
    /// </summary>
    /// <param name="delay">延迟时间（秒），0.1秒足够完成文件写入</param>
    private IEnumerator DelayLoadEndScene(float delay)
    {
        // 等待延迟时间（让RecordTargetHit的文件写入操作完成）
        yield return new WaitForSeconds(delay);

        // 也可以用 WaitForEndOfFrame()，确保当前帧所有逻辑执行完毕
        // yield return new WaitForEndOfFrame();

        // 延迟后再跳转场景
        SceneManager.LoadScene("EndScene");
    }

    /// <summary>
    /// 解析Target名称中的数字并记录
    /// </summary>
    private void RecordTargetHit()
    {
        if (PathRecording.Instance == null) return;

        // 解析Target名称中的数字（例如"Target19" -> "19"）
        string targetName = gameObject.name;
        string targetID = Regex.Replace(targetName, "[^0-9]", "");
        // 如果没有数字，默认设为"0"
        if (string.IsNullOrEmpty(targetID))
            targetID = "0";

        // 调用记录方法：NodeID设为"0"，位置为Target的位置，传入解析的TargetID
        PathRecording.Instance.RecordNode(
            nodeID: "0",
            position: gameObject.transform.position,
            targetID: targetID
        );
        //Debug.Log($"已记录Target命中数据：TargetID={targetID}，位置={gameObject.transform.position}");
    }
}