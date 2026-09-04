using UnityEngine;
using UnityEngine.SceneManagement;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

public class PathTrajectoryMultiVisualizer : MonoBehaviour
{
    [Serializable]
    public class TaskPathSelection
    {
        [Tooltip("对应 StreamingAssets/Reorder/TaskX/Path 中的 TaskX")]
        public int taskOrder = 1;

        [Tooltip("勾选后载入该任务 Path 文件夹中的全部 CSV")]
        public bool loadAllCsv = true;

        [Tooltip("未勾选 Load All 时使用。支持 1,3,5-8；index 从 1 开始，按文件名排序。")]
        public string csvIndices = "1";
    }

    [Header("Data Paths")]
    public string pathRootFolderName = "Reorder";
    public string pathSubFolderName = "Path";
    public string nodeInfoRelativePath = "ReadData/NodeInfo.csv";
    public string targetInfoRelativePath = "SaveData/AllTargetPositions.csv";

    [Tooltip("可添加多条任务。每条任务可载入全部 CSV，或指定 CSV index。")]
    public List<TaskPathSelection> taskSelections = new List<TaskPathSelection>();

    [Header("Scene")]
    public string sceneIDOverride = "";

    [Header("Visualization")]
    public bool showControlPanel = true;
    public bool showTrajectory = true;
    public bool showMarkers = true;
    public bool showOrderLabels = true;
    public bool showDirectionArrows = true;

    public float groundY = 0.1f;
    public float markerDiameter = 0.8f;
    public float markerThickness = 0.03f;
    public float lineWidth = 0.08f;

    [Range(0.05f, 1f)]
    public float trajectoryAlpha = 0.42f;

    public float orderLabelHeight = 0.55f;
    public int orderLabelFontSize = 72;
    public float orderLabelCharacterSize = 0.12f;
    public Color orderLabelColor = Color.white;

    public float arrowSize = 0.45f;
    public float arrowHeight = 0.16f;
    public Color arrowColor = Color.white;

    public Color lineColor = Color.white;
    public Color shortDecisionColor = Color.green;
    public Color middleDecisionColor = Color.yellow;
    public Color longDecisionColor = Color.red;
    public Color targetColor = Color.cyan;

    [Header("Decision Time Color Mapping")]
    public float minDecisionTime = 0f;
    public float midDecisionTime = 5f;
    public float maxDecisionTime = 15f;
    public bool useTargetColorWhenDecTimeIsZero = true;

    [Header("Multi-trajectory Layout")]
    [Tooltip("将多条轨迹在水平方向轻微错开，避免完全重合。")]
    public bool separateOverlappingLines = false;

    [Tooltip("相邻轨迹之间的侧向间距。")]
    public float lineSideOffset = 0.18f;

    [Range(0.1f, 0.95f)]
    public float arrowPositionRatio = 0.82f;

    [Tooltip("同一位置出现多个顺序编号时，编号向外错开的半径。")]
    public float repeatedLabelOffsetRadius = 1.5f;

    [Header("Panel")]
    public Rect panelRect = new Rect(520, 20, 470, 700);

    private Vector2 scrollPos = Vector2.zero;

    private GameObject rootObj;

    private readonly Dictionary<string, Vector3> nodePosMap = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, Vector3> targetPosMap = new Dictionary<string, Vector3>();

    private readonly List<TrajectoryData> trajectories = new List<TrajectoryData>();
    private readonly Dictionary<string, AggregatedPointData> aggregatedPointMap =
        new Dictionary<string, AggregatedPointData>();

    private readonly Dictionary<string, int> labelPositionCountMap = new Dictionary<string, int>();
    private readonly List<Material> generatedMaterials = new List<Material>();

    private string statusMessage = "Ready.";

    private void Awake()
    {
        EnsureTaskSelections();
    }

    private void Reset()
    {
        EnsureTaskSelections();
    }

    private void OnValidate()
    {
        EnsureTaskSelections();
        trajectoryAlpha = Mathf.Clamp01(trajectoryAlpha);
        lineSideOffset = Mathf.Max(0f, lineSideOffset);
    }

    private void EnsureTaskSelections()
    {
        if (taskSelections == null)
        {
            taskSelections = new List<TaskPathSelection>();
        }

        if (taskSelections.Count == 0)
        {
            taskSelections.Add(new TaskPathSelection());
        }
    }

