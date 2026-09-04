using UnityEngine;

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

public class GazeHitCsvVisualizer : MonoBehaviour
{
    // =========================
    // 1. 数据路径设置
    // =========================

    [Header("Data Path Settings")]
    public string processedRoot;

    [Tooltip("任务编号，例如 TaskOrder=1 会读取 Reorder_GazeHit/Task1/Eye")]
    public int taskOrder = 1;

    [Tooltip("Eye 文件夹中第几个 csv 文件，1 表示第一个。")]
    public int csvFileIndex = 1;

    [Tooltip("如果填写完整 CSV 路径，则优先读取该路径；如果为空，则按 Root + TaskOrder + FileIndex 自动查找。")]
    public string overrideCsvFullPath = "";

    // =========================
    // 2. 可视化设置
    // =========================

    [Header("Visualization Settings")]
    public bool showRays = true;
    public bool showHitPoints = true;

    [Tooltip("没有命中时，射线显示的固定长度。")]
    public float noHitRayLength = 10f;

    [Tooltip("碰撞点小球大小。")]
    public float hitPointSize = 0.08f;

    [Tooltip("射线宽度。")]
    public float rayWidth = 0.01f;

    [Tooltip("抽样显示间隔。1 表示全部显示；2 表示每 2 条显示 1 条；5 表示每 5 条显示 1 条。")]
    public int sampleStep = 1;

    [Tooltip("最多显示多少条记录，防止一次生成过多对象导致卡顿。0 表示不限制。")]
    public int maxVisualizedRecords = 0;

    public Color rayColor = new Color(0f, 1f, 0f, 0.45f);
    public Color hitPointColor = new Color(1f, 0.1f, 0f, 0.85f);
    [Header("Pupil-based Ray Color")]
    public bool usePupilColorForRays = false;
    public float pupilMin = 2.5f;
    public float pupilMid = 4.0f;
    public float pupilMax = 6.0f;
    public Color pupilSmallColor = Color.cyan;
    public Color pupilMiddleColor = Color.yellow;
    public Color pupilLargeColor = Color.red;
    private bool panelMinimized = false;
    private bool panelClosed = false;


    // =========================
    // 3. 面板设置
    // =========================

    [Header("Panel Settings")]
    public bool showControlPanel = true;
    public Rect panelRect = new Rect(520f, 20f, 560f, 820f);

    public float minPanelWidth = 360f;
    public float minPanelHeight = 260f;
    public float resizeBorderSize = 8f;

    private bool isResizingPanel = false;
    private PanelResizeMode resizeMode = PanelResizeMode.None;
    private Vector2 panelScrollPos = Vector2.zero;

    private void Awake()
    {
        processedRoot = ProjectPathConfig.GazeHitDataRoot;
    }

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

    [Tooltip("Node 停留段列表区域高度")]
    public float nodeListHeight = 360f;

    private Vector2 scrollPosition = Vector2.zero;

    // =========================
    // 4. 内部数据
    // =========================

    private List<GazeRecord> records = new List<GazeRecord>();
    private List<NodeStaySegment> nodeStays = new List<NodeStaySegment>();

    private GameObject visualizationRoot;
    private Material rayMaterial;
    private Material pointMaterial;

    private string loadedCsvPath = "";
    private string statusMessage = "No data loaded.";

    // =========================
    // 5. Unity GUI 面板
    // =========================

    private void OnGUI()
    {
        if (!showControlPanel || panelClosed)
        {
            return;
        }

        panelRect = GUI.Window(20260430, panelRect, DrawPanel, "");
    }

