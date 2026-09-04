using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Assertions;
using ViveSR.anipal.Eye;

public class GazeVisualizer : MonoBehaviour
{
    [Tooltip("射线长度")]
    public int rayLength = 25;
    [Tooltip("用于绘制视线的LineRenderer")]
    [SerializeField] private LineRenderer gazeRenderer;

    private static EyeData eyeData = new EyeData();
    private bool isCallbackRegistered = false;

    private void Start()
    {
        // 检查Eye框架是否启用
        if (!SRanipal_Eye_Framework.Instance.EnableEye)
        {
            Debug.LogError("眼动追踪未启用！");
            enabled = false;
            return;
        }
        // 检查LineRenderer是否赋值
        Assert.IsNotNull(gazeRenderer, "请为视线渲染器赋值！");
    }

    private void Update()
    {
        // 检查Eye框架状态
        if (SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.WORKING &&
            SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.NOT_SUPPORT)
            return;

        // 注册/注销眼动数据回调
        if (SRanipal_Eye_Framework.Instance.EnableEyeDataCallback && !isCallbackRegistered)
        {
            SRanipal_Eye.WrapperRegisterEyeDataCallback(
                Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback)
            );
            isCallbackRegistered = true;
        }
        else if (!SRanipal_Eye_Framework.Instance.EnableEyeDataCallback && isCallbackRegistered)
        {
            SRanipal_Eye.WrapperUnRegisterEyeDataCallback(
                Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback)
            );
            isCallbackRegistered = false;
        }

        // 获取视线原点和方向
        Vector3 gazeOriginLocal, gazeDirectionLocal;
        bool gotGaze = false;

        if (isCallbackRegistered)
        {
            // 通过回调数据获取视线（优先合并眼，再左眼，再右眼）
            gotGaze = SRanipal_Eye.GetGazeRay(GazeIndex.COMBINE, out gazeOriginLocal, out gazeDirectionLocal, eyeData) ||
                      SRanipal_Eye.GetGazeRay(GazeIndex.LEFT, out gazeOriginLocal, out gazeDirectionLocal, eyeData) ||
                      SRanipal_Eye.GetGazeRay(GazeIndex.RIGHT, out gazeOriginLocal, out gazeDirectionLocal, eyeData);
        }
        else
        {
            // 直接获取视线数据
            gotGaze = SRanipal_Eye.GetGazeRay(GazeIndex.COMBINE, out gazeOriginLocal, out gazeDirectionLocal) ||
                      SRanipal_Eye.GetGazeRay(GazeIndex.LEFT, out gazeOriginLocal, out gazeDirectionLocal) ||
                      SRanipal_Eye.GetGazeRay(GazeIndex.RIGHT, out gazeOriginLocal, out gazeDirectionLocal);
        }

        if (!gotGaze) return;

        // 转换视线方向到世界空间
        Vector3 gazeDirectionWorld = Camera.main.transform.TransformDirection(gazeDirectionLocal);
        // 计算射线起点（相机位置微调，模拟眼睛位置）
        Vector3 rayStart = Camera.main.transform.position - Camera.main.transform.up * 0.05f;
        // 计算射线终点
        Vector3 rayEnd = rayStart + gazeDirectionWorld * rayLength;

        // 更新LineRenderer位置
        gazeRenderer.SetPosition(0, rayStart);
        gazeRenderer.SetPosition(1, rayEnd);
    }

    // 释放资源（注销回调）
    private void OnDestroy()
    {
        if (isCallbackRegistered)
        {
            SRanipal_Eye.WrapperUnRegisterEyeDataCallback(
                Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback)
            );
            isCallbackRegistered = false;
        }
    }

    // 眼动数据回调（更新本地数据）
    private static void EyeCallback(ref EyeData data)
    {
        eyeData = data;
    }
}