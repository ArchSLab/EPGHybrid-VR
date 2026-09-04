using UnityEngine;
using UnityEngine.SceneManagement;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class DecisionPointViewExporter : MonoBehaviour
{
    [Header("Scene / Node Data")]
    public string sceneIDOverride = "";
    public string nodeInfoRelativePath = "ReadData/NodeInfo.csv";
    public string nodesParentName = "Nodes";

    [Header("Output")]
    public string outputRoot;

    public int imageWidth = 1920;
    public int imageHeight = 1080;

    [Header("Camera")]
    public Camera exportCamera;
    public bool autoCreateExportCamera = true;

    public float horizontalFOV = 98f;
    public float nearClip = 0.05f;
    public float farClip = 1000f;

    public Vector3 cameraPositionOffset = Vector3.zero;
    public Vector3 cameraRotationOffsetEuler = Vector3.zero;

    [Tooltip("Yaw offsets in degrees. Example: 0,90,180,270")]
    public string yawOffsetsText = "0,90,180,270";

    [Tooltip("Pitch offsets in degrees. Example: -30,0,30")]
    public string pitchOffsetsText = "-30,0,30";

    [Header("Export Range")]
    public bool exportAllNodesInCurrentScene = true;
    public int selectedNodeID = 1;

    [Header("Panorama Switching")]
    public bool switchPanoramaBeforeCapture = true;
    public UniversalPanoramaTest panoramaController;
    public int waitFramesAfterPanoramaSwitch = 2;

    [Header("Heatmap During Export")]
    public bool showCurrentNodeHeatmap = true;

    public GazeSurfaceSplatHeatmapVisualizer heatmapVisualizer;

    [Tooltip("If empty, the heatmap visualizer will use processedRoot + taskOrder + csvFileIndex.")]
    public string overrideHeatmapCsvFullPath = "";

    public string processedRoot;

    public int taskOrder = 1;
    public int csvFileIndex = 1;

    [Header("Hide Rays / Hit Points")]
    public bool hideGazeRaysAndHitPoints = true;
    public GazeHitCsvVisualizer gazeHitVisualizer;

    [Tooltip("Objects whose names contain these tokens will be hidden during export. Separate by comma.")]
    public string hideObjectNameTokens = "CurrentGazeRay,CurrentGazeHitPoint,GazeHit_Visualization,GazeRay,GazeHitPoint";

    [Header("Signage Highlight")]
    public bool highlightSignage = true;



    [Tooltip("Optional. If assigned, all renderers under these parents will be highlighted.")]
    public Transform[] signageParents;

    [Tooltip("Optional. Separate multiple parent names by comma. Example: Signage,Signs,TargetObjects")]
    public string signageParentNames = "Signage,Signs,TargetObjects";

    [Tooltip("Optional. If your signage objects have a tag, input it here.")]
    public string signageTag = "";

    [Tooltip("Optional. If your signage objects are on a layer, input the layer name here.")]
    public string signageLayerName = "";

    public Color signageHighlightColor = new Color(1f, 1f, 0f, 1f);
    public float signageHighlightLineWidth = 0.035f;

    [Header("Signage Name Labels")]
    public bool drawSignageObjectNames = true;

    [Tooltip("Optional. If null, built-in Arial will be used.")]
    public Font signageLabelFont;

    public int signageLabelFontSize = 96;
    public float signageLabelCharacterSize = 0.08f;
    public float signageLabelHeightOffset = 0.15f;
    public Color signageLabelColor = Color.yellow;

    [Tooltip("Make labels always face the export camera.")]
    public bool billboardSignageLabels = true;

    private GameObject signageLabelRoot;
    private List<TextMesh> signageTextMeshes = new List<TextMesh>();

    [Header("Panel")]
    public bool showPanel = true;
    public Rect panelRect = new Rect(20, 20, 620, 640);

    private Vector2 panelScroll = Vector2.zero;
    private bool isExporting = false;
    private string statusMessage = "Ready.";
    private float progress = 0f;

    private Dictionary<string, NodeRecord> nodeRecordMap = new Dictionary<string, NodeRecord>();

    private GameObject signageHighlightRoot;
    private Material highlightLineMaterial;

    private List<GameObject> temporarilyHiddenObjects = new List<GameObject>();
    private Dictionary<GameObject, bool> hiddenObjectOriginalActive = new Dictionary<GameObject, bool>();

    private const BindingFlags InstanceBindings =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;


    private void Awake()
    {
        outputRoot = ProjectPathConfig.DecisionPointExportRoot;
        processedRoot = ProjectPathConfig.GazeHitDataRoot;

        if (!Directory.Exists(outputRoot))
        {
            Directory.CreateDirectory(outputRoot);
        }
    }

    private void OnGUI()
    {
        if (!showPanel)
        {
            return;
        }

        panelRect = GUI.Window(20260507, panelRect, DrawPanel, "Decision Point View Exporter");
    }

    private void DrawPanel(int windowID)
    {
        panelScroll = GUILayout.BeginScrollView(panelScroll, false, true, GUILayout.Height(panelRect.height - 40));

        GUILayout.Label("Scene / Node", GUI.skin.box);

        GUILayout.Label("Scene ID Override:");
        sceneIDOverride = GUILayout.TextField(sceneIDOverride);

        GUILayout.Label("NodeInfo Relative Path:");
        nodeInfoRelativePath = GUILayout.TextField(nodeInfoRelativePath);

        GUILayout.Label("Nodes Parent Name:");
        nodesParentName = GUILayout.TextField(nodesParentName);

        GUILayout.Space(8);
        GUILayout.Label("Output", GUI.skin.box);

        GUILayout.Label("Output Root:");
        outputRoot = GUILayout.TextField(outputRoot);

        imageWidth = IntField("Image Width", imageWidth, 256, 8192);
        imageHeight = IntField("Image Height", imageHeight, 256, 8192);

        GUILayout.Space(8);
        GUILayout.Label("Camera", GUI.skin.box);

        horizontalFOV = FloatField("Horizontal FOV", horizontalFOV, 10f, 179f);
        nearClip = FloatField("Near Clip", nearClip, 0.001f, 10f);
        farClip = FloatField("Far Clip", farClip, 10f, 10000f);

        cameraPositionOffset = Vector3Field("Camera Position Offset", cameraPositionOffset);
        cameraRotationOffsetEuler = Vector3Field("Camera Rotation Offset", cameraRotationOffsetEuler);

        GUILayout.Label("Yaw Offsets:");
        yawOffsetsText = GUILayout.TextField(yawOffsetsText);

        GUILayout.Label("Pitch Offsets:");
        pitchOffsetsText = GUILayout.TextField(pitchOffsetsText);

        GUILayout.Space(8);
        GUILayout.Label("Export Range", GUI.skin.box);

        exportAllNodesInCurrentScene = GUILayout.Toggle(exportAllNodesInCurrentScene, "Export All Nodes In Current Scene");
        selectedNodeID = IntField("Selected Node ID", selectedNodeID, 1, 9999);

        GUILayout.Space(8);
        GUILayout.Label("Panorama / Heatmap", GUI.skin.box);

        switchPanoramaBeforeCapture = GUILayout.Toggle(switchPanoramaBeforeCapture, "Switch Panorama Before Capture");
        waitFramesAfterPanoramaSwitch = IntField("Wait Frames After Switch", waitFramesAfterPanoramaSwitch, 0, 30);

        showCurrentNodeHeatmap = GUILayout.Toggle(showCurrentNodeHeatmap, "Show Current Node Heatmap");

        GUILayout.Label("Override Heatmap CSV Full Path:");
        overrideHeatmapCsvFullPath = GUILayout.TextField(overrideHeatmapCsvFullPath);

        taskOrder = IntField("Heatmap TaskOrder", taskOrder, 1, 9999);
        csvFileIndex = IntField("Heatmap CSV Index", csvFileIndex, 1, 9999);

        GUILayout.Space(8);
        GUILayout.Label("Clean Export Image", GUI.skin.box);

        hideGazeRaysAndHitPoints = GUILayout.Toggle(hideGazeRaysAndHitPoints, "Hide Rays And Hit Points");

        GUILayout.Label("Hide Object Name Tokens:");
        hideObjectNameTokens = GUILayout.TextField(hideObjectNameTokens);

        GUILayout.Space(8);
        GUILayout.Label("Signage Highlight", GUI.skin.box);

        highlightSignage = GUILayout.Toggle(highlightSignage, "Highlight Signage");

        GUILayout.Label("Signage Parent Names:");
        signageParentNames = GUILayout.TextField(signageParentNames);

        GUILayout.Label("Signage Tag:");
        signageTag = GUILayout.TextField(signageTag);

        GUILayout.Label("Signage Layer Name:");
        signageLayerName = GUILayout.TextField(signageLayerName);

        signageHighlightLineWidth = FloatField("Highlight Line Width", signageHighlightLineWidth, 0.001f, 0.3f);

        GUILayout.Space(12);

        GUI.enabled = !isExporting;

        if (GUILayout.Button("Start Export", GUILayout.Height(32)))
        {
            StartCoroutine(ExportCurrentSceneViews());
        }

        GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.Label("Progress: " + Mathf.RoundToInt(progress * 100f) + "%");
        GUILayout.HorizontalSlider(progress, 0f, 1f);

        GUILayout.Label("Status:");
        GUILayout.TextArea(statusMessage, GUILayout.Height(90));

        GUILayout.EndScrollView();

        GUI.DragWindow();
    }

    private int IntField(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(180));

        string text = GUILayout.TextField(value.ToString(), GUILayout.Width(90));
        int parsed;

        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Clamp(parsed, min, max);
        }

        GUILayout.EndHorizontal();
        return value;
    }

    private float FloatField(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(180));

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

    private IEnumerator ExportCurrentSceneViews()
    {
        if (isExporting)
        {
            yield break;
        }

        isExporting = true;
        progress = 0f;

        try
        {
            string sceneID = ResolveSceneID();

            if (StringIsNullOrWhiteSpace(sceneID))
            {
                statusMessage = "Cannot resolve SceneID.";
                Debug.LogError(statusMessage);
                isExporting = false;
                yield break;
            }

            LoadNodeInfoCsv();

            List<NodeRecord> nodesToExport = GetNodesToExport(sceneID);

            if (nodesToExport.Count == 0)
            {
                statusMessage = "No nodes found for SceneID=" + sceneID;
                Debug.LogError(statusMessage);
                isExporting = false;
                yield break;
            }

            List<float> yawOffsets = ParseFloatList(yawOffsetsText);
            List<float> pitchOffsets = ParseFloatList(pitchOffsetsText);

            if (yawOffsets.Count == 0 || pitchOffsets.Count == 0)
            {
                statusMessage = "Yaw or Pitch offset list is empty.";
                Debug.LogError(statusMessage);
                isExporting = false;
                yield break;
            }

            EnsureExportCamera();
            PrepareCleanExportObjects();

          
            if (showCurrentNodeHeatmap)
            {
                EnsureHeatmapVisualizer();
                LoadHeatmapDataIfPossible();
            }
            else
            {
                ClearHeatmap();
            }

            string sceneFolder = Path.Combine(outputRoot, "Scene" + sceneID);
            Directory.CreateDirectory(sceneFolder);

            int total = nodesToExport.Count * yawOffsets.Count * pitchOffsets.Count;
            int done = 0;

            for (int n = 0; n < nodesToExport.Count; n++)
            {
                NodeRecord node = nodesToExport[n];

                statusMessage = "Exporting Scene" + sceneID + " Node" + node.NodeID;
                Debug.Log(statusMessage);

                if (switchPanoramaBeforeCapture)
                {
                    yield return StartCoroutine(SwitchPanoramaAndWait(node.NodeID));
                }
                else
                {
                    for (int i = 0; i < waitFramesAfterPanoramaSwitch; i++)
                    {
                        yield return null;
                    }
                }
                if (highlightSignage)
                {
                    DestroySignageHighlights();
                    CreateSignageHighlights();
                    UpdateSignageLabelBillboards();
                }

                if (showCurrentNodeHeatmap)
                {
                    RefreshHeatmapForNode(node.NodeID);

                    for (int i = 0; i < 2; i++)
                    {
                        yield return null;
                    }
                }

                string nodeFolder = Path.Combine(sceneFolder, "Node" + node.NodeID.ToString("D2"));
                Directory.CreateDirectory(nodeFolder);

                for (int p = 0; p < pitchOffsets.Count; p++)
                {
                    float pitch = pitchOffsets[p];

                    for (int y = 0; y < yawOffsets.Count; y++)
                    {
                        float yaw = yawOffsets[y];

                        ApplyCameraPose(node, yaw, pitch);
                        UpdateSignageLabelBillboards();

                        yield return null;

                        UpdateSignageLabelBillboards();

                        string fileName =
                            "Scene" + sceneID.PadLeft(2, '0') +
                            "_Node" + node.NodeID.ToString("D2") +
                            "_Y" + FormatAngleForFile(yaw) +
                            "_P" + FormatAngleForFile(pitch) +
                            ".png";

                        string fullPath = Path.Combine(nodeFolder, fileName);

                        CaptureCameraToPng(exportCamera, imageWidth, imageHeight, fullPath);

                        done++;
                        progress = done / Mathf.Max(1f, (float)total);
                    }
                }
            }

            statusMessage =
                "Export finished.\n" +
                "SceneID: " + sceneID + "\n" +
                "Nodes: " + nodesToExport.Count + "\n" +
                "Images: " + total + "\n" +
                "Output: " + sceneFolder;

            Debug.Log(statusMessage);
        }
        finally
        {
            RestoreCleanExportObjects();
            DestroySignageHighlights();

            isExporting = false;
            progress = 1f;
        }
    }

    private string ResolveSceneID()
    {
        if (!StringIsNullOrWhiteSpace(sceneIDOverride))
        {
            return NormalizeSceneID(sceneIDOverride);
        }

        string sceneName = SceneManager.GetActiveScene().name;
        return ExtractDigits(sceneName);
    }

    private string NormalizeSceneID(string raw)
    {
        if (StringIsNullOrWhiteSpace(raw))
        {
            return "";
        }

        raw = raw.Trim();

        int parsed;
        if (int.TryParse(raw, out parsed))
        {
            return parsed.ToString();
        }

        string digits = ExtractDigits(raw);

        if (!StringIsNullOrWhiteSpace(digits))
        {
            return digits;
        }

        return raw;
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

    private void LoadNodeInfoCsv()
    {
        nodeRecordMap.Clear();

        string fullPath = Path.Combine(Application.streamingAssetsPath, nodeInfoRelativePath);

        if (!File.Exists(fullPath))
        {
            Debug.LogError("NodeInfo.csv not found: " + fullPath);
            return;
        }

        List<List<string>> table = ReadCsv(fullPath);

        if (table == null || table.Count < 2)
        {
            Debug.LogError("NodeInfo.csv is empty: " + fullPath);
            return;
        }

        List<string> header = table[0];

        int idxSceneID = FindColumnIndex(header, "SceneID");
        int idxNodeID = FindColumnIndex(header, "NodeID");

        int idxX = FindColumnIndex(header, "X");
        int idxY = FindColumnIndex(header, "Y");
        int idxZ = FindColumnIndex(header, "Z");

        int idxRotX = FindColumnIndex(header, "RotateX");
        int idxRotY = FindColumnIndex(header, "RotateY");
        int idxRotZ = FindColumnIndex(header, "RotateZ");

        int idxTX = FindColumnIndex(header, "TranslateX");
        int idxTY = FindColumnIndex(header, "TranslateY");
        int idxTZ = FindColumnIndex(header, "TranslateZ");

        if (idxSceneID < 0 || idxNodeID < 0 ||
            idxX < 0 || idxY < 0 || idxZ < 0 ||
            idxRotX < 0 || idxRotY < 0 || idxRotZ < 0)
        {
            Debug.LogError("NodeInfo.csv is missing required columns.");
            return;
        }

        for (int i = 1; i < table.Count; i++)
        {
            List<string> row = table[i];

            int sceneID;
            int nodeID;

            if (!int.TryParse(SafeGet(row, idxSceneID), out sceneID))
            {
                continue;
            }

            if (!int.TryParse(SafeGet(row, idxNodeID), out nodeID))
            {
                continue;
            }

            NodeRecord record = new NodeRecord();
            record.SceneID = sceneID;
            record.NodeID = nodeID;

            bool hasTranslate =
    idxTX >= 0 &&
    idxTY >= 0 &&
    idxTZ >= 0;

            if (hasTranslate)
            {
                record.Position = new Vector3(
                    -ReadFloat(row, idxTX, 0f),
                    -ReadFloat(row, idxTY, 0f),
                    -ReadFloat(row, idxTZ, 0f)
                );
            }
            else
            {
                record.Position = new Vector3(
                    ReadFloat(row, idxX, 0f),
                    ReadFloat(row, idxY, 0f) * 2f,
                    ReadFloat(row, idxZ, 0f)
                );
            }

            record.RotationEuler = new Vector3(
                ReadFloat(row, idxRotX, 0f),
                ReadFloat(row, idxRotY, 0f),
                ReadFloat(row, idxRotZ, 0f)
            );

            string key = MakeNodeKey(sceneID.ToString(), nodeID);
            nodeRecordMap[key] = record;
        }

        Debug.Log("Loaded NodeInfo records: " + nodeRecordMap.Count);
    }

    private List<NodeRecord> GetNodesToExport(string sceneID)
    {
        List<NodeRecord> result = new List<NodeRecord>();

        if (exportAllNodesInCurrentScene)
        {
            foreach (KeyValuePair<string, NodeRecord> pair in nodeRecordMap)
            {
                if (pair.Value.SceneID.ToString() == sceneID)
                {
                    result.Add(pair.Value);
                }
            }

            result.Sort((a, b) => a.NodeID.CompareTo(b.NodeID));

            if (result.Count > 0)
            {
                return result;
            }

            return GetNodesFromHierarchyFallback(sceneID);
        }

        string key = MakeNodeKey(sceneID, selectedNodeID);

        if (nodeRecordMap.ContainsKey(key))
        {
            result.Add(nodeRecordMap[key]);
            return result;
        }

        NodeRecord fallback;

        if (TryGetNodeFromHierarchy(sceneID, selectedNodeID, out fallback))
        {
            result.Add(fallback);
        }

        return result;
    }

    private List<NodeRecord> GetNodesFromHierarchyFallback(string sceneID)
    {
        List<NodeRecord> result = new List<NodeRecord>();

        GameObject parent = GameObject.Find(nodesParentName);

        if (parent == null)
        {
            return result;
        }

        foreach (Transform child in parent.transform)
        {
            int nodeID;

            if (!TryParseNodeID(child.name, out nodeID))
            {
                continue;
            }

            NodeRecord record = new NodeRecord();
            record.SceneID = SafeInt(sceneID, 0);
            record.NodeID = nodeID;
            record.Position = new Vector3(
    child.position.x,
    child.position.y * 2f,
    child.position.z
);
            record.RotationEuler = child.rotation.eulerAngles;

            result.Add(record);
        }

        result.Sort((a, b) => a.NodeID.CompareTo(b.NodeID));
        return result;
    }

    private bool TryGetNodeFromHierarchy(string sceneID, int nodeID, out NodeRecord record)
    {
        record = null;

        GameObject parent = GameObject.Find(nodesParentName);

        if (parent == null)
        {
            return false;
        }

        Transform nodeTransform = parent.transform.Find("Node" + nodeID);

        if (nodeTransform == null)
        {
            return false;
        }

        record = new NodeRecord();
        record.SceneID = SafeInt(sceneID, 0);
        record.NodeID = nodeID;
        record.Position = new Vector3(
    nodeTransform.position.x,
    nodeTransform.position.y * 2f,
    nodeTransform.position.z
);
        record.RotationEuler = nodeTransform.rotation.eulerAngles;

        return true;
    }

    private bool TryParseNodeID(string name, out int nodeID)
    {
        nodeID = 0;

        string digits = ExtractDigits(name);

        if (StringIsNullOrWhiteSpace(digits))
        {
            return false;
        }

        return int.TryParse(digits, out nodeID);
    }

    private string MakeNodeKey(string sceneID, int nodeID)
    {
        return sceneID + "_" + nodeID;
    }

    private void EnsureExportCamera()
    {
        if (exportCamera != null)
        {
            exportCamera.enabled = false;
            return;
        }

        if (!autoCreateExportCamera)
        {
            exportCamera = Camera.main;

            if (exportCamera != null)
            {
                exportCamera.enabled = false;
            }

            return;
        }

        GameObject cameraObj = new GameObject("Generated_DecisionPointExportCamera");
        exportCamera = cameraObj.AddComponent<Camera>();
        exportCamera.enabled = false;
        exportCamera.clearFlags = CameraClearFlags.Skybox;
        exportCamera.cullingMask = ~0;
        exportCamera.nearClipPlane = nearClip;
        exportCamera.farClipPlane = farClip;
    }

    private void ApplyCameraPose(NodeRecord node, float yawOffset, float pitchOffset)
    {
        float aspect = imageWidth / Mathf.Max(1f, (float)imageHeight);
        float verticalFov = HorizontalToVerticalFOV(horizontalFOV, aspect);

        exportCamera.fieldOfView = Mathf.Clamp(verticalFov, 1f, 179f);
        exportCamera.nearClipPlane = Mathf.Max(0.001f, nearClip);
        exportCamera.farClipPlane = Mathf.Max(exportCamera.nearClipPlane + 0.1f, farClip);
        exportCamera.aspect = aspect;

        Quaternion baseRotation = Quaternion.Euler(node.RotationEuler);
        Quaternion offsetRotation =
            Quaternion.Euler(cameraRotationOffsetEuler) *
            Quaternion.Euler(pitchOffset, yawOffset, 0f);

        exportCamera.transform.position = node.Position + cameraPositionOffset;
        exportCamera.transform.rotation = baseRotation * offsetRotation;
    }

    private float HorizontalToVerticalFOV(float horizontalFovDeg, float aspect)
    {
        float hRad = horizontalFovDeg * Mathf.Deg2Rad;
        float vRad = 2f * Mathf.Atan(Mathf.Tan(hRad * 0.5f) / Mathf.Max(0.0001f, aspect));
        return vRad * Mathf.Rad2Deg;
    }

    private void CaptureCameraToPng(Camera cam, int width, int height, string filePath)
    {
        RenderTexture oldTarget = cam.targetTexture;
        RenderTexture oldActive = RenderTexture.active;

        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.Create();

        cam.targetTexture = rt;
        RenderTexture.active = rt;

        cam.Render();

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        byte[] pngBytes = tex.EncodeToPNG();

        File.WriteAllBytes(filePath, pngBytes);

        cam.targetTexture = oldTarget;
        RenderTexture.active = oldActive;

        Destroy(tex);
        rt.Release();
        Destroy(rt);
    }

    private void SwitchPanorama(int nodeID)
    {
        if (panoramaController == null)
        {
            panoramaController = FindObjectOfType<UniversalPanoramaTest>();
        }

        if (panoramaController == null)
        {
            Debug.LogWarning("UniversalPanoramaTest not found. Panorama will not be switched.");
            return;
        }

        MethodInfo replayMethod = panoramaController.GetType().GetMethod(
            "SwitchToNodeFromReplay",
            InstanceBindings
        );

        if (replayMethod != null)
        {
            replayMethod.Invoke(panoramaController, new object[] { nodeID.ToString() });
            return;
        }

        MethodInfo switchMethod = panoramaController.GetType().GetMethod(
            "SwitchToNode",
            InstanceBindings
        );

        if (switchMethod != null)
        {
            switchMethod.Invoke(panoramaController, new object[] { nodeID });
            return;
        }

        Debug.LogWarning("No panorama switch method found on UniversalPanoramaTest.");
    }

    private IEnumerator SwitchPanoramaAndWait(int nodeID)
    {
        if (panoramaController == null)
        {
            panoramaController = FindObjectOfType<UniversalPanoramaTest>();
        }

        if (panoramaController == null)
        {
            Debug.LogWarning("UniversalPanoramaTest not found. Panorama will not be switched.");
            yield break;
        }

        MethodInfo waitMethod = panoramaController.GetType().GetMethod(
            "SwitchToNodeAndWait",
            InstanceBindings
        );

        if (waitMethod != null)
        {
            object result = waitMethod.Invoke(
                panoramaController,
                new object[] { nodeID.ToString() }
            );

            IEnumerator routine = result as IEnumerator;

            if (routine != null)
            {
                yield return StartCoroutine(routine);
            }
        }
        else
        {
            // Fallback: old switching method.
            SwitchPanorama(nodeID);

            for (int i = 0; i < waitFramesAfterPanoramaSwitch; i++)
            {
                yield return null;
            }
        }

        // Extra safety frames after panorama update.
        for (int i = 0; i < waitFramesAfterPanoramaSwitch; i++)
        {
            yield return null;
        }

        yield return new WaitForEndOfFrame();
    }

    private void EnsureHeatmapVisualizer()
    {
        if (heatmapVisualizer == null)
        {
            heatmapVisualizer = FindObjectOfType<GazeSurfaceSplatHeatmapVisualizer>();
        }

        if (heatmapVisualizer == null)
        {
            heatmapVisualizer = gameObject.AddComponent<GazeSurfaceSplatHeatmapVisualizer>();
        }

        heatmapVisualizer.showControlPanel = false;
        heatmapVisualizer.processedRoot = processedRoot;
        heatmapVisualizer.taskOrder = taskOrder;
        heatmapVisualizer.csvFileIndex = csvFileIndex;
        heatmapVisualizer.overrideCsvFullPath = overrideHeatmapCsvFullPath;
    }

    private void LoadHeatmapDataIfPossible()
    {
        if (heatmapVisualizer == null)
        {
            return;
        }

        InvokeNoParamMethod(heatmapVisualizer, "LoadCsvAndBuildNodeStays");
    }

    private void RefreshHeatmapForNode(int nodeID)
    {
        if (heatmapVisualizer == null)
        {
            return;
        }

        ApplyNodeStaySelectionForNodeByReflection(heatmapVisualizer, nodeID);
        InvokeNoParamMethod(heatmapVisualizer, "RefreshHeatmap");
    }

    private void ClearHeatmap()
    {
        if (heatmapVisualizer != null)
        {
            InvokeNoParamMethod(heatmapVisualizer, "ClearHeatmap");
        }
    }

    private void PrepareCleanExportObjects()
    {
        temporarilyHiddenObjects.Clear();
        hiddenObjectOriginalActive.Clear();

        if (!hideGazeRaysAndHitPoints)
        {
            return;
        }

        if (gazeHitVisualizer == null)
        {
            gazeHitVisualizer = FindObjectOfType<GazeHitCsvVisualizer>();
        }

        if (gazeHitVisualizer != null)
        {
            InvokeNoParamMethod(gazeHitVisualizer, "ClearVisualization");
            gazeHitVisualizer.showControlPanel = false;
        }

        string[] tokens = SplitCsvTokens(hideObjectNameTokens);

        if (tokens.Length == 0)
        {
            return;
        }

        GameObject[] allObjects = FindObjectsOfType<GameObject>();

        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject obj = allObjects[i];

            if (obj == null || !obj.activeInHierarchy)
            {
                continue;
            }

            for (int t = 0; t < tokens.Length; t++)
            {
                if (StringIsNullOrWhiteSpace(tokens[t]))
                {
                    continue;
                }

                if (obj.name.IndexOf(tokens[t].Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    temporarilyHiddenObjects.Add(obj);
                    hiddenObjectOriginalActive[obj] = obj.activeSelf;
                    obj.SetActive(false);
                    break;
                }
            }
        }
    }

    private void RestoreCleanExportObjects()
    {
        foreach (KeyValuePair<GameObject, bool> pair in hiddenObjectOriginalActive)
        {
            if (pair.Key != null)
            {
                pair.Key.SetActive(pair.Value);
            }
        }

        temporarilyHiddenObjects.Clear();
        hiddenObjectOriginalActive.Clear();
    }

    private void CreateSignageHighlights()
    {
        DestroySignageHighlights();
        DestroySignageLabels();

        List<GameObject> signageTargets = CollectSignageTargetObjects();

        if (signageTargets.Count == 0)
        {
            Debug.LogWarning("No signage target objects found. Please set Signage Tag, Layer, or Parent Names.");
            return;
        }

        if (highlightLineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            highlightLineMaterial = new Material(shader);
            highlightLineMaterial.color = signageHighlightColor;
        }

        signageHighlightRoot = new GameObject("Generated_SignageHighlightRoot");

        if (drawSignageObjectNames)
        {
            signageLabelRoot = new GameObject("Generated_SignageLabelRoot");
        }

        int count = 0;

        for (int i = 0; i < signageTargets.Count; i++)
        {
            GameObject target = signageTargets[i];

            if (target == null)
            {
                continue;
            }

            Bounds bounds;
            if (!TryGetCombinedBounds(target, out bounds))
            {
                continue;
            }

            // Draw bounding box
            CreateBoundsBoxLines(bounds, signageHighlightRoot.transform);

            // Draw name label
            if (drawSignageObjectNames && signageLabelRoot != null)
            {
                CreateSignageNameLabel(GetSignageDisplayName(target), bounds, signageLabelRoot.transform);
            }

            count++;
        }

        Debug.Log("Signage annotations created: " + count);
    }


    private string GetSignageDisplayName(GameObject target)
    {
        if (target == null)
        {
            return "";
        }

        Transform parent = target.transform.parent;

        if (parent != null &&
            target.name.StartsWith("Node", StringComparison.OrdinalIgnoreCase))
        {
            return parent.name + "-" + target.name;
        }

        return target.name;
    }

    private List<GameObject> CollectSignageTargetObjects()
    {
        HashSet<GameObject> resultSet = new HashSet<GameObject>();

        GameObject signageRootObj = GameObject.Find("Signage");

        if (signageRootObj == null)
        {
            Debug.LogWarning("Cannot find Signage root object.");
            return new List<GameObject>(resultSet);
        }

        foreach (Transform signage in signageRootObj.transform)
        {
            if (signage == null || !signage.gameObject.activeInHierarchy)
            {
                continue;
            }

            AddVisibleSignageObject(signage, resultSet);
        }

        return new List<GameObject>(resultSet);
    }

    private void AddVisibleSignageObject(Transform signage, HashSet<GameObject> resultSet)
    {
        if (signage == null || !signage.gameObject.activeInHierarchy)
        {
            return;
        }

        // Case 1: no child objects.
        // Example: WS-01, HS-01
        // Use the signage object itself.
        if (signage.childCount == 0)
        {
            resultSet.Add(signage.gameObject);
            return;
        }

        // Case 2: has child objects.
        // Example: SS-05 / Node5, Node6, Node9, Node10
        // Only use currently active child objects.
        bool addedActiveChild = false;

        for (int i = 0; i < signage.childCount; i++)
        {
            Transform child = signage.GetChild(i);

            if (child == null)
            {
                continue;
            }

            if (child.gameObject.activeInHierarchy)
            {
                resultSet.Add(child.gameObject);
                addedActiveChild = true;
            }
        }

        // If it has children but none are active, do not add the parent.
        // This avoids showing the wrong signage variant.
    }
    

    private bool TryGetCombinedBounds(GameObject target, out Bounds bounds)
    {
        bounds = new Bounds();

        if (target == null)
        {
            return false;
        }

        Renderer[] rs = target.GetComponentsInChildren<Renderer>(false);

        if (rs == null || rs.Length == 0)
        {
            bounds = new Bounds(target.transform.position, Vector3.one * 0.1f);
            return false;
        }

        bounds = rs[0].bounds;

        for (int i = 1; i < rs.Length; i++)
        {
            bounds.Encapsulate(rs[i].bounds);
        }

        return true;
    }

    private void CreateSignageNameLabel(string labelText, Bounds bounds, Transform parent)
    {
        GameObject labelObj = new GameObject("SignageName_" + labelText);
        labelObj.transform.SetParent(parent, false);

        labelObj.transform.position =
            bounds.center + Vector3.up * (bounds.extents.y + signageLabelHeightOffset);

        TextMesh tm = labelObj.AddComponent<TextMesh>();

        if (signageLabelFont != null)
        {
            tm.font = signageLabelFont;
        }
        else
        {
            tm.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        tm.text = labelText;
        tm.fontSize = signageLabelFontSize;
        tm.characterSize = signageLabelCharacterSize;
        tm.color = signageLabelColor;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;

        MeshRenderer mr = labelObj.GetComponent<MeshRenderer>();
        if (mr != null && tm.font != null)
        {
            mr.material = tm.font.material;
        }

        signageTextMeshes.Add(tm);
    }

    private void UpdateSignageLabelBillboards()
    {
        if (!billboardSignageLabels)
        {
            return;
        }

        if (exportCamera == null)
        {
            return;
        }

        if (signageTextMeshes == null || signageTextMeshes.Count == 0)
        {
            return;
        }

        for (int i = 0; i < signageTextMeshes.Count; i++)
        {
            TextMesh tm = signageTextMeshes[i];

            if (tm == null)
            {
                continue;
            }

            Transform t = tm.transform;

            // Make TextMesh face the export camera.
            // For Unity TextMesh, this orientation is usually the readable one.
            Vector3 directionFromCameraToLabel = t.position - exportCamera.transform.position;

            if (directionFromCameraToLabel.sqrMagnitude > 0.0001f)
            {
                t.rotation = Quaternion.LookRotation(directionFromCameraToLabel, exportCamera.transform.up);
            }
        }
    }

    private void DestroySignageLabels()
    {
        if (signageLabelRoot != null)
        {
            Destroy(signageLabelRoot);
            signageLabelRoot = null;
        }

        signageTextMeshes.Clear();
    }

    private void DestroySignageHighlights()
    {
        if (signageHighlightRoot != null)
        {
            Destroy(signageHighlightRoot);
            signageHighlightRoot = null;
        }

        DestroySignageLabels();
    }

    private List<Renderer> CollectSignageRenderers()
    {
        HashSet<Renderer> resultSet = new HashSet<Renderer>();

        if (signageParents != null)
        {
            for (int i = 0; i < signageParents.Length; i++)
            {
                if (signageParents[i] == null)
                {
                    continue;
                }

                Renderer[] rs = signageParents[i].GetComponentsInChildren<Renderer>(true);

                for (int r = 0; r < rs.Length; r++)
                {
                    resultSet.Add(rs[r]);
                }
            }
        }

        string[] parentNames = SplitCsvTokens(signageParentNames);

        for (int i = 0; i < parentNames.Length; i++)
        {
            string parentName = parentNames[i].Trim();

            if (StringIsNullOrWhiteSpace(parentName))
            {
                continue;
            }

            GameObject parentObj = GameObject.Find(parentName);

            if (parentObj == null)
            {
                continue;
            }

            Renderer[] rs = parentObj.GetComponentsInChildren<Renderer>(true);

            for (int r = 0; r < rs.Length; r++)
            {
                resultSet.Add(rs[r]);
            }
        }

        if (!StringIsNullOrWhiteSpace(signageTag))
        {
            try
            {
                GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(signageTag.Trim());

                for (int i = 0; i < taggedObjects.Length; i++)
                {
                    Renderer[] rs = taggedObjects[i].GetComponentsInChildren<Renderer>(true);

                    for (int r = 0; r < rs.Length; r++)
                    {
                        resultSet.Add(rs[r]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Cannot search signage tag: " + signageTag + "\n" + ex.Message);
            }
        }

        if (!StringIsNullOrWhiteSpace(signageLayerName))
        {
            int layer = LayerMask.NameToLayer(signageLayerName.Trim());

            if (layer >= 0)
            {
                Renderer[] allRenderers = FindObjectsOfType<Renderer>();

                for (int i = 0; i < allRenderers.Length; i++)
                {
                    if (allRenderers[i].gameObject.layer == layer)
                    {
                        resultSet.Add(allRenderers[i]);
                    }
                }
            }
            else
            {
                Debug.LogWarning("Layer not found: " + signageLayerName);
            }
        }

        return new List<Renderer>(resultSet);
    }

    private void CreateBoundsBoxLines(Bounds b, Transform parent)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        Vector3[] p = new Vector3[8];

        p[0] = c + new Vector3(-e.x, -e.y, -e.z);
        p[1] = c + new Vector3(e.x, -e.y, -e.z);
        p[2] = c + new Vector3(e.x, -e.y, e.z);
        p[3] = c + new Vector3(-e.x, -e.y, e.z);

        p[4] = c + new Vector3(-e.x, e.y, -e.z);
        p[5] = c + new Vector3(e.x, e.y, -e.z);
        p[6] = c + new Vector3(e.x, e.y, e.z);
        p[7] = c + new Vector3(-e.x, e.y, e.z);

        AddLine(parent, p[0], p[1]);
        AddLine(parent, p[1], p[2]);
        AddLine(parent, p[2], p[3]);
        AddLine(parent, p[3], p[0]);

        AddLine(parent, p[4], p[5]);
        AddLine(parent, p[5], p[6]);
        AddLine(parent, p[6], p[7]);
        AddLine(parent, p[7], p[4]);

        AddLine(parent, p[0], p[4]);
        AddLine(parent, p[1], p[5]);
        AddLine(parent, p[2], p[6]);
        AddLine(parent, p[3], p[7]);
    }

    private void AddLine(Transform parent, Vector3 a, Vector3 b)
    {
        GameObject lineObj = new GameObject("SignageHighlightLine");
        lineObj.transform.SetParent(parent, false);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);

        lr.startWidth = signageHighlightLineWidth;
        lr.endWidth = signageHighlightLineWidth;

        lr.material = highlightLineMaterial;
        lr.startColor = signageHighlightColor;
        lr.endColor = signageHighlightColor;
    }

    private void ApplyNodeStaySelectionForNodeByReflection(object visualizer, int nodeID)
    {
        if (visualizer == null)
        {
            return;
        }

        FieldInfo nodeStaysField = visualizer.GetType().GetField("nodeStays", InstanceBindings);

        if (nodeStaysField == null)
        {
            Debug.LogWarning("Cannot find nodeStays field on " + visualizer.GetType().Name);
            return;
        }

        IList list = nodeStaysField.GetValue(visualizer) as IList;

        if (list == null)
        {
            Debug.LogWarning("nodeStays is null on " + visualizer.GetType().Name);
            return;
        }

        bool anySelected = false;

        for (int i = 0; i < list.Count; i++)
        {
            object segment = list[i];

            if (segment == null)
            {
                continue;
            }

            FieldInfo nodeIDField = segment.GetType().GetField("nodeID", InstanceBindings);
            FieldInfo selectedField = segment.GetType().GetField("selected", InstanceBindings);

            if (nodeIDField == null || selectedField == null)
            {
                continue;
            }

            string rawNodeID = nodeIDField.GetValue(segment) as string;
            int segmentNodeID;

            bool match = false;

            if (TryNormalizeNodeID(rawNodeID, out segmentNodeID))
            {
                match = segmentNodeID == nodeID;
            }

            selectedField.SetValue(segment, match);

            if (match)
            {
                anySelected = true;
            }
        }

        if (!anySelected)
        {
            Debug.LogWarning("No matching NodeStay found for Node" + nodeID + ". Heatmap may be empty.");
        }
    }

    private bool TryNormalizeNodeID(string raw, out int nodeID)
    {
        nodeID = 0;

        if (StringIsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string digits = ExtractDigits(raw);

        if (StringIsNullOrWhiteSpace(digits))
        {
            digits = raw.Trim();
        }

        return int.TryParse(digits, out nodeID);
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
            Debug.LogWarning("Cannot find method: " + methodName + " on " + target.GetType().Name);
            return false;
        }

        method.Invoke(target, null);
        return true;
    }

    private List<float> ParseFloatList(string text)
    {
        List<float> result = new List<float>();

        if (StringIsNullOrWhiteSpace(text))
        {
            return result;
        }

        string[] parts = text.Split(',');

        for (int i = 0; i < parts.Length; i++)
        {
            float value;

            if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private string[] SplitCsvTokens(string text)
    {
        if (StringIsNullOrWhiteSpace(text))
        {
            return new string[0];
        }

        return text.Split(',');
    }

    private string FormatAngleForFile(float angle)
    {
        string sign = angle >= 0 ? "p" : "m";
        int rounded = Mathf.RoundToInt(Mathf.Abs(angle));
        return sign + rounded.ToString("D3");
    }

    private int SafeInt(string text, int defaultValue)
    {
        int value;

        if (int.TryParse(text, out value))
        {
            return value;
        }

        return defaultValue;
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

    private bool StringIsNullOrWhiteSpace(string text)
    {
        return text == null || text.Trim().Length == 0;
    }

    private class NodeRecord
    {
        public int SceneID;
        public int NodeID;
        public Vector3 Position;
        public Vector3 RotationEuler;
    }
}