using UnityEngine;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

/// <summary>
/// Multi-participant local-patch surface-density heatmap visualizer.
///
/// Difference from the splat visualizer:
/// - This script does not create one circular quad per gaze hit.
/// - It groups gaze-hit points by surface, rasterizes them into a local 2D density field,
///   applies Gaussian-like spreading, then creates one transparent heatmap plane per surface.
///
/// Required CSV columns:
/// - NodeID
/// - HitPointX, HitPointY, HitPointZ
/// Recommended CSV columns:
/// - HitNormalX, HitNormalY, HitNormalZ
/// - GazeValid, GazeHit
/// - HitObjectName, HitObjectTag, HitObjectVariant, HitColliderPath
/// - GazeDirectionWorldX/Y/Z, SceneTime/Timestamp
///
/// Attach this component to a new empty GameObject.
/// It can coexist with the old splat visualizer and the whole-surface density visualizer because the class name is different.
/// </summary>
public class GazeSurfaceDensityPatchHeatmapMultiVisualizer : MonoBehaviour
{
    public enum CsvLoadMode
    {
        AllCsvInTaskFolder = 0,
        SingleCsvByIndex = 1,
        ManualCsvFileList = 2
    }

    public enum ParticipantWeightMode
    {
        RawSamples = 0,
        EqualParticipantWeight = 1
    }

    [Header("Data Path Settings")]
    public string processedRoot = @"Assets/StreamingAssets/Reorder_GazeHit";
    public int taskOrder = 1;
    public CsvLoadMode loadMode = CsvLoadMode.AllCsvInTaskFolder;
    public int singleCsvFileIndex = 1;

    [Tooltip("Optional. When filled, this folder replaces ProcessedRoot/TaskX/Eye.")]
    public string overrideEyeFolderFullPath = "";

    [TextArea(3, 8)]
    [Tooltip("Used only in ManualCsvFileList mode. Enter one full CSV path per line, or separate paths with semicolons.")]
    public string manualCsvFileList = "";

    [Header("Control Panel")]
    public bool showControlPanel = true;
    public Rect panelRect = new Rect(20, 20, 470, 820);
    public float participantListHeight = 150f;
    public float nodeListHeight = 210f;

    [Header("Record Filtering")]
    [Tooltip("Read every Nth valid row within selected NodeID stays.")]
    public int sampleStep = 1;

    [Tooltip("0 means unlimited. This limits the number of gaze-hit records used for density computation.")]
    public int maxRecords = 60000;

    [Tooltip("Empty or All means no tag filter. Example: Signage, Wall, Floor.")]
    public string hitObjectTagFilter = "";

    [Tooltip("Empty means no name filter. Example: HS-02.")]
    public string hitObjectNameContains = "";

    public ParticipantWeightMode weightMode = ParticipantWeightMode.EqualParticipantWeight;

    [Header("Surface Grouping")]
    public bool preferHitColliderPath = true;
    public bool includeHitObjectVariantInKey = true;
    public bool includeNormalBucketInKey = true;
    public float normalBucketStep = 0.25f;
    public int minRecordsPerSurface = 3;
    public int maxSurfaces = 400;

    [Header("Local Patch Projection")]
    [Tooltip("Recommended for curved or large complex surfaces. Splits one object surface into small local patches before planar density projection.")]
    public bool splitIntoLocalSurfacePatches = true;

    [Tooltip("World-space patch size in meters. Smaller values follow curved surfaces better, larger values look more continuous on flat surfaces.")]
    public float surfacePatchSize = 1.0f;

    [Tooltip("Use floor-based bins instead of rounded bins. Floor bins are more stable for patch partitioning.")]
    public bool useFloorPatchBucket = true;

    [Header("Density Texture Settings")]
    public int textureResolution = 256;
    public float densityRadius = 0.45f;
    public float surfacePadding = 0.15f;
    public float minSurfaceSize = 0.08f;

    [Tooltip("Display offset only. Set to 0 for exact surface placement; use 0.001-0.003 if z-fighting occurs.")]
    public float surfaceOffset = 0.0f;

    public bool normalizePerSurface = true;
    public bool usePercentileClamp = true;
    public float clampPercentile = 0.95f;
    public float manualMaxDensity = 20f;

    [Header("Heatmap Appearance")]
    public float minAlpha = 0.02f;
    public float maxAlpha = 0.72f;
    public float densityCutoff = 0.005f;
    public Color coldColor = new Color(0.05f, 0.18f, 1.00f, 1f);
    public Color midColor = new Color(1.00f, 0.95f, 0.05f, 1f);
    public Color hotColor = new Color(1.00f, 0.00f, 0.00f, 1f);

    private Vector2 panelScrollPosition = Vector2.zero;
    private Vector2 participantScrollPosition = Vector2.zero;
    private Vector2 nodeScrollPosition = Vector2.zero;

    private bool panelMinimized = false;
    private bool panelClosed = false;
    private bool panelResizing = false;

    private readonly List<ParticipantDataset> participantDatasets = new List<ParticipantDataset>();
    private readonly List<NodeGroup> nodeGroups = new List<NodeGroup>();

    private GameObject heatmapRoot;
    private string participantFilter = "";
    private string nodeFilter = "";
    private string statusMessage = "No data loaded.";
    private int totalLoadedRecords = 0;
    private int totalLoadedNodeStays = 0;

    private const float MinimumPanelWidth = 410f;
    private const float MinimumPanelHeight = 300f;

    private void OnGUI()
    {
        if (!showControlPanel || panelClosed)
        {
            return;
        }

        panelRect = GUI.Window(2026070802, panelRect, DrawPanel, "");
    }