    private void OnGUI()
    {
        if (!showControlPanel)
        {
            return;
        }

        EnsureTaskSelections();
        panelRect = GUI.Window(20260714, panelRect, DrawPanel, "Multi Path Trajectory Visualizer");
    }

    private void DrawPanel(int windowID)
    {
        scrollPos = GUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("Path Data Selection", GUI.skin.box);
        GUILayout.Label("CSV index 从 1 开始，并按文件名排序；支持 1,3,5-8。", GUI.skin.label);

        int removeIndex = -1;

        for (int i = 0; i < taskSelections.Count; i++)
        {
            TaskPathSelection selection = taskSelections[i];

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Task Selection " + (i + 1), GUILayout.Width(180));

            if (GUILayout.Button("Remove", GUILayout.Width(80)))
            {
                removeIndex = i;
            }

            GUILayout.EndHorizontal();

            selection.taskOrder = IntField("Task Order", selection.taskOrder);
            selection.loadAllCsv = GUILayout.Toggle(selection.loadAllCsv, "Load All CSV Data");

            if (!selection.loadAllCsv)
            {
                GUILayout.Label("CSV Indices:");
                selection.csvIndices = GUILayout.TextField(selection.csvIndices);
            }

            GUILayout.EndVertical();
        }

        if (removeIndex >= 0 && taskSelections.Count > 1)
        {
            taskSelections.RemoveAt(removeIndex);
        }

        if (GUILayout.Button("+ Add Task Selection", GUILayout.Height(26)))
        {
            taskSelections.Add(new TaskPathSelection());
        }

        GUILayout.Space(6);
        GUILayout.Label("Scene ID Override:");
        sceneIDOverride = GUILayout.TextField(sceneIDOverride);

        GUILayout.Space(8);
        GUILayout.Label("Display", GUI.skin.box);

        showTrajectory = GUILayout.Toggle(showTrajectory, "Show Trajectory Lines");
        showMarkers = GUILayout.Toggle(showMarkers, "Show Aggregated Markers");
        showOrderLabels = GUILayout.Toggle(showOrderLabels, "Show Per-trajectory Order Labels");
        showDirectionArrows = GUILayout.Toggle(showDirectionArrows, "Show Direction Arrows");

        markerDiameter = FloatField("Marker Diameter", markerDiameter);
        markerThickness = FloatField("Marker Thickness", markerThickness);
        lineWidth = FloatField("Line Width", lineWidth);
        trajectoryAlpha = SliderField("Trajectory Alpha", trajectoryAlpha, 0.05f, 1f);
        groundY = FloatField("Ground Y", groundY);

        separateOverlappingLines = GUILayout.Toggle(
            separateOverlappingLines,
            "Separate Overlapping Trajectories"
        );
        lineSideOffset = FloatField("Trajectory Side Offset", lineSideOffset);

        GUILayout.Space(8);
        GUILayout.Label("Order Labels", GUI.skin.box);

        orderLabelHeight = FloatField("Label Height", orderLabelHeight);
        orderLabelFontSize = IntField("Label Font Size", orderLabelFontSize);
        orderLabelCharacterSize = FloatField("Label Character Size", orderLabelCharacterSize);
        repeatedLabelOffsetRadius = FloatField("Repeated Label Offset", repeatedLabelOffsetRadius);

        GUILayout.Space(8);
        GUILayout.Label("Direction Arrows", GUI.skin.box);

        arrowSize = FloatField("Arrow Size", arrowSize);
        arrowHeight = FloatField("Arrow Height", arrowHeight);
        arrowPositionRatio = SliderField("Arrow Position Ratio", arrowPositionRatio, 0.1f, 0.95f);

        GUILayout.Space(8);
        GUILayout.Label("Decision Time Thresholds", GUI.skin.box);

        minDecisionTime = FloatField("Min DT", minDecisionTime);
        midDecisionTime = FloatField("Mid DT", midDecisionTime);
        maxDecisionTime = FloatField("Max DT", maxDecisionTime);

        GUILayout.Space(10);

        if (GUILayout.Button("Load And Draw Selected Paths", GUILayout.Height(32)))
        {
            LoadAndDrawPaths();
        }

        if (GUILayout.Button("Clear Paths", GUILayout.Height(28)))
        {
            ClearPaths();
        }

        GUILayout.Space(8);
        GUILayout.Label("Status:");
        GUILayout.TextArea(statusMessage, GUILayout.Height(130));

        GUILayout.EndScrollView();

        GUI.DragWindow(new Rect(0, 0, panelRect.width, 24));
    }

