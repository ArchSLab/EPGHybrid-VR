using UnityEngine;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

public class GazeSurfaceSplatHeatmapVisualizer : MonoBehaviour
{
    [Header("Data Path Settings")]
    public string processedRoot =
        @"Assets/StreamingAssets/Reorder_GazeHit";

    public int taskOrder = 1;
    public int csvFileIndex = 1;

    public string overrideCsvFullPath = "";

    [Header("Heatmap Display Settings")]
    public bool showControlPanel = true;
    public Rect panelRect = new Rect(20, 20, 300, 500);

    public float splatSize = 0.28f;
    public float splatAlpha = 0.38f;
    public float normalOffset = 0.01f;

    public int sampleStep = 1;
    public int maxSplats = 2500;

    public bool useDensityColor = true;
    public float densityRadius = 0.35f;
    public bool autoNormalizeDensity = true;
    public float manualMaxDensity = 25f;

    public Color coldColor = new Color(0f, 1f, 0f, 1f);
    public Color midColor = new Color(1f, 1f, 0f, 1f);
    public Color hotColor = new Color(1f, 0f, 0f, 1f);

    public float nodeListHeight = 320f;

    // Overall panel scroll
    private Vector2 panelScrollPosition = Vector2.zero;

    private bool panelMinimized = false;
    private bool panelClosed = false;

    // Node stay list scroll
    private Vector2 scrollPosition = Vector2.zero;

    private List<GazeRecord> records = new List<GazeRecord>();
    private List<NodeStaySegment> nodeStays = new List<NodeStaySegment>();

    private GameObject heatmapRoot;
    private Material splatMaterial;
    private Mesh splatMesh;
    private Texture2D splatTexture;
    private MaterialPropertyBlock propertyBlock;

    private string loadedCsvPath = "";
    private string statusMessage = "No data loaded.";

    private void OnGUI()
    {
        if (!showControlPanel || panelClosed)
        {
            return;
        }

        panelRect = GUI.Window(20260501, panelRect, DrawPanel, "");
    }

