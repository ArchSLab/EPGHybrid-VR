using System; // 新增
public static class GlobalTaskState
{
    public static bool IsTaskFailed { get; set; } = false;
    public static event Action OnTaskFailed; // 添加状态变更事件

    public static void Reset() => IsTaskFailed = false;

    // 添加设置失败状态的方法并触发事件
    public static void SetTaskFailed()
    {
        IsTaskFailed = true;
        OnTaskFailed?.Invoke();
    }
}