    private int IntField(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(190));

        int parsed;
        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(110));

        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Max(1, parsed);
        }

        GUILayout.EndHorizontal();
        return value;
    }

    private float FloatField(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(190));

        float parsed;
        string text = GUILayout.TextField(
            value.ToString("G4", CultureInfo.InvariantCulture),
            GUILayout.Width(110)
        );

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
        }

        GUILayout.EndHorizontal();
        return value;
    }

    private float SliderField(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(190));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(130));
        GUILayout.Label(value.ToString("F2", CultureInfo.InvariantCulture), GUILayout.Width(45));
        GUILayout.EndHorizontal();
        return value;
    }

    public void LoadAndDrawPaths()
    {
        ClearPaths();

        string sceneID = ResolveSceneID();
        if (StringIsNullOrWhiteSpace(sceneID))
        {
            statusMessage = "SceneID is empty. Please set Scene ID Override or use a scene name containing digits.";
            Debug.LogError(statusMessage);
            return;
        }

        LoadNodePositions();
        LoadTargetPositions();

        List<PathFileSelection> selectedFiles = ResolveSelectedPathFiles();
        if (selectedFiles.Count == 0)
        {
            statusMessage = "No CSV files were selected or found.\n" + statusMessage;
            Debug.LogWarning(statusMessage);
            return;
        }

        int emptyFileCount = 0;

        for (int i = 0; i < selectedFiles.Count; i++)
        {
            PathFileSelection fileSelection = selectedFiles[i];
            List<PathPoint> points = ReadPathCsv(fileSelection.fullPath, sceneID);

            if (points.Count == 0)
            {
                emptyFileCount++;
                Debug.LogWarning("No valid path points in: " + fileSelection.fullPath);
                continue;
            }

            TrajectoryData trajectory = new TrajectoryData();
            trajectory.taskOrder = fileSelection.taskOrder;
            trajectory.csvIndex = fileSelection.csvIndex;
            trajectory.fileName = Path.GetFileName(fileSelection.fullPath);
            trajectory.fullPath = fileSelection.fullPath;
            trajectory.points = points;
            trajectories.Add(trajectory);

            AddTrajectoryToAggregation(trajectory);
        }

        if (trajectories.Count == 0)
        {
            statusMessage = "CSV files were found, but no valid trajectories were loaded.";
            Debug.LogWarning(statusMessage);
            return;
        }

        FinalizeAggregatedDecisionTimes();
        DrawAllObjects();

        statusMessage =
            "Multi-path visualization loaded.\n" +
            "SceneID: " + sceneID + "\n" +
            "Selected CSV files: " + selectedFiles.Count + "\n" +
            "Valid trajectories: " + trajectories.Count + "\n" +
            "Unique nodes/targets: " + aggregatedPointMap.Count + "\n" +
            "Files without valid points: " + emptyFileCount;

        Debug.Log(statusMessage);
    }

    private List<PathFileSelection> ResolveSelectedPathFiles()
    {
        List<PathFileSelection> result = new List<PathFileSelection>();
        HashSet<string> addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StringBuilder warningBuilder = new StringBuilder();

        for (int i = 0; i < taskSelections.Count; i++)
        {
            TaskPathSelection selection = taskSelections[i];
            int taskOrder = Mathf.Max(1, selection.taskOrder);

            string folder = Path.Combine(
                Application.streamingAssetsPath,
                Path.Combine(pathRootFolderName, "Task" + taskOrder)
            );
            folder = Path.Combine(folder, pathSubFolderName);

            if (!Directory.Exists(folder))
            {
                warningBuilder.AppendLine("Task" + taskOrder + " folder not found: " + folder);
                continue;
            }

            string[] files = Directory.GetFiles(folder, "*.csv", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            if (files.Length == 0)
            {
                warningBuilder.AppendLine("Task" + taskOrder + " contains no CSV files.");
                continue;
            }

            List<int> selectedIndices;

            if (selection.loadAllCsv)
            {
                selectedIndices = new List<int>();
                for (int fileIndex = 1; fileIndex <= files.Length; fileIndex++)
                {
                    selectedIndices.Add(fileIndex);
                }
            }
            else
            {
                selectedIndices = ParseIndexExpression(selection.csvIndices, files.Length);
                if (selectedIndices.Count == 0)
                {
                    warningBuilder.AppendLine(
                        "Task" + taskOrder + " has no valid CSV indices: " + selection.csvIndices
                    );
                    continue;
                }
            }

            for (int j = 0; j < selectedIndices.Count; j++)
            {
                int oneBasedIndex = selectedIndices[j];
                string fullPath = files[oneBasedIndex - 1];

                if (!addedPaths.Add(fullPath))
                {
                    continue;
                }

                PathFileSelection item = new PathFileSelection();
                item.taskOrder = taskOrder;
                item.csvIndex = oneBasedIndex;
                item.fullPath = fullPath;
                result.Add(item);
            }
        }

        if (warningBuilder.Length > 0)
        {
            statusMessage = warningBuilder.ToString().TrimEnd();
        }
        else
        {
            statusMessage = "Path selection resolved.";
        }

        return result;
    }

    private List<int> ParseIndexExpression(string expression, int maxIndex)
    {
        SortedSet<int> result = new SortedSet<int>();

        if (StringIsNullOrWhiteSpace(expression) || maxIndex <= 0)
        {
            return new List<int>();
        }

        string normalized = expression
            .Replace('，', ',')
            .Replace('；', ',')
            .Replace(';', ',')
            .Replace(' ', ',');

        string[] tokens = normalized.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

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

                if (int.TryParse(token.Substring(0, dashIndex).Trim(), out start) &&
                    int.TryParse(token.Substring(dashIndex + 1).Trim(), out end))
                {
                    if (start > end)
                    {
                        int temp = start;
                        start = end;
                        end = temp;
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
                if (int.TryParse(token, out index) && index >= 1 && index <= maxIndex)
                {
                    result.Add(index);
                }
            }
        }

        return new List<int>(result);
    }

    private string ResolveSceneID()
    {
        if (!StringIsNullOrWhiteSpace(sceneIDOverride))
        {
            return NormalizeID(sceneIDOverride);
        }

        return ExtractDigits(SceneManager.GetActiveScene().name);
    }

    private void LoadNodePositions()
    {
        nodePosMap.Clear();

        string path = Path.Combine(Application.streamingAssetsPath, nodeInfoRelativePath);

        if (!File.Exists(path))
        {
            Debug.LogWarning("NodeInfo not found: " + path);
            return;
        }

        List<List<string>> table = ReadCsv(path);
        if (table.Count < 2)
        {
            return;
        }

        List<string> header = table[0];

        int idxScene = FindColumnIndex(header, "SceneID");
        int idxNode = FindColumnIndex(header, "NodeID");
        int idxX = FindColumnIndex(header, "X");
        int idxZ = FindColumnIndex(header, "Z");

        for (int i = 1; i < table.Count; i++)
        {
            List<string> row = table[i];

            string sceneID = NormalizeID(SafeGet(row, idxScene));
            string nodeID = NormalizeID(SafeGet(row, idxNode));

            if (StringIsNullOrWhiteSpace(sceneID) || StringIsNullOrWhiteSpace(nodeID))
            {
                continue;
            }

            Vector3 pos = new Vector3(
                ReadFloat(row, idxX, 0f),
                groundY,
                ReadFloat(row, idxZ, 0f)
            );

            nodePosMap[sceneID + "_N_" + nodeID] = pos;
        }
    }

    private void LoadTargetPositions()
    {
        targetPosMap.Clear();

        string path = Path.Combine(Application.streamingAssetsPath, targetInfoRelativePath);

        if (!File.Exists(path))
        {
            Debug.LogWarning("Target positions not found: " + path);
            return;
        }

        List<List<string>> table = ReadCsv(path);
        if (table.Count < 2)
        {
            return;
        }

        List<string> header = table[0];

        int idxScene = FindColumnIndex(header, "SceneID");
        int idxTarget = FindColumnIndex(header, "TargetID");
        int idxX = FindColumnIndex(header, "Position_x");
        int idxZ = FindColumnIndex(header, "Position_z");

        for (int i = 1; i < table.Count; i++)
        {
            List<string> row = table[i];

            string sceneID = NormalizeID(SafeGet(row, idxScene));
            string targetID = NormalizeID(SafeGet(row, idxTarget));

            if (StringIsNullOrWhiteSpace(sceneID) || StringIsNullOrWhiteSpace(targetID))
            {
                continue;
            }

            Vector3 pos = new Vector3(
                ReadFloat(row, idxX, 0f),
                groundY,
                ReadFloat(row, idxZ, 0f)
            );

            targetPosMap[sceneID + "_T_" + targetID] = pos;
        }
    }

    private List<PathPoint> ReadPathCsv(string path, string sceneID)
    {
        List<PathPoint> result = new List<PathPoint>();

        List<List<string>> table = ReadCsv(path);
        if (table.Count < 2)
        {
            return result;
        }

        List<string> header = table[0];

        int idxNode = FindColumnIndex(header, "NodeID");
        int idxTarget = FindColumnIndex(header, "TargetID");
        int idxDT = FindColumnIndex(header, "DecTime(s)");

        if (idxNode < 0 || idxTarget < 0)
        {
            Debug.LogWarning("Required NodeID/TargetID columns not found in: " + path);
            return result;
        }

        for (int i = 1; i < table.Count; i++)
        {
            List<string> row = table[i];

            int nodeID = ReadInt(row, idxNode, 0);
            int targetID = ReadInt(row, idxTarget, 0);
            float dt = ReadFloat(row, idxDT, 0f);

            bool isNode = nodeID != 0 && targetID == 0;
            bool isTarget = nodeID == 0 && targetID != 0;

            if (!isNode && !isTarget)
            {
                continue;
            }

            string key = isNode
                ? sceneID + "_N_" + nodeID
                : sceneID + "_T_" + targetID;

            Vector3 pos;

            if (isNode)
            {
                if (!nodePosMap.TryGetValue(key, out pos))
                {
                    Debug.LogWarning("Node position not found: " + key);
                    continue;
                }
            }
            else
            {
                if (!targetPosMap.TryGetValue(key, out pos))
                {
                    Debug.LogWarning("Target position not found: " + key);
                    continue;
                }
            }

            PathPoint point = new PathPoint();
            point.pointKey = key;
            point.isTarget = isTarget;
            point.nodeID = nodeID;
            point.targetID = targetID;
            point.decisionTime = dt;
            point.position = pos;
            point.order = result.Count + 1;

            result.Add(point);
        }

        return result;
    }

    private void AddTrajectoryToAggregation(TrajectoryData trajectory)
    {
        for (int i = 0; i < trajectory.points.Count; i++)
        {
            PathPoint point = trajectory.points[i];
            AggregatedPointData aggregated;

            if (!aggregatedPointMap.TryGetValue(point.pointKey, out aggregated))
            {
                aggregated = new AggregatedPointData();
                aggregated.pointKey = point.pointKey;
                aggregated.isTarget = point.isTarget;
                aggregated.nodeID = point.nodeID;
                aggregated.targetID = point.targetID;
                aggregated.position = point.position;
                aggregated.decisionTimes = new List<float>();
                aggregatedPointMap.Add(point.pointKey, aggregated);
            }

            aggregated.decisionTimes.Add(point.decisionTime);
        }
    }

    private void FinalizeAggregatedDecisionTimes()
    {
        foreach (KeyValuePair<string, AggregatedPointData> pair in aggregatedPointMap)
        {
            AggregatedPointData point = pair.Value;
            point.medianDecisionTime = CalculateMedian(point.decisionTimes);
            point.sampleCount = point.decisionTimes.Count;
        }
    }

    private float CalculateMedian(List<float> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0f;
        }

        List<float> sorted = new List<float>(values);
        sorted.Sort();

        int middle = sorted.Count / 2;

        if (sorted.Count % 2 == 1)
        {
            return sorted[middle];
        }

        return (sorted[middle - 1] + sorted[middle]) * 0.5f;
    }

    private void DrawAllObjects()
    {
        labelPositionCountMap.Clear();
        rootObj = new GameObject("Generated_MultiPathTrajectory");

        for (int i = 0; i < trajectories.Count; i++)
        {
            DrawTrajectory(trajectories[i], i, trajectories.Count);
        }

        if (showMarkers)
        {
            foreach (KeyValuePair<string, AggregatedPointData> pair in aggregatedPointMap)
            {
                CreateAggregatedMarker(pair.Value);
            }
        }
    }

    private void DrawTrajectory(TrajectoryData trajectory, int trajectoryIndex, int trajectoryCount)
    {
        GameObject trajectoryRoot = new GameObject(
            "Task" + trajectory.taskOrder +
            "_Index" + trajectory.csvIndex +
            "_" + Path.GetFileNameWithoutExtension(trajectory.fileName)
        );
        trajectoryRoot.transform.SetParent(rootObj.transform, false);

        Color transparentLineColor = WithAlpha(lineColor, lineColor.a * trajectoryAlpha);
        Color transparentArrowColor = WithAlpha(arrowColor, arrowColor.a * trajectoryAlpha);

        Material lineMaterial = CreateColorMaterial(transparentLineColor);
        Material arrowMaterial = CreateColorMaterial(transparentArrowColor);

        if (showTrajectory)
        {
            CreateTrajectoryLines(
                trajectory,
                trajectoryIndex,
                trajectoryCount,
                trajectoryRoot.transform,
                lineMaterial,
                transparentLineColor
            );
        }

        if (showOrderLabels)
        {
            for (int i = 0; i < trajectory.points.Count; i++)
            {
                CreateOrderLabel(
                    trajectory.points[i],
                    trajectoryIndex,
                    trajectoryCount,
                    trajectoryRoot.transform
                );
            }
        }

        if (showDirectionArrows)
        {
            CreateDirectionArrows(
                trajectory,
                trajectoryIndex,
                trajectoryCount,
                trajectoryRoot.transform,
                arrowMaterial,
                transparentArrowColor
            );
        }
    }

    private void CreateTrajectoryLines(
        TrajectoryData trajectory,
        int trajectoryIndex,
        int trajectoryCount,
        Transform parent,
        Material material,
        Color color
    )
    {
        for (int i = 0; i < trajectory.points.Count - 1; i++)
        {
            Vector3 a = GetOffsetPathPointForSegment(
                trajectory,
                i,
                true,
                trajectoryIndex,
                trajectoryCount
            );
            Vector3 b = GetOffsetPathPointForSegment(
                trajectory,
                i,
                false,
                trajectoryIndex,
                trajectoryCount
            );

            GameObject lineObj = new GameObject("Path_Line_" + (i + 1) + "_to_" + (i + 2));
            lineObj.transform.SetParent(parent, false);

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.sharedMaterial = material;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 2;

            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }
    }

    private Vector3 GetSegmentSideOffset(
        TrajectoryData trajectory,
        int segmentIndex,
        int trajectoryIndex,
        int trajectoryCount
    )
    {
        if (!separateOverlappingLines || trajectoryCount <= 1)
        {
            return Vector3.zero;
        }

        if (segmentIndex < 0 || segmentIndex >= trajectory.points.Count - 1)
        {
            return Vector3.zero;
        }

        Vector3 a = trajectory.points[segmentIndex].position;
        Vector3 b = trajectory.points[segmentIndex + 1].position;

        Vector3 dir = b - a;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;

        float centeredLaneIndex = trajectoryIndex - (trajectoryCount - 1) * 0.5f;
        return side * lineSideOffset * centeredLaneIndex;
    }

    private Vector3 GetOffsetPathPointForSegment(
        TrajectoryData trajectory,
        int segmentIndex,
        bool isStart,
        int trajectoryIndex,
        int trajectoryCount
    )
    {
        if (segmentIndex < 0 || segmentIndex >= trajectory.points.Count - 1)
        {
            return Vector3.zero;
        }

        Vector3 basePos = isStart
            ? trajectory.points[segmentIndex].position
            : trajectory.points[segmentIndex + 1].position;

        return basePos + GetSegmentSideOffset(
            trajectory,
            segmentIndex,
            trajectoryIndex,
            trajectoryCount
        );
    }

    private void CreateAggregatedMarker(AggregatedPointData point)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = GetPointName(point) +
            "_MedianDT_" + point.medianDecisionTime.ToString("F3", CultureInfo.InvariantCulture) +
            "_N_" + point.sampleCount;
        marker.transform.SetParent(rootObj.transform, false);
        marker.transform.position = point.position;
        marker.transform.localScale = new Vector3(markerDiameter, markerThickness, markerDiameter);

        Collider col = marker.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer rendererComponent = marker.GetComponent<Renderer>();
        if (rendererComponent != null)
        {
            Color color = GetPointColor(point);
            rendererComponent.material = CreateColorMaterial(color);
        }
    }

    private void CreateOrderLabel(
        PathPoint point,
        int trajectoryIndex,
        int trajectoryCount,
        Transform parent
    )
    {
        GameObject labelObj = new GameObject("OrderLabel_" + point.order);
        labelObj.transform.SetParent(parent, false);

        Vector3 trajectoryOffset = GetPointLaneOffset(
            point.position,
            trajectoryIndex,
            trajectoryCount
        );
        Vector3 repeatedOffset = GetRepeatedLabelOffset(point.position + trajectoryOffset);

        labelObj.transform.position =
            point.position + trajectoryOffset + repeatedOffset + Vector3.up * orderLabelHeight;

        TextMesh tm = labelObj.AddComponent<TextMesh>();
        tm.text = point.order.ToString();
        tm.fontSize = orderLabelFontSize;
        tm.characterSize = orderLabelCharacterSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = WithAlpha(orderLabelColor, orderLabelColor.a * Mathf.Max(trajectoryAlpha, 0.55f));

        labelObj.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
    }

    private Vector3 GetPointLaneOffset(Vector3 pointPosition, int trajectoryIndex, int trajectoryCount)
    {
        if (!separateOverlappingLines || trajectoryCount <= 1)
        {
            return Vector3.zero;
        }

        float centeredLaneIndex = trajectoryIndex - (trajectoryCount - 1) * 0.5f;
        return Vector3.right * lineSideOffset * centeredLaneIndex;
    }

    private void CreateDirectionArrows(
        TrajectoryData trajectory,
        int trajectoryIndex,
        int trajectoryCount,
        Transform parent,
        Material material,
        Color color
    )
    {
        if (trajectory.points.Count < 2)
        {
            return;
        }

        for (int i = 0; i < trajectory.points.Count - 1; i++)
        {
            Vector3 a = GetOffsetPathPointForSegment(
                trajectory,
                i,
                true,
                trajectoryIndex,
                trajectoryCount
            );
            Vector3 b = GetOffsetPathPointForSegment(
                trajectory,
                i,
                false,
                trajectoryIndex,
                trajectoryCount
            );

            Vector3 dir = b - a;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 position = Vector3.Lerp(a, b, arrowPositionRatio);
            position.y = groundY + arrowHeight;

            GameObject arrow = CreateTriangleArrowObject(
                "DirectionArrow_" + (i + 1) + "_to_" + (i + 2)
            );
            arrow.transform.SetParent(parent, false);
            arrow.transform.position = position;
            arrow.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            arrow.transform.localScale = Vector3.one * arrowSize;

            Renderer rendererComponent = arrow.GetComponent<Renderer>();
            if (rendererComponent != null)
            {
                rendererComponent.sharedMaterial = material;
            }
        }
    }

    private Color GetPointColor(AggregatedPointData point)
    {
        if (point.isTarget &&
            useTargetColorWhenDecTimeIsZero &&
            point.medianDecisionTime <= 0.0001f)
        {
            return targetColor;
        }

        float dt = Mathf.Clamp(point.medianDecisionTime, minDecisionTime, maxDecisionTime);

        if (dt <= midDecisionTime)
        {
            float t = Mathf.InverseLerp(minDecisionTime, midDecisionTime, dt);
            return Color.Lerp(shortDecisionColor, middleDecisionColor, t);
        }

        float upperT = Mathf.InverseLerp(midDecisionTime, maxDecisionTime, dt);
        return Color.Lerp(middleDecisionColor, longDecisionColor, upperT);
    }

    private string GetPointName(AggregatedPointData point)
    {
        if (point.isTarget)
        {
            return "Target" + point.targetID;
        }

        return "Node" + point.nodeID;
    }

    public void ClearPaths()
    {
        if (rootObj != null)
        {
            Destroy(rootObj);
            rootObj = null;
        }

        for (int i = 0; i < generatedMaterials.Count; i++)
        {
            if (generatedMaterials[i] != null)
            {
                Destroy(generatedMaterials[i]);
            }
        }

        generatedMaterials.Clear();
        trajectories.Clear();
        aggregatedPointMap.Clear();
        labelPositionCountMap.Clear();
    }

    private Material CreateColorMaterial(Color color)
    {
        bool transparent = color.a < 0.999f;
        Shader shader = null;

        if (transparent)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.color = color;

        if (transparent && shader != null && shader.name == "Standard")
        {
            ConfigureStandardMaterialForTransparency(material);
        }

        generatedMaterials.Add(material);
        return material;
    }

    private void ConfigureStandardMaterialForTransparency(Material material)
    {
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
    }

    private Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    private List<List<string>> ReadCsv(string path)
    {
        List<List<string>> table = new List<List<string>>();

        try
        {
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    table.Add(ParseCsvLine(line));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to read CSV: " + path + "\n" + ex.Message);
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
                sb.Length = 0;
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
    }

    private int FindColumnIndex(List<string> header, string columnName)
    {
        for (int i = 0; i < header.Count; i++)
        {
            string value = header[i];

            if (value == null)
            {
                continue;
            }

            value = value.Trim().Trim('\uFEFF');

            if (string.Equals(value, columnName, StringComparison.OrdinalIgnoreCase))
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

    private float ReadFloat(List<string> row, int index, float defaultValue)
    {
        float value;

        if (float.TryParse(
            SafeGet(row, index),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        ))
        {
            return value;
        }

        return defaultValue;
    }

    private int ReadInt(List<string> row, int index, int defaultValue)
    {
        int value;

        if (int.TryParse(SafeGet(row, index), out value))
        {
            return value;
        }

        return defaultValue;
    }

    private string NormalizeID(string raw)
    {
        if (StringIsNullOrWhiteSpace(raw))
        {
            return "";
        }

        int value;
        if (int.TryParse(raw.Trim(), out value))
        {
            return value.ToString();
        }

        return raw.Trim();
    }

    private string ExtractDigits(string text)
    {
        if (StringIsNullOrWhiteSpace(text))
        {
            return "";
        }

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }

    private bool StringIsNullOrWhiteSpace(string text)
    {
        return text == null || text.Trim().Length == 0;
    }

    private GameObject CreateTriangleArrowObject(string objectName)
    {
        GameObject obj = new GameObject(objectName);

        MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
        obj.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(0f, 0f, 0.6f),
            new Vector3(-0.35f, 0f, -0.35f),
            new Vector3(0.35f, 0f, -0.35f)
        };

        int[] trianglesArray = new int[]
        {
            0, 1, 2,
            0, 2, 1
        };

        mesh.vertices = vertices;
        mesh.triangles = trianglesArray;
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;

        return obj;
    }

    private Vector3 GetRepeatedLabelOffset(Vector3 position)
    {
        string key =
            Mathf.RoundToInt(position.x * 100f) + "_" +
            Mathf.RoundToInt(position.z * 100f);

        int count;

        if (labelPositionCountMap.TryGetValue(key, out count))
        {
            labelPositionCountMap[key] = count + 1;
        }
        else
        {
            count = 0;
            labelPositionCountMap[key] = 1;
        }

        if (count == 0)
        {
            return Vector3.zero;
        }

        float radius = repeatedLabelOffsetRadius * count;
        float angle = 45f * count * Mathf.Deg2Rad;

        return new Vector3(
            Mathf.Cos(angle) * radius,
            0f,
            Mathf.Sin(angle) * radius
        );
    }

    private class PathFileSelection
    {
        public int taskOrder;
        public int csvIndex;
        public string fullPath;
    }

    private class TrajectoryData
    {
        public int taskOrder;
        public int csvIndex;
        public string fileName;
        public string fullPath;
        public List<PathPoint> points;
    }

    private class PathPoint
    {
        public string pointKey;
        public int order;
        public bool isTarget;
        public int nodeID;
        public int targetID;
        public float decisionTime;
        public Vector3 position;
    }

    private class AggregatedPointData
    {
        public string pointKey;
        public bool isTarget;
        public int nodeID;
        public int targetID;
        public Vector3 position;
        public List<float> decisionTimes;
        public float medianDecisionTime;
        public int sampleCount;
    }
}
