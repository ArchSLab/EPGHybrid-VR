using UnityEngine;
using UnityEngine.SceneManagement;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class GazeSingleCameraReplay : MonoBehaviour
{
    [Header("Data Path Settings")]
    public string processedRoot;

    public int taskOrder = 1;
    public int csvFileIndex = 1;
    public string overrideCsvFullPath = "";

    [Header("Replay Camera")]
    public Camera replayCamera;
    public bool autoCreateReplayCamera = true;

    public bool applyRecordedCameraParams = true;
    public bool applyRecordedAspectRatio = false;
    public bool useRecordedVerticalFOV = true;

    public float manualVerticalFOV = 90f;
    public float manualNearClip = 0.3f;
    public float manualFarClip = 1000f;

    public Vector3 rotationOffsetEuler = Vector3.zero;
    public Vector3 positionOffset = Vector3.zero;

    [Header("Panorama Switching")]
    public bool switchPanoramaOnNodeChange = true;
    public bool useUniversalPanoramaController = true;
    public UniversalPanoramaTest panoramaController;

    public string sceneIDOverride = "";

    public bool removeNodePrefixForPanorama = true;
    public bool logPanoramaSwitch = true;

    private string lastPanoramaSceneID = "";
    private string lastPanoramaNodeID = "";
    private Coroutine panoramaSwitchCoroutine = null;
    private bool warnedMissingPanoramaManager = false;
    private bool warnedMissingUniversalController = false;

    [Header("Playback Settings")]
    public bool playOnLoad = false;
    public bool isPlaying = false;
    public bool loopPlayback = true;

    public float playbackFPS = 90f;
    public float playbackSpeed = 1f;

    public bool replayAllRecords = true;

    [Header("Integrated Visualization")]
    public bool useIntegratedVisualization = true;

    public GazeHitCsvVisualizer gazeHitVisualizer;
    public GazeSurfaceSplatHeatmapVisualizer heatmapVisualizer;

    public bool hideSubVisualizerPanels = true;

    public bool showStaticGazeRays = false;
    public bool showStaticHitPoints = false;
    public bool showSurfaceHeatmap = false;

    public bool autoFollowCurrentNodeVisualization = true;

    private bool visualizationDataLoaded = false;

    private int lastAutoVisualizationNodeStayIndex = -999;
    private bool lastShowStaticGazeRays = false;
    private bool lastShowStaticHitPoints = false;
    private bool lastShowSurfaceHeatmap = false;

    [Header("Current Frame Gaze Overlay")]
    public bool showCurrentGazeRay = true;
    public bool showCurrentHitPoint = true;

    public float noHitRayLength = 10f;
    public float gazeRayWidth = 0.015f;
    public float hitPointSize = 0.12f;

    public Color gazeRayColor = new Color(0f, 1f, 0f, 0.75f);
    public Color hitPointColor = new Color(1f, 0.05f, 0f, 0.9f);

    [Header("Panel Settings")]
    public bool showControlPanel = true;
    public Rect panelRect = new Rect(20, 20, 650, 720);
    public float nodeListHeight = 260f;

    private bool panelMinimized = false;
    private Vector2 lastExpandedPanelSize = new Vector2(650, 720);
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

    private PanelResizeMode panelResizeMode = PanelResizeMode.None;

    private const float PanelResizeBorderSize = 8f;
    private const float PanelContentMinWidth = 760f;

    private Vector2 panelScrollPosition = Vector2.zero;
    private Vector2 nodeScrollPosition = Vector2.zero;

    private List<ReplayRecord> records = new List<ReplayRecord>();
    private List<NodeStaySegment> nodeStays = new List<NodeStaySegment>();
    private List<int> playbackIndices = new List<int>();

    private int playbackCursor = 0;
    private float playbackTimer = 0f;

    private string loadedCsvPath = "";
    private string statusMessage = "No data loaded.";

    private LineRenderer gazeLine;
    private GameObject hitPointSphere;
    private Material gazeLineMaterial;
    private Material hitPointMaterial;

    private const float MinPanelWidth = 420f;
    private const float MinPanelHeight = 160f;
    private const float MinimizedPanelHeight = 48f;

    private const BindingFlags InstanceBindings =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        processedRoot = ProjectPathConfig.GazeHitDataRoot;
    }

    private void Update()
    {
        if (!isPlaying || playbackIndices.Count == 0)
        {
            return;
        }

        float fps = Mathf.Max(1f, playbackFPS);
        float interval = 1f / fps;

        playbackTimer += Time.deltaTime * Mathf.Max(0.01f, playbackSpeed);

        while (playbackTimer >= interval)
        {
            playbackTimer -= interval;
            StepForward();
        }
    }

    private void OnGUI()
    {
        if (!showControlPanel)
        {
            return;
        }

        panelRect.width = Mathf.Max(MinPanelWidth, panelRect.width);
        panelRect.height = panelMinimized
            ? MinimizedPanelHeight
            : Mathf.Max(MinPanelHeight, panelRect.height);

        panelRect = GUI.Window(20260502, panelRect, DrawPanel, "");
    }

    private void DrawPanel(int windowID)
    {
        DrawPanelHeader();

        if (panelMinimized)
        {
            HandlePanelResize();
            GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));
            return;
        }

        panelScrollPosition = GUILayout.BeginScrollView(
     panelScrollPosition,
     true,
     true,
     GUILayout.Width(panelRect.width - 14),
     GUILayout.Height(panelRect.height - 62)
 );

        // Give the content a minimum width so that a horizontal scrollbar appears
        // when the panel is narrower than the content.
        GUILayout.BeginVertical(GUILayout.MinWidth(PanelContentMinWidth));

        DrawDataSourceSection();
        DrawCameraSettingsSection();
        DrawPanoramaSwitchingSection();
        DrawPlaybackSection();
        DrawCurrentFrameGazeOverlaySection();
        DrawIntegratedVisualizationSection();
        DrawNodeStaySection();
        DrawCurrentFrameSection();
        DrawStatusSection();

        GUILayout.EndVertical();

        GUILayout.EndScrollView();

        HandlePanelResize();

        // Allow dragging from any empty area of the panel.
        // Buttons, text fields, sliders, and scrollbars will still receive their own events first.
        GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));
    }

    private void DrawPanelHeader()
    {
        GUILayout.BeginHorizontal(GUILayout.Height(28));

        GUILayout.Label("Single Camera Gaze Replay", GUILayout.Width(240));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button(panelMinimized ? "Expand" : "Minimize", GUILayout.Width(80), GUILayout.Height(24)))
        {
            TogglePanelMinimized();
        }

        if (!panelMinimized)
        {
            if (GUILayout.Button("Reset Size", GUILayout.Width(85), GUILayout.Height(24)))
            {
                panelRect.width = 650;
                panelRect.height = 720;
                lastExpandedPanelSize = new Vector2(panelRect.width, panelRect.height);
            }

            if (GUILayout.Button("Hide", GUILayout.Width(55), GUILayout.Height(24)))
            {
                showControlPanel = false;
            }
        }

        GUILayout.EndHorizontal();

        GUI.Box(new Rect(0, 29, panelRect.width, 1), "");
    }

    private void TogglePanelMinimized()
    {
        if (!panelMinimized)
        {
            lastExpandedPanelSize = new Vector2(panelRect.width, panelRect.height);
            panelMinimized = true;
            panelRect.height = MinimizedPanelHeight;
        }
        else
        {
            panelMinimized = false;
            panelRect.width = Mathf.Max(MinPanelWidth, lastExpandedPanelSize.x);
            panelRect.height = Mathf.Max(MinPanelHeight, lastExpandedPanelSize.y);
        }
    }

    private void HandlePanelResize()
    {
        Event e = Event.current;

        PanelResizeMode hitMode = GetPanelResizeMode(e.mousePosition);

        if (e.type == EventType.MouseDown && hitMode != PanelResizeMode.None)
        {
            panelResizeMode = hitMode;
            e.Use();
        }

        if (panelResizeMode != PanelResizeMode.None && e.type == EventType.MouseDrag)
        {
            Vector2 delta = e.delta;

            float x = panelRect.x;
            float y = panelRect.y;
            float w = panelRect.width;
            float h = panelRect.height;

            bool resizeLeft =
                panelResizeMode == PanelResizeMode.Left ||
                panelResizeMode == PanelResizeMode.TopLeft ||
                panelResizeMode == PanelResizeMode.BottomLeft;

            bool resizeRight =
                panelResizeMode == PanelResizeMode.Right ||
                panelResizeMode == PanelResizeMode.TopRight ||
                panelResizeMode == PanelResizeMode.BottomRight;

            bool resizeTop =
                panelResizeMode == PanelResizeMode.Top ||
                panelResizeMode == PanelResizeMode.TopLeft ||
                panelResizeMode == PanelResizeMode.TopRight;

            bool resizeBottom =
                panelResizeMode == PanelResizeMode.Bottom ||
                panelResizeMode == PanelResizeMode.BottomLeft ||
                panelResizeMode == PanelResizeMode.BottomRight;

            if (resizeLeft)
            {
                float newWidth = Mathf.Max(MinPanelWidth, w - delta.x);
                float actualDelta = w - newWidth;
                x += actualDelta;
                w = newWidth;
            }

            if (resizeRight)
            {
                w = Mathf.Max(MinPanelWidth, w + delta.x);
            }

            if (!panelMinimized)
            {
                if (resizeTop)
                {
                    float newHeight = Mathf.Max(MinPanelHeight, h - delta.y);
                    float actualDelta = h - newHeight;
                    y += actualDelta;
                    h = newHeight;
                }

                if (resizeBottom)
                {
                    h = Mathf.Max(MinPanelHeight, h + delta.y);
                }
            }
            else
            {
                h = MinimizedPanelHeight;
            }

            panelRect.x = x;
            panelRect.y = y;
            panelRect.width = w;
            panelRect.height = h;

            if (!panelMinimized)
            {
                lastExpandedPanelSize = new Vector2(panelRect.width, panelRect.height);
            }

            e.Use();
        }

        if (e.type == EventType.MouseUp)
        {
            panelResizeMode = PanelResizeMode.None;
        }

        DrawResizeHints();
    }

    private PanelResizeMode GetPanelResizeMode(Vector2 mousePosition)
    {
        float b = PanelResizeBorderSize;
        float w = panelRect.width;
        float h = panelRect.height;

        bool left = mousePosition.x >= 0 && mousePosition.x <= b;
        bool right = mousePosition.x >= w - b && mousePosition.x <= w;
        bool top = mousePosition.y >= 0 && mousePosition.y <= b;
        bool bottom = mousePosition.y >= h - b && mousePosition.y <= h;

        if (left && top)
        {
            return PanelResizeMode.TopLeft;
        }

        if (right && top)
        {
            return PanelResizeMode.TopRight;
        }

        if (left && bottom)
        {
            return PanelResizeMode.BottomLeft;
        }

        if (right && bottom)
        {
            return PanelResizeMode.BottomRight;
        }

        if (left)
        {
            return PanelResizeMode.Left;
        }

        if (right)
        {
            return PanelResizeMode.Right;
        }

        if (top)
        {
            return PanelResizeMode.Top;
        }

        if (bottom)
        {
            return PanelResizeMode.Bottom;
        }

        return PanelResizeMode.None;
    }

    private void DrawResizeHints()
    {
        float b = PanelResizeBorderSize;

        // Thin visual hints for resizable edges.
        // They are intentionally subtle and do not affect layout.
        Color oldColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.18f);

        GUI.Box(new Rect(0, 0, panelRect.width, b), "");
        GUI.Box(new Rect(0, panelRect.height - b, panelRect.width, b), "");
        GUI.Box(new Rect(0, 0, b, panelRect.height), "");
        GUI.Box(new Rect(panelRect.width - b, 0, b, panelRect.height), "");

        GUI.color = oldColor;
    }

    private void DrawDataSourceSection()
    {
        GUILayout.Space(8);
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

        if (GUILayout.Button("Clear Current Overlay", GUILayout.Height(28)))
        {
            ClearGazeOverlay();
        }

        GUILayout.EndHorizontal();
    }

    private void DrawCameraSettingsSection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Camera Settings", GUI.skin.box);

        applyRecordedCameraParams = GUILayout.Toggle(applyRecordedCameraParams, "Apply Recorded Camera Params");
        applyRecordedAspectRatio = GUILayout.Toggle(applyRecordedAspectRatio, "Apply Recorded Aspect Ratio");
        useRecordedVerticalFOV = GUILayout.Toggle(useRecordedVerticalFOV, "Use Recorded Vertical FOV");

        manualVerticalFOV = FloatField("Manual Vertical FOV", manualVerticalFOV, 10f, 170f);
        manualNearClip = FloatField("Manual Near Clip", manualNearClip, 0.001f, 10f);
        manualFarClip = FloatField("Manual Far Clip", manualFarClip, 10f, 5000f);

        rotationOffsetEuler = Vector3Field("Rotation Offset Euler", rotationOffsetEuler);
        positionOffset = Vector3Field("Position Offset", positionOffset);
    }

    private void DrawPanoramaSwitchingSection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Panorama Switching", GUI.skin.box);

        switchPanoramaOnNodeChange = GUILayout.Toggle(
            switchPanoramaOnNodeChange,
            "Switch Panorama On Node Change"
        );

        useUniversalPanoramaController = GUILayout.Toggle(
            useUniversalPanoramaController,
            "Use Universal Panorama Controller"
        );

        removeNodePrefixForPanorama = GUILayout.Toggle(
            removeNodePrefixForPanorama,
            "Remove Node Prefix For Panorama"
        );

        logPanoramaSwitch = GUILayout.Toggle(
            logPanoramaSwitch,
            "Log Panorama Switch"
        );

        GUILayout.Label("Scene ID Override:");
        sceneIDOverride = GUILayout.TextField(sceneIDOverride);

        GUILayout.Label("Last Panorama: Scene " + lastPanoramaSceneID + " / Node " + lastPanoramaNodeID);
    }

    private void DrawPlaybackSection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Playback", GUI.skin.box);

        bool oldReplayAllRecords = replayAllRecords;

        replayAllRecords = GUILayout.Toggle(replayAllRecords, "Replay All Records");

        if (oldReplayAllRecords != replayAllRecords)
        {
            BuildPlaybackIndicesFromSelection();
            ApplyCurrentFrame();

            if (useIntegratedVisualization && HasAnyVisualizationSwitchOn())
            {
                if (autoFollowCurrentNodeVisualization)
                {
                    ApplyIntegratedVisualizationForCurrentFrame();
                }
                else
                {
                    ApplyIntegratedVisualization();
                }
            }
        }

        GUILayout.BeginHorizontal();

        if (GUILayout.Button(isPlaying ? "Pause" : "Play", GUILayout.Height(30)))
        {
            isPlaying = !isPlaying;
        }

        if (GUILayout.Button("Prev", GUILayout.Height(30)))
        {
            StepBackward();
        }

        if (GUILayout.Button("Next", GUILayout.Height(30)))
        {
            StepForward();
        }

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Apply Node Selection", GUILayout.Height(28)))
        {
            BuildPlaybackIndicesFromSelection();
            ApplyCurrentFrame();

            if (useIntegratedVisualization && HasAnyVisualizationSwitchOn())
            {
                if (autoFollowCurrentNodeVisualization)
                {
                    ApplyIntegratedVisualizationForCurrentFrame();
                }
                else
                {
                    ApplyIntegratedVisualization();
                }
            }
        }

        if (GUILayout.Button("Jump First", GUILayout.Height(28)))
        {
            playbackCursor = 0;
            ApplyCurrentFrame();
        }

        GUILayout.EndHorizontal();

        playbackFPS = FloatField("Playback FPS", playbackFPS, 1f, 240f);
        playbackSpeed = FloatField("Playback Speed", playbackSpeed, 0.05f, 20f);
        loopPlayback = GUILayout.Toggle(loopPlayback, "Loop Playback");

        if (playbackIndices.Count > 0)
        {
            GUILayout.Label("Frame: " + (playbackCursor + 1) + " / " + playbackIndices.Count);

            float sliderValue = GUILayout.HorizontalSlider(
                playbackCursor,
                0,
                Mathf.Max(0, playbackIndices.Count - 1)
            );

            int newCursor = Mathf.RoundToInt(sliderValue);

            if (newCursor != playbackCursor)
            {
                playbackCursor = newCursor;
                ApplyCurrentFrame();
            }
        }
        else
        {
            GUILayout.Label("Frame: 0 / 0");
        }
    }

    private void DrawCurrentFrameGazeOverlaySection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Current Frame Gaze Overlay", GUI.skin.box);

        showCurrentGazeRay = GUILayout.Toggle(showCurrentGazeRay, "Show Current Frame Gaze Ray");
        showCurrentHitPoint = GUILayout.Toggle(showCurrentHitPoint, "Show Current Frame Hit Point");

        noHitRayLength = FloatField("No-hit Ray Length", noHitRayLength, 0.1f, 100f);
        gazeRayWidth = FloatField("Gaze Ray Width", gazeRayWidth, 0.001f, 0.2f);
        hitPointSize = FloatField("Hit Point Size", hitPointSize, 0.01f, 1f);
    }

    private void DrawIntegratedVisualizationSection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Visualization Overlay", GUI.skin.box);

        bool oldUseIntegratedVisualization = useIntegratedVisualization;
        bool oldHideSubPanels = hideSubVisualizerPanels;
        bool oldShowRays = showStaticGazeRays;
        bool oldShowHitPoints = showStaticHitPoints;
        bool oldShowHeatmap = showSurfaceHeatmap;
        bool oldAutoFollow = autoFollowCurrentNodeVisualization;

        useIntegratedVisualization = GUILayout.Toggle(
            useIntegratedVisualization,
            "Use Visualization Overlay"
        );

        hideSubVisualizerPanels = GUILayout.Toggle(
            hideSubVisualizerPanels,
            "Hide Sub Visualizer Panels"
        );

        showStaticGazeRays = GUILayout.Toggle(
            showStaticGazeRays,
            "Show Rays"
        );

        showStaticHitPoints = GUILayout.Toggle(
            showStaticHitPoints,
            "Show Hit Points"
        );

        showSurfaceHeatmap = GUILayout.Toggle(
            showSurfaceHeatmap,
            "Show Surface Heatmap"
        );

        autoFollowCurrentNodeVisualization = GUILayout.Toggle(
            autoFollowCurrentNodeVisualization,
            "Auto Follow Current Node"
        );

        bool visualizationSwitchChanged =
            oldUseIntegratedVisualization != useIntegratedVisualization ||
            oldShowRays != showStaticGazeRays ||
            oldShowHitPoints != showStaticHitPoints ||
            oldShowHeatmap != showSurfaceHeatmap ||
            oldAutoFollow != autoFollowCurrentNodeVisualization;

        bool panelVisibilityChanged = oldHideSubPanels != hideSubVisualizerPanels;

        if (panelVisibilityChanged)
        {
            EnsureIntegratedVisualizerComponents();
            ApplySubVisualizerPanelVisibility();
        }

        if (visualizationSwitchChanged)
        {
            if (autoFollowCurrentNodeVisualization)
            {
                ApplyIntegratedVisualizationForCurrentFrame();
            }
            else
            {
                ApplyIntegratedVisualization();
            }
        }

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Refresh Visualization", GUILayout.Height(26)))
        {
            if (autoFollowCurrentNodeVisualization)
            {
                ApplyIntegratedVisualizationForCurrentFrame();
            }
            else
            {
                ApplyIntegratedVisualization();
            }
        }

        if (GUILayout.Button("Clear Visualization", GUILayout.Height(26)))
        {
            ClearIntegratedVisualizations();
        }

        GUILayout.EndHorizontal();

        GUILayout.Label("Data loaded: " + visualizationDataLoaded);
    }

    private void DrawNodeStaySection()
    {
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
            GUILayout.Label("Playback records: " + playbackIndices.Count);
        }

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

        GUILayout.BeginHorizontal();
        GUILayout.Label("Node List Height:", GUILayout.Width(130));
        nodeListHeight = GUILayout.HorizontalSlider(nodeListHeight, 120f, 600f, GUILayout.Width(250));
        GUILayout.Label(Mathf.RoundToInt(nodeListHeight).ToString(), GUILayout.Width(50));
        GUILayout.EndHorizontal();

        nodeScrollPosition = GUILayout.BeginScrollView(
            nodeScrollPosition,
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
    }

    private void DrawCurrentFrameSection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Current Frame", GUI.skin.box);

        if (playbackIndices.Count > 0 && playbackCursor >= 0 && playbackCursor < playbackIndices.Count)
        {
            ReplayRecord rec = records[playbackIndices[playbackCursor]];
            GUILayout.Label("Source Row: " + rec.sourceRowIndex);
            GUILayout.Label("SceneID: " + rec.sceneID);
            GUILayout.Label("SceneTime: " + rec.sceneTime);
            GUILayout.Label("NodeID: " + rec.nodeID);
            GUILayout.Label("Hit: " + rec.gazeHit + " | Object: " + rec.hitObjectName);
        }
        else
        {
            GUILayout.Label("No current frame.");
        }
    }

    private void DrawStatusSection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Status:");
        GUILayout.TextArea(statusMessage, GUILayout.Height(90));
    }

    private float FloatField(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(160));

        string text = GUILayout.TextField(value.ToString("G4", CultureInfo.InvariantCulture), GUILayout.Width(90));
        float parsed;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = Mathf.Clamp(parsed, min, max);
        }

        GUILayout.EndHorizontal();
        return value;
    }

    private Vector3 Vector3Field(string label, Vector3 value)
    {
        GUILayout.Label(label + ":");

        GUILayout.BeginHorizontal();

        value.x = SmallFloatField("X", value.x);
        value.y = SmallFloatField("Y", value.y);
        value.z = SmallFloatField("Z", value.z);

        GUILayout.EndHorizontal();

        return value;
    }

    private float SmallFloatField(string label, float value)
    {
        GUILayout.Label(label, GUILayout.Width(18));

        string text = GUILayout.TextField(value.ToString("G4", CultureInfo.InvariantCulture), GUILayout.Width(75));
        float parsed;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
        }

        return value;
    }

    private void LoadCsvAndBuildNodeStays()
    {
        records.Clear();
        nodeStays.Clear();
        playbackIndices.Clear();

        playbackCursor = 0;
        playbackTimer = 0f;

        lastPanoramaSceneID = "";
        lastPanoramaNodeID = "";
        warnedMissingPanoramaManager = false;
        warnedMissingUniversalController = false;

        visualizationDataLoaded = false;
        lastAutoVisualizationNodeStayIndex = -999;

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

        int idxSceneID = FindColumnIndex(header, "SceneID");
        int idxNodeID = FindColumnIndex(header, "NodeID");
        int idxSceneTime = FindColumnIndex(header, "SceneTime");
        int idxTimestamp = FindColumnIndex(header, "Timestamp");

        int idxHmdX = FindColumnIndex(header, "HMDPositionX");
        int idxHmdY = FindColumnIndex(header, "HMDPositionY");
        int idxHmdZ = FindColumnIndex(header, "HMDPositionZ");

        int idxRotX = FindColumnIndex(header, "HMDRotationEulerX");
        int idxRotY = FindColumnIndex(header, "HMDRotationEulerY");
        int idxRotZ = FindColumnIndex(header, "HMDRotationEulerZ");

        int idxFovV = FindColumnIndex(header, "CameraFOV_V");
        int idxFovH = FindColumnIndex(header, "CameraFOV_H");
        int idxNear = FindColumnIndex(header, "CameraNearClip");
        int idxFar = FindColumnIndex(header, "CameraFarClip");
        int idxAspect = FindColumnIndex(header, "CameraAspectRatio");

        int idxGazeOriginX = FindColumnIndex(header, "GazeOriginWorldX");
        int idxGazeOriginY = FindColumnIndex(header, "GazeOriginWorldY");
        int idxGazeOriginZ = FindColumnIndex(header, "GazeOriginWorldZ");

        int idxGazeDirX = FindColumnIndex(header, "GazeDirectionWorldX");
        int idxGazeDirY = FindColumnIndex(header, "GazeDirectionWorldY");
        int idxGazeDirZ = FindColumnIndex(header, "GazeDirectionWorldZ");

        int idxHitX = FindColumnIndex(header, "HitPointX");
        int idxHitY = FindColumnIndex(header, "HitPointY");
        int idxHitZ = FindColumnIndex(header, "HitPointZ");
        int idxHitDistance = FindColumnIndex(header, "HitDistance");

        int idxGazeValid = FindColumnIndex(header, "GazeValid");
        int idxGazeHit = FindColumnIndex(header, "GazeHit");

        int idxHitObjectName = FindColumnIndex(header, "HitObjectName");
        int idxHitObjectTag = FindColumnIndex(header, "HitObjectTag");

        if (idxNodeID < 0 ||
            idxHmdX < 0 || idxHmdY < 0 || idxHmdZ < 0 ||
            idxRotX < 0 || idxRotY < 0 || idxRotZ < 0)
        {
            statusMessage =
                "CSV is missing required columns.\n" +
                "Required: NodeID, HMDPositionX/Y/Z, HMDRotationEulerX/Y/Z.\n" +
                csvPath;

            Debug.LogError(statusMessage);
            return;
        }

        for (int i = 1; i < table.Count; i++)
        {
            List<string> row = table[i];

            ReplayRecord rec = new ReplayRecord();
            rec.sourceRowIndex = i;

            rec.sceneID = idxSceneID >= 0 ? SafeGet(row, idxSceneID) : "";
            rec.nodeID = SafeGet(row, idxNodeID);
            rec.sceneTime = idxSceneTime >= 0 ? SafeGet(row, idxSceneTime) : "";
            rec.timestamp = idxTimestamp >= 0 ? SafeGet(row, idxTimestamp) : "";

            rec.hmdPosition = ReadVector3(row, idxHmdX, idxHmdY, idxHmdZ);
            rec.hmdRotationEuler = ReadVector3(row, idxRotX, idxRotY, idxRotZ);

            rec.cameraFovV = ReadFloat(row, idxFovV, manualVerticalFOV);
            rec.cameraFovH = ReadFloat(row, idxFovH, 0f);
            rec.cameraNear = ReadFloat(row, idxNear, manualNearClip);
            rec.cameraFar = ReadFloat(row, idxFar, manualFarClip);
            rec.cameraAspect = ReadFloat(row, idxAspect, 0f);

            rec.gazeOrigin = ReadVector3Optional(row, idxGazeOriginX, idxGazeOriginY, idxGazeOriginZ);
            rec.gazeDirection = ReadVector3Optional(row, idxGazeDirX, idxGazeDirY, idxGazeDirZ);

            rec.hitPoint = ReadVector3Optional(row, idxHitX, idxHitY, idxHitZ);
            rec.hitDistance = ReadFloat(row, idxHitDistance, 0f);

            rec.hitObjectName = idxHitObjectName >= 0 ? SafeGet(row, idxHitObjectName) : "";
            rec.hitObjectTag = idxHitObjectTag >= 0 ? SafeGet(row, idxHitObjectTag) : "";

            rec.gazeValid = DetermineGazeValid(row, idxGazeValid, rec);
            rec.gazeHit = DetermineGazeHit(row, idxGazeHit, rec);

            records.Add(rec);
        }

        BuildNodeStaySegments();
        BuildPlaybackIndicesFromSelection();

        EnsureReplayCamera();
        EnsureGazeOverlay();

        if (playbackIndices.Count > 0)
        {
            ApplyCurrentFrame();
        }

        isPlaying = playOnLoad;

        statusMessage =
            "Loaded CSV successfully.\n" +
            "File: " + Path.GetFileName(csvPath) + "\n" +
            "Records: " + records.Count + "\n" +
            "Node stays: " + nodeStays.Count + "\n" +
            "Playback records: " + playbackIndices.Count;

        Debug.Log(statusMessage);

        if (useIntegratedVisualization)
        {
            EnsureIntegratedVisualizerComponents();

            if (HasAnyVisualizationSwitchOn())
            {
                if (autoFollowCurrentNodeVisualization)
                {
                    ApplyIntegratedVisualizationForCurrentFrame();
                }
                else
                {
                    ApplyIntegratedVisualization();
                }
            }
        }
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

    private void BuildPlaybackIndicesFromSelection()
    {
        playbackIndices.Clear();

        if (records.Count == 0)
        {
            playbackCursor = 0;
            return;
        }

        if (replayAllRecords)
        {
            for (int i = 0; i < records.Count; i++)
            {
                playbackIndices.Add(i);
            }
        }
        else
        {
            for (int s = 0; s < nodeStays.Count; s++)
            {
                NodeStaySegment seg = nodeStays[s];

                if (!seg.selected)
                {
                    continue;
                }

                for (int i = seg.startRecordIndex; i <= seg.endRecordIndex; i++)
                {
                    playbackIndices.Add(i);
                }
            }
        }

        playbackCursor = Mathf.Clamp(playbackCursor, 0, Mathf.Max(0, playbackIndices.Count - 1));
    }

    private void StepForward()
    {
        if (playbackIndices.Count == 0)
        {
            return;
        }

        playbackCursor++;

        if (playbackCursor >= playbackIndices.Count)
        {
            if (loopPlayback)
            {
                playbackCursor = 0;
            }
            else
            {
                playbackCursor = playbackIndices.Count - 1;
                isPlaying = false;
            }
        }

        ApplyCurrentFrame();
    }

    private void StepBackward()
    {
        if (playbackIndices.Count == 0)
        {
            return;
        }

        playbackCursor--;

        if (playbackCursor < 0)
        {
            playbackCursor = loopPlayback ? playbackIndices.Count - 1 : 0;
        }

        ApplyCurrentFrame();
    }

    private void ApplyCurrentFrame()
    {
        if (playbackIndices.Count == 0 ||
            playbackCursor < 0 ||
            playbackCursor >= playbackIndices.Count)
        {
            return;
        }

        int recordIndex = playbackIndices[playbackCursor];

        if (recordIndex < 0 || recordIndex >= records.Count)
        {
            return;
        }

        ReplayRecord rec = records[recordIndex];

        ApplyCameraFrame(rec);
        SwitchPanoramaIfNeeded(rec);
        UpdateGazeOverlay(rec);
        SyncVisualizationToCurrentNode(recordIndex);
    }

    private void ApplyCameraFrame(ReplayRecord rec)
    {
        EnsureReplayCamera();

        if (replayCamera == null)
        {
            return;
        }

        replayCamera.transform.position = rec.hmdPosition + positionOffset;

        Quaternion baseRotation = Quaternion.Euler(rec.hmdRotationEuler);
        Quaternion offsetRotation = Quaternion.Euler(rotationOffsetEuler);
        replayCamera.transform.rotation = baseRotation * offsetRotation;

        if (applyRecordedCameraParams)
        {
            float fov = manualVerticalFOV;

            if (useRecordedVerticalFOV)
            {
                if (rec.cameraFovV > 1f && rec.cameraFovV < 179f)
                {
                    fov = rec.cameraFovV;
                }
            }
            else
            {
                if (rec.cameraFovH > 1f && rec.cameraFovH < 179f && rec.cameraAspect > 0.01f)
                {
                    fov = HorizontalToVerticalFOV(rec.cameraFovH, rec.cameraAspect);
                }
            }

            replayCamera.fieldOfView = Mathf.Clamp(fov, 1f, 179f);

            if (rec.cameraNear > 0.0001f)
            {
                replayCamera.nearClipPlane = rec.cameraNear;
            }

            if (rec.cameraFar > replayCamera.nearClipPlane + 0.1f)
            {
                replayCamera.farClipPlane = rec.cameraFar;
            }

            if (applyRecordedAspectRatio && rec.cameraAspect > 0.01f)
            {
                replayCamera.aspect = rec.cameraAspect;
            }
        }
        else
        {
            replayCamera.fieldOfView = Mathf.Clamp(manualVerticalFOV, 1f, 179f);
            replayCamera.nearClipPlane = Mathf.Max(0.001f, manualNearClip);
            replayCamera.farClipPlane = Mathf.Max(replayCamera.nearClipPlane + 0.1f, manualFarClip);
        }
    }

    private void SwitchPanoramaIfNeeded(ReplayRecord rec)
    {
        if (!switchPanoramaOnNodeChange)
        {
            return;
        }

        string sceneID = ResolveSceneID(rec);
        string nodeID = NormalizeNodeIDForPanorama(rec.nodeID);

        if (StringIsNullOrWhiteSpace(sceneID) || StringIsNullOrWhiteSpace(nodeID))
        {
            return;
        }

        if (sceneID == lastPanoramaSceneID && nodeID == lastPanoramaNodeID)
        {
            return;
        }

        lastPanoramaSceneID = sceneID;
        lastPanoramaNodeID = nodeID;

        if (logPanoramaSwitch)
        {
            Debug.Log("[Replay] Node changed, switch panorama: SceneID=" + sceneID + ", NodeID=" + nodeID);
        }

        if (useUniversalPanoramaController)
        {
            bool switchedByUniversalController = TrySwitchUsingUniversalPanoramaController(nodeID);

            if (switchedByUniversalController)
            {
                return;
            }

            if (!warnedMissingUniversalController)
            {
                Debug.LogWarning("[Replay] UniversalPanoramaTest controller not found or cannot be called. Falling back to PanoramaMaterialManager.");
                warnedMissingUniversalController = true;
            }
        }

        if (PanoramaMaterialManager.Instance == null)
        {
            if (!warnedMissingPanoramaManager)
            {
                Debug.LogWarning("[Replay] PanoramaMaterialManager.Instance is null. Please make sure PanoramaMaterialManager exists in the scene.");
                warnedMissingPanoramaManager = true;
            }

            return;
        }

        if (panoramaSwitchCoroutine != null)
        {
            StopCoroutine(panoramaSwitchCoroutine);
            panoramaSwitchCoroutine = null;
        }

        panoramaSwitchCoroutine = StartCoroutine(SwitchPanoramaCoroutine(nodeID, sceneID));
    }

    private bool TrySwitchUsingUniversalPanoramaController(string nodeID)
    {
        if (panoramaController == null)
        {
            panoramaController = FindObjectOfType<UniversalPanoramaTest>();
        }

        if (panoramaController == null)
        {
            return false;
        }

        Type controllerType = panoramaController.GetType();

        MethodInfo replayMethod = controllerType.GetMethod(
            "SwitchToNodeFromReplay",
            InstanceBindings
        );

        if (replayMethod != null)
        {
            replayMethod.Invoke(panoramaController, new object[] { nodeID });
            return true;
        }

        MethodInfo switchToNodeMethod = controllerType.GetMethod(
            "SwitchToNode",
            InstanceBindings
        );

        if (switchToNodeMethod != null)
        {
            int nodeNumber;
            string digits = ExtractDigits(nodeID);

            if (StringIsNullOrWhiteSpace(digits))
            {
                digits = nodeID;
            }

            if (int.TryParse(digits, out nodeNumber))
            {
                switchToNodeMethod.Invoke(panoramaController, new object[] { nodeNumber });
                return true;
            }
        }

        return false;
    }

    private IEnumerator SwitchPanoramaCoroutine(string nodeID, string sceneID)
    {
        if (PanoramaMaterialManager.Instance == null)
        {
            Debug.LogWarning("[Replay] PanoramaMaterialManager.Instance is null. Cannot switch panorama.");
            panoramaSwitchCoroutine = null;
            yield break;
        }

        yield return StartCoroutine(
            PanoramaMaterialManager.Instance.UpdatePanorama(nodeID, sceneID)
        );

        panoramaSwitchCoroutine = null;
    }

    private string ResolveSceneID(ReplayRecord rec)
    {
        if (!StringIsNullOrWhiteSpace(sceneIDOverride))
        {
            return NormalizeSceneID(sceneIDOverride.Trim());
        }

        if (!StringIsNullOrWhiteSpace(rec.sceneID))
        {
            return NormalizeSceneID(rec.sceneID);
        }

        string activeSceneName = SceneManager.GetActiveScene().name;
        return ExtractDigits(activeSceneName);
    }

    private string NormalizeSceneID(string rawSceneID)
    {
        if (StringIsNullOrWhiteSpace(rawSceneID))
        {
            return "";
        }

        rawSceneID = rawSceneID.Trim();

        int parsed;
        if (int.TryParse(rawSceneID, out parsed))
        {
            return parsed.ToString();
        }

        string digits = ExtractDigits(rawSceneID);

        if (!StringIsNullOrWhiteSpace(digits))
        {
            return digits;
        }

        return rawSceneID;
    }

    private string NormalizeNodeIDForPanorama(string rawNodeID)
    {
        if (StringIsNullOrWhiteSpace(rawNodeID))
        {
            return "";
        }

        string nodeID = rawNodeID.Trim();

        if (removeNodePrefixForPanorama &&
            nodeID.StartsWith("Node", StringComparison.OrdinalIgnoreCase))
        {
            nodeID = nodeID.Substring(4);
        }

        return nodeID.Trim();
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

    private float HorizontalToVerticalFOV(float horizontalFovDeg, float aspect)
    {
        float hRad = horizontalFovDeg * Mathf.Deg2Rad;
        float vRad = 2f * Mathf.Atan(Mathf.Tan(hRad * 0.5f) / Mathf.Max(0.0001f, aspect));
        return vRad * Mathf.Rad2Deg;
    }

    private void EnsureReplayCamera()
    {
        if (replayCamera != null)
        {
            replayCamera.enabled = true;
            return;
        }

        if (!autoCreateReplayCamera)
        {
            replayCamera = Camera.main;
            return;
        }

        GameObject cameraObj = new GameObject("Generated_SingleCameraReplayCamera");
        replayCamera = cameraObj.AddComponent<Camera>();

        replayCamera.depth = 100f;
        replayCamera.clearFlags = CameraClearFlags.Skybox;
        replayCamera.cullingMask = ~0;
        replayCamera.enabled = true;
    }

    private void EnsureGazeOverlay()
    {
        if (gazeLineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            gazeLineMaterial = new Material(shader);
            gazeLineMaterial.color = gazeRayColor;
        }

        if (hitPointMaterial == null)
        {
            Shader shader = Shader.Find("Standard");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            hitPointMaterial = new Material(shader);
            hitPointMaterial.color = hitPointColor;
        }

        if (gazeLine == null)
        {
            GameObject lineObj = new GameObject("CurrentGazeRay");
            lineObj.transform.SetParent(transform, false);

            gazeLine = lineObj.AddComponent<LineRenderer>();
            gazeLine.useWorldSpace = true;
            gazeLine.positionCount = 2;
            gazeLine.material = gazeLineMaterial;
            gazeLine.startWidth = gazeRayWidth;
            gazeLine.endWidth = gazeRayWidth;
        }

        if (hitPointSphere == null)
        {
            hitPointSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hitPointSphere.name = "CurrentGazeHitPoint";
            hitPointSphere.transform.SetParent(transform, false);

            Renderer renderer = hitPointSphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = hitPointMaterial;
            }

            Collider collider = hitPointSphere.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }
    }

    private void UpdateGazeOverlay(ReplayRecord rec)
    {
        EnsureGazeOverlay();

        if (gazeLine != null)
        {
            gazeLine.startWidth = gazeRayWidth;
            gazeLine.endWidth = gazeRayWidth;
            gazeLineMaterial.color = gazeRayColor;

            bool showLine =
                showCurrentGazeRay &&
                rec.gazeValid &&
                IsVectorFinite(rec.gazeOrigin) &&
                rec.gazeDirection.sqrMagnitude > 0.000001f;

            gazeLine.gameObject.SetActive(showLine);

            if (showLine)
            {
                Vector3 start = rec.gazeOrigin;
                Vector3 end;

                if (rec.gazeHit && IsVectorFinite(rec.hitPoint))
                {
                    end = rec.hitPoint;
                }
                else
                {
                    end = start + rec.gazeDirection.normalized * noHitRayLength;
                }

                gazeLine.SetPosition(0, start);
                gazeLine.SetPosition(1, end);
            }
        }

        if (hitPointSphere != null)
        {
            bool showPoint =
                showCurrentHitPoint &&
                rec.gazeValid &&
                rec.gazeHit &&
                IsVectorFinite(rec.hitPoint);

            hitPointSphere.SetActive(showPoint);

            if (showPoint)
            {
                hitPointSphere.transform.position = rec.hitPoint;
                hitPointSphere.transform.localScale = Vector3.one * hitPointSize;

                Renderer renderer = hitPointSphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = hitPointColor;
                }
            }
        }
    }

    private void ClearGazeOverlay()
    {
        if (gazeLine != null)
        {
            gazeLine.gameObject.SetActive(false);
        }

        if (hitPointSphere != null)
        {
            hitPointSphere.SetActive(false);
        }
    }

    private void EnsureIntegratedVisualizerComponents()
    {
        if (gazeHitVisualizer == null)
        {
            gazeHitVisualizer = GetComponent<GazeHitCsvVisualizer>();

            if (gazeHitVisualizer == null)
            {
                gazeHitVisualizer = FindObjectOfType<GazeHitCsvVisualizer>();
            }

            if (gazeHitVisualizer == null)
            {
                gazeHitVisualizer = gameObject.AddComponent<GazeHitCsvVisualizer>();
            }
        }

        if (heatmapVisualizer == null)
        {
            heatmapVisualizer = GetComponent<GazeSurfaceSplatHeatmapVisualizer>();

            if (heatmapVisualizer == null)
            {
                heatmapVisualizer = FindObjectOfType<GazeSurfaceSplatHeatmapVisualizer>();
            }

            if (heatmapVisualizer == null)
            {
                heatmapVisualizer = gameObject.AddComponent<GazeSurfaceSplatHeatmapVisualizer>();
            }
        }

        ApplySubVisualizerPanelVisibility();
    }

    private void ApplySubVisualizerPanelVisibility()
    {
        bool visible = !hideSubVisualizerPanels;

        if (gazeHitVisualizer != null)
        {
            gazeHitVisualizer.showControlPanel = visible;
        }

        if (heatmapVisualizer != null)
        {
            heatmapVisualizer.showControlPanel = visible;
        }
    }

    private string GetCurrentVisualizationOverridePath()
    {
        if (!StringIsNullOrWhiteSpace(loadedCsvPath) && File.Exists(loadedCsvPath))
        {
            return loadedCsvPath;
        }

        if (!StringIsNullOrWhiteSpace(overrideCsvFullPath) && File.Exists(overrideCsvFullPath))
        {
            return overrideCsvFullPath;
        }

        return "";
    }

    private void ConfigureIntegratedVisualizers()
    {
        string overridePath = GetCurrentVisualizationOverridePath();

        if (gazeHitVisualizer != null)
        {
            gazeHitVisualizer.processedRoot = processedRoot;
            gazeHitVisualizer.taskOrder = taskOrder;
            gazeHitVisualizer.csvFileIndex = csvFileIndex;
            gazeHitVisualizer.overrideCsvFullPath = overridePath;
        }

        if (heatmapVisualizer != null)
        {
            heatmapVisualizer.processedRoot = processedRoot;
            heatmapVisualizer.taskOrder = taskOrder;
            heatmapVisualizer.csvFileIndex = csvFileIndex;
            heatmapVisualizer.overrideCsvFullPath = overridePath;
        }
    }

    private void LoadIntegratedVisualizationData()
    {
        if (!useIntegratedVisualization)
        {
            return;
        }

        EnsureIntegratedVisualizerComponents();
        ConfigureIntegratedVisualizers();

        if (gazeHitVisualizer != null)
        {
            InvokeNoParamMethod(gazeHitVisualizer, "LoadCsvAndBuildNodeStays");
        }

        if (heatmapVisualizer != null)
        {
            InvokeNoParamMethod(heatmapVisualizer, "LoadCsvAndBuildNodeStays");
        }

        visualizationDataLoaded = true;
    }

    private HashSet<int> GetSelectedNodeStayIndicesForVisualization()
    {
        HashSet<int> selectedIndices = new HashSet<int>();

        if (records.Count == 0 || nodeStays.Count == 0)
        {
            return selectedIndices;
        }

        if (replayAllRecords)
        {
            for (int i = 0; i < nodeStays.Count; i++)
            {
                selectedIndices.Add(i);
            }

            return selectedIndices;
        }

        for (int i = 0; i < nodeStays.Count; i++)
        {
            if (nodeStays[i].selected)
            {
                selectedIndices.Add(i);
            }
        }

        return selectedIndices;
    }

    private bool HasAnyVisualizationSwitchOn()
    {
        return showStaticGazeRays || showStaticHitPoints || showSurfaceHeatmap;
    }

    private void ApplyIntegratedVisualization()
    {
        if (!useIntegratedVisualization)
        {
            ClearIntegratedVisualizations();
            statusMessage = "Visualization overlay disabled.";
            return;
        }

        if (records.Count == 0)
        {
            statusMessage = "Please load CSV before showing visualization overlay.";
            return;
        }

        if (!visualizationDataLoaded)
        {
            LoadIntegratedVisualizationData();
        }

        EnsureIntegratedVisualizerComponents();
        ConfigureIntegratedVisualizers();

        HashSet<int> selectedIndices = GetSelectedNodeStayIndicesForVisualization();

        bool needGazeVisualization = showStaticGazeRays || showStaticHitPoints;

        if (gazeHitVisualizer != null)
        {
            if (needGazeVisualization)
            {
                gazeHitVisualizer.showRays = showStaticGazeRays;
                gazeHitVisualizer.showHitPoints = showStaticHitPoints;

                ApplyNodeStaySelectionByReflection(gazeHitVisualizer, selectedIndices);
                InvokeNoParamMethod(gazeHitVisualizer, "RefreshVisualization");
            }
            else
            {
                InvokeNoParamMethod(gazeHitVisualizer, "ClearVisualization");
            }
        }

        if (heatmapVisualizer != null)
        {
            if (showSurfaceHeatmap)
            {
                ApplyNodeStaySelectionByReflection(heatmapVisualizer, selectedIndices);
                InvokeNoParamMethod(heatmapVisualizer, "RefreshHeatmap");
            }
            else
            {
                InvokeNoParamMethod(heatmapVisualizer, "ClearHeatmap");
            }
        }

        lastShowStaticGazeRays = showStaticGazeRays;
        lastShowStaticHitPoints = showStaticHitPoints;
        lastShowSurfaceHeatmap = showSurfaceHeatmap;

        statusMessage =
            "Visualization overlay updated.\n" +
            "Show rays: " + showStaticGazeRays + "\n" +
            "Show hit points: " + showStaticHitPoints + "\n" +
            "Show heatmap: " + showSurfaceHeatmap + "\n" +
            "Selected node stays: " + selectedIndices.Count;
    }

    private void SyncVisualizationToCurrentNode(int recordIndex)
    {
        if (!useIntegratedVisualization)
        {
            return;
        }

        if (!autoFollowCurrentNodeVisualization)
        {
            return;
        }

        if (!HasAnyVisualizationSwitchOn())
        {
            return;
        }

        int nodeStayIndex = FindNodeStayIndexByRecordIndex(recordIndex);

        if (nodeStayIndex < 0)
        {
            return;
        }

        if (nodeStayIndex == lastAutoVisualizationNodeStayIndex &&
            showStaticGazeRays == lastShowStaticGazeRays &&
            showStaticHitPoints == lastShowStaticHitPoints &&
            showSurfaceHeatmap == lastShowSurfaceHeatmap)
        {
            return;
        }

        lastAutoVisualizationNodeStayIndex = nodeStayIndex;

        ApplyIntegratedVisualizationForNodeStay(nodeStayIndex);
    }

    private int FindNodeStayIndexByRecordIndex(int recordIndex)
    {
        for (int i = 0; i < nodeStays.Count; i++)
        {
            NodeStaySegment seg = nodeStays[i];

            if (recordIndex >= seg.startRecordIndex &&
                recordIndex <= seg.endRecordIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private void ApplyIntegratedVisualizationForCurrentFrame()
    {
        if (playbackIndices.Count == 0 ||
            playbackCursor < 0 ||
            playbackCursor >= playbackIndices.Count)
        {
            return;
        }

        int recordIndex = playbackIndices[playbackCursor];
        int nodeStayIndex = FindNodeStayIndexByRecordIndex(recordIndex);

        if (nodeStayIndex < 0)
        {
            return;
        }

        lastAutoVisualizationNodeStayIndex = nodeStayIndex;

        ApplyIntegratedVisualizationForNodeStay(nodeStayIndex);
    }

    private void ApplyIntegratedVisualizationForNodeStay(int nodeStayIndex)
    {
        if (!useIntegratedVisualization)
        {
            ClearIntegratedVisualizations();
            statusMessage = "Visualization overlay disabled.";
            return;
        }

        if (records.Count == 0)
        {
            statusMessage = "Please load CSV before showing visualization overlay.";
            return;
        }

        if (nodeStayIndex < 0 || nodeStayIndex >= nodeStays.Count)
        {
            statusMessage = "Invalid NodeStay index: " + nodeStayIndex;
            return;
        }

        if (!visualizationDataLoaded)
        {
            LoadIntegratedVisualizationData();
        }

        EnsureIntegratedVisualizerComponents();
        ConfigureIntegratedVisualizers();

        HashSet<int> selectedIndices = new HashSet<int>();
        selectedIndices.Add(nodeStayIndex);

        bool needGazeVisualization = showStaticGazeRays || showStaticHitPoints;

        if (gazeHitVisualizer != null)
        {
            if (needGazeVisualization)
            {
                gazeHitVisualizer.showRays = showStaticGazeRays;
                gazeHitVisualizer.showHitPoints = showStaticHitPoints;

                ApplyNodeStaySelectionByReflection(gazeHitVisualizer, selectedIndices);
                InvokeNoParamMethod(gazeHitVisualizer, "RefreshVisualization");
            }
            else
            {
                InvokeNoParamMethod(gazeHitVisualizer, "ClearVisualization");
            }
        }

        if (heatmapVisualizer != null)
        {
            if (showSurfaceHeatmap)
            {
                ApplyNodeStaySelectionByReflection(heatmapVisualizer, selectedIndices);
                InvokeNoParamMethod(heatmapVisualizer, "RefreshHeatmap");
            }
            else
            {
                InvokeNoParamMethod(heatmapVisualizer, "ClearHeatmap");
            }
        }

        lastShowStaticGazeRays = showStaticGazeRays;
        lastShowStaticHitPoints = showStaticHitPoints;
        lastShowSurfaceHeatmap = showSurfaceHeatmap;

        NodeStaySegment currentSeg = nodeStays[nodeStayIndex];

        statusMessage =
            "Visualization follows current NodeStay.\n" +
            "NodeStay index: " + (nodeStayIndex + 1) + "\n" +
            "NodeID: " + currentSeg.nodeID + "\n" +
            "Show rays: " + showStaticGazeRays + "\n" +
            "Show hit points: " + showStaticHitPoints + "\n" +
            "Show heatmap: " + showSurfaceHeatmap;
    }

    private void ClearIntegratedVisualizations()
    {
        if (gazeHitVisualizer != null)
        {
            InvokeNoParamMethod(gazeHitVisualizer, "ClearVisualization");
        }

        if (heatmapVisualizer != null)
        {
            InvokeNoParamMethod(heatmapVisualizer, "ClearHeatmap");
        }

        visualizationDataLoaded = false;
        lastAutoVisualizationNodeStayIndex = -999;

        statusMessage = "Visualization overlay cleared.";
    }

    private bool InvokeNoParamMethod(object target, string methodName)
    {
        if (target == null)
        {
            return false;
        }

        MethodInfo method = target.GetType().GetMethod(methodName, InstanceBindings);

        if (method == null)
        {
            Debug.LogWarning("[Replay] Cannot find method: " + methodName + " on " + target.GetType().Name);
            return false;
        }

        method.Invoke(target, null);
        return true;
    }

    private void ApplyNodeStaySelectionByReflection(object visualizer, HashSet<int> selectedNodeStayIndices)
    {
        if (visualizer == null)
        {
            return;
        }

        FieldInfo nodeStaysField = visualizer.GetType().GetField("nodeStays", InstanceBindings);

        if (nodeStaysField == null)
        {
            Debug.LogWarning("[Replay] Cannot find field nodeStays on " + visualizer.GetType().Name);
            return;
        }

        IList list = nodeStaysField.GetValue(visualizer) as IList;

        if (list == null)
        {
            Debug.LogWarning("[Replay] nodeStays is null or not IList on " + visualizer.GetType().Name);
            return;
        }

        bool selectAll = selectedNodeStayIndices == null || selectedNodeStayIndices.Count == 0;

        for (int i = 0; i < list.Count; i++)
        {
            object segment = list[i];

            if (segment == null)
            {
                continue;
            }

            FieldInfo selectedField = segment.GetType().GetField("selected", InstanceBindings);

            if (selectedField == null)
            {
                continue;
            }

            bool selected = selectAll || selectedNodeStayIndices.Contains(i);
            selectedField.SetValue(segment, selected);
        }
    }

    private void SetAllNodeStaySelection(bool selected)
    {
        for (int i = 0; i < nodeStays.Count; i++)
        {
            nodeStays[i].selected = selected;
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
        return new Vector3(
            ReadFloat(row, ix, 0f),
            ReadFloat(row, iy, 0f),
            ReadFloat(row, iz, 0f)
        );
    }

    private Vector3 ReadVector3Optional(List<string> row, int ix, int iy, int iz)
    {
        if (ix < 0 || iy < 0 || iz < 0)
        {
            return Vector3.zero;
        }

        return ReadVector3(row, ix, iy, iz);
    }

    private float ReadFloat(List<string> row, int index, float defaultValue)
    {
        if (index < 0 || index >= row.Count)
        {
            return defaultValue;
        }

        float value;

        if (float.TryParse(row[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return defaultValue;
    }

    private bool DetermineGazeValid(List<string> row, int idxGazeValid, ReplayRecord rec)
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

        return rec.gazeDirection.sqrMagnitude > 0.000001f;
    }

    private bool DetermineGazeHit(List<string> row, int idxGazeHit, ReplayRecord rec)
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

        return rec.hitDistance > 0.0001f;
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

    private class ReplayRecord
    {
        public int sourceRowIndex;

        public string sceneID;
        public string nodeID;
        public string sceneTime;
        public string timestamp;

        public Vector3 hmdPosition;
        public Vector3 hmdRotationEuler;

        public float cameraFovV;
        public float cameraFovH;
        public float cameraNear;
        public float cameraFar;
        public float cameraAspect;

        public Vector3 gazeOrigin;
        public Vector3 gazeDirection;

        public Vector3 hitPoint;
        public float hitDistance;

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