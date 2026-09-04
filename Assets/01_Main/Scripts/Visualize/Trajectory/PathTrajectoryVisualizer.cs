using UnityEngine;
using UnityEngine.SceneManagement;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

public class PathTrajectoryVisualizer : MonoBehaviour
{
    [Header("Data Paths")]
    public string pathRootFolderName = "Reorder";
    public string pathSubFolderName = "Path";
    public string nodeInfoRelativePath = "ReadData/NodeInfo.csv";
    public string targetInfoRelativePath = "SaveData/AllTargetPositions.csv";

    public int taskOrder = 1;
    public int csvFileIndex = 1;
    public string overridePathCsvFullPath = "";

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

    [Header("Panel")]
    public Rect panelRect = new Rect(520, 20, 420, 560);

    private Vector2 scrollPos = Vector2.zero;

    private GameObject rootObj;
    private LineRenderer pathLine;

    private Dictionary<string, Vector3> nodePosMap = new Dictionary<string, Vector3>();
    private Dictionary<string, Vector3> targetPosMap = new Dictionary<string, Vector3>();

    private List<PathPoint> pathPoints = new List<PathPoint>();

    private string statusMessage = "Ready.";

    public float repeatedLabelOffsetRadius = 1.5f;

    private Dictionary<string, int> labelPositionCountMap = new Dictionary<string, int>();
    [Range(0.1f, 0.95f)]
    public float arrowPositionRatio = 0.82f;
    public bool separateOverlappingLines = true;
    public float lineSideOffset = 0.18f;

    private void OnGUI()
    {
        if (!showControlPanel)
        {
            return;
        }

        panelRect = GUI.Window(20260531, panelRect, DrawPanel, "Path Trajectory Visualizer");
    }