    private void DrawPanel(int windowID)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(28));

        GUILayout.Label("Surface Splat Heatmap", GUILayout.Width(190));
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
            GUI.DragWindow(new Rect(0, 0, panelRect.width, 28));
            return;
        }

        panelScrollPosition = GUILayout.BeginScrollView(
                panelScrollPosition,
            false,
            true,
            GUILayout.Width(panelRect.width - 15),
            GUILayout.Height(panelRect.height - 30)
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

        if (GUILayout.Button("Clear Heatmap", GUILayout.Height(28)))
        {
            ClearHeatmap();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("Heatmap Settings", GUI.skin.box);

        splatSize = FloatFieldWithSlider("Splat Size", splatSize, 0.03f, 1.5f);
        splatAlpha = FloatFieldWithSlider("Alpha", splatAlpha, 0.01f, 1.0f);
        normalOffset = FloatFieldWithSlider("Normal Offset", normalOffset, 0.000f, 0.08f);

        sampleStep = IntField("Sample Step", sampleStep, 1, 999);
        maxSplats = IntField("Max Splats", maxSplats, 0, 200000);

        useDensityColor = GUILayout.Toggle(useDensityColor, "Use Density Color");

        if (useDensityColor)
        {
            densityRadius = FloatFieldWithSlider("Density Radius", densityRadius, 0.03f, 2.0f);
            autoNormalizeDensity = GUILayout.Toggle(autoNormalizeDensity, "Auto Normalize Density");

            if (!autoNormalizeDensity)
            {
                manualMaxDensity = FloatFieldWithSlider("Manual Max Density", manualMaxDensity, 1f, 200f);
            }
        }

        GUILayout.Space(8);
        GUILayout.Label("Color Settings", GUI.skin.box);

        coldColor = DrawColorSliders("Cold Color", coldColor);
        midColor = DrawColorSliders("Mid Color", midColor);
        hotColor = DrawColorSliders("Hot Color", hotColor);

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

        if (GUILayout.Button("Generate / Refresh Heatmap", GUILayout.Height(32)))
        {
            RefreshHeatmap();
        }

        GUILayout.Space(8);
        GUILayout.Label("Node Stay Segments", GUI.skin.box);

        if (records.Count == 0)
        {
            GUILayout.Label("Click Load CSV first.");
        }
        else
        {
            GUILayout.Label("Loaded: " + Path.GetFileName(loadedCsvPath));
            GUILayout.Label("Records: " + records.Count + " | Node stays: " + nodeStays.Count);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Node List Height:", GUILayout.Width(130));
        nodeListHeight = GUILayout.HorizontalSlider(nodeListHeight, 150f, 650f, GUILayout.Width(240));
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

            if (!StringIsNullOrWhiteSpace(seg.startTime) || !StringIsNullOrWhiteSpace(seg.endTime))
            {
                label += " | Time=" + seg.startTime + " - " + seg.endTime;
            }

            seg.selected = GUILayout.Toggle(seg.selected, label);
        }

        GUILayout.EndScrollView();

        GUILayout.Space(8);
        GUILayout.Label("Status:");
        GUILayout.TextArea(statusMessage, GUILayout.Height(80));

        GUILayout.EndScrollView();

        // Only allow dragging from the top title area,
        // otherwise it may conflict with the scroll bar.
        GUI.DragWindow(new Rect(0, 0, panelRect.width, 28));
    }

    private float FloatFieldWithSlider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(130));

        string text = GUILayout.TextField(value.ToString("G4", CultureInfo.InvariantCulture), GUILayout.Width(80));
        float parsed;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = Mathf.Clamp(parsed, min, max);
        }

        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220));
        GUILayout.EndHorizontal();

        return value;
    }

    private int IntField(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(130));

        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(80));
        int parsed;
        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Clamp(parsed, min, max);
        }

        GUILayout.EndHorizontal();

        return value;
    }

    private Color DrawColorSliders(string label, Color color)
    {
        GUILayout.Label(label);

        color.r = ColorSlider("R", color.r);
        color.g = ColorSlider("G", color.g);
        color.b = ColorSlider("B", color.b);

        Rect previewRect = GUILayoutUtility.GetRect(80, 16);
        Color oldColor = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, 1f);
        GUI.Box(previewRect, "");
        GUI.color = oldColor;

        return color;
    }

    private float ColorSlider(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(18));
        value = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.Width(260));
        GUILayout.Label(value.ToString("F2"), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        return value;
    }

    private void LoadCsvAndBuildNodeStays()
    {
        ClearHeatmap();

        records.Clear();
        nodeStays.Clear();

        string csvPath = GetTargetCsvPath();

        if (StringIsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
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

        int idxNormalX = FindColumnIndex(header, "HitNormalX");
        int idxNormalY = FindColumnIndex(header, "HitNormalY");
        int idxNormalZ = FindColumnIndex(header, "HitNormalZ");

        int idxGazeValid = FindColumnIndex(header, "GazeValid");
        int idxGazeHit = FindColumnIndex(header, "GazeHit");

        int idxHitObjectName = FindColumnIndex(header, "HitObjectName");
        int idxHitObjectTag = FindColumnIndex(header, "HitObjectTag");

        int idxSceneTime = FindColumnIndex(header, "SceneTime");
        int idxTimestamp = FindColumnIndex(header, "Timestamp");

        if (idxNodeID < 0 ||
            idxHitX < 0 || idxHitY < 0 || idxHitZ < 0)
        {
            statusMessage =
                "CSV is missing required columns.\n" +
                "Required: NodeID, HitPointX, HitPointY, HitPointZ.\n" +
                "Recommended: HitNormalX/Y/Z, GazeValid, GazeHit.\n" +
                csvPath;

            Debug.LogError(statusMessage);
            return;
        }

        bool hasNormal =
            idxNormalX >= 0 &&
            idxNormalY >= 0 &&
            idxNormalZ >= 0;

        if (!hasNormal)
        {
            Debug.LogWarning("HitNormalX/Y/Z not found. The script will use fallback normal from gaze direction.");
        }

        for (int i = 1; i < table.Count; i++)
        {
            List<string> row = table[i];

            GazeRecord rec = new GazeRecord();
            rec.sourceRowIndex = i;

            rec.nodeID = SafeGet(row, idxNodeID);
            rec.sceneTime = idxSceneTime >= 0 ? SafeGet(row, idxSceneTime) : "";
            rec.timestamp = idxTimestamp >= 0 ? SafeGet(row, idxTimestamp) : "";

            rec.hitPoint = ReadVector3(row, idxHitX, idxHitY, idxHitZ);

            if (hasNormal)
            {
                rec.hitNormal = ReadVector3(row, idxNormalX, idxNormalY, idxNormalZ);
            }
            else
            {
                rec.hitNormal = Vector3.zero;
            }

            if (idxOriginX >= 0 && idxOriginY >= 0 && idxOriginZ >= 0)
            {
                rec.origin = ReadVector3(row, idxOriginX, idxOriginY, idxOriginZ);
            }

            if (idxDirX >= 0 && idxDirY >= 0 && idxDirZ >= 0)
            {
                rec.direction = ReadVector3(row, idxDirX, idxDirY, idxDirZ);
            }

            rec.hitObjectName = idxHitObjectName >= 0 ? SafeGet(row, idxHitObjectName) : "";
            rec.hitObjectTag = idxHitObjectTag >= 0 ? SafeGet(row, idxHitObjectTag) : "";

            rec.gazeValid = DetermineGazeValid(row, idxGazeValid, rec);
            rec.gazeHit = DetermineGazeHit(row, idxGazeHit, rec);

            if (rec.hitNormal.sqrMagnitude < 0.000001f)
            {
                if (rec.direction.sqrMagnitude > 0.000001f)
                {
                    rec.hitNormal = -rec.direction.normalized;
                }
                else
                {
                    rec.hitNormal = Vector3.up;
                }
            }
            else
            {
                rec.hitNormal.Normalize();
            }

            records.Add(rec);
        }

        BuildNodeStaySegments();

        statusMessage =
            "Loaded CSV successfully.\n" +
            "File: " + Path.GetFileName(csvPath) + "\n" +
            "Records: " + records.Count + "\n" +
            "Node stays: " + nodeStays.Count + "\n" +
            "Has HitNormal: " + hasNormal;

        Debug.Log(statusMessage);
    }

    private string GetTargetCsvPath()
    {
        if (!StringIsNullOrWhiteSpace(overrideCsvFullPath))
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
            seg.startTime = !StringIsNullOrWhiteSpace(records[startIndex].sceneTime)
                ? records[startIndex].sceneTime
                : records[startIndex].timestamp;
        }

        if (endIndex >= 0 && endIndex < records.Count)
        {
            seg.endTime = !StringIsNullOrWhiteSpace(records[endIndex].sceneTime)
                ? records[endIndex].sceneTime
                : records[endIndex].timestamp;
        }

        nodeStays.Add(seg);
    }

    private void RefreshHeatmap()
    {
        ClearHeatmap();

        if (records.Count == 0 || nodeStays.Count == 0)
        {
            statusMessage = "No loaded records. Please Load CSV first.";
            Debug.LogWarning(statusMessage);
            return;
        }

        EnsureHeatmapResources();

        List<GazeRecord> selectedHits = CollectSelectedHitRecords();

        if (selectedHits.Count == 0)
        {
            statusMessage = "No valid gaze hit records selected.";
            Debug.LogWarning(statusMessage);
            return;
        }

        float[] densities = null;
        float maxDensity = 1f;

        if (useDensityColor)
        {
            densities = ComputeDensities(selectedHits, densityRadius);
            maxDensity = GetMaxDensity(densities);

            if (!autoNormalizeDensity)
            {
                maxDensity = Mathf.Max(0.0001f, manualMaxDensity);
            }
        }

        heatmapRoot = new GameObject("SurfaceSplatHeatmap_Task" + taskOrder);
        heatmapRoot.transform.SetParent(transform, false);

        int createdCount = 0;

        for (int i = 0; i < selectedHits.Count; i++)
        {
            GazeRecord rec = selectedHits[i];

            float t = 1f;

            if (useDensityColor && densities != null)
            {
                t = Mathf.Clamp01(densities[i] / Mathf.Max(0.0001f, maxDensity));
            }

            Color color = useDensityColor ? EvaluateHeatColor(t) : hotColor;
            color.a = splatAlpha;

            CreateSplat(rec.hitPoint, rec.hitNormal, color, heatmapRoot.transform);
            createdCount++;
        }

        statusMessage =
            "Heatmap generated.\n" +
            "Selected hit records: " + selectedHits.Count + "\n" +
            "Splats created: " + createdCount + "\n" +
            "Use density color: " + useDensityColor + "\n" +
            "Max density: " + maxDensity.ToString("F3", CultureInfo.InvariantCulture);

        Debug.Log(statusMessage);
    }

    private List<GazeRecord> CollectSelectedHitRecords()
    {
        List<GazeRecord> selected = new List<GazeRecord>();

        int step = Mathf.Max(1, sampleStep);
        int limit = maxSplats <= 0 ? int.MaxValue : maxSplats;

        for (int s = 0; s < nodeStays.Count; s++)
        {
            NodeStaySegment seg = nodeStays[s];

            if (!seg.selected)
            {
                continue;
            }

            for (int i = seg.startRecordIndex; i <= seg.endRecordIndex; i += step)
            {
                if (i < 0 || i >= records.Count)
                {
                    continue;
                }

                GazeRecord rec = records[i];

                if (!rec.gazeValid || !rec.gazeHit)
                {
                    continue;
                }

                if (!IsVectorFinite(rec.hitPoint))
                {
                    continue;
                }

                selected.Add(rec);

                if (selected.Count >= limit)
                {
                    return selected;
                }
            }
        }

        return selected;
    }

    private float[] ComputeDensities(List<GazeRecord> selectedHits, float radius)
    {
        int count = selectedHits.Count;
        float[] densities = new float[count];

        float r = Mathf.Max(0.001f, radius);
        float r2 = r * r;
        float sigma = r * 0.5f;
        float twoSigma2 = 2f * sigma * sigma;

        for (int i = 0; i < count; i++)
        {
            Vector3 pi = selectedHits[i].hitPoint;
            float density = 0f;

            for (int j = 0; j < count; j++)
            {
                Vector3 pj = selectedHits[j].hitPoint;
                float d2 = (pi - pj).sqrMagnitude;

                if (d2 <= r2)
                {
                    density += Mathf.Exp(-d2 / twoSigma2);
                }
            }

            densities[i] = density;
        }

        return densities;
    }

    private float GetMaxDensity(float[] densities)
    {
        if (densities == null || densities.Length == 0)
        {
            return 1f;
        }

        float maxValue = 0f;

        for (int i = 0; i < densities.Length; i++)
        {
            if (densities[i] > maxValue)
            {
                maxValue = densities[i];
            }
        }

        return Mathf.Max(0.0001f, maxValue);
    }

    private Color EvaluateHeatColor(float t)
    {
        t = Mathf.Clamp01(t);

        if (t < 0.5f)
        {
            return Color.Lerp(coldColor, midColor, t / 0.5f);
        }

        return Color.Lerp(midColor, hotColor, (t - 0.5f) / 0.5f);
    }

    private void CreateSplat(Vector3 hitPoint, Vector3 hitNormal, Color color, Transform parent)
    {
        GameObject obj = new GameObject("HeatSplat");
        obj.transform.SetParent(parent, false);

        Vector3 normal = hitNormal;

        if (normal.sqrMagnitude < 0.000001f)
        {
            normal = Vector3.up;
        }
        else
        {
            normal.Normalize();
        }

        obj.transform.position = hitPoint + normal * normalOffset;
        obj.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
        obj.transform.localScale = Vector3.one * splatSize;

        MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = splatMesh;

        MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = splatMaterial;

        propertyBlock.Clear();
        propertyBlock.SetColor("_Color", color);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private void ClearHeatmap()
    {
        if (heatmapRoot != null)
        {
            Destroy(heatmapRoot);
            heatmapRoot = null;
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

    public void RefreshHeatmapFromReplay(HashSet<int> selectedNodeStayIndices)
    {
        ApplyNodeStaySelectionFromReplay(selectedNodeStayIndices);
        RefreshHeatmap();
    }

    public void ClearHeatmapFromReplay()
    {
        ClearHeatmap();
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

    private void EnsureHeatmapResources()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        if (splatTexture == null)
        {
            splatTexture = CreateSoftCircleTexture(128);
        }

        if (splatMesh == null)
        {
            splatMesh = CreateDoubleSidedQuadMesh();
        }

        if (splatMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            splatMaterial = new Material(shader);
            splatMaterial.name = "Generated_SurfaceSplat_Material";
            splatMaterial.mainTexture = splatTexture;
            splatMaterial.renderQueue = 3100;
        }
    }

    private Texture2D CreateSoftCircleTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];

        float center = (size - 1) * 0.5f;
        float radius = center;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / radius;
                float dy = (y - center) / radius;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha;

                if (dist >= 1f)
                {
                    alpha = 0f;
                }
                else
                {
                    float inner = 0.15f;
                    float fade = Mathf.InverseLerp(1f, inner, dist);
                    alpha = Mathf.SmoothStep(0f, 1f, fade);
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return tex;
    }

    private Mesh CreateDoubleSidedQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Generated_DoubleSided_Splat_Quad";

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),

            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        };

        Vector2[] uvs = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),

            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };

        int[] triangles = new int[]
        {
            0, 1, 2,
            0, 2, 3,

            6, 5, 4,
            7, 6, 4
        };

        Vector3[] normals = new Vector3[]
        {
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,

            Vector3.back,
            Vector3.back,
            Vector3.back,
            Vector3.back
        };

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.normals = normals;

        mesh.RecalculateBounds();

        return mesh;
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
            string current = header[i];

            if (current == null)
            {
                continue;
            }

            current = current.Trim().Trim('\uFEFF');

            if (string.Equals(current, columnName, StringComparison.OrdinalIgnoreCase))
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

        return IsVectorFinite(rec.hitPoint);
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

        if (!StringIsNullOrWhiteSpace(rec.hitObjectName) &&
            rec.hitObjectName != "0" &&
            rec.hitObjectName != "NoHit" &&
            rec.hitObjectName != "InvalidGaze")
        {
            return true;
        }

        return rec.hitPoint.sqrMagnitude > 0.000001f;
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

    private bool StringIsNullOrWhiteSpace(string text)
    {
        return text == null || text.Trim().Length == 0;
    }

    private class GazeRecord
    {
        public int sourceRowIndex;

        public string nodeID;
        public string sceneTime;
        public string timestamp;

        public Vector3 origin;
        public Vector3 direction;

        public Vector3 hitPoint;
        public Vector3 hitNormal;

        public string hitObjectName;
        public string hitObjectTag;

        public bool gazeValid;
        public bool gazeHit;
    }

    private class NodeStaySegment
    {
        public string nodeID;
        public int startRecordIndex;
        public int endRecordIndex;

        public string startTime;
        public string endTime;

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