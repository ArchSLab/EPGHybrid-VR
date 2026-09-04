//========= Copyright 2018, HTC Corporation. All rights reserved. ===========
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Collections.Generic;

namespace ViveSR
{
    namespace anipal
    {
        namespace Eye
        {
            public class EyeTrackingDataSaver : MonoBehaviour
            {
                private TaskListParser taskListParser;

                [SerializeField] private bool useEyeDataCallback = true;
                [SerializeField] private Transform hmdTransform; // 头部变换组件
                [SerializeField] private int recordingFrequency = 120; // 记录频率(Hz)
                [SerializeField] private LayerMask targetLayers = ~0; // 射线检测目标层，默认为所有层
                [SerializeField] private float maxRaycastDistance = 100f; // 最大射线距离
                [SerializeField] private GazeHitVisualizer hitVisualizer; // 视线命中可视化组件
                private string saveBasePath;

                private static EyeData_v2 eyeData = new EyeData_v2();
                private bool eyeCallbackRegistered = false;
                private Camera mainCamera;
                private StreamWriter dataWriter;
                private string savePath;
                private float recordInterval; // 记录间隔(秒)
                private float lastRecordTime; // 上一次记录时间
                private bool isRecording = false; // 记录状态标识

                private bool isFileInitialized = false; // 数据文件是否已初始化
                private bool hasDataBeenWritten = false; // 数据是否已被写入

                // 场景和节点管理引用
                private VRGameManager gameManager;
                private TaskTipSceneManager taskTipManager;

                public bool IsRecording => isRecording; // 只读属性，返回记录状态
                public string SavePath => savePath;


                private void Start()
                {

                    saveBasePath = ProjectPathConfig.RecordingSaveRoot;

                    if (!Directory.Exists(saveBasePath))
                    {
                        Directory.CreateDirectory(saveBasePath);
                    }

                    // 初始化任务列表解析器并加载任务列表
                    taskListParser = new TaskListParser();
                    LoadTaskList();

                    // 检查眼动框架
                    if (SRanipal_Eye_Framework.Instance == null)
                    {
                        Debug.LogError("SRanipal_Eye_Framework component not found");
                        enabled = false;
                        return;
                    }

                    // 检查眼动追踪是否启用
                    if (!SRanipal_Eye_Framework.Instance.EnableEye)
                    {
                        Debug.LogError("Eye tracking is not enabled in SRanipal_Eye_Framework");
                        enabled = false;
                        return;
                    }

                    // 获取主相机
                    mainCamera = Camera.main;
                    if (mainCamera == null)
                    {
                        Debug.LogError("Main Camera not found (Tag: MainCamera)");
                        enabled = false;
                        return;
                    }

                    // 设置头部变换组件，默认为主相机
                    if (hmdTransform == null)
                    {
                        hmdTransform = mainCamera.transform;
                        Debug.LogWarning("HMD Transform not assigned, using Main Camera transform instead");
                    }

                    // 检查命中可视化组件
                    if (hitVisualizer == null)
                    {
                        Debug.LogWarning("GazeHitVisualizer not assigned, visualization will be disabled");
                    }

                    // 初始化记录频率和间隔
                    if (recordingFrequency <= 0)
                    {
                        recordingFrequency = 30;
                        Debug.LogWarning("Invalid recording frequency, setting to 30Hz");
                    }
                    recordInterval = 1f / recordingFrequency;
                    lastRecordTime = -recordInterval; // 确保第一帧能被记录

                    // 查找游戏管理器
                    gameManager = FindObjectOfType<VRGameManager>();
                    if (gameManager == null)
                    {
                        Debug.LogWarning("VRGameManager not found, NodeID will be 'Unknown'");
                    }

                }

                private void LoadTaskList()
                {
                    string taskListPath = Path.Combine(Application.streamingAssetsPath, "ReadData/TaskList.csv");
                    if (!taskListParser.ParseCSV(taskListPath))
                        Debug.LogError("任务列表加载失败，可能影响SceneID获取");
                }