    private void DrawPanel(int windowID)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(28));

        GUILayout.Label("Gaze Hit Visualizer", GUILayout.Width(180));
        GUILayout.FlexibleSpace();

        if (GUILayout.Button(panelMinimized ? "Expand" : "Minimize", GUILayout.Width(80)))
        {
            panelMinimized = !panelMinimized;
        }

        if (GUILayout.Button("Close", GUILayout.Width(60)))
        {
            panelClosed = true;
            return;
        }

        GUILayout.EndHorizontal();

        if (panelMinimized)
        {
            HandlePanelResize();
            GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));
            return;
        }

        panelScrollPos = GUILayout.BeginScrollView(
    panelScrollPos,
    false,
    true,
    GUILayout.Width(panelRect.width - 14f),
    GUILayout.Height(panelRect.height - 28f)
);
        GUILayout.Label("Data Source", GUI.skin.box);

        GUILayout.Label("Processed Root:");
        processedRoot = GUILayout.TextField(processedRoot);

        GUILayout.BeginHorizontal();
        GUILayout.Label("TaskOrder:", GUILayout.Width(80));
        string taskText = GUILayout.TextField(taskOrder.ToString(), GUILayout.Width(80));
        int parsedTask;
        if (int.TryParse(taskText, out parsedTask))
        {
            taskOrder = Mathf.Max(1, parsedTask);
        }

        GUILayout.Label("CSV Index:", GUILayout.Width(80));
        string indexText = GUILayout.TextField(csvFileIndex.ToString(), GUILayout.Width(80));
        int parsedIndex;
        if (int.TryParse(indexText, out parsedIndex))
        {
            csvFileIndex = Mathf.Max(1, parsedIndex);
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("Override CSV Full Path:");
        overrideCsvFullPath = GUILayout.TextField(overrideCsvFullPath);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Load CSV", GUILayout.Height(28)))
        {
            LoadCsvAndBuildNodeStays();
        }

        if (GUILayout.Button("Clear Visualization", GUILayout.Height(28)))
        {
            ClearVisualization();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("Visualization Options", GUI.skin.box);

        showRays = GUILayout.Toggle(showRays, "Show Rays");
        showHitPoints = GUILayout.Toggle(showHitPoints, "Show Hit Points");
        usePupilColorForRays = GUILayout.Toggle(usePupilColorForRays, "Use Pupil Color For Rays");

        pupilMin = FloatField("Pupil Min", pupilMin);
        pupilMid = FloatField("Pupil Mid", pupilMid);
        pupilMax = FloatField("Pupil Max", pupilMax);

        GUILayout.BeginHorizontal();
        GUILayout.Label("NoHit Ray Length:", GUILayout.Width(130));
        string noHitLenText = GUILayout.TextField(noHitRayLength.ToString(CultureInfo.InvariantCulture), GUILayout.Width(80));
        float parsedNoHitLen;
        if (float.TryParse(noHitLenText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedNoHitLen))
        {
            noHitRayLength = Mathf.Max(0.01f, parsedNoHitLen);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Point Size:", GUILayout.Width(130));
        string pointSizeText = GUILayout.TextField(hitPointSize.ToString(CultureInfo.InvariantCulture), GUILayout.Width(80));
        float parsedPointSize;
        if (float.TryParse(pointSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedPointSize))
        {
            hitPointSize = Mathf.Max(0.001f, parsedPointSize);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Ray Width:", GUILayout.Width(130));
        string rayWidthText = GUILayout.TextField(rayWidth.ToString(CultureInfo.InvariantCulture), GUILayout.Width(80));
        float parsedRayWidth;
        if (float.TryParse(rayWidthText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedRayWidth))
        {
            rayWidth = Mathf.Max(0.0001f, parsedRayWidth);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Sample Step:", GUILayout.Width(130));
        string sampleStepText = GUILayout.TextField(sampleStep.ToString(), GUILayout.Width(80));
        int parsedSampleStep;
        if (int.TryParse(sampleStepText, out parsedSampleStep))
        {
            sampleStep = Mathf.Max(1, parsedSampleStep);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Max Records:", GUILayout.Width(130));
        string maxRecordText = GUILayout.TextField(maxVisualizedRecords.ToString(), GUILayout.Width(80));
        int parsedMaxRecord;
        if (int.TryParse(maxRecordText, out parsedMaxRecord))
        {
            maxVisualizedRecords = Mathf.Max(0, parsedMaxRecord);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All Node Stays"))
        {
            SetAllNodeStaySelection(true);
        }

        if (GUILayout.Button("Unselect All"))
        {
            SetAllNodeStaySelection(false);
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Refresh Visualization", GUILayout.Height(30)))
        {
            RefreshVisualization();
        }

        GUILayout.Space(8);
        GUILayout.Label("Node Stay Segments", GUI.skin.box);

        if (records.Count == 0)
        {
            GUILayout.Label("请先点击 Load CSV。");
        }
        else
        {
            GUILayout.Label("Loaded: " + Path.GetFileName(loadedCsvPath));
            GUILayout.Label("Records: " + records.Count + " | Node stays: " + nodeStays.Count);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Node List Height:", GUILayout.Width(130));
        nodeListHeight = GUILayout.HorizontalSlider(nodeListHeight, 150f, 600f, GUILayout.Width(220));
        GUILayout.Label(Mathf.RoundToInt(nodeListHeight).ToString(), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        scrollPosition = GUILayout.BeginScrollView(
            scrollPosition,
            false,
            true,
            GUILayout.Height(nodeListHeight)
        );

        for (int i = 0; i < nodeStays.Count; i++)
        {
            NodeStaySegment seg = nodeStays[i];

            string label =
                "#" + (i + 1).ToString("D2") +
                " | NodeID=" + seg.nodeID +
                " | Rows=" + (seg.startRecordIndex + 1) + "-" + (seg.endRecordIndex + 1) +
                " | Count=" + seg.RecordCount;

            if (!string.IsNullOrEmpty(seg.startSceneTime) || !string.IsNullOrEmpty(seg.endSceneTime))
            {
                label += " | Time=" + seg.startSceneTime + " - " + seg.endSceneTime;
            }

            seg.selected = GUILayout.Toggle(seg.selected, label);
        }

        GUILayout.EndScrollView();

        GUILayout.Space(8);
        GUILayout.Label("Status:");
        GUILayout.TextArea(statusMessage, GUILayout.Height(80));
        GUILayout.EndScrollView();
        HandlePanelResize();
        GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));

        DrawResizeHandle();

    }

    private void DrawResizeHandle()
    {
        Rect resizeRect = new Rect(
            panelRect.width - resizeBorderSize,
            panelRect.height - resizeBorderSize,
            resizeBorderSize,
            resizeBorderSize
        );

        GUI.Box(resizeRect, "↘");

        Event e = Event.current;

        if (e.type == EventType.MouseDown && resizeRect.Contains(e.mousePosition))
        {
            isResizingPanel = true;
            e.Use();
        }

        if (isResizingPanel && e.type == EventType.MouseDrag)
        {
            panelRect.width = Mathf.Max(minPanelWidth, panelRect.width + e.delta.x);
            panelRect.height = Mathf.Max(minPanelHeight, panelRect.height + e.delta.y);
            e.Use();
        }

        if (e.type == EventType.MouseUp)
        {
            isResizingPanel = false;
        }
    }

    private void HandlePanelResize()
    {
        Event e = Event.current;

        PanelResizeMode hitMode = GetPanelResizeMode(e.mousePosition);

        if (e.type == EventType.MouseDown && hitMode != PanelResizeMode.None)
        {
            resizeMode = hitMode;
            isResizingPanel = true;
            e.Use();
        }

        if (isResizingPanel && e.type == EventType.MouseDrag)
        {
            Vector2 delta = e.delta;

            float x = panelRect.x;
            float y = panelRect.y;
            float w = panelRect.width;
            float h = panelRect.height;

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
                float newWidth = Mathf.Max(minPanelWidth, w - delta.x);
                x += w - newWidth;
                w = newWidth;
            }

            if (resizeRight)
            {
                w = Mathf.Max(minPanelWidth, w + delta.x);
            }

            if (resizeTop)
            {
                float newHeight = Mathf.Max(minPanelHeight, h - delta.y);
                y += h - newHeight;
                h = newHeight;
            }

            if (resizeBottom)
            {
                h = Mathf.Max(minPanelHeight, h + delta.y);
            }

            panelRect.x = x;
            panelRect.y = y;
            panelRect.width = w;
            panelRect.height = h;

            e.Use();
        }

        if (e.type == EventType.MouseUp)
        {
            isResizingPanel = false;
            resizeMode = PanelResizeMode.None;
        }

        DrawResizeBorderHints();
    }

    private PanelResizeMode GetPanelResizeMode(Vector2 mousePosition)
    {
        float b = resizeBorderSize;
        float w = panelRect.width;
        float h = panelRect.height;

        bool left = mousePosition.x >= 0 && mousePosition.x <= b;
        bool right = mousePosition.x >= w - b && mousePosition.x <= w;
        bool top = mousePosition.y >= 0 && mousePosition.y <= b;
        bool bottom = mousePosition.y >= h - b && mousePosition.y <= h;

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
        GUI.color = new Color(1f, 1f, 1f, 0.18f);

        float b = resizeBorderSize;

        GUI.Box(new Rect(0, 0, panelRect.width, b), "");
        GUI.Box(new Rect(0, panelRect.height - b, panelRect.width, b), "");
        GUI.Box(new Rect(0, 0, b, panelRect.height), "");
        GUI.Box(new Rect(panelRect.width - b, 0, b, panelRect.height), "");

        GUI.color = oldColor;
    }
    private float FloatField(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(130));

        string text = GUILayout.TextField(value.ToString(CultureInfo.InvariantCulture), GUILayout.Width(80));
        float parsed;

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
        }

        GUILayout.EndHorizontal();
        return value;
    }

    // =========================
    // 6. 加载数据
    // =========================

    private void LoadCsvAndBuildNodeStays()
    {
        ClearVisualization();

        records.Clear();
        nodeStays.Clear();

        string csvPath = GetTargetCsvPath();

        if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
        {
            statusMessage = "CSV file not found:\n" + csvPath;
            Debug.LogError(statusMessage);
            return;
        }

        loadedCsvPath = csvPath;

        List<List<string>> table = ReadCsv(csvPath);

        if (table == null || table.Count < 2)
        {
            statusMessage = "CSV is empty or only has header:\n" + csvPath;
            Debug.LogError(statusMessage);
            return;
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
            idxHitX < 0 || idxHitY < 0 || idxHitZ < 0 || idxHitDistance < 0)
        {
            statusMessage =
                "CSV 缺少必要字段。\n" +
                "至少需要：NodeID, GazeOriginWorldX/Y/Z, GazeDirectionWorldX/Y/Z, HitPointX/Y/Z, HitDistance。\n" +
                csvPath;

            Debug.LogError(statusMessage);
            return;
        }

        for (int i = 1; i < table.Count; i++)
        {
            List<string> row = table[i];

            GazeRecord rec = new GazeRecord();
            rec.sourceRowIndex = i;

            rec.nodeID = SafeGet(row, idxNodeID);

            rec.sceneTime = idxSceneTime >= 0 ? SafeGet(row, idxSceneTime) : "";
            rec.timestamp = idxTimestamp >= 0 ? SafeGet(row, idxTimestamp) : "";

            rec.origin = ReadVector3(row, idxOriginX, idxOriginY, idxOriginZ);
            rec.direction = ReadVector3(row, idxDirX, idxDirY, idxDirZ);

            rec.hitPoint = ReadVector3(row, idxHitX, idxHitY, idxHitZ);

            float hitDistance;
            if (TryParseFloat(SafeGet(row, idxHitDistance), out hitDistance))
            {
                rec.hitDistance = hitDistance;
            }
            else
            {
                rec.hitDistance = 0f;
            }

            rec.hitObjectName = idxHitObjectName >= 0 ? SafeGet(row, idxHitObjectName) : "";
            rec.hitObjectTag = idxHitObjectTag >= 0 ? SafeGet(row, idxHitObjectTag) : "";

            ReadPupilDiameter(row, idxLeftPupil, idxRightPupil, rec);

            rec.gazeValid = DetermineGazeValid(row, idxGazeValid, rec);
            rec.gazeHit = DetermineGazeHit(row, idxGazeHit, rec);

            records.Add(rec);
        }

        BuildNodeStaySegments();

        statusMessage =
            "Loaded CSV successfully.\n" +
            "File: " + Path.GetFileName(csvPath) + "\n" +
            "Records: " + records.Count + "\n" +
            "Node stay segments: " + nodeStays.Count;

        Debug.Log(statusMessage);
    }

    private string GetTargetCsvPath()
    {
        if (!string.IsNullOrWhiteSpace(overrideCsvFullPath))
        {
            return overrideCsvFullPath.Trim();
        }

        string eyeFolder = Path.Combine(
            Path.Combine(processedRoot, "Task" + taskOrder),
            "Eye"
        );

        if (!Directory.Exists(eyeFolder))
        {
            return eyeFolder;
        }

        string[] csvFiles = Directory.GetFiles(eyeFolder, "*.csv", SearchOption.TopDirectoryOnly);
        Array.Sort(csvFiles, StringComparer.OrdinalIgnoreCase);

        if (csvFiles.Length == 0)
        {
            return eyeFolder + " | No csv files found";
        }

        int index = Mathf.Clamp(csvFileIndex, 1, csvFiles.Length) - 1;
        return csvFiles[index];
    }

    // =========================
    // 7. 根据连续 NodeID 划分停留段
    // =========================

    private void BuildNodeStaySegments()
    {
        nodeStays.Clear();

        if (records.Count == 0)
        {
            return;
        }

        int startIndex = 0;
        string currentNodeID = records[0].nodeID;

        for (int i = 1; i < records.Count; i++)
        {
            string nodeID = records[i].nodeID;

            if (nodeID != currentNodeID)
            {
                AddNodeStaySegment(startIndex, i - 1, currentNodeID);

                startIndex = i;
                currentNodeID = nodeID;
            }
        }

        AddNodeStaySegment(startIndex, records.Count - 1, currentNodeID);
    }

    private void AddNodeStaySegment(int startIndex, int endIndex, string nodeID)
    {
        NodeStaySegment seg = new NodeStaySegment();
        seg.nodeID = nodeID;
        seg.startRecordIndex = startIndex;
        seg.endRecordIndex = endIndex;
        seg.selected = true;

        if (startIndex >= 0 && startIndex < records.Count)
        {
            seg.startSceneTime = !string.IsNullOrEmpty(records[startIndex].sceneTime)
                ? records[startIndex].sceneTime
                : records[startIndex].timestamp;
        }

        if (endIndex >= 0 && endIndex < records.Count)
        {
            seg.endSceneTime = !string.IsNullOrEmpty(records[endIndex].sceneTime)
                ? records[endIndex].sceneTime
                : records[endIndex].timestamp;
        }

        nodeStays.Add(seg);
    }

    // =========================
    // 8. 刷新可视化
    // =========================

    private void RefreshVisualization()
    {
        ClearVisualization();

        if (records.Count == 0 || nodeStays.Count == 0)
        {
            statusMessage = "No loaded records. Please Load CSV first.";
            Debug.LogWarning(statusMessage);
            return;
        }

        CreateMaterials();

        visualizationRoot = new GameObject("GazeHit_Visualization_Task" + taskOrder);
        visualizationRoot.transform.SetParent(transform, false);

        int createdRayCount = 0;
        int createdPointCount = 0;
        int usedRecordCount = 0;

        int step = Mathf.Max(1, sampleStep);

        for (int s = 0; s < nodeStays.Count; s++)
        {
            NodeStaySegment seg = nodeStays[s];

            if (!seg.selected)
            {
                continue;
            }

            GameObject segmentRoot = new GameObject(
                "NodeStay_" + (s + 1).ToString("D2") + "_NodeID_" + seg.nodeID
            );
            segmentRoot.transform.SetParent(visualizationRoot.transform, false);

            for (int i = seg.startRecordIndex; i <= seg.endRecordIndex; i += step)
            {
                if (i < 0 || i >= records.Count)
                {
                    continue;
                }

                if (maxVisualizedRecords > 0 && usedRecordCount >= maxVisualizedRecords)
                {
                    break;
                }

                GazeRecord rec = records[i];

                if (!rec.gazeValid)
                {
                    continue;
                }

                usedRecordCount++;

                Vector3 rayStart = rec.origin;
                Vector3 rayEnd;

                if (rec.gazeHit)
                {
                    rayEnd = rec.hitPoint;
                }
                else
                {
                    Vector3 dir = rec.direction;
                    if (dir.sqrMagnitude < 0.000001f)
                    {
                        continue;
                    }

                    dir.Normalize();
                    rayEnd = rayStart + dir * noHitRayLength;
                }

                if (showRays)
                {
                    CreateRayLine(rayStart, rayEnd, segmentRoot.transform, GetRayColor(rec));
                    createdRayCount++;
                }

                if (showHitPoints && rec.gazeHit)
                {
                    CreateHitPointSphere(rec.hitPoint, segmentRoot.transform);
                    createdPointCount++;
                }
            }
        }

        statusMessage =
            "Visualization refreshed.\n" +
            "Selected records used: " + usedRecordCount + "\n" +
            "Rays: " + createdRayCount + "\n" +
            "Hit points: " + createdPointCount + "\n" +
            "Sample step: " + step;

        Debug.Log(statusMessage);
    }

    private void CreateRayLine(Vector3 start, Vector3 end, Transform parent, Color color)
    {
        GameObject lineObj = new GameObject("GazeRay");
        lineObj.transform.SetParent(parent, false);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        lr.startWidth = rayWidth;
        lr.endWidth = rayWidth;

        lr.material = rayMaterial;
        lr.startColor = color;
        lr.endColor = color;
    }

    private void CreateHitPointSphere(Vector3 position, Transform parent)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "GazeHitPoint";
        sphere.transform.SetParent(parent, false);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * hitPointSize;

        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = pointMaterial;
        }

        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
    }

    private void ClearVisualization()
    {
        if (visualizationRoot != null)
        {
            Destroy(visualizationRoot);
            visualizationRoot = null;
        }
    }

    private void SetAllNodeStaySelection(bool selected)
    {
        for (int i = 0; i < nodeStays.Count; i++)
        {
            nodeStays[i].selected = selected;
        }
    }

    public void ConfigureFromReplay(
    string root,
    int task,
    int csvIndex,
    string overridePath)
    {
        processedRoot = root;
        taskOrder = task;
        csvFileIndex = csvIndex;
        overrideCsvFullPath = overridePath;
    }

    public void LoadFromReplay()
    {
        LoadCsvAndBuildNodeStays();
    }

    public void RefreshFromReplay(
        bool displayRays,
        bool displayHitPoints,
        HashSet<int> selectedNodeStayIndices)
    {
        showRays = displayRays;
        showHitPoints = displayHitPoints;

        ApplyNodeStaySelectionFromReplay(selectedNodeStayIndices);

        RefreshVisualization();
    }

    public void ClearFromReplay()
    {
        ClearVisualization();
    }

    public void SetPanelVisibleFromReplay(bool visible)
    {
        showControlPanel = visible;
    }

    private void ApplyNodeStaySelectionFromReplay(HashSet<int> selectedNodeStayIndices)
    {
        if (selectedNodeStayIndices == null || selectedNodeStayIndices.Count == 0)
        {
            for (int i = 0; i < nodeStays.Count; i++)
            {
                nodeStays[i].selected = true;
            }

            return;
        }

        for (int i = 0; i < nodeStays.Count; i++)
        {
            nodeStays[i].selected = selectedNodeStayIndices.Contains(i);
        }
    }

    // =========================
    // 9. 材质
    // =========================

    private void CreateMaterials()
    {
        if (rayMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            rayMaterial = new Material(shader);
            rayMaterial.color = Color.white;
        }

        if (pointMaterial == null)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            pointMaterial = new Material(shader);
            pointMaterial.color = hitPointColor;
        }
    }

    // =========================
    // 10. CSV 读取
    // =========================

    private List<List<string>> ReadCsv(string path)
    {
        List<List<string>> table = new List<List<string>>();

        using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
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
        StringBuilder sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());

        return result;
    }

    // =========================
    // 11. 字段读取与判断
    // =========================

    private int FindColumnIndex(List<string> header, string columnName)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i].Trim(), columnName, StringComparison.OrdinalIgnoreCase))
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

    private Vector3 ReadVector3(List<string> row, int ix, int iy, int iz)
    {
        float x = 0f;
        float y = 0f;
        float z = 0f;

        TryParseFloat(SafeGet(row, ix), out x);
        TryParseFloat(SafeGet(row, iy), out y);
        TryParseFloat(SafeGet(row, iz), out z);

        return new Vector3(x, y, z);
    }

    private bool DetermineGazeValid(List<string> row, int idxGazeValid, GazeRecord rec)
    {
        if (idxGazeValid >= 0)
        {
            string value = SafeGet(row, idxGazeValid).Trim();

            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return IsVectorFinite(rec.origin) &&
               IsVectorFinite(rec.direction) &&
               rec.direction.sqrMagnitude > 0.000001f;
    }

    private bool DetermineGazeHit(List<string> row, int idxGazeHit, GazeRecord rec)
    {
        if (idxGazeHit >= 0)
        {
            string value = SafeGet(row, idxGazeHit).Trim();

            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(rec.hitObjectName) &&
            rec.hitObjectName != "0" &&
            rec.hitObjectName != "NoHit" &&
            rec.hitObjectName != "InvalidGaze")
        {
            return true;
        }

        return rec.hitDistance > 0.0001f;
    }
    private void ReadPupilDiameter(
    List<string> row,
    int idxLeftPupil,
    int idxRightPupil,
    GazeRecord rec)
    {
        float left = 0f;
        float right = 0f;

        bool hasLeft =
            idxLeftPupil >= 0 &&
            TryParseFloat(SafeGet(row, idxLeftPupil), out left) &&
            left > 0f;

        bool hasRight =
            idxRightPupil >= 0 &&
            TryParseFloat(SafeGet(row, idxRightPupil), out right) &&
            right > 0f;

        if (hasLeft && hasRight)
        {
            rec.pupilDiameter = (left + right) * 0.5f;
            rec.pupilValid = true;
        }
        else if (hasLeft)
        {
            rec.pupilDiameter = left;
            rec.pupilValid = true;
        }
        else if (hasRight)
        {
            rec.pupilDiameter = right;
            rec.pupilValid = true;
        }
        else
        {
            rec.pupilDiameter = 0f;
            rec.pupilValid = false;
        }
    }

    private Color GetRayColor(GazeRecord rec)
    {
        if (!usePupilColorForRays || !rec.pupilValid)
        {
            return rayColor;
        }

        float v = Mathf.Clamp(rec.pupilDiameter, pupilMin, pupilMax);

        if (v <= pupilMid)
        {
            float t = Mathf.InverseLerp(pupilMin, pupilMid, v);
            return Color.Lerp(pupilSmallColor, pupilMiddleColor, t);
        }
        else
        {
            float t = Mathf.InverseLerp(pupilMid, pupilMax, v);
            return Color.Lerp(pupilMiddleColor, pupilLargeColor, t);
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

    private bool IsVectorFinite(Vector3 v)
    {
        return
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }

    // =========================
    // 12. 数据结构
    // =========================

    private class GazeRecord
    {
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

        public bool selected = true;

        public int RecordCount
        {
            get
            {
                return endRecordIndex - startRecordIndex + 1;
            }
        }
    }
}