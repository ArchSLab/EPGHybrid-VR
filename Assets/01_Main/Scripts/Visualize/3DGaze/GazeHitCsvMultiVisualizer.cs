using UnityEngine;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

/// <summary>
/// 多被试视线射线与碰撞点可视化。
///
/// 使用方式：
/// 1. 将本脚本挂载到一个新的空物体上；
/// 2. 读取 TaskX/Eye 文件夹中的多个 CSV；
/// 3. 在运行时面板中选择被试和 NodeID；
/// 4. 点击 Generate / Refresh Visualization。
///
/// 每个 CSV 会作为一个独立被试处理，并单独划分连续 NodeID 停留段，
/// 因此不同文件之间不会发生错误拼接。
/// </summary>
public class GazeHitCsvMultiVisualizer : MonoBehaviour
{
    [Serializable]
    public class TaskEyeSelection
    {
        [Tooltip("对应 ProcessedRoot/TaskX/Eye 中的 TaskX。")]
        public int taskOrder = 1;

        [Tooltip("勾选后载入该任务 Eye 文件夹中的全部 CSV。")]
        public bool loadAllCsv = true;

        [Tooltip("未勾选 Load All 时使用。支持 1,3,5-8；index 从 1 开始，按文件名排序。")]
        public string csvIndices = "1";
    }

    public enum ParticipantSamplingMode
    {
        RawSamples = 0,
        EqualParticipantQuota = 1
    }

    public enum RayColorMode
    {
        FixedColor = 0,
        ByParticipant = 1,
        ByPupilDiameter = 2
    }

    public enum HitPointColorMode
    {
        FixedColor = 0,
        ByParticipant = 1,
        MatchRayColor = 2
    }

    // =========================
    // 1. 数据路径
    // =========================

    [Header("Data Path Settings")]
    public string processedRoot;

    public string eyeSubFolderName = "Eye";

    [Tooltip("可添加多条任务。每条任务可载入全部 CSV，或指定 CSV index。")]
    public List<TaskEyeSelection> taskSelections =
        new List<TaskEyeSelection>();

    // =========================
    // 2. 可视化设置
    // =========================

    [Header("Visualization Settings")]
    public bool showRays = true;
    public bool showHitPoints = true;

    [Tooltip("是否显示没有发生碰撞的有效视线射线。")]
    public bool showNoHitRays = true;

    [Tooltip("没有命中时射线的固定显示长度。")]
    public float noHitRayLength = 10f;

    [Tooltip("碰撞点球体大小。")]
    public float hitPointSize = 0.08f;

    [Tooltip("射线宽度。")]
    public float rayWidth = 0.01f;

    [Tooltip("在每个节点停留段内每隔 N 条有效记录取一条。")]
    public int sampleStep = 1;

    [Tooltip("最终最多显示多少条记录。0 表示不限制，但多被试数据不建议设为 0。")]
    public int maxVisualizedRecords = 5000;

    [Tooltip("EqualParticipantQuota 模式下，每名被试最多保留多少条记录。0 表示只受总上限控制。")]
    public int maxRecordsPerParticipant = 300;

    public ParticipantSamplingMode samplingMode =
        ParticipantSamplingMode.EqualParticipantQuota;

    public RayColorMode rayColorMode = RayColorMode.ByParticipant;
    public HitPointColorMode hitPointColorMode = HitPointColorMode.ByParticipant;

    public Color fixedRayColor = new Color(0f, 1f, 0f, 0.35f);
    public Color fixedHitPointColor = new Color(1f, 0.1f, 0f, 0.85f);

    [Tooltip("按被试着色时，射线使用该透明度。")]
    [Range(0.01f, 1f)]
    public float participantRayAlpha = 0.32f;

    [Tooltip("按被试着色时，碰撞点使用该透明度。")]
    [Range(0.01f, 1f)]
    public float participantPointAlpha = 0.85f;

    [Header("Pupil-based Ray Color")]
    public float pupilMin = 2.5f;
    public float pupilMid = 4.0f;
    public float pupilMax = 6.0f;
    public Color pupilSmallColor = Color.cyan;
    public Color pupilMiddleColor = Color.yellow;
    public Color pupilLargeColor = Color.red;

    // =========================
    // 3. 面板设置
    // =========================

    [Header("Panel Settings")]
    public bool showControlPanel = true;
    public Rect panelRect = new Rect(20f, 20f, 520f, 820f);

    public float minPanelWidth = 390f;
    public float minPanelHeight = 280f;
    public float resizeBorderSize = 8f;

    [Header("Panel List Heights")]
    public float participantListHeight = 180f;
    public float nodeListHeight = 280f;

    private bool panelMinimized = false;
    private bool panelClosed = false;
    private bool isResizingPanel = false;
    private PanelResizeMode resizeMode = PanelResizeMode.None;

    private Vector2 panelScrollPosition = Vector2.zero;
    private Vector2 participantScrollPosition = Vector2.zero;
    private Vector2 nodeScrollPosition = Vector2.zero;

    private string participantFilter = "";
    private string nodeFilter = "";



    private enum PanelResizeMode
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    // =========================
    // 4. 内部数据
    // =========================

    private readonly List<ParticipantDataset> participantDatasets =
        new List<ParticipantDataset>();

    private readonly List<NodeGroup> nodeGroups =
        new List<NodeGroup>();

    private GameObject visualizationRoot;
    private Material rayMaterial;
    private Material pointMaterial;
    private MaterialPropertyBlock pointPropertyBlock;

    private int totalLoadedRecords = 0;
    private int totalLoadedNodeStays = 0;
    private string statusMessage = "No data loaded.";
    private string lastSelectionWarnings = "";

    // =========================
    // 5. Unity 生命周期与 GUI
    // =========================

    private void Awake()
    {
        processedRoot = ProjectPathConfig.GazeHitDataRoot;
        EnsureTaskSelections();
    }

    private void Reset()
    {
        EnsureTaskSelections();
    }

    private void OnValidate()
    {
        EnsureTaskSelections();
    }

    private void EnsureTaskSelections()
    {
        if (taskSelections == null)
        {
            taskSelections = new List<TaskEyeSelection>();
        }

        if (taskSelections.Count == 0)
        {
            taskSelections.Add(new TaskEyeSelection());
        }
    }

    private void OnGUI()
    {
        if (!showControlPanel || panelClosed)
        {
            return;
        }

        EnsureTaskSelections();
        panelRect = GUI.Window(20260614, panelRect, DrawPanel, "");
    }