                private void InitializeDataFile()
                {
                    try
                    {
                        string basePath = saveBasePath;
                        if (!Directory.Exists(basePath))
                        {
                            Directory.CreateDirectory(basePath);
                        }

                        // 使用TaskDataRecorder记录的当前任务Unix时间戳
                        long taskUnixTimestamp = TaskDataRecorder.CurrentTaskUnixTimestamp;

                        // 眼动数据文件名格式：Eye_TesterID_TaskOrder_UnixTimestamp
                        string suffix = GlobalTaskState.IsTaskFailed ? "_failed" : "";
                        string fileName = $"Eye_ID{SceneManagerScript.testerID}_Task{SceneManagerScript.taskOrder}_{taskUnixTimestamp}{suffix}.csv";
                        savePath = Path.Combine(basePath, fileName);

                        dataWriter = new StreamWriter(savePath);
                        // CSV头部信息包含所有需要记录的数据
                        dataWriter.WriteLine("UnixTimestamp," +
                                            "Timestamp," +
                                            "RecordingTime," +
                                             "SceneTime," +  // 场景运行时间
                                            "SceneID," +  // 场景ID
                                            "NodeID," +    // 节点ID
                                            "GazeIndex," +
                                            // 原始眼动坐标系数据
                                            "GazeOriginLocalX,GazeOriginLocalY,GazeOriginLocalZ," +
                                            "GazeDirectionLocalX,GazeDirectionLocalY,GazeDirectionLocalZ," +
                                            // 世界坐标系数据
                                            "GazeOriginWorldX,GazeOriginWorldY,GazeOriginWorldZ," +
                                            "GazeDirectionWorldX,GazeDirectionWorldY,GazeDirectionWorldZ," +
                                            // 射线检测结果数据
                                            "HitPointX,HitPointY,HitPointZ," +
                                            "HitDistance," +
                                            "HitObjectName," +
                                            "HitObjectTag," +
                                            // 眼动追踪数据
                                            "LeftPupilDiameter,RightPupilDiameter," +
                                            "LeftEyeOpenness,RightEyeOpenness," +
                                            // HMD姿态数据
                                            "HMDPositionX,HMDPositionY,HMDPositionZ," +
                                            "HMDRotationEulerX,HMDRotationEulerY,HMDRotationEulerZ," +
                                             // 相机参数
                                             "CameraFOV_V," +               // 垂直视场角
                                            "CameraFOV_H," +               // 水平视场角
                                            "CameraNearClip," +           // 近裁剪面
                                            "CameraFarClip," +            // 远裁剪面
                                            "CameraAspectRatio," +        // 宽高比
                                            "CameraPixelWidth," +         // 像素宽度
                                            "CameraPixelHeight," +        // 像素高度
                                            "CameraFocalLength," +   // 焦距(mm)
                                            "TaskHintPanel");


                        Debug.Log($"Data will be saved to: {savePath}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to initialize data file: {e.Message}");
                        enabled = false;
                    }
                }

                public void StartRecording()
                {
                    // 防止重复开始记录
                    if (isRecording) return;

                    // 设置记录状态
                    isRecording = true;
                    Debug.Log("开始记录眼动数据");

                    // 初始化文件（如果尚未初始化）
                    if (!isFileInitialized)
                    {
                        InitializeDataFile();
                        isFileInitialized = true;
                    }
                }