    private void DrawPanel(int windowID)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(28));
        GUILayout.Label("Multi-Participant Local-Patch Density Heatmap", GUILayout.Width(315));
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
        DrawFilteringSection();
        DrawSurfaceSettingsSection();
        DrawAppearanceSection();
        DrawParticipantSelectionSection();
        DrawNodeSelectionSection();
        DrawGenerateSection();
        DrawStatusSection();

        GUILayout.EndScrollView();

        HandlePanelResize();
        GUI.DragWindow(new Rect(0, 0, panelRect.width - 24f, 28));
    }

    private void DrawDataSourceSection()
    {
        GUILayout.Label("Data Source", GUI.skin.box);

        GUILayout.Label("Processed Root:");
        processedRoot = GUILayout.TextField(processedRoot);

        GUILayout.BeginHorizontal();
        GUILayout.Label("TaskOrder:", GUILayout.Width(90));
        taskOrder = DrawCompactIntField(taskOrder, 1, 999999, 80f);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Label("Load Mode:");
        loadMode = (CsvLoadMode)GUILayout.SelectionGrid(
            (int)loadMode,
            new string[]
            {
                "All CSVs in Task/Eye",
                "Single CSV by Index",
                "Manual CSV List"
            },
            1
        );

        if (loadMode == CsvLoadMode.SingleCsvByIndex)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("CSV Index (1-based):", GUILayout.Width(145));
            singleCsvFileIndex = DrawCompactIntField(singleCsvFileIndex, 1, 999999, 80f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.Label("Override Eye Folder (optional):");
        overrideEyeFolderFullPath = GUILayout.TextField(overrideEyeFolderFullPath);

        if (loadMode == CsvLoadMode.ManualCsvFileList)
        {
            GUILayout.Label("Manual CSV paths, one per line or separated by ;");
            manualCsvFileList = GUILayout.TextArea(manualCsvFileList, GUILayout.Height(80));
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

    private void DrawFilteringSection()
    {
        GUILayout.Label("Record Filtering", GUI.skin.box);

        sampleStep = IntField("Sample Step", sampleStep, 1, 99999);
        maxRecords = IntField("Max Records", maxRecords, 0, 1000000);
        minRecordsPerSurface = IntField("Min Records / Surface", minRecordsPerSurface, 1, 10000);
        maxSurfaces = IntField("Max Surfaces", maxSurfaces, 1, 10000);

        GUILayout.Label("Participant Weight Mode:");
        weightMode = (ParticipantWeightMode)GUILayout.SelectionGrid(
            (int)weightMode,
            new string[]
            {
                "Raw Samples: cumulative sampled gaze frames",
                "Equal Participant Weight: each participant contributes equally"
            },
            1
        );

        GUILayout.Label("HitObjectTag Filter (empty / All = no filter):");
        hitObjectTagFilter = GUILayout.TextField(hitObjectTagFilter);

        GUILayout.Label("HitObjectName Contains (empty = no filter):");
        hitObjectNameContains = GUILayout.TextField(hitObjectNameContains);

        GUILayout.Space(8);
    }

    private void DrawSurfaceSettingsSection()
    {
        GUILayout.Label("Surface Grouping", GUI.skin.box);
        preferHitColliderPath = GUILayout.Toggle(preferHitColliderPath, "Prefer HitColliderPath as surface key");
        includeHitObjectVariantInKey = GUILayout.Toggle(includeHitObjectVariantInKey, "Include HitObjectVariant in key");
        includeNormalBucketInKey = GUILayout.Toggle(includeNormalBucketInKey, "Include normal bucket in key");
        normalBucketStep = FloatFieldWithSlider("Normal Bucket Step", normalBucketStep, 0.05f, 1.0f);

        GUILayout.Space(6);
        GUILayout.Label("Local Patch Projection", GUI.skin.box);
        splitIntoLocalSurfacePatches = GUILayout.Toggle(splitIntoLocalSurfacePatches, "Split curved / large surfaces into local patches");
        surfacePatchSize = FloatFieldWithSlider("Surface Patch Size (m)", surfacePatchSize, 0.20f, 5.0f);
        useFloorPatchBucket = GUILayout.Toggle(useFloorPatchBucket, "Use floor patch bucket");

        GUILayout.Space(6);
        GUILayout.Label("Density Texture", GUI.skin.box);
        textureResolution = IntField("Texture Resolution", textureResolution, 32, 1024);
        densityRadius = FloatFieldWithSlider("Density Radius", densityRadius, 0.02f, 3.0f);
        surfacePadding = FloatFieldWithSlider("Surface Padding", surfacePadding, 0.0f, 2.0f);
        minSurfaceSize = FloatFieldWithSlider("Min Surface Size", minSurfaceSize, 0.01f, 1.0f);
        surfaceOffset = FloatFieldWithSlider("Surface Offset", surfaceOffset, 0.0f, 0.05f);
        normalizePerSurface = GUILayout.Toggle(normalizePerSurface, "Normalize per surface");
        usePercentileClamp = GUILayout.Toggle(usePercentileClamp, "Use percentile clamp");

        if (usePercentileClamp)
        {
            clampPercentile = FloatFieldWithSlider("Clamp Percentile", clampPercentile, 0.50f, 1.00f);
        }
        else
        {
            manualMaxDensity = FloatFieldWithSlider("Manual Max Density", manualMaxDensity, 0.0001f, 500f);
        }

        GUILayout.Space(8);
    }

    private void DrawAppearanceSection()
    {
        GUILayout.Label("Appearance", GUI.skin.box);
        minAlpha = FloatFieldWithSlider("Min Alpha", minAlpha, 0.0f, 1.0f);
        maxAlpha = FloatFieldWithSlider("Max Alpha", maxAlpha, 0.0f, 1.0f);
        densityCutoff = FloatFieldWithSlider("Density Cutoff", densityCutoff, 0.0f, 0.20f);
        coldColor = DrawColorSliders("Cold Color", coldColor);
        midColor = DrawColorSliders("Mid Color", midColor);
        hotColor = DrawColorSliders("Hot Color", hotColor);
        GUILayout.Space(8);
    }

    private void DrawParticipantSelectionSection()
    {
        GUILayout.Label("Participants", GUI.skin.box);

        if (participantDatasets.Count == 0)
        {
            GUILayout.Label("Load participant data first.");
            GUILayout.Space(8);
            return;
        }

        GUILayout.Label("Loaded: " + participantDatasets.Count + " | Selected: " + CountSelectedParticipants());

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

        participantListHeight = DrawListHeightSlider("Participant List Height", participantListHeight, 90f, 500f);

        participantScrollPosition = GUILayout.BeginScrollView(
            participantScrollPosition,
            false,
            true,
            GUILayout.Height(participantListHeight)
        );

        string filter = participantFilter == null ? "" : participantFilter.Trim();

        for (int i = 0; i < participantDatasets.Count; i++)
        {
            ParticipantDataset dataset = participantDatasets[i];

            if (filter.Length > 0 &&
                dataset.participantID.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                dataset.sourceFileName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            string label = dataset.participantID +
                           " | Records=" + dataset.records.Count +
                           " | Stays=" + dataset.nodeStays.Count;

            dataset.selected = GUILayout.Toggle(dataset.selected, label);
        }

        GUILayout.EndScrollView();
        GUILayout.Space(8);
    }

    private void DrawNodeSelectionSection()
    {
        GUILayout.Label("NodeID Selection", GUI.skin.box);

        if (nodeGroups.Count == 0)
        {
            GUILayout.Label("No NodeIDs available.");
            GUILayout.Space(8);
            return;
        }

        GUILayout.Label("NodeIDs: " + nodeGroups.Count + " | Selected: " + CountSelectedNodeGroups());

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

        nodeListHeight = DrawListHeightSlider("Node List Height", nodeListHeight, 100f, 650f);

        nodeScrollPosition = GUILayout.BeginScrollView(
            nodeScrollPosition,
            false,
            true,
            GUILayout.Height(nodeListHeight)
        );

        string filter = nodeFilter == null ? "" : nodeFilter.Trim();

        for (int i = 0; i < nodeGroups.Count; i++)
        {
            NodeGroup group = nodeGroups[i];

            if (filter.Length > 0 &&
                group.nodeID.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
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
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate / Refresh Density Heatmap", GUILayout.Height(34)))
        {
            RefreshHeatmap();
        }
        if (GUILayout.Button("Clear Heatmap", GUILayout.Height(34), GUILayout.Width(120)))
        {
            ClearHeatmap();
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
    }

    private void DrawStatusSection()
    {
        GUILayout.Label("Status", GUI.skin.box);
        GUILayout.TextArea(statusMessage, GUILayout.Height(120));
    }

    private void HandlePanelResize()
    {
        Rect resizeRect = new Rect(
            Mathf.Max(0f, panelRect.width - 20f),
            Mathf.Max(0f, panelRect.height - 20f),
            20f,
            20f
        );

        GUI.Box(resizeRect, "↘");
        Event currentEvent = Event.current;

        if (currentEvent.type == EventType.MouseDown &&
            currentEvent.button == 0 &&
            resizeRect.Contains(currentEvent.mousePosition))
        {
            panelResizing = true;
            currentEvent.Use();
        }

        if (panelResizing && currentEvent.type == EventType.MouseDrag)
        {
            panelRect.width = Mathf.Max(MinimumPanelWidth, currentEvent.mousePosition.x + 6f);
            panelRect.height = Mathf.Max(MinimumPanelHeight, currentEvent.mousePosition.y + 6f);
            currentEvent.Use();
        }

        if (panelResizing && currentEvent.rawType == EventType.MouseUp)
        {
            panelResizing = false;
        }
    }

    private float DrawListHeightSlider(string label, float value, float minimum, float maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(160));
        value = GUILayout.HorizontalSlider(value, minimum, maximum);
        GUILayout.Label(Mathf.RoundToInt(value).ToString(), GUILayout.Width(42));
        GUILayout.EndHorizontal();
        return value;
    }

    private int DrawCompactIntField(int value, int minimum, int maximum, float width)
    {
        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(width));
        int parsed;
        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Clamp(parsed, minimum, maximum);
        }
        return value;
    }

    private int IntField(string label, int value, int minimum, int maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(155));
        value = DrawCompactIntField(value, minimum, maximum, 85f);
        GUILayout.EndHorizontal();
        return value;
    }

    private float FloatFieldWithSlider(string label, float value, float minimum, float maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(155));

        string text = GUILayout.TextField(value.ToString("G4", CultureInfo.InvariantCulture), GUILayout.Width(85));
        float parsed;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = Mathf.Clamp(parsed, minimum, maximum);
        }

        value = GUILayout.HorizontalSlider(value, minimum, maximum);
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
        value = GUILayout.HorizontalSlider(value, 0f, 1f);
        GUILayout.Label(value.ToString("F2"), GUILayout.Width(42));
        GUILayout.EndHorizontal();
        return value;
    }

    public void LoadParticipantData()
    {
        ClearHeatmap();
        participantDatasets.Clear();
        nodeGroups.Clear();
        totalLoadedRecords = 0;
        totalLoadedNodeStays = 0;

        List<string> csvPaths = ResolveCsvPaths();

        if (csvPaths.Count == 0)
        {
            statusMessage = "No CSV files were found.\nResolved folder: " + ResolveEyeFolderPath();
            Debug.LogError(statusMessage);
            return;
        }

        int failedCount = 0;
        StringBuilder failureDetails = new StringBuilder();

        for (int i = 0; i < csvPaths.Count; i++)
        {
            ParticipantDataset dataset;
            string errorMessage;

            if (TryLoadParticipantCsv(csvPaths[i], i, out dataset, out errorMessage))
            {
                participantDatasets.Add(dataset);
                totalLoadedRecords += dataset.records.Count;
                totalLoadedNodeStays += dataset.nodeStays.Count;
            }
            else
            {
                failedCount++;
                failureDetails.AppendLine(Path.GetFileName(csvPaths[i]) + ": " + errorMessage);
                Debug.LogWarning("Failed to load participant CSV: " + csvPaths[i] + "\n" + errorMessage);
            }
        }

        BuildNodeGroups();

        statusMessage =
            "Participant data loaded.\n" +
            "Files requested: " + csvPaths.Count + "\n" +
            "Participants loaded: " + participantDatasets.Count + "\n" +
            "Failed files: " + failedCount + "\n" +
            "Records: " + totalLoadedRecords + "\n" +
            "Node stays: " + totalLoadedNodeStays + "\n" +
            "Unique NodeIDs: " + nodeGroups.Count;

        if (failedCount > 0)
        {
            statusMessage += "\n\nFailed details:\n" + failureDetails.ToString();
        }

        Debug.Log(statusMessage);
    }

    private List<string> ResolveCsvPaths()
    {
        List<string> paths = new List<string>();

        if (loadMode == CsvLoadMode.ManualCsvFileList)
        {
            string normalized = manualCsvFileList == null ? "" : manualCsvFileList.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] parts = normalized.Split(new char[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < parts.Length; i++)
            {
                string path = parts[i].Trim().Trim('"');
                if (path.Length == 0 || !path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (uniquePaths.Add(path))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        string eyeFolder = ResolveEyeFolderPath();
        if (!Directory.Exists(eyeFolder))
        {
            return paths;
        }

        string[] csvFiles = Directory.GetFiles(eyeFolder, "*.csv", SearchOption.TopDirectoryOnly);
        Array.Sort(csvFiles, StringComparer.OrdinalIgnoreCase);

        if (loadMode == CsvLoadMode.SingleCsvByIndex)
        {
            if (csvFiles.Length == 0)
            {
                return paths;
            }

            int index = Mathf.Clamp(singleCsvFileIndex, 1, csvFiles.Length) - 1;
            paths.Add(csvFiles[index]);
            return paths;
        }

        paths.AddRange(csvFiles);
        return paths;
    }

    private string ResolveEyeFolderPath()
    {
        if (!StringIsNullOrWhiteSpace(overrideEyeFolderFullPath))
        {
            return overrideEyeFolderFullPath.Trim().Trim('"');
        }

        return Path.Combine(Path.Combine(processedRoot, "Task" + taskOrder), "Eye");
    }

    private bool TryLoadParticipantCsv(string csvPath, int participantIndex, out ParticipantDataset dataset, out string errorMessage)
    {
        dataset = null;
        errorMessage = "";

        if (StringIsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
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
        int idxNormalX = FindColumnIndex(header, "HitNormalX");
        int idxNormalY = FindColumnIndex(header, "HitNormalY");
        int idxNormalZ = FindColumnIndex(header, "HitNormalZ");
        int idxGazeValid = FindColumnIndex(header, "GazeValid");
        int idxGazeHit = FindColumnIndex(header, "GazeHit");
        int idxHitObjectName = FindColumnIndex(header, "HitObjectName");
        int idxHitObjectTag = FindColumnIndex(header, "HitObjectTag");
        int idxHitObjectVariant = FindColumnIndex(header, "HitObjectVariant");
        int idxHitColliderPath = FindColumnIndex(header, "HitColliderPath");
        int idxSceneTime = FindColumnIndex(header, "SceneTime");
        int idxTimestamp = FindColumnIndex(header, "Timestamp");

        if (idxNodeID < 0 || idxHitX < 0 || idxHitY < 0 || idxHitZ < 0)
        {
            errorMessage = "Missing required columns. Required: NodeID, HitPointX, HitPointY, HitPointZ.";
            return false;
        }

        bool hasNormal = idxNormalX >= 0 && idxNormalY >= 0 && idxNormalZ >= 0;

        dataset = new ParticipantDataset();
        dataset.participantIndex = participantIndex;
        dataset.csvPath = csvPath;
        dataset.sourceFileName = Path.GetFileName(csvPath);
        dataset.participantID = DeriveParticipantID(csvPath);
        dataset.selected = true;
        dataset.hasRecordedHitNormal = hasNormal;

        for (int rowIndex = 1; rowIndex < table.Count; rowIndex++)
        {
            List<string> row = table[rowIndex];
            GazeRecord record = new GazeRecord();
            record.participantIndex = participantIndex;
            record.participantID = dataset.participantID;
            record.sourceFileName = dataset.sourceFileName;
            record.sourceRowIndex = rowIndex;
            record.nodeID = SafeGet(row, idxNodeID).Trim();
            record.sceneTime = idxSceneTime >= 0 ? SafeGet(row, idxSceneTime) : "";
            record.timestamp = idxTimestamp >= 0 ? SafeGet(row, idxTimestamp) : "";
            record.hitPoint = ReadVector3(row, idxHitX, idxHitY, idxHitZ);

            if (hasNormal)
            {
                record.hitNormal = ReadVector3(row, idxNormalX, idxNormalY, idxNormalZ);
            }
            else
            {
                record.hitNormal = Vector3.zero;
            }

            if (idxOriginX >= 0 && idxOriginY >= 0 && idxOriginZ >= 0)
            {
                record.origin = ReadVector3(row, idxOriginX, idxOriginY, idxOriginZ);
            }

            if (idxDirX >= 0 && idxDirY >= 0 && idxDirZ >= 0)
            {
                record.direction = ReadVector3(row, idxDirX, idxDirY, idxDirZ);
            }

            record.hitObjectName = idxHitObjectName >= 0 ? SafeGet(row, idxHitObjectName) : "";
            record.hitObjectTag = idxHitObjectTag >= 0 ? SafeGet(row, idxHitObjectTag) : "";
            record.hitObjectVariant = idxHitObjectVariant >= 0 ? SafeGet(row, idxHitObjectVariant) : "";
            record.hitColliderPath = idxHitColliderPath >= 0 ? SafeGet(row, idxHitColliderPath) : "";
            record.gazeValid = DetermineGazeValid(row, idxGazeValid, record);
            record.gazeHit = DetermineGazeHit(row, idxGazeHit, record);

            if (record.hitNormal.sqrMagnitude < 0.000001f)
            {
                if (record.direction.sqrMagnitude > 0.000001f)
                {
                    record.hitNormal = -record.direction.normalized;
                }
                else
                {
                    record.hitNormal = Vector3.up;
                }
            }
            else
            {
                record.hitNormal.Normalize();
            }

            dataset.records.Add(record);
        }

        BuildNodeStaySegments(dataset);
        return true;
    }

    private string DeriveParticipantID(string csvPath)
    {
        return Path.GetFileNameWithoutExtension(csvPath);
    }

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
            if (!string.Equals(nodeID, currentNodeID, StringComparison.Ordinal))
            {
                AddNodeStaySegment(dataset, startIndex, i - 1, currentNodeID);
                startIndex = i;
                currentNodeID = nodeID;
            }
        }

        AddNodeStaySegment(dataset, startIndex, dataset.records.Count - 1, currentNodeID);
    }

    private void AddNodeStaySegment(ParticipantDataset dataset, int startIndex, int endIndex, string nodeID)
    {
        NodeStaySegment segment = new NodeStaySegment();
        segment.participantIndex = dataset.participantIndex;
        segment.participantID = dataset.participantID;
        segment.sourceFileName = dataset.sourceFileName;
        segment.nodeID = nodeID;
        segment.startRecordIndex = startIndex;
        segment.endRecordIndex = endIndex;

        if (startIndex >= 0 && startIndex < dataset.records.Count)
        {
            segment.startTime = !StringIsNullOrWhiteSpace(dataset.records[startIndex].sceneTime)
                ? dataset.records[startIndex].sceneTime
                : dataset.records[startIndex].timestamp;
        }

        if (endIndex >= 0 && endIndex < dataset.records.Count)
        {
            segment.endTime = !StringIsNullOrWhiteSpace(dataset.records[endIndex].sceneTime)
                ? dataset.records[endIndex].sceneTime
                : dataset.records[endIndex].timestamp;
        }

        dataset.nodeStays.Add(segment);
    }

    private void BuildNodeGroups()
    {
        nodeGroups.Clear();
        Dictionary<string, NodeGroupBuilder> builders = new Dictionary<string, NodeGroupBuilder>(StringComparer.OrdinalIgnoreCase);

        for (int participantIndex = 0; participantIndex < participantDatasets.Count; participantIndex++)
        {
            ParticipantDataset dataset = participantDatasets[participantIndex];
            for (int stayIndex = 0; stayIndex < dataset.nodeStays.Count; stayIndex++)
            {
                NodeStaySegment stay = dataset.nodeStays[stayIndex];
                if (StringIsNullOrWhiteSpace(stay.nodeID))
                {
                    continue;
                }

                NodeGroupBuilder builder;
                if (!builders.TryGetValue(stay.nodeID, out builder))
                {
                    builder = new NodeGroupBuilder();
                    builder.nodeID = stay.nodeID;
                    builders.Add(stay.nodeID, builder);
                }

                builder.stayCount++;
                builder.recordCount += stay.RecordCount;
                builder.participantIndices.Add(dataset.participantIndex);
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
        bool aIsNumber = TryExtractNodeNumber(a.nodeID, out aNumber);
        bool bIsNumber = TryExtractNodeNumber(b.nodeID, out bNumber);

        if (aIsNumber && bIsNumber)
        {
            return aNumber.CompareTo(bNumber);
        }
        if (aIsNumber && !bIsNumber)
        {
            return -1;
        }
        if (!aIsNumber && bIsNumber)
        {
            return 1;
        }
        return string.Compare(a.nodeID, b.nodeID, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryExtractNodeNumber(string nodeID, out int number)
    {
        number = 0;
        if (StringIsNullOrWhiteSpace(nodeID))
        {
            return false;
        }

        string s = nodeID.Trim();
        if (s.StartsWith("Node", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(4).Trim();
        }

        return int.TryParse(s, out number);
    }

    public void RefreshHeatmap()
    {
        ClearHeatmap();

        if (participantDatasets.Count == 0)
        {
            statusMessage = "No participant data loaded.";
            Debug.LogWarning(statusMessage);
            return;
        }

        HashSet<string> selectedNodeIDs = GetSelectedNodeIDs();
        if (selectedNodeIDs.Count == 0)
        {
            statusMessage = "No NodeID selected.";
            Debug.LogWarning(statusMessage);
            return;
        }

        List<ParticipantHitCandidates> participantCandidates = CollectCandidateHitsByParticipant(selectedNodeIDs);
        if (participantCandidates.Count == 0)
        {
            statusMessage = "No valid gaze-hit records matched the selected participants, NodeIDs and filters.";
            Debug.LogWarning(statusMessage);
            return;
        }

        List<WeightedHit> selectedHits = SampleAndWeightCandidates(participantCandidates);
        if (selectedHits.Count == 0)
        {
            statusMessage = "No valid gaze-hit records remained after sampling.";
            Debug.LogWarning(statusMessage);
            return;
        }

        Dictionary<string, SurfaceGroup> groups = BuildSurfaceGroups(selectedHits);
        if (groups.Count == 0)
        {
            statusMessage = "No surface groups were created. Check grouping and CSV fields.";
            Debug.LogWarning(statusMessage);
            return;
        }

        List<SurfaceRenderData> renderDataList = new List<SurfaceRenderData>();
        int skippedSmallGroups = 0;

        foreach (KeyValuePair<string, SurfaceGroup> kvp in groups)
        {
            SurfaceGroup group = kvp.Value;
            if (group.records.Count < Mathf.Max(1, minRecordsPerSurface))
            {
                skippedSmallGroups++;
                continue;
            }

            SurfaceRenderData data = BuildSurfaceRenderData(group);
            if (data != null)
            {
                renderDataList.Add(data);
            }
        }

        renderDataList.Sort(delegate (SurfaceRenderData a, SurfaceRenderData b)
        {
            int weightCompare = b.totalWeight.CompareTo(a.totalWeight);
            if (weightCompare != 0)
            {
                return weightCompare;
            }
            return b.recordCount.CompareTo(a.recordCount);
        });

        if (renderDataList.Count > maxSurfaces)
        {
            renderDataList.RemoveRange(maxSurfaces, renderDataList.Count - maxSurfaces);
        }

        if (renderDataList.Count == 0)
        {
            statusMessage = "No valid surfaces after grouping.\nGroups: " + groups.Count +
                            " | Skipped small groups: " + skippedSmallGroups;
            Debug.LogWarning(statusMessage);
            return;
        }

        float globalClamp = 1f;
        if (!normalizePerSurface)
        {
            if (usePercentileClamp)
            {
                globalClamp = ComputeGlobalPercentile(renderDataList, Mathf.Clamp01(clampPercentile));
            }
            else
            {
                globalClamp = Mathf.Max(0.0001f, manualMaxDensity);
            }
        }

        heatmapRoot = new GameObject("SurfaceDensityHeatmap_Multi_Task" + taskOrder);
        heatmapRoot.transform.SetParent(transform, false);

        int createdSurfaces = 0;
        int totalRecordsInCreatedSurfaces = 0;
        float totalWeightInCreatedSurfaces = 0f;

        for (int i = 0; i < renderDataList.Count; i++)
        {
            SurfaceRenderData data = renderDataList[i];
            float clampValue = normalizePerSurface ? data.localClampDensity : globalClamp;
            CreateSurfaceHeatmap(data, clampValue, heatmapRoot.transform);
            createdSurfaces++;
            totalRecordsInCreatedSurfaces += data.recordCount;
            totalWeightInCreatedSurfaces += data.totalWeight;
        }

        int candidateCount = 0;
        for (int i = 0; i < participantCandidates.Count; i++)
        {
            candidateCount += participantCandidates[i].records.Count;
        }

        statusMessage =
            "Density heatmap generated.\n" +
            "Selected participants with valid hits: " + participantCandidates.Count + "\n" +
            "Selected NodeIDs: " + selectedNodeIDs.Count + "\n" +
            "Candidate hit records: " + candidateCount + "\n" +
            "Weighted records used: " + selectedHits.Count + "\n" +
            "Surface groups: " + groups.Count + "\n" +
            "Surfaces created: " + createdSurfaces + "\n" +
            "Records in created surfaces: " + totalRecordsInCreatedSurfaces + "\n" +
            "Total weight in created surfaces: " + totalWeightInCreatedSurfaces.ToString("F4", CultureInfo.InvariantCulture) + "\n" +
            "Skipped small groups: " + skippedSmallGroups + "\n" +
            "Weight mode: " + weightMode + "\n" +
            "Normalize per surface: " + normalizePerSurface + "\n" +
            "Global clamp: " + globalClamp.ToString("F4", CultureInfo.InvariantCulture);

        Debug.Log(statusMessage);
    }

    private HashSet<string> GetSelectedNodeIDs()
    {
        HashSet<string> selectedNodeIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < nodeGroups.Count; i++)
        {
            if (nodeGroups[i].selected)
            {
                selectedNodeIDs.Add(nodeGroups[i].nodeID);
            }
        }
        return selectedNodeIDs;
    }

    private List<ParticipantHitCandidates> CollectCandidateHitsByParticipant(HashSet<string> selectedNodeIDs)
    {
        List<ParticipantHitCandidates> result = new List<ParticipantHitCandidates>();
        int step = Mathf.Max(1, sampleStep);

        string tagFilter = hitObjectTagFilter == null ? "" : hitObjectTagFilter.Trim();
        bool useTagFilter = tagFilter.Length > 0 && !tagFilter.Equals("All", StringComparison.OrdinalIgnoreCase);
        string nameFilter = hitObjectNameContains == null ? "" : hitObjectNameContains.Trim();
        bool useNameFilter = nameFilter.Length > 0;

        for (int datasetIndex = 0; datasetIndex < participantDatasets.Count; datasetIndex++)
        {
            ParticipantDataset dataset = participantDatasets[datasetIndex];
            if (!dataset.selected)
            {
                continue;
            }

            ParticipantHitCandidates candidates = new ParticipantHitCandidates();
            candidates.dataset = dataset;

            for (int stayIndex = 0; stayIndex < dataset.nodeStays.Count; stayIndex++)
            {
                NodeStaySegment stay = dataset.nodeStays[stayIndex];
                if (!selectedNodeIDs.Contains(stay.nodeID))
                {
                    continue;
                }

                for (int recordIndex = stay.startRecordIndex; recordIndex <= stay.endRecordIndex; recordIndex += step)
                {
                    if (recordIndex < 0 || recordIndex >= dataset.records.Count)
                    {
                        continue;
                    }

                    GazeRecord record = dataset.records[recordIndex];
                    if (!record.gazeValid || !record.gazeHit)
                    {
                        continue;
                    }

                    if (!IsVectorFinite(record.hitPoint))
                    {
                        continue;
                    }

                    if (useTagFilter && !string.Equals(record.hitObjectTag, tagFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (useNameFilter)
                    {
                        string objectName = record.hitObjectName == null ? "" : record.hitObjectName;
                        if (objectName.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    candidates.records.Add(record);
                }
            }

            if (candidates.records.Count > 0)
            {
                result.Add(candidates);
            }
        }

        return result;
    }

    private List<WeightedHit> SampleAndWeightCandidates(List<ParticipantHitCandidates> participantCandidates)
    {
        List<WeightedHit> result = new List<WeightedHit>();

        int totalCandidates = 0;
        for (int i = 0; i < participantCandidates.Count; i++)
        {
            totalCandidates += participantCandidates[i].records.Count;
        }

        int limit = maxRecords <= 0 ? totalCandidates : Mathf.Min(maxRecords, totalCandidates);
        int[] quotas;

        if (limit >= totalCandidates)
        {
            quotas = new int[participantCandidates.Count];
            for (int i = 0; i < participantCandidates.Count; i++)
            {
                quotas[i] = participantCandidates[i].records.Count;
            }
        }
        else if (weightMode == ParticipantWeightMode.EqualParticipantWeight)
        {
            quotas = AllocateEqualQuotas(participantCandidates, limit);
        }
        else
        {
            quotas = AllocateProportionalQuotas(participantCandidates, limit);
        }

        for (int participantIndex = 0; participantIndex < participantCandidates.Count; participantIndex++)
        {
            ParticipantHitCandidates candidates = participantCandidates[participantIndex];
            int quota = Mathf.Clamp(quotas[participantIndex], 0, candidates.records.Count);
            if (quota == 0)
            {
                continue;
            }

            List<GazeRecord> sampledRecords = EvenlySampleRecords(candidates.records, quota);
            float recordWeight = 1f;

            if (weightMode == ParticipantWeightMode.EqualParticipantWeight)
            {
                recordWeight = 1f / Mathf.Max(1, sampledRecords.Count);
            }

            for (int recordIndex = 0; recordIndex < sampledRecords.Count; recordIndex++)
            {
                WeightedHit weightedHit = new WeightedHit();
                weightedHit.record = sampledRecords[recordIndex];
                weightedHit.weight = recordWeight;
                result.Add(weightedHit);
            }
        }

        return result;
    }

    private int[] AllocateEqualQuotas(List<ParticipantHitCandidates> participantCandidates, int limit)
    {
        int participantCount = participantCandidates.Count;
        int[] quotas = new int[participantCount];

        if (participantCount == 0 || limit <= 0)
        {
            return quotas;
        }

        int remaining = limit;
        bool assignedInPass = true;

        while (remaining > 0 && assignedInPass)
        {
            assignedInPass = false;

            for (int i = 0; i < participantCount && remaining > 0; i++)
            {
                if (quotas[i] < participantCandidates[i].records.Count)
                {
                    quotas[i]++;
                    remaining--;
                    assignedInPass = true;
                }
            }
        }

        return quotas;
    }

    private int[] AllocateProportionalQuotas(List<ParticipantHitCandidates> participantCandidates, int limit)
    {
        int participantCount = participantCandidates.Count;
        int[] quotas = new int[participantCount];
        float[] fractions = new float[participantCount];

        int total = 0;
        for (int i = 0; i < participantCount; i++)
        {
            total += participantCandidates[i].records.Count;
        }

        if (total <= 0 || limit <= 0)
        {
            return quotas;
        }

        int assigned = 0;
        for (int i = 0; i < participantCount; i++)
        {
            float exact = (float)participantCandidates[i].records.Count / (float)total * limit;
            int floorValue = Mathf.FloorToInt(exact);
            floorValue = Mathf.Min(floorValue, participantCandidates[i].records.Count);
            quotas[i] = floorValue;
            fractions[i] = exact - floorValue;
            assigned += floorValue;
        }

        int remaining = limit - assigned;
        while (remaining > 0)
        {
            int bestIndex = -1;
            float bestFraction = -1f;

            for (int i = 0; i < participantCount; i++)
            {
                if (quotas[i] >= participantCandidates[i].records.Count)
                {
                    continue;
                }

                if (fractions[i] > bestFraction)
                {
                    bestFraction = fractions[i];
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            quotas[bestIndex]++;
            fractions[bestIndex] = 0f;
            remaining--;
        }

        return quotas;
    }

    private List<GazeRecord> EvenlySampleRecords(List<GazeRecord> source, int targetCount)
    {
        List<GazeRecord> sampled = new List<GazeRecord>();

        if (source == null || source.Count == 0 || targetCount <= 0)
        {
            return sampled;
        }

        if (targetCount >= source.Count)
        {
            sampled.AddRange(source);
            return sampled;
        }

        for (int i = 0; i < targetCount; i++)
        {
            float position = ((float)i + 0.5f) * source.Count / targetCount;
            int index = Mathf.Clamp(Mathf.FloorToInt(position), 0, source.Count - 1);
            sampled.Add(source[index]);
        }

        return sampled;
    }

    private Dictionary<string, SurfaceGroup> BuildSurfaceGroups(List<WeightedHit> selectedHits)
    {
        Dictionary<string, SurfaceGroup> groups = new Dictionary<string, SurfaceGroup>();

        for (int i = 0; i < selectedHits.Count; i++)
        {
            WeightedHit hit = selectedHits[i];
            string key = BuildSurfaceKey(hit.record);

            SurfaceGroup group;
            if (!groups.TryGetValue(key, out group))
            {
                group = new SurfaceGroup();
                group.key = key;
                group.objectName = hit.record.hitObjectName;
                group.objectTag = hit.record.hitObjectTag;
                group.objectVariant = hit.record.hitObjectVariant;
                group.colliderPath = hit.record.hitColliderPath;
                groups.Add(key, group);
            }

            group.records.Add(hit);
            group.totalWeight += hit.weight;
        }

        return groups;
    }

    private string BuildSurfaceKey(GazeRecord rec)
    {
        StringBuilder sb = new StringBuilder();

        if (preferHitColliderPath && !StringIsNullOrWhiteSpace(rec.hitColliderPath))
        {
            sb.Append(rec.hitColliderPath.Trim());
        }
        else
        {
            sb.Append("Tag=").Append(SafeKeyPart(rec.hitObjectTag));
            sb.Append("|Name=").Append(SafeKeyPart(rec.hitObjectName));

            if (includeHitObjectVariantInKey && !StringIsNullOrWhiteSpace(rec.hitObjectVariant))
            {
                sb.Append("|Variant=").Append(SafeKeyPart(rec.hitObjectVariant));
            }
        }

        if (includeNormalBucketInKey)
        {
            sb.Append("|N=").Append(GetNormalBucket(rec.hitNormal));
        }

        if (splitIntoLocalSurfacePatches)
        {
            sb.Append("|P=").Append(GetSpatialPatchBucket(rec.hitPoint));
        }

        return sb.ToString();
    }

    private string SafeKeyPart(string value)
    {
        if (StringIsNullOrWhiteSpace(value))
        {
            return "None";
        }
        return value.Trim();
    }

    private string GetNormalBucket(Vector3 normal)
    {
        Vector3 n = normal;
        if (n.sqrMagnitude < 0.000001f)
        {
            n = Vector3.up;
        }
        else
        {
            n.Normalize();
        }

        float step = Mathf.Max(0.01f, normalBucketStep);
        int x = Mathf.RoundToInt(n.x / step);
        int y = Mathf.RoundToInt(n.y / step);
        int z = Mathf.RoundToInt(n.z / step);
        return x + "," + y + "," + z;
    }

    private string GetSpatialPatchBucket(Vector3 point)
    {
        float size = Mathf.Max(0.05f, surfacePatchSize);

        int x;
        int y;
        int z;

        if (useFloorPatchBucket)
        {
            x = Mathf.FloorToInt(point.x / size);
            y = Mathf.FloorToInt(point.y / size);
            z = Mathf.FloorToInt(point.z / size);
        }
        else
        {
            x = Mathf.RoundToInt(point.x / size);
            y = Mathf.RoundToInt(point.y / size);
            z = Mathf.RoundToInt(point.z / size);
        }

        return x + "," + y + "," + z;
    }

    private SurfaceRenderData BuildSurfaceRenderData(SurfaceGroup group)
    {
        Vector3 averageNormal = Vector3.zero;
        Vector3 averagePoint = Vector3.zero;
        float totalWeight = 0f;

        for (int i = 0; i < group.records.Count; i++)
        {
            WeightedHit hit = group.records[i];
            float w = Mathf.Max(0f, hit.weight);
            averageNormal += hit.record.hitNormal * w;
            averagePoint += hit.record.hitPoint * w;
            totalWeight += w;
        }

        if (totalWeight <= 0.000001f)
        {
            return null;
        }

        averagePoint /= totalWeight;

        if (averageNormal.sqrMagnitude < 0.000001f)
        {
            averageNormal = Vector3.up;
        }
        else
        {
            averageNormal.Normalize();
        }

        Vector3 referenceUp = Mathf.Abs(Vector3.Dot(averageNormal, Vector3.up)) > 0.95f
            ? Vector3.forward
            : Vector3.up;

        Vector3 uAxis = Vector3.Cross(referenceUp, averageNormal);
        if (uAxis.sqrMagnitude < 0.000001f)
        {
            uAxis = Vector3.right;
        }
        else
        {
            uAxis.Normalize();
        }

        Vector3 vAxis = Vector3.Cross(averageNormal, uAxis);
        if (vAxis.sqrMagnitude < 0.000001f)
        {
            vAxis = Vector3.up;
        }
        else
        {
            vAxis.Normalize();
        }

        float minU = float.MaxValue;
        float maxU = float.MinValue;
        float minV = float.MaxValue;
        float maxV = float.MinValue;

        List<WeightedPoint2D> localPoints = new List<WeightedPoint2D>();

        for (int i = 0; i < group.records.Count; i++)
        {
            WeightedHit hit = group.records[i];
            Vector3 delta = hit.record.hitPoint - averagePoint;
            float u = Vector3.Dot(delta, uAxis);
            float v = Vector3.Dot(delta, vAxis);

            WeightedPoint2D p = new WeightedPoint2D();
            p.position = new Vector2(u, v);
            p.weight = Mathf.Max(0f, hit.weight);
            localPoints.Add(p);

            if (u < minU) minU = u;
            if (u > maxU) maxU = u;
            if (v < minV) minV = v;
            if (v > maxV) maxV = v;
        }

        float padding = Mathf.Max(0f, surfacePadding);
        minU -= padding;
        maxU += padding;
        minV -= padding;
        maxV += padding;

        float width = Mathf.Max(minSurfaceSize, maxU - minU);
        float height = Mathf.Max(minSurfaceSize, maxV - minV);

        if (width <= minSurfaceSize)
        {
            float centerU = (minU + maxU) * 0.5f;
            minU = centerU - minSurfaceSize * 0.5f;
            maxU = centerU + minSurfaceSize * 0.5f;
            width = minSurfaceSize;
        }

        if (height <= minSurfaceSize)
        {
            float centerV = (minV + maxV) * 0.5f;
            minV = centerV - minSurfaceSize * 0.5f;
            maxV = centerV + minSurfaceSize * 0.5f;
            height = minSurfaceSize;
        }

        int res = Mathf.Clamp(textureResolution, 32, 1024);
        float[] density = RasterizeDensity(localPoints, minU, minV, width, height, res, densityRadius);

        float localClamp;
        if (usePercentileClamp)
        {
            localClamp = ComputePercentile(density, Mathf.Clamp01(clampPercentile));
        }
        else
        {
            localClamp = Mathf.Max(0.0001f, manualMaxDensity);
        }

        if (localClamp <= 0.0001f)
        {
            localClamp = GetMaxDensity(density);
        }

        Vector3 planeCenter = averagePoint + uAxis * ((minU + maxU) * 0.5f) + vAxis * ((minV + maxV) * 0.5f);

        SurfaceRenderData data = new SurfaceRenderData();
        data.key = group.key;
        data.objectName = group.objectName;
        data.objectTag = group.objectTag;
        data.objectVariant = group.objectVariant;
        data.colliderPath = group.colliderPath;
        data.recordCount = group.records.Count;
        data.totalWeight = totalWeight;
        data.center = planeCenter;
        data.normal = averageNormal;
        data.uAxis = uAxis;
        data.vAxis = vAxis;
        data.width = width;
        data.height = height;
        data.resolution = res;
        data.density = density;
        data.localClampDensity = Mathf.Max(0.0001f, localClamp);
        return data;
    }

    private float[] RasterizeDensity(List<WeightedPoint2D> localPoints, float minU, float minV, float width, float height, int resolution, float radiusWorld)
    {
        float[] density = new float[resolution * resolution];

        if (localPoints == null || localPoints.Count == 0)
        {
            return density;
        }

        float radius = Mathf.Max(0.001f, radiusWorld);
        float sigma = Mathf.Max(0.0001f, radius * 0.5f);
        float twoSigma2 = 2f * sigma * sigma;

        float pixelSizeU = width / Mathf.Max(1, resolution - 1);
        float pixelSizeV = height / Mathf.Max(1, resolution - 1);
        int radiusPixelU = Mathf.CeilToInt(radius / Mathf.Max(0.0001f, pixelSizeU));
        int radiusPixelV = Mathf.CeilToInt(radius / Mathf.Max(0.0001f, pixelSizeV));

        for (int p = 0; p < localPoints.Count; p++)
        {
            WeightedPoint2D weightedPoint = localPoints[p];
            if (weightedPoint.weight <= 0f)
            {
                continue;
            }

            Vector2 uv = weightedPoint.position;
            float fx = (uv.x - minU) / Mathf.Max(0.0001f, width) * (resolution - 1);
            float fy = (uv.y - minV) / Mathf.Max(0.0001f, height) * (resolution - 1);

            int cx = Mathf.RoundToInt(fx);
            int cy = Mathf.RoundToInt(fy);

            int x0 = Mathf.Max(0, cx - radiusPixelU);
            int x1 = Mathf.Min(resolution - 1, cx + radiusPixelU);
            int y0 = Mathf.Max(0, cy - radiusPixelV);
            int y1 = Mathf.Min(resolution - 1, cy + radiusPixelV);

            for (int y = y0; y <= y1; y++)
            {
                float sampleV = minV + ((float)y / Mathf.Max(1, resolution - 1)) * height;
                float dv = sampleV - uv.y;

                for (int x = x0; x <= x1; x++)
                {
                    float sampleU = minU + ((float)x / Mathf.Max(1, resolution - 1)) * width;
                    float du = sampleU - uv.x;
                    float d2 = du * du + dv * dv;

                    if (d2 <= radius * radius)
                    {
                        density[y * resolution + x] += weightedPoint.weight * Mathf.Exp(-d2 / twoSigma2);
                    }
                }
            }
        }

        return density;
    }

    private void CreateSurfaceHeatmap(SurfaceRenderData data, float clampDensity, Transform parent)
    {
        Texture2D texture = CreateHeatmapTexture(data.density, data.resolution, clampDensity);
        Mesh mesh = CreateDoubleSidedPlaneMesh(data.width, data.height);

        GameObject obj = new GameObject("DensitySurface_" + SanitizeName(data.objectTag) + "_" + SanitizeName(data.objectName));
        obj.transform.SetParent(parent, false);
        obj.transform.position = data.center + data.normal * surfaceOffset;
        obj.transform.rotation = Quaternion.LookRotation(data.normal, data.vAxis);

        MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
        Material material = CreateTransparentMaterial(texture, obj.name + "_Material");
        meshRenderer.sharedMaterial = material;
    }

    private Texture2D CreateHeatmapTexture(float[] density, int resolution, float clampDensity)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[resolution * resolution];
        float clamp = Mathf.Max(0.0001f, clampDensity);
        float cutoff = Mathf.Clamp01(densityCutoff);

        for (int i = 0; i < pixels.Length; i++)
        {
            float t = Mathf.Clamp01(density[i] / clamp);
            if (t <= cutoff)
            {
                pixels[i] = new Color(0f, 0f, 0f, 0f);
                continue;
            }

            float normalized = Mathf.InverseLerp(cutoff, 1f, t);
            Color c = EvaluateHeatColor(normalized);
            c.a = Mathf.Lerp(minAlpha, maxAlpha, normalized);
            pixels[i] = c;
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
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

    private Mesh CreateDoubleSidedPlaneMesh(float width, float height)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Generated_DensityHeatmap_Plane";

        float hw = width * 0.5f;
        float hh = height * 0.5f;

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-hw, -hh, 0f),
            new Vector3( hw, -hh, 0f),
            new Vector3( hw,  hh, 0f),
            new Vector3(-hw,  hh, 0f),
            new Vector3(-hw, -hh, 0f),
            new Vector3( hw, -hh, 0f),
            new Vector3( hw,  hh, 0f),
            new Vector3(-hw,  hh, 0f)
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

    private Material CreateTransparentMaterial(Texture2D texture, string materialName)
    {
        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.mainTexture = texture;
        material.color = Color.white;
        material.renderQueue = 3100;
        return material;
    }

    private float ComputeGlobalPercentile(List<SurfaceRenderData> dataList, float percentile)
    {
        List<float> values = new List<float>();
        for (int i = 0; i < dataList.Count; i++)
        {
            float[] density = dataList[i].density;
            for (int j = 0; j < density.Length; j++)
            {
                if (density[j] > 0.000001f)
                {
                    values.Add(density[j]);
                }
            }
        }

        if (values.Count == 0)
        {
            return 1f;
        }

        values.Sort();
        int index = Mathf.Clamp(Mathf.RoundToInt((values.Count - 1) * Mathf.Clamp01(percentile)), 0, values.Count - 1);
        return Mathf.Max(0.0001f, values[index]);
    }

    private float ComputePercentile(float[] values, float percentile)
    {
        if (values == null || values.Length == 0)
        {
            return 1f;
        }

        List<float> nonZeroValues = new List<float>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > 0.000001f)
            {
                nonZeroValues.Add(values[i]);
            }
        }

        if (nonZeroValues.Count == 0)
        {
            return 1f;
        }

        nonZeroValues.Sort();
        int index = Mathf.Clamp(Mathf.RoundToInt((nonZeroValues.Count - 1) * Mathf.Clamp01(percentile)), 0, nonZeroValues.Count - 1);
        return Mathf.Max(0.0001f, nonZeroValues[index]);
    }

    private float GetMaxDensity(float[] density)
    {
        float maxValue = 0f;
        if (density == null)
        {
            return 1f;
        }

        for (int i = 0; i < density.Length; i++)
        {
            if (density[i] > maxValue)
            {
                maxValue = density[i];
            }
        }

        return Mathf.Max(0.0001f, maxValue);
    }

    public void ClearHeatmap()
    {
        if (heatmapRoot != null)
        {
            Destroy(heatmapRoot);
            heatmapRoot = null;
        }
    }

    public void ClearLoadedData()
    {
        ClearHeatmap();
        participantDatasets.Clear();
        nodeGroups.Clear();
        totalLoadedRecords = 0;
        totalLoadedNodeStays = 0;
        statusMessage = "Loaded data cleared.";
    }

    private void SetAllParticipantSelection(bool selected)
    {
        for (int i = 0; i < participantDatasets.Count; i++)
        {
            participantDatasets[i].selected = selected;
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

    private void SetAllNodeGroupSelection(bool selected)
    {
        for (int i = 0; i < nodeGroups.Count; i++)
        {
            nodeGroups[i].selected = selected;
        }
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

    public void ConfigureFromReplay(string root, int task, string overrideEyeFolder)
    {
        processedRoot = root;
        taskOrder = task;
        overrideEyeFolderFullPath = overrideEyeFolder;
    }

    public void LoadFromReplay()
    {
        LoadParticipantData();
    }

    public void RefreshHeatmapFromReplay(HashSet<string> selectedNodeIDs)
    {
        ApplyNodeSelectionFromReplay(selectedNodeIDs);
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

    private void ApplyNodeSelectionFromReplay(HashSet<string> selectedNodeIDs)
    {
        if (selectedNodeIDs == null || selectedNodeIDs.Count == 0)
        {
            for (int i = 0; i < nodeGroups.Count; i++)
            {
                nodeGroups[i].selected = true;
            }
            return;
        }

        for (int i = 0; i < nodeGroups.Count; i++)
        {
            nodeGroups[i].selected = selectedNodeIDs.Contains(nodeGroups[i].nodeID);
        }
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

    private bool DetermineGazeValid(List<string> row, int idxGazeValid, GazeRecord record)
    {
        if (idxGazeValid >= 0)
        {
            string value = SafeGet(row, idxGazeValid).Trim();
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return IsVectorFinite(record.hitPoint);
    }

    private bool DetermineGazeHit(List<string> row, int idxGazeHit, GazeRecord record)
    {
        if (idxGazeHit >= 0)
        {
            string value = SafeGet(row, idxGazeHit).Trim();
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (!StringIsNullOrWhiteSpace(record.hitObjectName) &&
            record.hitObjectName != "0" &&
            !record.hitObjectName.Equals("NoHit", StringComparison.OrdinalIgnoreCase) &&
            !record.hitObjectName.Equals("InvalidGaze", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return record.hitPoint.sqrMagnitude > 0.000001f;
    }

    private bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private bool IsVectorFinite(Vector3 v)
    {
        return !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
               !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
               !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }

    private bool StringIsNullOrWhiteSpace(string text)
    {
        return text == null || text.Trim().Length == 0;
    }

    private string SanitizeName(string value)
    {
        if (StringIsNullOrWhiteSpace(value))
        {
            return "None";
        }

        string s = value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
        {
            s = s.Replace(invalid[i], '_');
        }
        s = s.Replace('/', '_').Replace('\\', '_').Replace('|', '_').Replace(':', '_');
        return s;
    }

    private class ParticipantDataset
    {
        public int participantIndex;
        public string participantID;
        public string sourceFileName;
        public string csvPath;
        public bool selected = true;
        public bool hasRecordedHitNormal = false;
        public List<GazeRecord> records = new List<GazeRecord>();
        public List<NodeStaySegment> nodeStays = new List<NodeStaySegment>();
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
        public Vector3 hitNormal;
        public string hitObjectName;
        public string hitObjectTag;
        public string hitObjectVariant;
        public string hitColliderPath;
        public bool gazeValid;
        public bool gazeHit;
    }

    private class NodeStaySegment
    {
        public int participantIndex;
        public string participantID;
        public string sourceFileName;
        public string nodeID;
        public int startRecordIndex;
        public int endRecordIndex;
        public string startTime;
        public string endTime;

        public int RecordCount
        {
            get { return endRecordIndex - startRecordIndex + 1; }
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
        public int stayCount;
        public int recordCount;
        public HashSet<int> participantIndices = new HashSet<int>();
    }

    private class ParticipantHitCandidates
    {
        public ParticipantDataset dataset;
        public List<GazeRecord> records = new List<GazeRecord>();
    }

    private class WeightedHit
    {
        public GazeRecord record;
        public float weight = 1f;
    }

    private class WeightedPoint2D
    {
        public Vector2 position;
        public float weight;
    }

    private class SurfaceGroup
    {
        public string key;
        public string objectName;
        public string objectTag;
        public string objectVariant;
        public string colliderPath;
        public int recordCount
        {
            get { return records.Count; }
        }
        public float totalWeight = 0f;
        public List<WeightedHit> records = new List<WeightedHit>();
    }

    private class SurfaceRenderData
    {
        public string key;
        public string objectName;
        public string objectTag;
        public string objectVariant;
        public string colliderPath;
        public int recordCount;
        public float totalWeight;
        public Vector3 center;
        public Vector3 normal;
        public Vector3 uAxis;
        public Vector3 vAxis;
        public float width;
        public float height;
        public int resolution;
        public float[] density;
        public float localClampDensity;
    }
}