    private void DrawPanel(int windowID)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(28));

        GUILayout.Label("Multi-Participant Gaze Visualizer", GUILayout.Width(265));
        GUILayout.FlexibleSpace();

        if (GUILayout.Button(panelMinimized ? "Expand" : "Minimize", GUILayout.Width(78)))
        {
            panelMinimized = !panelMinimized;
        }

        if (GUILayout.Button("Close", GUILayout.Width(52)))
        {
            panelClosed = true;
            return;
        }

        GUILayout.EndHorizontal();

        if (panelMinimized)
        {
            HandlePanelResize();
            GUI.DragWindow(new Rect(0, 0, panelRect.width, 28));
            return;
        }

        panelScrollPosition = GUILayout.BeginScrollView(
            panelScrollPosition,
            false,
            true,
            GUILayout.Width(Mathf.Max(100f, panelRect.width - 12f)),
            GUILayout.Height(Mathf.Max(100f, panelRect.height - 38f))
        );

        DrawDataSourceSection();
        DrawVisualizationSettingsSection();
        DrawParticipantSelectionSection();
        DrawNodeSelectionSection();
        DrawGenerateSection();
        DrawStatusSection();

        GUILayout.EndScrollView();

        HandlePanelResize();

        // 仅标题栏用于拖动，避免与滚动条、按钮和滑块冲突。
        GUI.DragWindow(new Rect(0, 0, panelRect.width - 20f, 28));
    }

    private void DrawDataSourceSection()
    {
        GUILayout.Label("Data Source", GUI.skin.box);

        GUILayout.Label("Processed Root:");
        processedRoot = GUILayout.TextField(processedRoot);

        GUILayout.Label("Eye Subfolder:");
        eyeSubFolderName = GUILayout.TextField(eyeSubFolderName);

        GUILayout.Label(
            "CSV index starts from 1 after sorting by filename; " +
            "expressions such as 1,3,5-8 are supported."
        );

        int removeIndex = -1;

        for (int i = 0; i < taskSelections.Count; i++)
        {
            TaskEyeSelection selection = taskSelections[i];

            if (selection == null)
            {
                selection = new TaskEyeSelection();
                taskSelections[i] = selection;
            }

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Task Selection " + (i + 1), GUILayout.Width(180));

            if (GUILayout.Button("Remove", GUILayout.Width(80)))
            {
                removeIndex = i;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Task Order:", GUILayout.Width(120));
            selection.taskOrder = DrawCompactIntField(
                selection.taskOrder,
                1,
                999999,
                80f
            );
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            selection.loadAllCsv = GUILayout.Toggle(
                selection.loadAllCsv,
                "Load All CSV Data"
            );

            if (!selection.loadAllCsv)
            {
                GUILayout.Label("CSV Indices:");
                selection.csvIndices = GUILayout.TextField(
                    selection.csvIndices ?? ""
                );
            }

            GUILayout.EndVertical();
        }

        if (removeIndex >= 0 && taskSelections.Count > 1)
        {
            taskSelections.RemoveAt(removeIndex);
        }

        if (GUILayout.Button("+ Add Task Selection", GUILayout.Height(26)))
        {
            taskSelections.Add(new TaskEyeSelection());
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Load Participant Data", GUILayout.Height(30)))
        {
            LoadParticipantData();
        }

        if (GUILayout.Button("Clear Loaded Data", GUILayout.Height(30)))
        {
            ClearLoadedData();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
    }

    private void DrawVisualizationSettingsSection()
    {
        GUILayout.Label("Visualization Settings", GUI.skin.box);

        showRays = GUILayout.Toggle(showRays, "Show Rays");
        showHitPoints = GUILayout.Toggle(showHitPoints, "Show Hit Points");

        if (showRays)
        {
            showNoHitRays = GUILayout.Toggle(
                showNoHitRays,
                "Show No-Hit Rays"
            );
        }

        noHitRayLength = FloatField(
            "NoHit Ray Length",
            noHitRayLength,
            0.01f,
            1000f
        );

        hitPointSize = FloatField(
            "Point Size",
            hitPointSize,
            0.001f,
            10f
        );

        rayWidth = FloatField(
            "Ray Width",
            rayWidth,
            0.0001f,
            10f
        );

        sampleStep = IntField("Sample Step", sampleStep, 1, 100000);
        maxVisualizedRecords = IntField(
            "Max Total Records",
            maxVisualizedRecords,
            0,
            1000000
        );

        GUILayout.Label("Sampling Mode:");
        samplingMode = (ParticipantSamplingMode)GUILayout.SelectionGrid(
            (int)samplingMode,
            new string[]
            {
                "Raw Samples: keep participant sample proportions",
                "Equal Participant Quota: balanced participant contribution"
            },
            1
        );

        if (samplingMode == ParticipantSamplingMode.EqualParticipantQuota)
        {
            maxRecordsPerParticipant = IntField(
                "Max Per Participant",
                maxRecordsPerParticipant,
                0,
                1000000
            );
        }

        GUILayout.Label("Ray Color Mode:");
        rayColorMode = (RayColorMode)GUILayout.SelectionGrid(
            (int)rayColorMode,
            new string[]
            {
                "Fixed Color",
                "Color by Participant",
                "Color by Pupil Diameter"
            },
            1
        );

        GUILayout.Label("Hit Point Color Mode:");
        hitPointColorMode = (HitPointColorMode)GUILayout.SelectionGrid(
            (int)hitPointColorMode,
            new string[]
            {
                "Fixed Color",
                "Color by Participant",
                "Match Ray Color"
            },
            1
        );

        if (rayColorMode == RayColorMode.ByPupilDiameter)
        {
            pupilMin = FloatField("Pupil Min", pupilMin, 0f, 20f);
            pupilMid = FloatField("Pupil Mid", pupilMid, 0f, 20f);
            pupilMax = FloatField("Pupil Max", pupilMax, 0f, 20f);

            if (pupilMid < pupilMin)
            {
                pupilMid = pupilMin;
            }

            if (pupilMax < pupilMid)
            {
                pupilMax = pupilMid;
            }
        }

        GUILayout.Space(8);
    }

    private void DrawParticipantSelectionSection()
    {
        GUILayout.Label("Participants", GUI.skin.box);

        if (participantDatasets.Count == 0)
        {
            GUILayout.Label("No participant data loaded.");
            GUILayout.Space(8);
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All"))
        {
            SetAllParticipantSelection(true);
        }

        if (GUILayout.Button("Unselect All"))
        {
            SetAllParticipantSelection(false);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Filter:", GUILayout.Width(48));
        participantFilter = GUILayout.TextField(participantFilter);
        GUILayout.EndHorizontal();

        GUILayout.Label(
            "Loaded: " + participantDatasets.Count +
            " | Selected: " + CountSelectedParticipants()
        );

        participantListHeight = DrawListHeightSlider(
            "Participant List Height",
            participantListHeight,
            100f,
            600f
        );

        participantScrollPosition = GUILayout.BeginScrollView(
            participantScrollPosition,
            false,
            true,
            GUILayout.Height(participantListHeight)
        );

        for (int i = 0; i < participantDatasets.Count; i++)
        {
            ParticipantDataset dataset = participantDatasets[i];

            if (!MatchesFilter(dataset.participantID, participantFilter) &&
                !MatchesFilter(dataset.sourceFileName, participantFilter) &&
                !MatchesFilter(
                    "Task" + dataset.taskOrder,
                    participantFilter
                ))
            {
                continue;
            }

            GUILayout.BeginHorizontal();

            dataset.selected = GUILayout.Toggle(
                dataset.selected,
                "",
                GUILayout.Width(22)
            );

            DrawColorSwatch(dataset.participantColor, 18f);

            string label =
                "Task" + dataset.taskOrder +
                " | Index=" + dataset.csvIndex +
                " | " + dataset.participantID +
                " | Records=" + dataset.records.Count +
                " | Stays=" + dataset.nodeStays.Count;

            GUILayout.Label(label);
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        GUILayout.Space(8);
    }

    private void DrawNodeSelectionSection()
    {
        GUILayout.Label("Node IDs", GUI.skin.box);

        if (nodeGroups.Count == 0)
        {
            GUILayout.Label("No NodeID groups available.");
            GUILayout.Space(8);
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All Nodes"))
        {
            SetAllNodeGroupSelection(true);
        }

        if (GUILayout.Button("Unselect All Nodes"))
        {
            SetAllNodeGroupSelection(false);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Filter:", GUILayout.Width(48));
        nodeFilter = GUILayout.TextField(nodeFilter);
        GUILayout.EndHorizontal();

        GUILayout.Label(
            "Unique NodeIDs: " + nodeGroups.Count +
            " | Selected: " + CountSelectedNodeGroups()
        );

        nodeListHeight = DrawListHeightSlider(
            "Node List Height",
            nodeListHeight,
            120f,
            700f
        );

        nodeScrollPosition = GUILayout.BeginScrollView(
            nodeScrollPosition,
            false,
            true,
            GUILayout.Height(nodeListHeight)
        );

        for (int i = 0; i < nodeGroups.Count; i++)
        {
            NodeGroup group = nodeGroups[i];

            if (!MatchesFilter(group.nodeID, nodeFilter))
            {
                continue;
            }

            string label =
                "NodeID=" + group.nodeID +
                " | Participants=" + group.participantCount +
                " | Stays=" + group.stayCount +
                " | Records=" + group.recordCount;

            group.selected = GUILayout.Toggle(group.selected, label);
        }

        GUILayout.EndScrollView();
        GUILayout.Space(8);
    }

    private void DrawGenerateSection()
    {
        GUILayout.Label("Generate", GUI.skin.box);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate / Refresh Visualization", GUILayout.Height(34)))
        {
            RefreshVisualization();
        }

        if (GUILayout.Button("Clear Visualization", GUILayout.Height(34)))
        {
            ClearVisualization();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
    }

    private void DrawStatusSection()
    {
        GUILayout.Label("Status", GUI.skin.box);
        GUILayout.TextArea(statusMessage, GUILayout.Height(105));
    }

    private void DrawColorSwatch(Color color, float size)
    {
        Rect rect = GUILayoutUtility.GetRect(
            size,
            size,
            GUILayout.Width(size),
            GUILayout.Height(size)
        );

        Color oldColor = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, 1f);
        GUI.Box(rect, "");
        GUI.color = oldColor;
    }

    private float DrawListHeightSlider(
        string label,
        float value,
        float minimum,
        float maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(165));
        value = GUILayout.HorizontalSlider(value, minimum, maximum);
        GUILayout.Label(Mathf.RoundToInt(value).ToString(), GUILayout.Width(42));
        GUILayout.EndHorizontal();
        return value;
    }

    private int DrawCompactIntField(
        int value,
        int minimum,
        int maximum,
        float width)
    {
        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(width));
        int parsed;

        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Clamp(parsed, minimum, maximum);
        }

        return value;
    }

    private float FloatField(
        string label,
        float value,
        float minimum,
        float maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(155));

        string text = GUILayout.TextField(
            value.ToString("G5", CultureInfo.InvariantCulture),
            GUILayout.Width(90)
        );

        float parsed;
        if (float.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out parsed))
        {
            value = Mathf.Clamp(parsed, minimum, maximum);
        }

        GUILayout.EndHorizontal();
        return value;
    }

    private int IntField(
        string label,
        int value,
        int minimum,
        int maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(155));

        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(90));
        int parsed;

        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Clamp(parsed, minimum, maximum);
        }

        GUILayout.EndHorizontal();
        return value;
    }

    // =========================
    // 6. 面板移动与缩放
    // =========================

    private void HandlePanelResize()
    {
        Event currentEvent = Event.current;
        PanelResizeMode hitMode = GetPanelResizeMode(currentEvent.mousePosition);

        if (currentEvent.type == EventType.MouseDown &&
            hitMode != PanelResizeMode.None)
        {
            resizeMode = hitMode;
            isResizingPanel = true;
            currentEvent.Use();
        }

        if (isResizingPanel && currentEvent.type == EventType.MouseDrag)
        {
            Vector2 delta = currentEvent.delta;

            float x = panelRect.x;
            float y = panelRect.y;
            float width = panelRect.width;
            float height = panelRect.height;

            bool resizeLeft =
                resizeMode == PanelResizeMode.Left ||
                resizeMode == PanelResizeMode.TopLeft ||
                resizeMode == PanelResizeMode.BottomLeft;

            bool resizeRight =
                resizeMode == PanelResizeMode.Right ||
                resizeMode == PanelResizeMode.TopRight ||
                resizeMode == PanelResizeMode.BottomRight;

            bool resizeTop =
                resizeMode == PanelResizeMode.Top ||
                resizeMode == PanelResizeMode.TopLeft ||
                resizeMode == PanelResizeMode.TopRight;

            bool resizeBottom =
                resizeMode == PanelResizeMode.Bottom ||
                resizeMode == PanelResizeMode.BottomLeft ||
                resizeMode == PanelResizeMode.BottomRight;

            if (resizeLeft)
            {
                float newWidth = Mathf.Max(minPanelWidth, width - delta.x);
                x += width - newWidth;
                width = newWidth;
            }

            if (resizeRight)
            {
                width = Mathf.Max(minPanelWidth, width + delta.x);
            }

            if (resizeTop)
            {
                float newHeight = Mathf.Max(minPanelHeight, height - delta.y);
                y += height - newHeight;
                height = newHeight;
            }

            if (resizeBottom)
            {
                height = Mathf.Max(minPanelHeight, height + delta.y);
            }

            panelRect.x = x;
            panelRect.y = y;
            panelRect.width = width;
            panelRect.height = height;

            currentEvent.Use();
        }

        if (currentEvent.type == EventType.MouseUp)
        {
            isResizingPanel = false;
            resizeMode = PanelResizeMode.None;
        }

        DrawResizeBorderHints();
    }

    private PanelResizeMode GetPanelResizeMode(Vector2 mousePosition)
    {
        float border = resizeBorderSize;
        float width = panelRect.width;
        float height = panelRect.height;

        bool left = mousePosition.x >= 0f && mousePosition.x <= border;
        bool right = mousePosition.x >= width - border && mousePosition.x <= width;
        bool top = mousePosition.y >= 0f && mousePosition.y <= border;
        bool bottom = mousePosition.y >= height - border && mousePosition.y <= height;

        if (left && top) return PanelResizeMode.TopLeft;
        if (right && top) return PanelResizeMode.TopRight;
        if (left && bottom) return PanelResizeMode.BottomLeft;
        if (right && bottom) return PanelResizeMode.BottomRight;

        if (left) return PanelResizeMode.Left;
        if (right) return PanelResizeMode.Right;
        if (top) return PanelResizeMode.Top;
        if (bottom) return PanelResizeMode.Bottom;

        return PanelResizeMode.None;
    }

    private void DrawResizeBorderHints()
    {
        Color oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.16f);

        float border = resizeBorderSize;

        GUI.Box(new Rect(0, 0, panelRect.width, border), "");
        GUI.Box(
            new Rect(0, panelRect.height - border, panelRect.width, border),
            ""
        );
        GUI.Box(new Rect(0, 0, border, panelRect.height), "");
        GUI.Box(
            new Rect(panelRect.width - border, 0, border, panelRect.height),
            ""
        );

        GUI.color = oldColor;
    }

    // =========================
    // 7. 批量加载 CSV
    // =========================

    public void LoadParticipantData()
    {
        ClearVisualization();
        participantDatasets.Clear();
        nodeGroups.Clear();
        totalLoadedRecords = 0;
        totalLoadedNodeStays = 0;

        List<CsvFileSelection> selectedFiles = ResolveSelectedEyeFiles();

        if (selectedFiles.Count == 0)
        {
            statusMessage =
                "No CSV files were selected or found.";

            if (!string.IsNullOrEmpty(lastSelectionWarnings))
            {
                statusMessage += "\n" + lastSelectionWarnings;
            }

            Debug.LogError(statusMessage);
            return;
        }

        int failedCount = 0;
        StringBuilder failureDetails = new StringBuilder();

        for (int i = 0; i < selectedFiles.Count; i++)
        {
            CsvFileSelection fileSelection = selectedFiles[i];
            ParticipantDataset dataset;
            string errorMessage;

            if (TryLoadParticipantCsv(
                fileSelection,
                i,
                out dataset,
                out errorMessage))
            {
                dataset.participantColor = GenerateParticipantColor(
                    participantDatasets.Count
                );

                participantDatasets.Add(dataset);
                totalLoadedRecords += dataset.records.Count;
                totalLoadedNodeStays += dataset.nodeStays.Count;
            }
            else
            {
                failedCount++;
                failureDetails.AppendLine(
                    "Task" + fileSelection.taskOrder +
                    " Index" + fileSelection.csvIndex +
                    " " + Path.GetFileName(fileSelection.fullPath) +
                    ": " + errorMessage
                );

                Debug.LogWarning(
                    "Failed to load participant CSV: " +
                    fileSelection.fullPath +
                    "\n" + errorMessage
                );
            }
        }

        BuildNodeGroups();

        statusMessage =
            "Participant data loaded.\n" +
            "Files requested: " + selectedFiles.Count + "\n" +
            "Participants loaded: " + participantDatasets.Count + "\n" +
            "Failed files: " + failedCount + "\n" +
            "Records: " + totalLoadedRecords + "\n" +
            "Node stays: " + totalLoadedNodeStays + "\n" +
            "Unique NodeIDs: " + nodeGroups.Count;

        if (failedCount > 0)
        {
            statusMessage += "\n\nFailed details:\n" + failureDetails.ToString();
        }

        if (!string.IsNullOrEmpty(lastSelectionWarnings))
        {
            statusMessage +=
                "\n\nSelection warnings:\n" + lastSelectionWarnings;
        }

        Debug.Log(statusMessage);
    }

    private List<CsvFileSelection> ResolveSelectedEyeFiles()
    {
        EnsureTaskSelections();

        List<CsvFileSelection> result = new List<CsvFileSelection>();
        HashSet<string> addedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StringBuilder warningBuilder = new StringBuilder();

        for (int i = 0; i < taskSelections.Count; i++)
        {
            TaskEyeSelection selection = taskSelections[i];

            if (selection == null)
            {
                warningBuilder.AppendLine(
                    "Task Selection " + (i + 1) + " is empty."
                );
                continue;
            }

            int selectedTaskOrder = Mathf.Max(1, selection.taskOrder);
            string eyeFolder = Path.Combine(
                Path.Combine(
                    processedRoot == null ? "" : processedRoot.Trim().Trim('"'),
                    "Task" + selectedTaskOrder
                ),
                string.IsNullOrWhiteSpace(eyeSubFolderName)
                    ? "Eye"
                    : eyeSubFolderName.Trim().Trim('"')
            );

            if (!Directory.Exists(eyeFolder))
            {
                warningBuilder.AppendLine(
                    "Task" + selectedTaskOrder +
                    " folder not found: " + eyeFolder
                );
                continue;
            }

            string[] files = Directory.GetFiles(
                eyeFolder,
                "*.csv",
                SearchOption.TopDirectoryOnly
            );
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            if (files.Length == 0)
            {
                warningBuilder.AppendLine(
                    "Task" + selectedTaskOrder +
                    " contains no CSV files."
                );
                continue;
            }

            List<int> selectedIndices;

            if (selection.loadAllCsv)
            {
                selectedIndices = new List<int>();

                for (int fileIndex = 1;
                     fileIndex <= files.Length;
                     fileIndex++)
                {
                    selectedIndices.Add(fileIndex);
                }
            }
            else
            {
                selectedIndices = ParseIndexExpression(
                    selection.csvIndices,
                    files.Length
                );

                if (selectedIndices.Count == 0)
                {
                    warningBuilder.AppendLine(
                        "Task" + selectedTaskOrder +
                        " has no valid CSV indices: " +
                        selection.csvIndices
                    );
                    continue;
                }
            }

            for (int index = 0; index < selectedIndices.Count; index++)
            {
                int oneBasedIndex = selectedIndices[index];
                string fullPath = files[oneBasedIndex - 1];

                if (!addedPaths.Add(fullPath))
                {
                    continue;
                }

                CsvFileSelection item = new CsvFileSelection();
                item.taskOrder = selectedTaskOrder;
                item.csvIndex = oneBasedIndex;
                item.fullPath = fullPath;
                result.Add(item);
            }
        }

        lastSelectionWarnings = warningBuilder.ToString().TrimEnd();
        return result;
    }

    private List<int> ParseIndexExpression(string expression, int maxIndex)
    {
        SortedSet<int> result = new SortedSet<int>();

        if (string.IsNullOrWhiteSpace(expression) || maxIndex <= 0)
        {
            return new List<int>();
        }

        string normalized = expression
            .Replace('，', ',')
            .Replace('；', ',')
            .Replace(';', ',')
            .Replace(' ', ',');

        string[] tokens = normalized.Split(
            new char[] { ',' },
            StringSplitOptions.RemoveEmptyEntries
        );

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim();

            if (token.Length == 0)
            {
                continue;
            }

            int dashIndex = token.IndexOf('-');

            if (dashIndex > 0 && dashIndex < token.Length - 1)
            {
                int start;
                int end;

                if (int.TryParse(
                        token.Substring(0, dashIndex).Trim(),
                        out start) &&
                    int.TryParse(
                        token.Substring(dashIndex + 1).Trim(),
                        out end))
                {
                    if (start > end)
                    {
                        int temporary = start;
                        start = end;
                        end = temporary;
                    }

                    start = Mathf.Clamp(start, 1, maxIndex);
                    end = Mathf.Clamp(end, 1, maxIndex);

                    for (int index = start; index <= end; index++)
                    {
                        result.Add(index);
                    }
                }
            }
            else
            {
                int index;

                if (int.TryParse(token, out index) &&
                    index >= 1 && index <= maxIndex)
                {
                    result.Add(index);
                }
            }
        }

        return new List<int>(result);
    }

    private bool TryLoadParticipantCsv(
        CsvFileSelection fileSelection,
        int participantIndex,
        out ParticipantDataset dataset,
        out string errorMessage)
    {
        dataset = null;
        errorMessage = "";

        string csvPath = fileSelection == null
            ? ""
            : fileSelection.fullPath;

        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            errorMessage = "CSV file does not exist.";
            return false;
        }

        List<List<string>> table;

        try
        {
            table = ReadCsv(csvPath);
        }
        catch (Exception exception)
        {
            errorMessage = "CSV read error: " + exception.Message;
            return false;
        }

        if (table == null || table.Count < 2)
        {
            errorMessage = "CSV is empty or only contains a header.";
            return false;
        }

        List<string> header = table[0];

        int idxNodeID = FindColumnIndex(header, "NodeID");

        int idxOriginX = FindColumnIndex(header, "GazeOriginWorldX");
        int idxOriginY = FindColumnIndex(header, "GazeOriginWorldY");
        int idxOriginZ = FindColumnIndex(header, "GazeOriginWorldZ");

        int idxDirX = FindColumnIndex(header, "GazeDirectionWorldX");
        int idxDirY = FindColumnIndex(header, "GazeDirectionWorldY");
        int idxDirZ = FindColumnIndex(header, "GazeDirectionWorldZ");

        int idxHitX = FindColumnIndex(header, "HitPointX");
        int idxHitY = FindColumnIndex(header, "HitPointY");
        int idxHitZ = FindColumnIndex(header, "HitPointZ");
        int idxHitDistance = FindColumnIndex(header, "HitDistance");

        int idxHitObjectName = FindColumnIndex(header, "HitObjectName");
        int idxHitObjectTag = FindColumnIndex(header, "HitObjectTag");

        int idxGazeValid = FindColumnIndex(header, "GazeValid");
        int idxGazeHit = FindColumnIndex(header, "GazeHit");

        int idxSceneTime = FindColumnIndex(header, "SceneTime");
        int idxTimestamp = FindColumnIndex(header, "Timestamp");

        int idxLeftPupil = FindColumnIndex(header, "LeftPupilDiameter");
        int idxRightPupil = FindColumnIndex(header, "RightPupilDiameter");

        if (idxNodeID < 0 ||
            idxOriginX < 0 || idxOriginY < 0 || idxOriginZ < 0 ||
            idxDirX < 0 || idxDirY < 0 || idxDirZ < 0 ||
            idxHitX < 0 || idxHitY < 0 || idxHitZ < 0 ||
            idxHitDistance < 0)
        {
            errorMessage =
                "Missing required columns. Required: NodeID, " +
                "GazeOriginWorldX/Y/Z, GazeDirectionWorldX/Y/Z, " +
                "HitPointX/Y/Z, HitDistance.";

            return false;
        }

        dataset = new ParticipantDataset();
        dataset.participantIndex = participantIndex;
        dataset.taskOrder = fileSelection.taskOrder;
        dataset.csvIndex = fileSelection.csvIndex;
        dataset.csvPath = csvPath;
        dataset.sourceFileName = Path.GetFileName(csvPath);
        dataset.participantID = DeriveParticipantID(csvPath);
        dataset.selected = true;

        for (int rowIndex = 1; rowIndex < table.Count; rowIndex++)
        {
            List<string> row = table[rowIndex];

            GazeRecord record = new GazeRecord();
            record.participantIndex = participantIndex;
            record.participantID = dataset.participantID;
            record.sourceFileName = dataset.sourceFileName;
            record.sourceRowIndex = rowIndex;

            record.nodeID = SafeGet(row, idxNodeID).Trim();
            record.sceneTime = idxSceneTime >= 0
                ? SafeGet(row, idxSceneTime)
                : "";
            record.timestamp = idxTimestamp >= 0
                ? SafeGet(row, idxTimestamp)
                : "";

            record.origin = ReadVector3(
                row,
                idxOriginX,
                idxOriginY,
                idxOriginZ
            );

            record.direction = ReadVector3(
                row,
                idxDirX,
                idxDirY,
                idxDirZ
            );

            record.hitPoint = ReadVector3(
                row,
                idxHitX,
                idxHitY,
                idxHitZ
            );

            float hitDistance;
            if (TryParseFloat(SafeGet(row, idxHitDistance), out hitDistance))
            {
                record.hitDistance = hitDistance;
            }
            else
            {
                record.hitDistance = 0f;
            }

            record.hitObjectName = idxHitObjectName >= 0
                ? SafeGet(row, idxHitObjectName)
                : "";

            record.hitObjectTag = idxHitObjectTag >= 0
                ? SafeGet(row, idxHitObjectTag)
                : "";

            ReadPupilDiameter(
                row,
                idxLeftPupil,
                idxRightPupil,
                record
            );

            record.gazeValid = DetermineGazeValid(
                row,
                idxGazeValid,
                record
            );

            record.gazeHit = DetermineGazeHit(
                row,
                idxGazeHit,
                record
            );

            dataset.records.Add(record);
        }

        BuildNodeStaySegments(dataset);
        return true;
    }

    private string DeriveParticipantID(string csvPath)
    {
        return Path.GetFileNameWithoutExtension(csvPath);
    }

    private Color GenerateParticipantColor(int index)
    {
        // 黄金比例步进可以让相邻被试颜色尽量分散。
        float hue = Mathf.Repeat(index * 0.61803398875f, 1f);
        Color color = Color.HSVToRGB(hue, 0.72f, 1f);
        color.a = 1f;
        return color;
    }

    // =========================
    // 8. 节点停留段与 NodeID 汇总
    // =========================

    private void BuildNodeStaySegments(ParticipantDataset dataset)
    {
        dataset.nodeStays.Clear();

        if (dataset.records.Count == 0)
        {
            return;
        }

        int startIndex = 0;
        string currentNodeID = dataset.records[0].nodeID;

        for (int i = 1; i < dataset.records.Count; i++)
        {
            string nodeID = dataset.records[i].nodeID;

            if (!string.Equals(
                nodeID,
                currentNodeID,
                StringComparison.Ordinal))
            {
                AddNodeStaySegment(
                    dataset,
                    startIndex,
                    i - 1,
                    currentNodeID
                );

                startIndex = i;
                currentNodeID = nodeID;
            }
        }

        AddNodeStaySegment(
            dataset,
            startIndex,
            dataset.records.Count - 1,
            currentNodeID
        );
    }

    private void AddNodeStaySegment(
        ParticipantDataset dataset,
        int startIndex,
        int endIndex,
        string nodeID)
    {
        NodeStaySegment segment = new NodeStaySegment();
        segment.nodeID = nodeID;
        segment.startRecordIndex = startIndex;
        segment.endRecordIndex = endIndex;

        if (startIndex >= 0 && startIndex < dataset.records.Count)
        {
            GazeRecord startRecord = dataset.records[startIndex];
            segment.startSceneTime = !string.IsNullOrEmpty(startRecord.sceneTime)
                ? startRecord.sceneTime
                : startRecord.timestamp;
        }

        if (endIndex >= 0 && endIndex < dataset.records.Count)
        {
            GazeRecord endRecord = dataset.records[endIndex];
            segment.endSceneTime = !string.IsNullOrEmpty(endRecord.sceneTime)
                ? endRecord.sceneTime
                : endRecord.timestamp;
        }

        dataset.nodeStays.Add(segment);
    }

    private void BuildNodeGroups()
    {
        nodeGroups.Clear();

        Dictionary<string, NodeGroupBuilder> builders =
            new Dictionary<string, NodeGroupBuilder>(
                StringComparer.OrdinalIgnoreCase
            );

        for (int participantIndex = 0;
             participantIndex < participantDatasets.Count;
             participantIndex++)
        {
            ParticipantDataset dataset = participantDatasets[participantIndex];

            for (int stayIndex = 0;
                 stayIndex < dataset.nodeStays.Count;
                 stayIndex++)
            {
                NodeStaySegment stay = dataset.nodeStays[stayIndex];
                string nodeID = stay.nodeID == null ? "" : stay.nodeID.Trim();

                NodeGroupBuilder builder;
                if (!builders.TryGetValue(nodeID, out builder))
                {
                    builder = new NodeGroupBuilder();
                    builder.nodeID = nodeID;
                    builders.Add(nodeID, builder);
                }

                builder.participantIndices.Add(participantIndex);
                builder.stayCount++;
                builder.recordCount += stay.RecordCount;
            }
        }

        foreach (KeyValuePair<string, NodeGroupBuilder> pair in builders)
        {
            NodeGroupBuilder builder = pair.Value;

            NodeGroup group = new NodeGroup();
            group.nodeID = builder.nodeID;
            group.participantCount = builder.participantIndices.Count;
            group.stayCount = builder.stayCount;
            group.recordCount = builder.recordCount;
            group.selected = true;

            nodeGroups.Add(group);
        }

        nodeGroups.Sort(CompareNodeGroups);
    }

    private int CompareNodeGroups(NodeGroup a, NodeGroup b)
    {
        int aNumber;
        int bNumber;

        bool aHasNumber = TryExtractInteger(a.nodeID, out aNumber);
        bool bHasNumber = TryExtractInteger(b.nodeID, out bNumber);

        if (aHasNumber && bHasNumber && aNumber != bNumber)
        {
            return aNumber.CompareTo(bNumber);
        }

        if (aHasNumber != bHasNumber)
        {
            return aHasNumber ? -1 : 1;
        }

        return string.Compare(
            a.nodeID,
            b.nodeID,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private bool TryExtractInteger(string text, out int value)
    {
        value = 0;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        StringBuilder digits = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];

            if (char.IsDigit(character))
            {
                digits.Append(character);
            }
        }

        if (digits.Length == 0)
        {
            return false;
        }

        return int.TryParse(digits.ToString(), out value);
    }

    // =========================
    // 9. 生成多被试射线与碰撞点
    // =========================

    public void RefreshVisualization()
    {
        ClearVisualization();

        if (participantDatasets.Count == 0)
        {
            statusMessage = "No participant data loaded.";
            Debug.LogWarning(statusMessage);
            return;
        }

        if (!showRays && !showHitPoints)
        {
            statusMessage = "Both Show Rays and Show Hit Points are disabled.";
            Debug.LogWarning(statusMessage);
            return;
        }

        HashSet<string> selectedNodeIDs = GetSelectedNodeIDs();

        if (selectedNodeIDs.Count == 0)
        {
            statusMessage = "No NodeID is selected.";
            Debug.LogWarning(statusMessage);
            return;
        }

        List<ParticipantCandidateSet> candidateSets =
            CollectCandidateRecords(selectedNodeIDs);

        if (candidateSets.Count == 0)
        {
            statusMessage =
                "No valid records match the current participant and NodeID selection.";
            Debug.LogWarning(statusMessage);
            return;
        }

        int totalCandidateCount = CountCandidateRecords(candidateSets);
        int[] quotas = CalculateParticipantQuotas(candidateSets);

        CreateMaterials();

        visualizationRoot = new GameObject(
            "GazeHit_MultiVisualization_SelectedTasks"
        );
        visualizationRoot.transform.SetParent(transform, false);

        int usedRecordCount = 0;
        int createdRayCount = 0;
        int createdPointCount = 0;
        int visualizedParticipantCount = 0;

        for (int setIndex = 0; setIndex < candidateSets.Count; setIndex++)
        {
            ParticipantCandidateSet candidateSet = candidateSets[setIndex];
            int quota = quotas[setIndex];

            if (quota <= 0)
            {
                continue;
            }

            List<GazeRecord> selectedRecords = SelectEvenlyDistributedRecords(
                candidateSet.records,
                quota
            );

            if (selectedRecords.Count == 0)
            {
                continue;
            }

            visualizedParticipantCount++;

            GameObject participantRoot = new GameObject(
                "Participant_" +
                (visualizedParticipantCount).ToString("D2") + "_" +
                "Task" + candidateSet.dataset.taskOrder + "_" +
                "Index" + candidateSet.dataset.csvIndex + "_" +
                SanitizeObjectName(candidateSet.dataset.participantID)
            );
            participantRoot.transform.SetParent(visualizationRoot.transform, false);

            Dictionary<string, Transform> nodeRoots =
                new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

            for (int recordIndex = 0;
                 recordIndex < selectedRecords.Count;
                 recordIndex++)
            {
                GazeRecord record = selectedRecords[recordIndex];

                Transform nodeRoot = GetOrCreateNodeRoot(
                    participantRoot.transform,
                    nodeRoots,
                    record.nodeID
                );

                Vector3 rayEnd;
                bool hasDrawableRay = TryGetRayEnd(record, out rayEnd);
                Color rayColor = GetRayColor(record, candidateSet.dataset);

                if (showRays && hasDrawableRay)
                {
                    CreateRayLine(
                        record.origin,
                        rayEnd,
                        nodeRoot,
                        rayColor,
                        record.sourceRowIndex
                    );

                    createdRayCount++;
                }

                if (showHitPoints && record.gazeHit)
                {
                    Color pointColor = GetHitPointColor(
                        record,
                        candidateSet.dataset,
                        rayColor
                    );

                    CreateHitPointSphere(
                        record.hitPoint,
                        nodeRoot,
                        pointColor,
                        record.sourceRowIndex
                    );

                    createdPointCount++;
                }

                usedRecordCount++;
            }
        }

        statusMessage =
            "Visualization refreshed.\n" +
            "Selected participants with candidates: " + candidateSets.Count + "\n" +
            "Participants visualized: " + visualizedParticipantCount + "\n" +
            "Candidate records after Sample Step: " + totalCandidateCount + "\n" +
            "Records used: " + usedRecordCount + "\n" +
            "Rays: " + createdRayCount + "\n" +
            "Hit points: " + createdPointCount;

        if (maxVisualizedRecords == 0 && usedRecordCount > 10000)
        {
            statusMessage +=
                "\nWarning: more than 10,000 records were created as GameObjects.";
        }

        Debug.Log(statusMessage);
    }

    private List<ParticipantCandidateSet> CollectCandidateRecords(
        HashSet<string> selectedNodeIDs)
    {
        List<ParticipantCandidateSet> result =
            new List<ParticipantCandidateSet>();

        int step = Mathf.Max(1, sampleStep);

        for (int participantIndex = 0;
             participantIndex < participantDatasets.Count;
             participantIndex++)
        {
            ParticipantDataset dataset = participantDatasets[participantIndex];

            if (!dataset.selected)
            {
                continue;
            }

            ParticipantCandidateSet candidateSet =
                new ParticipantCandidateSet();
            candidateSet.dataset = dataset;

            for (int stayIndex = 0;
                 stayIndex < dataset.nodeStays.Count;
                 stayIndex++)
            {
                NodeStaySegment stay = dataset.nodeStays[stayIndex];

                if (!selectedNodeIDs.Contains(stay.nodeID ?? ""))
                {
                    continue;
                }

                for (int recordIndex = stay.startRecordIndex;
                     recordIndex <= stay.endRecordIndex;
                     recordIndex += step)
                {
                    if (recordIndex < 0 || recordIndex >= dataset.records.Count)
                    {
                        continue;
                    }

                    GazeRecord record = dataset.records[recordIndex];

                    if (!record.gazeValid)
                    {
                        continue;
                    }

                    bool usefulRay =
                        showRays &&
                        (record.gazeHit || showNoHitRays) &&
                        record.direction.sqrMagnitude > 0.000001f;

                    bool usefulPoint = showHitPoints && record.gazeHit;

                    if (!usefulRay && !usefulPoint)
                    {
                        continue;
                    }

                    candidateSet.records.Add(record);
                }
            }

            if (candidateSet.records.Count > 0)
            {
                result.Add(candidateSet);
            }
        }

        return result;
    }

    private int[] CalculateParticipantQuotas(
        List<ParticipantCandidateSet> candidateSets)
    {
        int count = candidateSets.Count;
        int[] capacities = new int[count];

        for (int i = 0; i < count; i++)
        {
            int capacity = candidateSets[i].records.Count;

            if (samplingMode == ParticipantSamplingMode.EqualParticipantQuota &&
                maxRecordsPerParticipant > 0)
            {
                capacity = Mathf.Min(capacity, maxRecordsPerParticipant);
            }

            capacities[i] = capacity;
        }

        int totalCapacity = Sum(capacities);

        if (maxVisualizedRecords <= 0 || totalCapacity <= maxVisualizedRecords)
        {
            return capacities;
        }

        if (samplingMode == ParticipantSamplingMode.EqualParticipantQuota)
        {
            return AllocateEqualQuotas(
                capacities,
                maxVisualizedRecords
            );
        }

        return AllocateProportionalQuotas(
            capacities,
            maxVisualizedRecords
        );
    }

    private int[] AllocateEqualQuotas(int[] capacities, int totalLimit)
    {
        int count = capacities.Length;
        int[] quotas = new int[count];

        if (count == 0 || totalLimit <= 0)
        {
            return quotas;
        }

        int totalCapacity = Sum(capacities);
        if (totalCapacity <= totalLimit)
        {
            Array.Copy(capacities, quotas, count);
            return quotas;
        }

        int maxCapacity = 0;
        for (int i = 0; i < count; i++)
        {
            maxCapacity = Mathf.Max(maxCapacity, capacities[i]);
        }

        int low = 0;
        int high = maxCapacity;
        int equalLevel = 0;

        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            long requiredCount = 0;

            for (int i = 0; i < count; i++)
            {
                requiredCount += Math.Min(capacities[i], middle);
            }

            if (requiredCount <= totalLimit)
            {
                equalLevel = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        int used = 0;
        List<int> eligible = new List<int>();

        for (int i = 0; i < count; i++)
        {
            quotas[i] = Mathf.Min(capacities[i], equalLevel);
            used += quotas[i];

            if (capacities[i] > quotas[i])
            {
                eligible.Add(i);
            }
        }

        int remaining = totalLimit - used;
        DistributeRemainingEvenly(quotas, capacities, eligible, remaining);

        return quotas;
    }

    private int[] AllocateProportionalQuotas(
        int[] capacities,
        int totalLimit)
    {
        int count = capacities.Length;
        int[] quotas = new int[count];

        if (count == 0 || totalLimit <= 0)
        {
            return quotas;
        }

        int totalCapacity = Sum(capacities);
        if (totalCapacity <= totalLimit)
        {
            Array.Copy(capacities, quotas, count);
            return quotas;
        }

        // 当总上限小于被试数时，仍然尽量在被试列表中均匀分布。
        if (totalLimit < count)
        {
            for (int k = 0; k < totalLimit; k++)
            {
                int index = Mathf.FloorToInt(
                    (k + 0.5f) * count / totalLimit
                );
                index = Mathf.Clamp(index, 0, count - 1);

                if (capacities[index] > 0)
                {
                    quotas[index] = 1;
                }
            }

            return quotas;
        }

        int baseUsed = 0;
        for (int i = 0; i < count; i++)
        {
            if (capacities[i] > 0)
            {
                quotas[i] = 1;
                baseUsed++;
            }
        }

        int remainingLimit = Mathf.Max(0, totalLimit - baseUsed);
        int remainingCapacity = 0;

        for (int i = 0; i < count; i++)
        {
            remainingCapacity += Mathf.Max(0, capacities[i] - quotas[i]);
        }

        List<QuotaRemainder> remainders = new List<QuotaRemainder>();
        int added = 0;

        for (int i = 0; i < count; i++)
        {
            int capacityLeft = Mathf.Max(0, capacities[i] - quotas[i]);

            if (capacityLeft == 0 || remainingCapacity == 0)
            {
                remainders.Add(new QuotaRemainder(i, 0f));
                continue;
            }

            float exact =
                (float)capacityLeft /
                remainingCapacity *
                remainingLimit;

            int floorValue = Mathf.Min(
                capacityLeft,
                Mathf.FloorToInt(exact)
            );

            quotas[i] += floorValue;
            added += floorValue;

            remainders.Add(
                new QuotaRemainder(i, exact - floorValue)
            );
        }

        int leftover = remainingLimit - added;

        remainders.Sort(delegate(QuotaRemainder a, QuotaRemainder b)
        {
            int comparison = b.remainder.CompareTo(a.remainder);
            if (comparison != 0)
            {
                return comparison;
            }

            return a.index.CompareTo(b.index);
        });

        while (leftover > 0)
        {
            bool progressed = false;

            for (int i = 0; i < remainders.Count && leftover > 0; i++)
            {
                int index = remainders[i].index;

                if (quotas[index] >= capacities[index])
                {
                    continue;
                }

                quotas[index]++;
                leftover--;
                progressed = true;
            }

            if (!progressed)
            {
                break;
            }
        }

        return quotas;
    }

    private void DistributeRemainingEvenly(
        int[] quotas,
        int[] capacities,
        List<int> eligible,
        int remaining)
    {
        while (remaining > 0 && eligible.Count > 0)
        {
            int eligibleCount = eligible.Count;
            int additionsThisRound = Mathf.Min(remaining, eligibleCount);

            for (int k = 0; k < additionsThisRound; k++)
            {
                int position = Mathf.FloorToInt(
                    (k + 0.5f) * eligibleCount / additionsThisRound
                );
                position = Mathf.Clamp(position, 0, eligibleCount - 1);

                int index = eligible[position];

                if (quotas[index] < capacities[index])
                {
                    quotas[index]++;
                    remaining--;
                }
            }

            for (int i = eligible.Count - 1; i >= 0; i--)
            {
                int index = eligible[i];

                if (quotas[index] >= capacities[index])
                {
                    eligible.RemoveAt(i);
                }
            }

            if (additionsThisRound == 0)
            {
                break;
            }
        }
    }

    private List<GazeRecord> SelectEvenlyDistributedRecords(
        List<GazeRecord> source,
        int quota)
    {
        List<GazeRecord> selected = new List<GazeRecord>();

        if (source == null || source.Count == 0 || quota <= 0)
        {
            return selected;
        }

        if (quota >= source.Count)
        {
            selected.AddRange(source);
            return selected;
        }

        int previousIndex = -1;

        for (int i = 0; i < quota; i++)
        {
            int index = Mathf.FloorToInt(
                (i + 0.5f) * source.Count / quota
            );
            index = Mathf.Clamp(index, 0, source.Count - 1);

            if (index <= previousIndex)
            {
                index = Mathf.Min(previousIndex + 1, source.Count - 1);
            }

            selected.Add(source[index]);
            previousIndex = index;
        }

        return selected;
    }

    private Transform GetOrCreateNodeRoot(
        Transform participantRoot,
        Dictionary<string, Transform> nodeRoots,
        string nodeID)
    {
        string key = nodeID ?? "";
        Transform nodeRoot;

        if (nodeRoots.TryGetValue(key, out nodeRoot))
        {
            return nodeRoot;
        }

        GameObject nodeObject = new GameObject(
            "NodeID_" + SanitizeObjectName(key)
        );
        nodeObject.transform.SetParent(participantRoot, false);

        nodeRoot = nodeObject.transform;
        nodeRoots.Add(key, nodeRoot);
        return nodeRoot;
    }

    private bool TryGetRayEnd(GazeRecord record, out Vector3 rayEnd)
    {
        rayEnd = record.origin;

        if (record.gazeHit)
        {
            rayEnd = record.hitPoint;
            return IsVectorFinite(rayEnd);
        }

        if (!showNoHitRays)
        {
            return false;
        }

        Vector3 direction = record.direction;

        if (!IsVectorFinite(direction) ||
            direction.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        direction.Normalize();
        rayEnd = record.origin + direction * noHitRayLength;
        return IsVectorFinite(rayEnd);
    }

    private void CreateRayLine(
        Vector3 start,
        Vector3 end,
        Transform parent,
        Color color,
        int sourceRowIndex)
    {
        GameObject lineObject = new GameObject(
            "GazeRay_Row" + sourceRowIndex
        );
        lineObject.transform.SetParent(parent, false);

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);

        lineRenderer.startWidth = rayWidth;
        lineRenderer.endWidth = rayWidth;
        lineRenderer.numCapVertices = 1;
        lineRenderer.numCornerVertices = 0;

        lineRenderer.sharedMaterial = rayMaterial;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
    }

    private void CreateHitPointSphere(
        Vector3 position,
        Transform parent,
        Color color,
        int sourceRowIndex)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "GazeHitPoint_Row" + sourceRowIndex;
        sphere.transform.SetParent(parent, false);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * hitPointSize;

        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = pointMaterial;

            if (pointPropertyBlock == null)
            {
                pointPropertyBlock = new MaterialPropertyBlock();
            }

            pointPropertyBlock.Clear();
            pointPropertyBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(pointPropertyBlock);
        }

        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
    }

    // =========================
    // 10. 颜色与材质
    // =========================

    private Color GetRayColor(
        GazeRecord record,
        ParticipantDataset dataset)
    {
        if (rayColorMode == RayColorMode.FixedColor)
        {
            return fixedRayColor;
        }

        if (rayColorMode == RayColorMode.ByParticipant)
        {
            Color participantColor = dataset.participantColor;
            participantColor.a = participantRayAlpha;
            return participantColor;
        }

        Color pupilColor;

        if (!record.pupilValid)
        {
            pupilColor = fixedRayColor;
        }
        else
        {
            float safeMin = Mathf.Min(pupilMin, pupilMid);
            float safeMid = Mathf.Max(safeMin, pupilMid);
            float safeMax = Mathf.Max(safeMid, pupilMax);

            float value = Mathf.Clamp(
                record.pupilDiameter,
                safeMin,
                safeMax
            );

            if (value <= safeMid)
            {
                float denominator = Mathf.Max(0.000001f, safeMid - safeMin);
                float t = (value - safeMin) / denominator;
                pupilColor = Color.Lerp(
                    pupilSmallColor,
                    pupilMiddleColor,
                    t
                );
            }
            else
            {
                float denominator = Mathf.Max(0.000001f, safeMax - safeMid);
                float t = (value - safeMid) / denominator;
                pupilColor = Color.Lerp(
                    pupilMiddleColor,
                    pupilLargeColor,
                    t
                );
            }
        }

        pupilColor.a = fixedRayColor.a;
        return pupilColor;
    }

    private Color GetHitPointColor(
        GazeRecord record,
        ParticipantDataset dataset,
        Color rayColor)
    {
        if (hitPointColorMode == HitPointColorMode.FixedColor)
        {
            return fixedHitPointColor;
        }

        if (hitPointColorMode == HitPointColorMode.ByParticipant)
        {
            Color participantColor = dataset.participantColor;
            participantColor.a = participantPointAlpha;
            return participantColor;
        }

        rayColor.a = fixedHitPointColor.a;
        return rayColor;
    }

    private void CreateMaterials()
    {
        if (rayMaterial == null)
        {
            Shader rayShader = Shader.Find("Sprites/Default");
            if (rayShader == null)
            {
                rayShader = Shader.Find("Unlit/Color");
            }

            rayMaterial = new Material(rayShader);
            rayMaterial.name = "GazeMulti_RayMaterial";
            rayMaterial.color = Color.white;
        }

        if (pointMaterial == null)
        {
            Shader pointShader = Shader.Find("Sprites/Default");
            if (pointShader == null)
            {
                pointShader = Shader.Find("Unlit/Color");
            }
            if (pointShader == null)
            {
                pointShader = Shader.Find("Standard");
            }

            pointMaterial = new Material(pointShader);
            pointMaterial.name = "GazeMulti_PointMaterial";
            pointMaterial.color = Color.white;
            pointMaterial.enableInstancing = true;
        }
    }

    // =========================
    // 11. 选择、清理和外部调用
    // =========================

    private HashSet<string> GetSelectedNodeIDs()
    {
        HashSet<string> result =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < nodeGroups.Count; i++)
        {
            if (nodeGroups[i].selected)
            {
                result.Add(nodeGroups[i].nodeID ?? "");
            }
        }

        return result;
    }

    private void SetAllParticipantSelection(bool selected)
    {
        for (int i = 0; i < participantDatasets.Count; i++)
        {
            participantDatasets[i].selected = selected;
        }
    }

    private void SetAllNodeGroupSelection(bool selected)
    {
        for (int i = 0; i < nodeGroups.Count; i++)
        {
            nodeGroups[i].selected = selected;
        }
    }

    private int CountSelectedParticipants()
    {
        int count = 0;

        for (int i = 0; i < participantDatasets.Count; i++)
        {
            if (participantDatasets[i].selected)
            {
                count++;
            }
        }

        return count;
    }

    private int CountSelectedNodeGroups()
    {
        int count = 0;

        for (int i = 0; i < nodeGroups.Count; i++)
        {
            if (nodeGroups[i].selected)
            {
                count++;
            }
        }

        return count;
    }

    public void ClearVisualization()
    {
        if (visualizationRoot != null)
        {
            Destroy(visualizationRoot);
            visualizationRoot = null;
        }
    }

    public void ClearLoadedData()
    {
        ClearVisualization();
        participantDatasets.Clear();
        nodeGroups.Clear();
        totalLoadedRecords = 0;
        totalLoadedNodeStays = 0;
        statusMessage = "Loaded data and visualization cleared.";
    }

    public void SetPanelVisible(bool visible)
    {
        showControlPanel = visible;

        if (visible)
        {
            panelClosed = false;
        }
    }

    public void OpenPanel()
    {
        showControlPanel = true;
        panelClosed = false;
        panelMinimized = false;
    }

    private void OnDestroy()
    {
        if (rayMaterial != null)
        {
            Destroy(rayMaterial);
            rayMaterial = null;
        }

        if (pointMaterial != null)
        {
            Destroy(pointMaterial);
            pointMaterial = null;
        }
    }

    // =========================
    // 12. CSV 工具
    // =========================

    private List<List<string>> ReadCsv(string path)
    {
        List<List<string>> table = new List<List<string>>();

        using (StreamReader reader = new StreamReader(
            path,
            Encoding.UTF8,
            true))
        {
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                table.Add(ParseCsvLine(line));
            }
        }

        return table;
    }

    private List<string> ParseCsvLine(string line)
    {
        List<string> result = new List<string>();
        StringBuilder builder = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char character = line[i];

            if (character == '"')
            {
                if (inQuotes &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Length = 0;
            }
            else
            {
                builder.Append(character);
            }
        }

        result.Add(builder.ToString());
        return result;
    }

    private int FindColumnIndex(List<string> header, string columnName)
    {
        for (int i = 0; i < header.Count; i++)
        {
            string headerText = header[i]
                .Trim()
                .Trim('\uFEFF');

            if (string.Equals(
                headerText,
                columnName,
                StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private string SafeGet(List<string> row, int index)
    {
        if (index < 0 || index >= row.Count)
        {
            return "";
        }

        return row[index];
    }

    private Vector3 ReadVector3(
        List<string> row,
        int indexX,
        int indexY,
        int indexZ)
    {
        float x = 0f;
        float y = 0f;
        float z = 0f;

        TryParseFloat(SafeGet(row, indexX), out x);
        TryParseFloat(SafeGet(row, indexY), out y);
        TryParseFloat(SafeGet(row, indexZ), out z);

        return new Vector3(x, y, z);
    }

    private bool DetermineGazeValid(
        List<string> row,
        int indexGazeValid,
        GazeRecord record)
    {
        if (indexGazeValid >= 0)
        {
            string value = SafeGet(row, indexGazeValid).Trim();

            if (value == "1" ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return
            IsVectorFinite(record.origin) &&
            IsVectorFinite(record.direction) &&
            record.direction.sqrMagnitude > 0.000001f;
    }

    private bool DetermineGazeHit(
        List<string> row,
        int indexGazeHit,
        GazeRecord record)
    {
        if (indexGazeHit >= 0)
        {
            string value = SafeGet(row, indexGazeHit).Trim();

            if (value == "1" ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(record.hitObjectName) &&
            record.hitObjectName != "0" &&
            !record.hitObjectName.Equals(
                "NoHit",
                StringComparison.OrdinalIgnoreCase) &&
            !record.hitObjectName.Equals(
                "InvalidGaze",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return record.hitDistance > 0.0001f;
    }

    private void ReadPupilDiameter(
        List<string> row,
        int indexLeftPupil,
        int indexRightPupil,
        GazeRecord record)
    {
        float left = 0f;
        float right = 0f;

        bool hasLeft =
            indexLeftPupil >= 0 &&
            TryParseFloat(
                SafeGet(row, indexLeftPupil),
                out left
            ) &&
            left > 0f;

        bool hasRight =
            indexRightPupil >= 0 &&
            TryParseFloat(
                SafeGet(row, indexRightPupil),
                out right
            ) &&
            right > 0f;

        if (hasLeft && hasRight)
        {
            record.pupilDiameter = (left + right) * 0.5f;
            record.pupilValid = true;
        }
        else if (hasLeft)
        {
            record.pupilDiameter = left;
            record.pupilValid = true;
        }
        else if (hasRight)
        {
            record.pupilDiameter = right;
            record.pupilValid = true;
        }
        else
        {
            record.pupilDiameter = 0f;
            record.pupilValid = false;
        }
    }

    private bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    private bool IsVectorFinite(Vector3 vector)
    {
        return
            !float.IsNaN(vector.x) && !float.IsInfinity(vector.x) &&
            !float.IsNaN(vector.y) && !float.IsInfinity(vector.y) &&
            !float.IsNaN(vector.z) && !float.IsInfinity(vector.z);
    }

    private bool MatchesFilter(string value, string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        if (value == null)
        {
            return false;
        }

        return value.IndexOf(
            filter,
            StringComparison.OrdinalIgnoreCase
        ) >= 0;
    }

    private string SanitizeObjectName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "Empty";
        }

        char[] invalidCharacters =
            new char[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        string result = value;

        for (int i = 0; i < invalidCharacters.Length; i++)
        {
            result = result.Replace(invalidCharacters[i], '_');
        }

        return result;
    }

    private int Sum(int[] values)
    {
        int sum = 0;

        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }

        return sum;
    }

    private int CountCandidateRecords(
        List<ParticipantCandidateSet> candidateSets)
    {
        int count = 0;

        for (int i = 0; i < candidateSets.Count; i++)
        {
            count += candidateSets[i].records.Count;
        }

        return count;
    }

    // =========================
    // 13. 数据结构
    // =========================

    private class ParticipantDataset
    {
        public int participantIndex;
        public int taskOrder;
        public int csvIndex;
        public string participantID;
        public string csvPath;
        public string sourceFileName;
        public bool selected = true;
        public Color participantColor = Color.white;

        public readonly List<GazeRecord> records =
            new List<GazeRecord>();

        public readonly List<NodeStaySegment> nodeStays =
            new List<NodeStaySegment>();
    }

    private class GazeRecord
    {
        public int participantIndex;
        public string participantID;
        public string sourceFileName;
        public int sourceRowIndex;

        public string nodeID;
        public string sceneTime;
        public string timestamp;

        public Vector3 origin;
        public Vector3 direction;

        public Vector3 hitPoint;
        public float hitDistance;

        public string hitObjectName;
        public string hitObjectTag;

        public bool gazeValid;
        public bool gazeHit;

        public float pupilDiameter;
        public bool pupilValid;
    }

    private class NodeStaySegment
    {
        public string nodeID;
        public int startRecordIndex;
        public int endRecordIndex;

        public string startSceneTime;
        public string endSceneTime;

        public int RecordCount
        {
            get
            {
                return endRecordIndex - startRecordIndex + 1;
            }
        }
    }

    private class NodeGroup
    {
        public string nodeID;
        public int participantCount;
        public int stayCount;
        public int recordCount;
        public bool selected = true;
    }

    private class NodeGroupBuilder
    {
        public string nodeID;
        public readonly HashSet<int> participantIndices =
            new HashSet<int>();
        public int stayCount;
        public int recordCount;
    }

    private class ParticipantCandidateSet
    {
        public ParticipantDataset dataset;
        public readonly List<GazeRecord> records =
            new List<GazeRecord>();
    }

    private class CsvFileSelection
    {
        public int taskOrder;
        public int csvIndex;
        public string fullPath;
    }

    private struct QuotaRemainder
    {
        public int index;
        public float remainder;

        public QuotaRemainder(int index, float remainder)
        {
            this.index = index;
            this.remainder = remainder;
        }
    }
}