    private void DrawPanel(int windowID)
    {
        scrollPos = GUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("Path Data", GUI.skin.box);

        taskOrder = IntField("Task Order", taskOrder);
        csvFileIndex = IntField("CSV File Index", csvFileIndex);

        GUILayout.Label("Override Path CSV Full Path:");
        overridePathCsvFullPath = GUILayout.TextField(overridePathCsvFullPath);

        GUILayout.Label("Scene ID Override:");
        sceneIDOverride = GUILayout.TextField(sceneIDOverride);

        GUILayout.Space(8);
        GUILayout.Label("Display", GUI.skin.box);

        showTrajectory = GUILayout.Toggle(showTrajectory, "Show Trajectory Line");
        showMarkers = GUILayout.Toggle(showMarkers, "Show Markers");
        showOrderLabels = GUILayout.Toggle(showOrderLabels, "Show Order Labels");
        showDirectionArrows = GUILayout.Toggle(showDirectionArrows, "Show Direction Arrows");

        markerDiameter = FloatField("Marker Diameter", markerDiameter);
        markerThickness = FloatField("Marker Thickness", markerThickness);
        lineWidth = FloatField("Line Width", lineWidth);
        groundY = FloatField("Ground Y", groundY);
        separateOverlappingLines = GUILayout.Toggle(separateOverlappingLines, "Separate Overlapping Lines");
        lineSideOffset = FloatField("Line Side Offset", lineSideOffset);

        GUILayout.Space(8);
        GUILayout.Label("Order Labels", GUI.skin.box);

        orderLabelHeight = FloatField("Label Height", orderLabelHeight);
        orderLabelFontSize = IntField("Label Font Size", orderLabelFontSize);
        orderLabelCharacterSize = FloatField("Label Character Size", orderLabelCharacterSize);

        GUILayout.Space(8);
        GUILayout.Label("Direction Arrows", GUI.skin.box);

        arrowSize = FloatField("Arrow Size", arrowSize);
        arrowHeight = FloatField("Arrow Height", arrowHeight);

        GUILayout.Space(8);
        GUILayout.Label("Decision Time Thresholds", GUI.skin.box);

        minDecisionTime = FloatField("Min DT", minDecisionTime);
        midDecisionTime = FloatField("Mid DT", midDecisionTime);
        maxDecisionTime = FloatField("Max DT", maxDecisionTime);

        GUILayout.Space(10);

        if (GUILayout.Button("Load And Draw Path", GUILayout.Height(30)))
        {
            LoadAndDrawPath();
        }

        if (GUILayout.Button("Clear Path", GUILayout.Height(28)))
        {
            ClearPath();
        }

        GUILayout.Space(8);
        GUILayout.Label("Status:");
        GUILayout.TextArea(statusMessage, GUILayout.Height(90));

        GUILayout.EndScrollView();

        GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));
    }

    private int IntField(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(170));

        int parsed;
        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(100));

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
        GUILayout.Label(label + ":", GUILayout.Width(170));

        float parsed;
        string text = GUILayout.TextField(value.ToString("G4", CultureInfo.InvariantCulture), GUILayout.Width(100));

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
        }

        GUILayout.EndHorizontal();
        return value;
    }

    public void LoadAndDrawPath()
    {
        ClearPath();

        string sceneID = ResolveSceneID();

        LoadNodePositions();
        LoadTargetPositions();

        string pathCsv = GetPathCsvPath();

        if (!File.Exists(pathCsv))
        {
            statusMessage = "Path CSV not found:\n" + pathCsv;
            Debug.LogError(statusMessage);
            return;
        }

        pathPoints = ReadPathCsv(pathCsv, sceneID);

        if (pathPoints.Count == 0)
        {
            statusMessage = "No valid path points loaded.";
            Debug.LogWarning(statusMessage);
            return;
        }

        DrawPathObjects();

        statusMessage =
            "Path loaded successfully.\n" +
            "SceneID: " + sceneID + "\n" +
            "File: " + Path.GetFileName(pathCsv) + "\n" +
            "Points: " + pathPoints.Count;

        Debug.Log(statusMessage);
    }

    private string GetPathCsvPath()
    {
        if (!StringIsNullOrWhiteSpace(overridePathCsvFullPath))
        {
            return overridePathCsvFullPath.Trim();
        }

        string folder = Path.Combine(
            Application.streamingAssetsPath,
            Path.Combine(pathRootFolderName, "Task" + taskOrder)
        );

        folder = Path.Combine(folder, pathSubFolderName);

        if (!Directory.Exists(folder))
        {
            return folder;
        }

        string[] files = Directory.GetFiles(folder, "*.csv", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        if (files.Length == 0)
        {
            return folder + " | No csv files";
        }

        int index = Mathf.Clamp(csvFileIndex, 1, files.Length) - 1;
        return files[index];
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
        if (table.Count < 2) return;

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
        if (table.Count < 2) return;

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
        if (table.Count < 2) return result;

        List<string> header = table[0];

        int idxNode = FindColumnIndex(header, "NodeID");
        int idxTarget = FindColumnIndex(header, "TargetID");
        int idxDT = FindColumnIndex(header, "DecTime(s)");

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

    private void DrawPathObjects()
    {
        labelPositionCountMap.Clear();
        rootObj = new GameObject("Generated_PathTrajectory");

        if (showTrajectory)
        {
            CreatePathLine();
        }

        for (int i = 0; i < pathPoints.Count; i++)
        {
            PathPoint p = pathPoints[i];

            if (showMarkers)
            {
                CreateMarker(p);
            }

            if (showOrderLabels)
            {
                CreateOrderLabel(p);
            }
        }

        if (showDirectionArrows)
        {
            CreateDirectionArrows();
        }
    }

    private void CreatePathLine()
    {
        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            Vector3 a = GetOffsetPathPointForSegment(i, true);
            Vector3 b = GetOffsetPathPointForSegment(i, false);

            GameObject lineObj = new GameObject("Path_Line_" + (i + 1) + "_to_" + (i + 2));
            lineObj.transform.SetParent(rootObj.transform, false);

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;

            lr.material = CreateUnlitMaterial(lineColor);
            lr.startColor = lineColor;
            lr.endColor = lineColor;

            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }
    }
    private Vector3 GetSegmentSideOffset(int segmentIndex)
    {
        if (!separateOverlappingLines)
        {
            return Vector3.zero;
        }

        if (segmentIndex < 0 || segmentIndex >= pathPoints.Count - 1)
        {
            return Vector3.zero;
        }

        Vector3 a = pathPoints[segmentIndex].position;
        Vector3 b = pathPoints[segmentIndex + 1].position;

        Vector3 dir = b - a;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        dir.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;

        return side * lineSideOffset;
    }

    private Vector3 GetOffsetPathPointForSegment(int segmentIndex, bool isStart)
    {
        if (segmentIndex < 0 || segmentIndex >= pathPoints.Count - 1)
        {
            return Vector3.zero;
        }

        Vector3 basePos = isStart
            ? pathPoints[segmentIndex].position
            : pathPoints[segmentIndex + 1].position;

        return basePos + GetSegmentSideOffset(segmentIndex);
    }


    private void CreateMarker(PathPoint p)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = GetPointName(p);
        marker.transform.SetParent(rootObj.transform, false);
        marker.transform.position = p.position;
        marker.transform.localScale = new Vector3(markerDiameter, markerThickness, markerDiameter);

        Collider col = marker.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer r = marker.GetComponent<Renderer>();
        if (r != null)
        {
            Color c = GetPointColor(p);
            r.material = CreateUnlitMaterial(c);
        }
    }

    private void CreateOrderLabel(PathPoint p)
    {
        GameObject labelObj = new GameObject("OrderLabel_" + p.order);
        labelObj.transform.SetParent(rootObj.transform, false);

        Vector3 offset = GetRepeatedLabelOffset(p.position);
        labelObj.transform.position = p.position + offset + Vector3.up * orderLabelHeight;

        TextMesh tm = labelObj.AddComponent<TextMesh>();
        tm.text = p.order.ToString();
        tm.fontSize = orderLabelFontSize;
        tm.characterSize = orderLabelCharacterSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = orderLabelColor;

        labelObj.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
    }

    private void CreateDirectionArrows()
    {
        if (pathPoints.Count < 2)
        {
            return;
        }

        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            Vector3 a = GetOffsetPathPointForSegment(i, true);
            Vector3 b = GetOffsetPathPointForSegment(i, false);

            Vector3 dir = b - a;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 mid = Vector3.Lerp(a, b, arrowPositionRatio);
            mid.y = groundY + 0.03f;

            GameObject arrow = CreateTriangleArrowObject("DirectionArrow_" + (i + 1) + "_to_" + (i + 2));
            arrow.name = "DirectionArrow_" + (i + 1) + "_to_" + (i + 2);
            arrow.transform.SetParent(rootObj.transform, false);

            arrow.transform.position = mid;

            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            arrow.transform.rotation = rot;
            arrow.transform.localScale = Vector3.one * arrowSize;

            Collider col = arrow.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Renderer r = arrow.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = CreateUnlitMaterial(arrowColor);
            }
        }
    }

    private Color GetPointColor(PathPoint p)
    {
        if (p.isTarget && useTargetColorWhenDecTimeIsZero && p.decisionTime <= 0.0001f)
        {
            return targetColor;
        }

        float dt = Mathf.Clamp(p.decisionTime, minDecisionTime, maxDecisionTime);

        if (dt <= midDecisionTime)
        {
            float t = Mathf.InverseLerp(minDecisionTime, midDecisionTime, dt);
            return Color.Lerp(shortDecisionColor, middleDecisionColor, t);
        }
        else
        {
            float t = Mathf.InverseLerp(midDecisionTime, maxDecisionTime, dt);
            return Color.Lerp(middleDecisionColor, longDecisionColor, t);
        }
    }

    private string GetPointName(PathPoint p)
    {
        if (p.isTarget)
        {
            return "Target" + p.targetID;
        }

        return "Node" + p.nodeID;
    }

    public void ClearPath()
    {
        if (rootObj != null)
        {
            Destroy(rootObj);
            rootObj = null;
        }

        pathPoints.Clear();
        pathLine = null;
    }

    private Material CreateUnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

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
            string h = header[i];

            if (h == null) continue;

            h = h.Trim().Trim('\uFEFF');

            if (string.Equals(h, columnName, StringComparison.OrdinalIgnoreCase))
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
        float v;

        if (float.TryParse(SafeGet(row, index), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
        {
            return v;
        }

        return defaultValue;
    }

    private int ReadInt(List<string> row, int index, int defaultValue)
    {
        int v;

        if (int.TryParse(SafeGet(row, index), out v))
        {
            return v;
        }

        return defaultValue;
    }

    private string NormalizeID(string raw)
    {
        if (StringIsNullOrWhiteSpace(raw)) return "";

        int v;
        if (int.TryParse(raw.Trim(), out v))
        {
            return v.ToString();
        }

        return raw.Trim();
    }

    private string ExtractDigits(string text)
    {
        if (StringIsNullOrWhiteSpace(text)) return "";

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

    private class PathPoint
    {
        public int order;
        public bool isTarget;
        public int nodeID;
        public int targetID;
        public float decisionTime;
        public Vector3 position;
    }

    private GameObject CreateTriangleArrowObject(string name)
    {
        GameObject obj = new GameObject(name);

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        MeshRenderer mr = obj.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();

        Vector3[] vertices = new Vector3[]
        {
        new Vector3(0f, 0f, 0.6f),
        new Vector3(-0.35f, 0f, -0.35f),
        new Vector3(0.35f, 0f, -0.35f)
        };

        int[] triangles = new int[]
        {
        0, 1, 2,
        0, 2, 1
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        mf.mesh = mesh;
        mr.material = CreateUnlitMaterial(arrowColor);

        return obj;
    }

    private Vector3 GetRepeatedLabelOffset(Vector3 pos)
    {
        string key =
            Mathf.RoundToInt(pos.x * 100f) + "_" +
            Mathf.RoundToInt(pos.z * 100f);

        int count = 0;

        if (labelPositionCountMap.ContainsKey(key))
        {
            count = labelPositionCountMap[key];
            labelPositionCountMap[key] = count + 1;
        }
        else
        {
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
}