                private void Update()
                {
                    if (SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.WORKING &&
                        SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.NOT_SUPPORT)
                        return;

                    // 注册/注销回调
                    if (useEyeDataCallback && !eyeCallbackRegistered)
                    {
                        SRanipal_Eye_v2.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye_v2.CallbackBasic)EyeCallback));
                        eyeCallbackRegistered = true;
                    }
                    else if (!useEyeDataCallback && eyeCallbackRegistered)
                    {
                        SRanipal_Eye_v2.WrapperUnRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye_v2.CallbackBasic)EyeCallback));
                        eyeCallbackRegistered = false;
                    }

                    // 按设定频率记录数据
                    if (Time.unscaledTime - lastRecordTime >= recordInterval)
                    {
                        LogAndSaveGazeData();
                        lastRecordTime = Time.unscaledTime;
                    }
                }

                private void LogAndSaveGazeData()
                {
                    // 非记录状态时退出
                    if (!isRecording) return;

                    if (dataWriter == null) return;

                    // 初始化眼动数据变量
                    GazeIndex gazeIndex = (GazeIndex)0;
                    Vector3 gazeOriginLocal = Vector3.zero;
                    Vector3 gazeDirectionLocal = Vector3.zero;
                    Vector3 gazeOriginWorld = Vector3.zero;
                    Vector3 gazeDirectionWorld = Vector3.zero;
                    Vector3 hitPoint = Vector3.zero;
                    float hitDistance = 0f;
                    string hitObjectName = "0";
                    string hitObjectTag = "0";
                    float leftPupilDiameter = 0f;
                    float rightPupilDiameter = 0f;
                    float leftEyeOpenness = 0f;
                    float rightEyeOpenness = 0f;
                    bool dataValid = false;

                    // 眼动追踪未关闭时获取数据
                    if (!SceneManagerScript.isEyeTrackingClosed)
                    {
                        gazeIndex = GazeIndex.COMBINE;
                        if (eyeCallbackRegistered)
                        {
                            dataValid = SRanipal_Eye_v2.GetGazeRay(gazeIndex, out gazeOriginLocal, out gazeDirectionLocal, eyeData);
                            if (!dataValid)
                            {
                                gazeIndex = GazeIndex.LEFT;
                                dataValid = SRanipal_Eye_v2.GetGazeRay(gazeIndex, out gazeOriginLocal, out gazeDirectionLocal, eyeData);
                                if (!dataValid)
                                {
                                    gazeIndex = GazeIndex.RIGHT;
                                    dataValid = SRanipal_Eye_v2.GetGazeRay(gazeIndex, out gazeOriginLocal, out gazeDirectionLocal, eyeData);
                                }
                            }
                        }
                        else
                        {
                            dataValid = SRanipal_Eye_v2.GetGazeRay(gazeIndex, out gazeOriginLocal, out gazeDirectionLocal);
                            if (!dataValid)
                            {
                                gazeIndex = GazeIndex.LEFT;
                                dataValid = SRanipal_Eye_v2.GetGazeRay(gazeIndex, out gazeOriginLocal, out gazeDirectionLocal);
                                if (!dataValid)
                                {
                                    gazeIndex = GazeIndex.RIGHT;
                                    dataValid = SRanipal_Eye_v2.GetGazeRay(gazeIndex, out gazeOriginLocal, out gazeDirectionLocal);
                                }
                            }
                        }

                        if (dataValid)
                        {
                            // 转换到世界坐标系
                            gazeOriginWorld = mainCamera.transform.TransformPoint(gazeOriginLocal);
                            gazeDirectionWorld = mainCamera.transform.TransformDirection(gazeDirectionLocal);

                            // 射线检测
                            RaycastHit hit;
                            bool hasHit = Physics.Raycast(gazeOriginWorld, gazeDirectionWorld, out hit, maxRaycastDistance, targetLayers);
                            if (hasHit)
                            {
                                hitPoint = hit.point;
                                hitDistance = hit.distance;
                                if (hit.collider != null)
                                {
                                    hitObjectName = hit.collider.gameObject.name;
                                    hitObjectTag = hit.collider.gameObject.tag;
                                }
                            }

                            // 获取瞳孔和眼睛状态数据
                            leftPupilDiameter = eyeData.verbose_data.left.pupil_diameter_mm;
                            rightPupilDiameter = eyeData.verbose_data.right.pupil_diameter_mm;
                            leftEyeOpenness = eyeData.verbose_data.left.eye_openness;
                            rightEyeOpenness = eyeData.verbose_data.right.eye_openness;

                            // 更新可视化
                            if (hitVisualizer != null)
                            {
                                hitVisualizer.UpdateHitPoint(hitPoint, hasHit);
                            }
                        }
                    }

                    // 获取HMD姿态
                    Vector3 hmdPosition = hmdTransform.position;
                    Vector3 hmdRotationEuler = hmdTransform.rotation.eulerAngles;

                    // 记录时间
                    float recordingTime = Time.time;
                    long unixTimestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                    // 相机参数
                    float cameraFOV_V = mainCamera.fieldOfView;
                    float aspectRatio = mainCamera.aspect;
                    float cameraFOV_H = 2 * Mathf.Atan(Mathf.Tan(cameraFOV_V * 0.5f * Mathf.Deg2Rad) * aspectRatio) * Mathf.Rad2Deg;

                    // 节点ID
                    string currentNodeID = "Unknown";
                    if (gameManager != null)
                    {
                        string originalNodeID = gameManager.GetCurrentNodeID();
                        if (int.TryParse(originalNodeID.Replace("Node", ""), out int nodeNumber))
                        {
                            currentNodeID = nodeNumber.ToString();
                        }
                        else
                        {
                            currentNodeID = originalNodeID;
                        }
                    }

                    // 场景ID
                    string currentSceneID = "Unknown";
                    int currentTaskOrder = SceneManagerScript.taskOrder;
                    int sceneID = taskListParser.GetSceneIDByTaskOrder(currentTaskOrder);
                    if (sceneID != -1)
                    {
                        currentSceneID = sceneID.ToString();
                    }
                    else
                    {
                        Debug.LogWarning($"无法获取任务序号为{currentTaskOrder}的SceneID");
                    }

                    // 场景运行时间
                    float sceneTime = Time.time - TaskTipSceneManager.sceneStartTime;

                    // 获取提示面板状态
                    string taskHintState = "off";
                    if (TaskHintUIController.Instance != null)
                    {
                        var hintPanel = TaskHintUIController.Instance.GetHintPanel();
                        if (hintPanel != null && hintPanel.activeSelf)
                        {
                            taskHintState = "on";
                        }
                    }

                    // 写入CSV数据
                    try
                    {
                        hasDataBeenWritten = true;
                        dataWriter.WriteLine($"{unixTimestamp}," +
                                     $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}," +
                                     $"{recordingTime:F6}," +
                                     $"{sceneTime:F6}," +
                                     $"{(string.IsNullOrEmpty(currentSceneID) ? "Unknown" : currentSceneID)}," +
                                     $"{currentNodeID}," +
                                     $"{(int)gazeIndex}," +
                                     $"{gazeOriginLocal.x:F6},{gazeOriginLocal.y:F6},{gazeOriginLocal.z:F6}," +
                                     $"{gazeDirectionLocal.x:F6},{gazeDirectionLocal.y:F6},{gazeDirectionLocal.z:F6}," +
                                     $"{gazeOriginWorld.x:F6},{gazeOriginWorld.y:F6},{gazeOriginWorld.z:F6}," +
                                     $"{gazeDirectionWorld.x:F6},{gazeDirectionWorld.y:F6},{gazeDirectionWorld.z:F6}," +
                                     $"{hitPoint.x:F6},{hitPoint.y:F6},{hitPoint.z:F6}," +
                                     $"{hitDistance:F6}," +
                                     $"{hitObjectName}," +
                                     $"{hitObjectTag}," +
                                     $"{leftPupilDiameter:F6},{rightPupilDiameter:F6}," +
                                     $"{leftEyeOpenness:F6},{rightEyeOpenness:F6}," +
                                     $"{hmdPosition.x:F6},{hmdPosition.y:F6},{hmdPosition.z:F6}," +
                                     $"{hmdRotationEuler.x:F6},{hmdRotationEuler.y:F6},{hmdRotationEuler.z:F6}," +
                                     $"{cameraFOV_V:F6}," +
                                     $"{cameraFOV_H:F6}," +
                                     $"{mainCamera.nearClipPlane:F6}," +
                                     $"{mainCamera.farClipPlane:F6}," +
                                     $"{aspectRatio:F6}," +
                                     $"{mainCamera.pixelWidth}," +
                                     $"{mainCamera.pixelHeight}," +
                                     $"{mainCamera.focalLength:F6}," +
                                     $"{taskHintState}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"写入数据失败: {e.Message}");
                    }
                }

                private float CalculateFocalLength(Camera camera)
                {
                    // 传感器高度，单位mm，可根据实际设备校准
                    float sensorHeight = 24f;
                    // 视场角，弧度
                    float fovRadians = camera.fieldOfView * Mathf.Deg2Rad;
                    // 焦距计算公式: f = sensorHeight / (2 * tan(FOV/2))
                    return sensorHeight / (2 * Mathf.Tan(fovRadians / 2));
                }

                public void StopRecording()
                {
                    isRecording = false;
                    Debug.Log("停止记录眼动数据");

                    // 安全关闭文件
                    if (dataWriter != null)
                    {
                        dataWriter.Flush();
                        dataWriter.Close();
                        dataWriter.Dispose();
                        dataWriter = null;

                        if (hasDataBeenWritten)
                        {
                            Debug.Log($"眼动数据已保存: {savePath}");
                        }
                    }
                }

                // 新增：处理放弃操作的公开方法
                public void HandleAbandon()
                {
                    // 停止记录
                    StopRecording();

                    // 如果有数据文件但未写入数据，则删除该文件
                    if (!hasDataBeenWritten && !string.IsNullOrEmpty(savePath) && File.Exists(savePath))
                    {
                        try
                        {
                            File.Delete(savePath);
                            Debug.Log("放弃操作，删除空数据文件");
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"删除空数据文件失败: {e.Message}");
                        }
                    }
                }

                private void OnDestroy()
                {
                    // 注销回调
                    if (eyeCallbackRegistered)
                    {
                        SRanipal_Eye_v2.WrapperUnRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye_v2.CallbackBasic)EyeCallback));
                        eyeCallbackRegistered = false;
                    }

                    // 关闭文件
                    if (dataWriter != null)
                    {
                        dataWriter.Flush();
                        dataWriter.Close();
                        dataWriter.Dispose();
                    }
                }

                private static void EyeCallback(ref EyeData_v2 eye_data)
                {
                    eyeData = eye_data;
                }
            }
        }
    }
}