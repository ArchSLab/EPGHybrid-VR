using UnityEngine;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

/// <summary>
/// Multi-participant surface splat heatmap visualizer.
///
/// Main workflow:
/// 1. Load one or more gaze-hit CSV files.
/// 2. Each CSV is treated as one participant dataset and segmented independently.
/// 3. Select participants and NodeIDs in the runtime control panel.
/// 4. Generate either a raw-sample heatmap or an equal-participant-weight heatmap.
///
/// Attach this component to a new empty GameObject.
/// Do not attach the old single-participant visualizer to the same GameObject.
/// </summary>
public class GazeSurfaceSplatHeatmapMultiVisualizer : MonoBehaviour
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
    public string processedRoot =
        @"Assets/StreamingAssets/Reorder_GazeHit";

    public int taskOrder = 1;
    public CsvLoadMode loadMode = CsvLoadMode.AllCsvInTaskFolder;
    public int singleCsvFileIndex = 1;

    [Tooltip("Optional. When filled, this folder replaces ProcessedRoot/TaskX/Eye.")]
    public string overrideEyeFolderFullPath = "";

    [TextArea(3, 8)]
    [Tooltip("Used only in ManualCsvFileList mode. Enter one full CSV path per line, or separate paths with semicolons.")]
    public string manualCsvFileList = "";

    [Header("Heatmap Display Settings")]
    public bool showControlPanel = true;
    public Rect panelRect = new Rect(20, 20, 440, 760);

    public float splatSize = 0.28f;
    public float splatAlpha = 0.38f;
    public float normalOffset = 0.01f;

    [Tooltip("Read every Nth valid row within each selected node stay.")]
    public int sampleStep = 1;

    [Tooltip("0 means unlimited. A moderate limit is recommended because each splat is a GameObject.")]
    public int maxSplats = 2500;

    public ParticipantWeightMode weightMode = ParticipantWeightMode.EqualParticipantWeight;

    public bool useDensityColor = true;
    public float densityRadius = 0.35f;
    public bool autoNormalizeDensity = true;
    public float manualMaxDensity = 25f;

    public Color coldColor = new Color(0f, 1f, 0f, 1f);
    public Color midColor = new Color(1f, 1f, 0f, 1f);
    public Color hotColor = new Color(1f, 0f, 0f, 1f);

    [Header("Panel List Heights")]
    public float participantListHeight = 170f;
    public float nodeListHeight = 260f;

    private Vector2 panelScrollPosition = Vector2.zero;
    private Vector2 participantScrollPosition = Vector2.zero;
    private Vector2 nodeScrollPosition = Vector2.zero;

    private bool panelMinimized = false;
    private bool panelClosed = false;
    private bool panelResizing = false;

    private readonly List<ParticipantDataset> participantDatasets =
        new List<ParticipantDataset>();

    private readonly List<NodeGroup> nodeGroups =
        new List<NodeGroup>();

    private GameObject heatmapRoot;
    private Material splatMaterial;
    private Mesh splatMesh;
    private Texture2D splatTexture;
    private MaterialPropertyBlock propertyBlock;

    private string participantFilter = "";
    private string nodeFilter = "";
    private string statusMessage = "No data loaded.";

    private int totalLoadedRecords = 0;
    private int totalLoadedNodeStays = 0;

    private const float MinimumPanelWidth = 390f;
    private const float MinimumPanelHeight = 260f;

    private void OnGUI()
    {
        if (!showControlPanel || panelClosed)
        {
            return;
        }

        panelRect = GUI.Window(20260612, panelRect, DrawPanel, "");
    }

    private void DrawPanel(int windowID)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(28));

        GUILayout.Label("Multi-Participant Surface Heatmap", GUILayout.Width(255));
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
        DrawHeatmapSettingsSection();
        DrawParticipantSelectionSection();
        DrawNodeSelectionSection();
        DrawGenerateSection();
        DrawStatusSection();

        GUILayout.EndScrollView();

        HandlePanelResize();

        // Drag only from the title bar to avoid conflicts with lists and sliders.
        GUI.DragWindow(new Rect(0, 0, panelRect.width - 24f, 28));
    }

    private void DrawDataSourceSection()
    {
        GUILayout.Label("Data Source", GUI.skin.box);

        GUILayout.Label("Processed Root:");
        processedRoot = GUILayout.TextField(processedRoot);

        GUILayout.BeginHorizontal();
        GUILayout.Label("TaskOrder:", GUILayout.Width(88));
        taskOrder = DrawCompactIntField(taskOrder, 1, 999999, 75f);
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
            singleCsvFileIndex = DrawCompactIntField(singleCsvFileIndex, 1, 999999, 75f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.Label("Override Eye Folder (optional):");
        overrideEyeFolderFullPath = GUILayout.TextField(overrideEyeFolderFullPath);

        if (loadMode == CsvLoadMode.ManualCsvFileList)
        {
            GUILayout.Label("Manual CSV paths (one per line or separated by ;):");
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

    private void DrawHeatmapSettingsSection()
    {
        GUILayout.Label("Heatmap Settings", GUI.skin.box);

        splatSize = FloatFieldWithSlider("Splat Size", splatSize, 0.03f, 1.5f);
        splatAlpha = FloatFieldWithSlider("Alpha", splatAlpha, 0.01f, 1.0f);
        normalOffset = FloatFieldWithSlider("Normal Offset", normalOffset, 0.000f, 0.08f);

        sampleStep = IntField("Sample Step", sampleStep, 1, 999);
        maxSplats = IntField("Max Splats", maxSplats, 0, 200000);

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

        useDensityColor = GUILayout.Toggle(useDensityColor, "Use Density Color");

        if (useDensityColor)
        {
            densityRadius = FloatFieldWithSlider("Density Radius", densityRadius, 0.03f, 2.0f);
            autoNormalizeDensity = GUILayout.Toggle(autoNormalizeDensity, "Auto Normalize Density");

            if (!autoNormalizeDensity)
            {
                manualMaxDensity = FloatFieldWithSlider(
                    "Manual Max Density",
                    manualMaxDensity,
                    0.001f,
                    200f
                );
            }
        }

        GUILayout.Space(6);
        GUILayout.Label("Color Settings", GUI.skin.box);

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

        GUILayout.Label(
            "Loaded participants: " + participantDatasets.Count +
            " | Selected: " + CountSelectedParticipants()
        );

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

        participantListHeight = DrawListHeightSlider(
            "Participant List Height",
            participantListHeight,
            90f,
            500f
        );

        participantScrollPosition = GUILayout.BeginScrollView(
            participantScrollPosition,
            false,
            true,
            GUILayout.Height(participantListHeight)
        );

        string filter = participantFilter == null
            ? ""
            : participantFilter.Trim();

        for (int i = 0; i < participantDatasets.Count; i++)
        {
            ParticipantDataset dataset = participantDatasets[i];

            if (filter.Length > 0 &&
                dataset.participantID.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                dataset.sourceFileName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            string label =
                dataset.participantID +
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

        GUILayout.Label(
            "NodeIDs: " + nodeGroups.Count +
            " | Selected: " + CountSelectedNodeGroups()
        );

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

        nodeListHeight = DrawListHeightSlider(
            "Node List Height",
            nodeListHeight,
            100f,
            650f
        );

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

        if (GUILayout.Button("Generate / Refresh Heatmap", GUILayout.Height(34)))
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
        GUILayout.TextArea(statusMessage, GUILayout.Height(110));
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

    private float DrawListHeightSlider(
        string label,
        float value,
        float minimum,
        float maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(150));
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

    private float FloatFieldWithSlider(string label, float value, float minimum, float maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(132));

        string text = GUILayout.TextField(
            value.ToString("G4", CultureInfo.InvariantCulture),
            GUILayout.Width(72)
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

        value = GUILayout.HorizontalSlider(value, minimum, maximum);
        GUILayout.EndHorizontal();

        return value;
    }

    private int IntField(string label, int value, int minimum, int maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(132));

        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(72));
        int parsed;

        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Clamp(parsed, minimum, maximum);
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
        value = GUILayout.HorizontalSlider(value, 0f, 1f);
        GUILayout.Label(value.ToString("F2"), GUILayout.Width(40));
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
            statusMessage =
                "No CSV files were found.\n" +
                "Resolved folder: " + ResolveEyeFolderPath();

            Debug.LogError(statusMessage);
            return;
        }

        int failedCount = 0;
        StringBuilder failureDetails = new StringBuilder();

        for (int i = 0; i < csvPaths.Count; i++)
        {
            string csvPath = csvPaths[i];

            ParticipantDataset dataset;
            string errorMessage;

            if (TryLoadParticipantCsv(csvPath, i, out dataset, out errorMessage))
            {
                participantDatasets.Add(dataset);
                totalLoadedRecords += dataset.records.Count;
                totalLoadedNodeStays += dataset.nodeStays.Count;
            }
            else
            {
                failedCount++;
                failureDetails.AppendLine(Path.GetFileName(csvPath) + ": " + errorMessage);
                Debug.LogWarning("Failed to load participant CSV: " + csvPath + "\n" + errorMessage);
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
            string normalized = manualCsvFileList == null
                ? ""
                : manualCsvFileList.Replace("\r\n", "\n").Replace('\r', '\n');

            string[] parts = normalized.Split(
                new char[] { '\n', ';' },
                StringSplitOptions.RemoveEmptyEntries
            );

            HashSet<string> uniquePaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        string[] csvFiles = Directory.GetFiles(
            eyeFolder,
            "*.csv",
            SearchOption.TopDirectoryOnly
        );

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

        return Path.Combine(
            Path.Combine(processedRoot, "Task" + taskOrder),
            "Eye"
        );
    }

    private bool TryLoadParticipantCsv(
        string csvPath,
        int participantIndex,
        out ParticipantDataset dataset,
        out string errorMessage)
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

        int idxSceneTime = FindColumnIndex(header, "SceneTime");
        int idxTimestamp = FindColumnIndex(header, "Timestamp");

        if (idxNodeID < 0 || idxHitX < 0 || idxHitY < 0 || idxHitZ < 0)
        {
            errorMessage =
                "Missing required columns. Required: NodeID, HitPointX, HitPointY, HitPointZ.";
            return false;
        }

        bool hasNormal =
            idxNormalX >= 0 &&
            idxNormalY >= 0 &&
            idxNormalZ >= 0;

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

            record.hitObjectName = idxHitObjectName >= 0
                ? SafeGet(row, idxHitObjectName)
                : "";

            record.hitObjectTag = idxHitObjectTag >= 0
                ? SafeGet(row, idxHitObjectTag)
                : "";

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
        // The script intentionally uses the CSV filename as the participant label.
        // This is robust to different naming conventions and keeps the source traceable.
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
        segment.participantIndex = dataset.participantIndex;
        segment.participantID = dataset.participantID;
        segment.sourceFileName = dataset.sourceFileName;
        segment.nodeID = nodeID;
        segment.startRecordIndex = startIndex;
        segment.endRecordIndex = endIndex;

        if (startIndex >= 0 && startIndex < dataset.records.Count)
        {
            segment.startTime =
                !StringIsNullOrWhiteSpace(dataset.records[startIndex].sceneTime)
                ? dataset.records[startIndex].sceneTime
                : dataset.records[startIndex].timestamp;
        }

        if (endIndex >= 0 && endIndex < dataset.records.Count)
        {
            segment.endTime =
                !StringIsNullOrWhiteSpace(dataset.records[endIndex].sceneTime)
                ? dataset.records[endIndex].sceneTime
                : dataset.records[endIndex].timestamp;
        }

        dataset.nodeStays.Add(segment);
    }

    private void BuildNodeGroups()
    {
        nodeGroups.Clear();

        Dictionary<string, NodeGroupBuilder> builders =
            new Dictionary<string, NodeGroupBuilder>(StringComparer.OrdinalIgnoreCase);

        for (int participantIndex = 0;
            participantIndex < participantDatasets.Count;
            participantIndex++)
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

        bool aIsNumber = int.TryParse(a.nodeID, out aNumber);
        bool bIsNumber = int.TryParse(b.nodeID, out bNumber);

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

        List<ParticipantHitCandidates> participantCandidates =
            CollectCandidateHitsByParticipant(selectedNodeIDs);

        if (participantCandidates.Count == 0)
        {
            statusMessage =
                "No valid gaze-hit records matched the selected participants and NodeIDs.";
            Debug.LogWarning(statusMessage);
            return;
        }

        List<WeightedHit> selectedHits =
            SampleAndWeightCandidates(participantCandidates);

        if (selectedHits.Count == 0)
        {
            statusMessage = "No valid gaze-hit records remained after sampling.";
            Debug.LogWarning(statusMessage);
            return;
        }

        EnsureHeatmapResources();

        float[] densities = null;
        float maxDensity = 1f;

        if (useDensityColor)
        {
            densities = ComputeDensitiesSpatialHash(selectedHits, densityRadius);
            maxDensity = GetMaxDensity(densities);

            if (!autoNormalizeDensity)
            {
                maxDensity = Mathf.Max(0.0001f, manualMaxDensity);
            }
        }

        heatmapRoot = new GameObject(
            "SurfaceSplatHeatmap_Multi_Task" + taskOrder
        );
        heatmapRoot.transform.SetParent(transform, false);

        // In equal-participant mode, density already uses per-participant weights.
        // The alpha below is also normalized so a participant with more rows does
        // not become visually stronger merely because more transparent quads overlap.
        int renderedParticipantCount = CountRenderedParticipants(selectedHits);
        float averageSamplesPerRenderedParticipant =
            (float)selectedHits.Count / Mathf.Max(1, renderedParticipantCount);

        for (int i = 0; i < selectedHits.Count; i++)
        {
            float normalizedDensity = 1f;

            if (useDensityColor && densities != null)
            {
                normalizedDensity = Mathf.Clamp01(
                    densities[i] / Mathf.Max(0.0001f, maxDensity)
                );
            }

            Color color = useDensityColor
                ? EvaluateHeatColor(normalizedDensity)
                : hotColor;

            float renderedAlpha = splatAlpha;

            if (weightMode == ParticipantWeightMode.EqualParticipantWeight)
            {
                renderedAlpha =
                    splatAlpha *
                    selectedHits[i].weight *
                    averageSamplesPerRenderedParticipant;
            }

            color.a = Mathf.Clamp01(renderedAlpha);

            CreateSplat(
                selectedHits[i].record.hitPoint,
                selectedHits[i].record.hitNormal,
                color,
                heatmapRoot.transform
            );
        }

        int candidateCount = 0;
        for (int i = 0; i < participantCandidates.Count; i++)
        {
            candidateCount += participantCandidates[i].records.Count;
        }

        statusMessage =
            "Heatmap generated.\n" +
            "Selected participants with valid hits: " + participantCandidates.Count + "\n" +
            "Selected NodeIDs: " + selectedNodeIDs.Count + "\n" +
            "Candidate hit records: " + candidateCount + "\n" +
            "Rendered splats: " + selectedHits.Count + "\n" +
            "Weight mode: " + weightMode + "\n" +
            "Density radius: " + densityRadius.ToString("F3", CultureInfo.InvariantCulture) + "\n" +
            "Max density: " + maxDensity.ToString("F4", CultureInfo.InvariantCulture);

        Debug.Log(statusMessage);
    }

    private int CountRenderedParticipants(List<WeightedHit> selectedHits)
    {
        HashSet<int> participantIndices = new HashSet<int>();

        for (int i = 0; i < selectedHits.Count; i++)
        {
            participantIndices.Add(selectedHits[i].record.participantIndex);
        }

        return participantIndices.Count;
    }

    private HashSet<string> GetSelectedNodeIDs()
    {
        HashSet<string> selectedNodeIDs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < nodeGroups.Count; i++)
        {
            if (nodeGroups[i].selected)
            {
                selectedNodeIDs.Add(nodeGroups[i].nodeID);
            }
        }

        return selectedNodeIDs;
    }

    private List<ParticipantHitCandidates> CollectCandidateHitsByParticipant(
        HashSet<string> selectedNodeIDs)
    {
        List<ParticipantHitCandidates> result =
            new List<ParticipantHitCandidates>();

        int step = Mathf.Max(1, sampleStep);

        for (int datasetIndex = 0;
            datasetIndex < participantDatasets.Count;
            datasetIndex++)
        {
            ParticipantDataset dataset = participantDatasets[datasetIndex];

            if (!dataset.selected)
            {
                continue;
            }

            ParticipantHitCandidates candidates = new ParticipantHitCandidates();
            candidates.dataset = dataset;

            for (int stayIndex = 0;
                stayIndex < dataset.nodeStays.Count;
                stayIndex++)
            {
                NodeStaySegment stay = dataset.nodeStays[stayIndex];

                if (!selectedNodeIDs.Contains(stay.nodeID))
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

                    if (!record.gazeValid || !record.gazeHit)
                    {
                        continue;
                    }

                    if (!IsVectorFinite(record.hitPoint))
                    {
                        continue;
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

    private List<WeightedHit> SampleAndWeightCandidates(
        List<ParticipantHitCandidates> participantCandidates)
    {
        List<WeightedHit> result = new List<WeightedHit>();

        int totalCandidates = 0;
        for (int i = 0; i < participantCandidates.Count; i++)
        {
            totalCandidates += participantCandidates[i].records.Count;
        }

        int limit = maxSplats <= 0
            ? totalCandidates
            : Mathf.Min(maxSplats, totalCandidates);

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

        for (int participantIndex = 0;
            participantIndex < participantCandidates.Count;
            participantIndex++)
        {
            ParticipantHitCandidates candidates = participantCandidates[participantIndex];
            int quota = Mathf.Clamp(
                quotas[participantIndex],
                0,
                candidates.records.Count
            );

            if (quota == 0)
            {
                continue;
            }

            List<GazeRecord> sampledRecords =
                EvenlySampleRecords(candidates.records, quota);

            float recordWeight = 1f;

            if (weightMode == ParticipantWeightMode.EqualParticipantWeight)
            {
                recordWeight = 1f / Mathf.Max(1, sampledRecords.Count);
            }

            for (int recordIndex = 0;
                recordIndex < sampledRecords.Count;
                recordIndex++)
            {
                WeightedHit weightedHit = new WeightedHit();
                weightedHit.record = sampledRecords[recordIndex];
                weightedHit.weight = recordWeight;
                result.Add(weightedHit);
            }
        }

        return result;
    }

    private int[] AllocateEqualQuotas(
        List<ParticipantHitCandidates> participantCandidates,
        int limit)
    {
        int participantCount = participantCandidates.Count;
        int[] quotas = new int[participantCount];

        if (participantCount == 0 || limit <= 0)
        {
            return quotas;
        }

        int remaining = limit;
        bool assignedInPass = true;

        // Round-robin allocation ensures no participant is omitted merely
        // because its file appears later in the folder.
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

    private int[] AllocateProportionalQuotas(
        List<ParticipantHitCandidates> participantCandidates,
        int limit)
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
            float exact =
                (float)participantCandidates[i].records.Count /
                (float)total *
                limit;

            int floorValue = Mathf.FloorToInt(exact);
            floorValue = Mathf.Min(
                floorValue,
                participantCandidates[i].records.Count
            );

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

    private List<GazeRecord> EvenlySampleRecords(
        List<GazeRecord> source,
        int targetCount)
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

    private float[] ComputeDensitiesSpatialHash(
        List<WeightedHit> selectedHits,
        float radius)
    {
        int count = selectedHits.Count;
        float[] densities = new float[count];

        float safeRadius = Mathf.Max(0.001f, radius);
        float radiusSquared = safeRadius * safeRadius;
        float sigma = safeRadius * 0.5f;
        float twoSigmaSquared = 2f * sigma * sigma;
        float cellSize = safeRadius;

        Dictionary<GridKey, List<int>> grid =
            new Dictionary<GridKey, List<int>>();

        for (int i = 0; i < count; i++)
        {
            GridKey key = GetGridKey(selectedHits[i].record.hitPoint, cellSize);
            List<int> indices;

            if (!grid.TryGetValue(key, out indices))
            {
                indices = new List<int>();
                grid.Add(key, indices);
            }

            indices.Add(i);
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 point = selectedHits[i].record.hitPoint;
            GridKey centerKey = GetGridKey(point, cellSize);
            float density = 0f;

            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                for (int yOffset = -1; yOffset <= 1; yOffset++)
                {
                    for (int zOffset = -1; zOffset <= 1; zOffset++)
                    {
                        GridKey neighborKey = new GridKey(
                            centerKey.x + xOffset,
                            centerKey.y + yOffset,
                            centerKey.z + zOffset
                        );

                        List<int> neighborIndices;

                        if (!grid.TryGetValue(neighborKey, out neighborIndices))
                        {
                            continue;
                        }

                        for (int listIndex = 0;
                            listIndex < neighborIndices.Count;
                            listIndex++)
                        {
                            int j = neighborIndices[listIndex];
                            Vector3 otherPoint = selectedHits[j].record.hitPoint;
                            float squaredDistance = (point - otherPoint).sqrMagnitude;

                            if (squaredDistance <= radiusSquared)
                            {
                                density +=
                                    selectedHits[j].weight *
                                    Mathf.Exp(-squaredDistance / twoSigmaSquared);
                            }
                        }
                    }
                }
            }

            densities[i] = density;
        }

        return densities;
    }

    private GridKey GetGridKey(Vector3 point, float cellSize)
    {
        return new GridKey(
            Mathf.FloorToInt(point.x / cellSize),
            Mathf.FloorToInt(point.y / cellSize),
            Mathf.FloorToInt(point.z / cellSize)
        );
    }

    private float GetMaxDensity(float[] densities)
    {
        if (densities == null || densities.Length == 0)
        {
            return 1f;
        }

        float maximum = 0f;

        for (int i = 0; i < densities.Length; i++)
        {
            if (densities[i] > maximum)
            {
                maximum = densities[i];
            }
        }

        return Mathf.Max(0.0001f, maximum);
    }

    private Color EvaluateHeatColor(float value)
    {
        float t = Mathf.Clamp01(value);

        if (t < 0.5f)
        {
            return Color.Lerp(coldColor, midColor, t / 0.5f);
        }

        return Color.Lerp(midColor, hotColor, (t - 0.5f) / 0.5f);
    }

    private void CreateSplat(
        Vector3 hitPoint,
        Vector3 hitNormal,
        Color color,
        Transform parent)
    {
        GameObject splatObject = new GameObject("HeatSplat");
        splatObject.transform.SetParent(parent, false);

        Vector3 normal = hitNormal;

        if (normal.sqrMagnitude < 0.000001f)
        {
            normal = Vector3.up;
        }
        else
        {
            normal.Normalize();
        }

        Vector3 upReference =
            Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.98f
            ? Vector3.right
            : Vector3.up;

        splatObject.transform.position = hitPoint + normal * normalOffset;
        //splatObject.transform.position = hitPoint;

        splatObject.transform.rotation = Quaternion.LookRotation(normal, upReference);
        splatObject.transform.localScale = Vector3.one * splatSize;

        MeshFilter meshFilter = splatObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = splatMesh;

        MeshRenderer meshRenderer = splatObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = splatMaterial;

        propertyBlock.Clear();
        propertyBlock.SetColor("_Color", color);
        meshRenderer.SetPropertyBlock(propertyBlock);
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
        participantScrollPosition = Vector2.zero;
        nodeScrollPosition = Vector2.zero;
        statusMessage = "Loaded participant data cleared.";
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
            splatMaterial.name = "Generated_Multi_SurfaceSplat_Material";
            splatMaterial.mainTexture = splatTexture;
            splatMaterial.renderQueue = 3100;
        }
    }

    private Texture2D CreateSoftCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(
            size,
            size,
            TextureFormat.ARGB32,
            false
        );

        texture.name = "Generated_Multi_SurfaceSplat_Texture";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / radius;
                float dy = (y - center) / radius;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha;

                if (distance >= 1f)
                {
                    alpha = 0f;
                }
                else
                {
                    float inner = 0.15f;
                    float fade = Mathf.InverseLerp(1f, inner, distance);
                    alpha = Mathf.SmoothStep(0f, 1f, fade);
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private Mesh CreateDoubleSidedQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Generated_Multi_DoubleSided_Splat_Quad";

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

        using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
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
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
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
            string current = header[i];

            if (current == null)
            {
                continue;
            }

            current = current.Trim().Trim('\uFEFF');

            if (string.Equals(
                current,
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

    private Vector3 ReadVector3(List<string> row, int xIndex, int yIndex, int zIndex)
    {
        float x = 0f;
        float y = 0f;
        float z = 0f;

        TryParseFloat(SafeGet(row, xIndex), out x);
        TryParseFloat(SafeGet(row, yIndex), out y);
        TryParseFloat(SafeGet(row, zIndex), out z);

        return new Vector3(x, y, z);
    }

    private bool DetermineGazeValid(
        List<string> row,
        int gazeValidIndex,
        GazeRecord record)
    {
        if (gazeValidIndex >= 0)
        {
            string value = SafeGet(row, gazeValidIndex).Trim();

            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return IsVectorFinite(record.hitPoint);
    }

    private bool DetermineGazeHit(
        List<string> row,
        int gazeHitIndex,
        GazeRecord record)
    {
        if (gazeHitIndex >= 0)
        {
            string value = SafeGet(row, gazeHitIndex).Trim();

            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!StringIsNullOrWhiteSpace(record.hitObjectName) &&
            record.hitObjectName != "0" &&
            record.hitObjectName != "NoHit" &&
            record.hitObjectName != "InvalidGaze")
        {
            return true;
        }

        return record.hitPoint.sqrMagnitude > 0.000001f;
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

    private bool StringIsNullOrWhiteSpace(string text)
    {
        return text == null || text.Trim().Length == 0;
    }

    private void OnDestroy()
    {
        if (splatMaterial != null)
        {
            Destroy(splatMaterial);
            splatMaterial = null;
        }

        if (splatTexture != null)
        {
            Destroy(splatTexture);
            splatTexture = null;
        }

        if (splatMesh != null)
        {
            Destroy(splatMesh);
            splatMesh = null;
        }
    }

    private class ParticipantDataset
    {
        public int participantIndex;
        public string participantID;
        public string csvPath;
        public string sourceFileName;
        public bool selected = true;
        public bool hasRecordedHitNormal;

        public readonly List<GazeRecord> records = new List<GazeRecord>();
        public readonly List<NodeStaySegment> nodeStays = new List<NodeStaySegment>();
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
        public int stayCount;
        public int recordCount;
        public readonly HashSet<int> participantIndices = new HashSet<int>();
    }

    private class ParticipantHitCandidates
    {
        public ParticipantDataset dataset;
        public readonly List<GazeRecord> records = new List<GazeRecord>();
    }

    private class WeightedHit
    {
        public GazeRecord record;
        public float weight;
    }

    private struct GridKey : IEquatable<GridKey>
    {
        public int x;
        public int y;
        public int z;

        public GridKey(int xValue, int yValue, int zValue)
        {
            x = xValue;
            y = yValue;
            z = zValue;
        }

        public bool Equals(GridKey other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is GridKey))
            {
                return false;
            }

            return Equals((GridKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                return hash;
            }
        }
    }
}
