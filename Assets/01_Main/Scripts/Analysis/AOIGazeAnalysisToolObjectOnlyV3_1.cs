using UnityEngine;
using UnityEngine.SceneManagement;

using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// AOI gaze-hit analysis tool.
///
/// Main workflow:
/// 1. Select one or more TaskOrder values and one or more NodeID values.
/// 2. Read participant eye-tracking CSV files.
/// 3. Raycast each valid gaze ray against the AOI layer.
/// 4. Resolve Category/ObjectName from the hierarchy:
///      AOI
///        └─ Category
///             └─ ObjectName (optional)
/// 5. Accumulate AOI duration using adjacent timestamps.
/// 6. Save frame-level gaze-hit files, trial summaries, and node-level summaries.
/// 7. Display node-level median AOI proportions in the Game view.
///
/// Designed for Unity 2020.3+.
/// </summary>
public class AOIGazeAnalysisToolObjectOnlyV3_1 : MonoBehaviour
{
    private const string ToolVersion = "ObjectOnlyV3.1";
    [Serializable]
    public class TaskDataSelection
    {
        [Tooltip("对应 StreamingAssets/Reorder/TaskX 中的 TaskX")]
        public int taskOrder = 1;

        [Tooltip("勾选后载入该任务数据文件夹中的全部 CSV")]
        public bool loadAllCsv = true;

        [Tooltip("未勾选 Load All 时使用。支持 1,3,5-8；index 从 1 开始，按文件名排序。")]
        public string csvIndices = "1";
    }

    public enum TimestampUnit
    {
        Auto,
        Seconds,
        Milliseconds,
        Microseconds
    }

    [Header("AOI Model")]
    [Tooltip("AOI父物体。若为空，运行时会按名称查找。")]
    public Transform aoiRoot;

    [Tooltip("AOI父物体名称。")]
    public string aoiRootObjectName = "AOI";

    [Tooltip("所有AOI模型应设置到该Layer。")]
    public LayerMask aoiLayerMask;

    [Tooltip("射线最大检测距离。")]
    public float maxRayDistance = 1000f;

    [Header("Input Data Paths")]
    public string dataRootFolderName = "Reorder";
    public string dataSubFolderName = "Eye";

    [Tooltip("可添加多个TaskOrder。每条任务可载入全部CSV，或指定CSV index。")]
    public List<TaskDataSelection> taskSelections = new List<TaskDataSelection>();

    [Tooltip("支持 12,15,18-20。")]
    public string nodeIds = "12";

    [Tooltip("为空时，从当前Scene名称中提取数字。")]
    public string sceneIDOverride = "";

    [Header("CSV Column Names")]
    public string timestampColumn = "Timestamp";
    public string sceneIdColumn = "SceneID";
    public string participantIdColumn = "ID";
    public string nodeIdColumn = "NodeID";

    public string gazeOriginXColumn = "GazeOriginWorldX";
    public string gazeOriginYColumn = "GazeOriginWorldY";
    public string gazeOriginZColumn = "GazeOriginWorldZ";

    public string gazeDirectionXColumn = "GazeDirectionWorldX";
    public string gazeDirectionYColumn = "GazeDirectionWorldY";
    public string gazeDirectionZColumn = "GazeDirectionWorldZ";

    [Tooltip("可留空。若填写但列不存在，则自动根据射线向量判断有效性。")]
    public string gazeValidColumn = "";

    [Tooltip("有效值，逗号分隔，例如 1,true,valid。")]
    public string gazeValidValues = "1,true,valid";

    [Header("Timestamp Processing")]
    public TimestampUnit timestampUnit = TimestampUnit.Auto;

    [Tooltip("相邻帧间隔超过该值时不累计。设为0则不限制。120Hz数据建议0.1秒。")]
    public float maxAcceptedDeltaSeconds = 0.1f;

    [Header("Output")]
    public string aoiOutputRootFolder = "AOI";
    public string gazeHitFolderName = "GazeHit";
    public string summaryFolderName = "Output";

    public bool saveFrameLevelGazeHit = true;
    public bool useExistingGazeHitCache = true;
    public bool overwriteExistingGazeHit = false;
    public bool appendTimestampToSummaryFileName = true;

    [Header("Processing")]
    [Tooltip("每处理多少行后让出一帧，避免Unity长时间无响应。")]
    public int yieldEveryNRows = 1000;

    [Tooltip("运行前自动检查AOI层级、Layer和Collider。")]
    public bool validateBeforeRun = true;

    [Header("Game View Panel")]
    public bool showControlPanel = true;
    public Rect panelRect = new Rect(20, 20, 600, 760);
    public int percentageDecimals = 1;

    private Vector2 scrollPos = Vector2.zero;
    private bool isProcessing = false;
    private bool cancelRequested = false;
    private float progress01 = 0f;
    private string statusMessage = "Ready.";

    private readonly List<TrialNodeStats> allTrialNodeStats = new List<TrialNodeStats>();
    private readonly List<NodeSummaryRow> currentNodeSummaryRows = new List<NodeSummaryRow>();

    private readonly List<AOIObjectDefinition> aoiObjectDefinitions = new List<AOIObjectDefinition>();
    private readonly List<string> aoiCategories = new List<string>();

    private void Awake()
    {
        EnsureTaskSelections();
        ResolveAOIRoot();
        EnsureAOILayerMask();
    }

    private void Reset()
    {
        EnsureTaskSelections();
        ResolveAOIRoot();
        EnsureAOILayerMask();
    }

    private void OnValidate()
    {
        EnsureTaskSelections();
        maxRayDistance = Mathf.Max(0.01f, maxRayDistance);
        yieldEveryNRows = Mathf.Max(50, yieldEveryNRows);
        percentageDecimals = Mathf.Clamp(percentageDecimals, 0, 4);
    }

    private void EnsureTaskSelections()
    {
        if (taskSelections == null)
        {
            taskSelections = new List<TaskDataSelection>();
        }

        if (taskSelections.Count == 0)
        {
            taskSelections.Add(new TaskDataSelection());
        }
    }

    private void ResolveAOIRoot()
    {
        if (aoiRoot != null)
        {
            return;
        }

        GameObject found = GameObject.Find(aoiRootObjectName);
        if (found != null)
        {
            aoiRoot = found.transform;
        }
    }

    private void EnsureAOILayerMask()
    {
        if (aoiLayerMask.value != 0)
        {
            return;
        }

        int layer = LayerMask.NameToLayer("AOI");
        if (layer >= 0)
        {
            aoiLayerMask = 1 << layer;
        }
    }

    private void OnGUI()
    {
        if (!showControlPanel)
        {
            return;
        }

        EnsureTaskSelections();
        panelRect = GUI.Window(20260731, panelRect, DrawPanel, "AOI Gaze Analysis Tool - " + ToolVersion);
    }

    private void DrawPanel(int windowID)
    {
        scrollPos = GUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("AOI Model", GUI.skin.box);
        GUILayout.Label(
            aoiRoot != null
                ? "AOI Root: " + aoiRoot.name
                : "AOI Root: Not found",
            GUI.skin.label
        );

        if (GUILayout.Button("Find AOI Root", GUILayout.Height(26)))
        {
            aoiRoot = null;
            ResolveAOIRoot();
            EnsureAOILayerMask();
            statusMessage = aoiRoot != null
                ? "AOI root found: " + aoiRoot.name
                : "AOI root not found.";
        }

        if (GUILayout.Button("Validate AOI Model", GUILayout.Height(26)))
        {
            ValidateAOIModel();
        }

        GUILayout.Space(8);
        GUILayout.Label("Task Data Selection", GUI.skin.box);
        GUILayout.Label("CSV index 从1开始并按文件名排序；支持 1,3,5-8。");

        int removeIndex = -1;

        for (int i = 0; i < taskSelections.Count; i++)
        {
            TaskDataSelection selection = taskSelections[i];

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Task Selection " + (i + 1), GUILayout.Width(190));

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

        if (removeIndex >= 0 && taskSelections.Count > 1 && !isProcessing)
        {
            taskSelections.RemoveAt(removeIndex);
        }

        GUI.enabled = !isProcessing;

        if (GUILayout.Button("+ Add Task Selection", GUILayout.Height(26)))
        {
            taskSelections.Add(new TaskDataSelection());
        }

        GUILayout.Space(6);
        GUILayout.Label("Node IDs:");
        nodeIds = GUILayout.TextField(nodeIds);

        GUILayout.Label("Scene ID Override:");
        sceneIDOverride = GUILayout.TextField(sceneIDOverride);

        GUILayout.Space(8);
        GUILayout.Label("Cache / Output", GUI.skin.box);

        saveFrameLevelGazeHit = GUILayout.Toggle(
            saveFrameLevelGazeHit,
            "Save Frame-level GazeHit"
        );

        useExistingGazeHitCache = GUILayout.Toggle(
            useExistingGazeHitCache,
            "Use Existing GazeHit Cache"
        );

        overwriteExistingGazeHit = GUILayout.Toggle(
            overwriteExistingGazeHit,
            "Overwrite Existing GazeHit"
        );

        GUILayout.Space(8);

        if (GUILayout.Button("Run AOI Analysis", GUILayout.Height(34)))
        {
            StartAnalysis();
        }

        if (GUILayout.Button("Clear Current Results", GUILayout.Height(28)))
        {
            allTrialNodeStats.Clear();
            currentNodeSummaryRows.Clear();
            progress01 = 0f;
            statusMessage = "Current in-memory results cleared.";
        }

        GUI.enabled = true;

        if (isProcessing)
        {
            if (GUILayout.Button("Cancel", GUILayout.Height(28)))
            {
                cancelRequested = true;
            }
        }

        GUILayout.Space(8);
        GUILayout.Label("Progress", GUI.skin.box);

        Rect progressRect = GUILayoutUtility.GetRect(10f, 22f, GUILayout.ExpandWidth(true));
        GUI.Box(progressRect, "");
        Rect fillRect = new Rect(
            progressRect.x + 2f,
            progressRect.y + 2f,
            Mathf.Max(0f, (progressRect.width - 4f) * Mathf.Clamp01(progress01)),
            progressRect.height - 4f
        );
        GUI.Box(fillRect, "");
        GUI.Label(
            progressRect,
            (progress01 * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%",
            CenteredLabelStyle()
        );

        GUILayout.Label("Status:");
        GUILayout.TextArea(statusMessage, GUILayout.MinHeight(180));

        DrawResultSummary();

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, panelRect.width, 24));
    }

    private GUIStyle CenteredLabelStyle()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        return style;
    }

    private void DrawResultSummary()
    {
        GUILayout.Space(8);
        GUILayout.Label("Node-level AOI Object Proportions", GUI.skin.box);

        if (currentNodeSummaryRows.Count == 0)
        {
            GUILayout.Label("No results.");
            return;
        }

        GUILayout.Label(
            "Version: " + ToolVersion +
            ". One row per Category + ObjectName; no Category summary rows."
        );

        string currentNodeKey = "";

        for (int i = 0; i < currentNodeSummaryRows.Count; i++)
        {
            NodeSummaryRow row = currentNodeSummaryRows[i];
            string nodeKey = row.sceneID + "_Node" + row.nodeID;

            if (nodeKey != currentNodeKey)
            {
                currentNodeKey = nodeKey;
                GUILayout.Space(4);
                GUILayout.Label(
                    "Scene " + row.sceneID +
                    " | Node " + row.nodeID +
                    " | Valid trials: " + row.validTrialCount,
                    GUI.skin.box
                );
            }

            string displayName = string.Equals(
                row.category,
                row.objectName,
                StringComparison.OrdinalIgnoreCase
            )
                ? row.objectName
                : row.category + " / " + row.objectName;

            GUILayout.BeginHorizontal();
            GUILayout.Label(displayName, GUILayout.Width(320));

            string valueText =
                "Median " +
                (row.medianProportion * 100f).ToString(
                    "F" + percentageDecimals,
                    CultureInfo.InvariantCulture
                ) +
                "% | Mean " +
                (row.meanProportion * 100f).ToString(
                    "F" + percentageDecimals,
                    CultureInfo.InvariantCulture
                ) +
                "%";

            GUILayout.Label(valueText, GUILayout.Width(220));
            GUILayout.EndHorizontal();
        }
    }

    private int IntField(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(190));

        string text = GUILayout.TextField(
            value.ToString(CultureInfo.InvariantCulture),
            GUILayout.Width(110)
        );

        int parsed;
        if (int.TryParse(text, out parsed))
        {
            value = Mathf.Max(1, parsed);
        }

        GUILayout.EndHorizontal();
        return value;
    }

    public void StartAnalysis()
    {
        if (isProcessing)
        {
            return;
        }

        StartCoroutine(RunAnalysisCoroutine());
    }

    private IEnumerator RunAnalysisCoroutine()
    {
        isProcessing = true;
        cancelRequested = false;
        progress01 = 0f;
        statusMessage = "Preparing analysis...";

        allTrialNodeStats.Clear();
        currentNodeSummaryRows.Clear();

        ResolveAOIRoot();
        EnsureAOILayerMask();

        if (aoiRoot == null)
        {
            statusMessage = "AOI root not found. Assign it in the Inspector or use the Find AOI Root button.";
            isProcessing = false;
            yield break;
        }

        List<int> selectedNodes = ParseIndexExpressionUnlimited(nodeIds);

        if (selectedNodes.Count == 0)
        {
            statusMessage = "No valid NodeID values were provided.";
            isProcessing = false;
            yield break;
        }

        if (validateBeforeRun && !ValidateAOIModel())
        {
            statusMessage += "\nAnalysis stopped because AOI validation failed.";
            isProcessing = false;
            yield break;
        }

        BuildAOIDefinitions();

        List<SourceFileSelection> sourceFiles = ResolveSelectedSourceFiles();

        if (sourceFiles.Count == 0)
        {
            statusMessage = "No source CSV files were selected or found.";
            isProcessing = false;
            yield break;
        }

        string sceneID = ResolveSceneID();

        if (StringIsNullOrWhiteSpace(sceneID))
        {
            statusMessage = "SceneID is empty. Set Scene ID Override or use a scene name containing digits.";
            isProcessing = false;
            yield break;
        }

        Physics.SyncTransforms();

        int totalFiles = sourceFiles.Count;

        for (int fileIndex = 0; fileIndex < sourceFiles.Count; fileIndex++)
        {
            if (cancelRequested)
            {
                statusMessage = "Analysis cancelled.";
                isProcessing = false;
                yield break;
            }

            SourceFileSelection source = sourceFiles[fileIndex];

            statusMessage =
                "Processing " + (fileIndex + 1) + " / " + totalFiles + "\n" +
                "Task" + source.taskOrder + "\n" +
                Path.GetFileName(source.fullPath);

            bool allCachesAvailable = useExistingGazeHitCache &&
                                      !overwriteExistingGazeHit &&
                                      AreAllCachesAvailable(sceneID, source, selectedNodes);

            if (allCachesAvailable)
            {
                for (int nodeIndex = 0; nodeIndex < selectedNodes.Count; nodeIndex++)
                {
                    int nodeID = selectedNodes[nodeIndex];
                    TrialNodeStats cachedStats = LoadTrialStatsFromCache(
                        GetGazeHitCachePath(sceneID, source, nodeID)
                    );

                    if (cachedStats != null)
                    {
                        EnsureAllZeroEntries(cachedStats);
                        allTrialNodeStats.Add(cachedStats);
                    }

                    if ((nodeIndex + 1) % 5 == 0)
                    {
                        yield return null;
                    }
                }
            }
            else
            {
                List<FrameHitRecord> records = new List<FrameHitRecord>();
                string participantID = Path.GetFileNameWithoutExtension(source.fullPath);

                IEnumerator readRoutine = ReadAndRaycastSourceFile(
                    source,
                    sceneID,
                    selectedNodes,
                    records,
                    value => participantID = value
                );

                while (readRoutine.MoveNext())
                {
                    if (cancelRequested)
                    {
                        statusMessage = "Analysis cancelled.";
                        isProcessing = false;
                        yield break;
                    }

                    yield return readRoutine.Current;
                }

                if (records.Count == 0)
                {
                    Debug.LogWarning("No selected-node records in: " + source.fullPath);
                }
                else
                {
                    ApplyDeltaTimes(records);

                    Dictionary<int, TrialNodeStats> statsByNode =
                        BuildTrialNodeStatsFromRecords(
                            source,
                            sceneID,
                            participantID,
                            selectedNodes,
                            records
                        );

                    foreach (KeyValuePair<int, TrialNodeStats> pair in statsByNode)
                    {
                        EnsureAllZeroEntries(pair.Value);
                        allTrialNodeStats.Add(pair.Value);
                    }

                    if (saveFrameLevelGazeHit)
                    {
                        SaveFrameHitFiles(sceneID, source, selectedNodes, records);
                    }
                }
            }

            progress01 = (fileIndex + 1f) / totalFiles;
            yield return null;
        }

        if (allTrialNodeStats.Count == 0)
        {
            statusMessage = "No valid trial-node statistics were generated.";
            isProcessing = false;
            yield break;
        }

        string outputFolder = GetSummaryOutputFolder(ResolveSceneID());
        Directory.CreateDirectory(outputFolder);

        string suffix = BuildOutputSuffix();

        string trialSummaryPath = Path.Combine(
            outputFolder,
            "AOI_TrialSummary_" + suffix + ".csv"
        );

        string nodeSummaryPath = Path.Combine(
            outputFolder,
            "AOI_NodeSummary_" + suffix + ".csv"
        );

        WriteTrialSummary(trialSummaryPath);
        BuildNodeSummaryRows();
        WriteNodeSummary(nodeSummaryPath);

        progress01 = 1f;
        statusMessage =
            "AOI analysis completed. Version: " + ToolVersion + "\n" +
            "Trial-node records: " + allTrialNodeStats.Count + "\n" +
            "Trial summary:\n" + trialSummaryPath + "\n" +
            "Node summary:\n" + nodeSummaryPath;

        isProcessing = false;
    }

    private IEnumerator ReadAndRaycastSourceFile(
        SourceFileSelection source,
        string fallbackSceneID,
        List<int> selectedNodes,
        List<FrameHitRecord> records,
        Action<string> participantIdCallback
    )
    {
        HashSet<int> selectedNodeSet = new HashSet<int>(selectedNodes);

        using (StreamReader reader = new StreamReader(source.fullPath, Encoding.UTF8))
        {
            string headerLine = reader.ReadLine();

            if (headerLine == null)
            {
                yield break;
            }

            List<string> header = ParseCsvLine(headerLine);

            int idxTimestamp = FindColumnIndex(header, timestampColumn);
            int idxScene = FindColumnIndex(header, sceneIdColumn);
            int idxParticipant = FindColumnIndex(header, participantIdColumn);
            int idxNode = FindColumnIndex(header, nodeIdColumn);

            int idxOriginX = FindColumnIndex(header, gazeOriginXColumn);
            int idxOriginY = FindColumnIndex(header, gazeOriginYColumn);
            int idxOriginZ = FindColumnIndex(header, gazeOriginZColumn);

            int idxDirectionX = FindColumnIndex(header, gazeDirectionXColumn);
            int idxDirectionY = FindColumnIndex(header, gazeDirectionYColumn);
            int idxDirectionZ = FindColumnIndex(header, gazeDirectionZColumn);

            int idxValidity = StringIsNullOrWhiteSpace(gazeValidColumn)
                ? -1
                : FindColumnIndex(header, gazeValidColumn);

            if (idxTimestamp < 0 ||
                idxNode < 0 ||
                idxOriginX < 0 ||
                idxOriginY < 0 ||
                idxOriginZ < 0 ||
                idxDirectionX < 0 ||
                idxDirectionY < 0 ||
                idxDirectionZ < 0)
            {
                Debug.LogError(
                    "Required columns are missing in: " + source.fullPath +
                    "\nRequired: Timestamp, NodeID, Gaze Origin XYZ, Gaze Direction XYZ."
                );
                yield break;
            }

            string line;
            int sourceRowIndex = 0;
            int processedLineCount = 0;
            string participantID = Path.GetFileNameWithoutExtension(source.fullPath);

            while ((line = reader.ReadLine()) != null)
            {
                sourceRowIndex++;
                processedLineCount++;

                List<string> row = ParseCsvLine(line);

                if (idxParticipant >= 0)
                {
                    string value = SafeGet(row, idxParticipant).Trim();
                    if (!StringIsNullOrWhiteSpace(value))
                    {
                        participantID = value;
                    }
                }

                int nodeID = ReadInt(row, idxNode, 0);

                if (!selectedNodeSet.Contains(nodeID))
                {
                    if (processedLineCount % yieldEveryNRows == 0)
                    {
                        yield return null;
                    }
                    continue;
                }

                double rawTimestamp = ReadDouble(row, idxTimestamp, double.NaN);

                Vector3 origin = new Vector3(
                    ReadFloat(row, idxOriginX, float.NaN),
                    ReadFloat(row, idxOriginY, float.NaN),
                    ReadFloat(row, idxOriginZ, float.NaN)
                );

                Vector3 direction = new Vector3(
                    ReadFloat(row, idxDirectionX, float.NaN),
                    ReadFloat(row, idxDirectionY, float.NaN),
                    ReadFloat(row, idxDirectionZ, float.NaN)
                );

                bool gazeValid = DetermineGazeValidity(
                    row,
                    idxValidity,
                    origin,
                    direction
                );

                string rowSceneID = idxScene >= 0
                    ? NormalizeID(SafeGet(row, idxScene))
                    : "";

                FrameHitRecord record = new FrameHitRecord();
                record.taskOrder = source.taskOrder;
                record.participantID = participantID;
                record.sourceFile = Path.GetFileName(source.fullPath);
                record.sourceRowIndex = sourceRowIndex;
                record.frameIndex = sourceRowIndex;
                record.rawTimestamp = rawTimestamp;
                record.sceneID = StringIsNullOrWhiteSpace(rowSceneID)
                    ? fallbackSceneID
                    : rowSceneID;
                record.nodeID = nodeID;
                record.gazeValid = gazeValid;
                record.origin = origin;
                record.direction = direction;
                record.deltaTime = 0f;

                if (!gazeValid)
                {
                    record.hitStatus = "InvalidGaze";
                }
                else
                {
                    RaycastHit hit;

                    bool hitSomething = Physics.Raycast(
                        origin,
                        direction.normalized,
                        out hit,
                        maxRayDistance,
                        aoiLayerMask,
                        QueryTriggerInteraction.Ignore
                    );

                    if (hitSomething)
                    {
                        string category;
                        string objectName;

                        if (TryResolveAOIHierarchy(
                            hit.collider.transform,
                            out category,
                            out objectName
                        ))
                        {
                            record.hitStatus = "ValidHit";
                            record.category = category;
                            record.objectName = objectName;
                            record.hitPoint = hit.point;
                            record.hitDistance = hit.distance;
                        }
                        else
                        {
                            record.hitStatus = "NoHit";
                        }
                    }
                    else
                    {
                        record.hitStatus = "NoHit";
                    }
                }

                records.Add(record);

                if (processedLineCount % yieldEveryNRows == 0)
                {
                    yield return null;
                }
            }

            participantIdCallback(participantID);
        }
    }

    private bool DetermineGazeValidity(
        List<string> row,
        int idxValidity,
        Vector3 origin,
        Vector3 direction
    )
    {
        if (idxValidity >= 0)
        {
            string raw = SafeGet(row, idxValidity).Trim();

            if (!StringIsNullOrWhiteSpace(raw))
            {
                string[] validTokens = gazeValidValues.Split(',');

                for (int i = 0; i < validTokens.Length; i++)
                {
                    if (string.Equals(
                        raw,
                        validTokens[i].Trim(),
                        StringComparison.OrdinalIgnoreCase
                    ))
                    {
                        return IsFiniteVector(origin) &&
                               IsFiniteVector(direction) &&
                               direction.sqrMagnitude > 0.000001f;
                    }
                }

                return false;
            }
        }

        return IsFiniteVector(origin) &&
               IsFiniteVector(direction) &&
               direction.sqrMagnitude > 0.000001f;
    }

    private bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void ApplyDeltaTimes(List<FrameHitRecord> records)
    {
        double secondsScale = DetermineTimestampScale(records);

        for (int i = 0; i < records.Count; i++)
        {
            FrameHitRecord current = records[i];
            current.deltaTime = 0f;

            if (i >= records.Count - 1)
            {
                continue;
            }

            FrameHitRecord next = records[i + 1];

            bool consecutiveSourceRows =
                next.sourceRowIndex == current.sourceRowIndex + 1;

            bool sameNode = next.nodeID == current.nodeID;

            if (!consecutiveSourceRows || !sameNode)
            {
                continue;
            }

            if (double.IsNaN(current.rawTimestamp) ||
                double.IsNaN(next.rawTimestamp))
            {
                continue;
            }

            double delta = (next.rawTimestamp - current.rawTimestamp) * secondsScale;

            if (delta <= 0.0 || double.IsNaN(delta) || double.IsInfinity(delta))
            {
                continue;
            }

            if (maxAcceptedDeltaSeconds > 0f &&
                delta > maxAcceptedDeltaSeconds)
            {
                continue;
            }

            current.deltaTime = (float)delta;
        }
    }

    private double DetermineTimestampScale(List<FrameHitRecord> records)
    {
        if (timestampUnit == TimestampUnit.Seconds)
        {
            return 1.0;
        }

        if (timestampUnit == TimestampUnit.Milliseconds)
        {
            return 0.001;
        }

        if (timestampUnit == TimestampUnit.Microseconds)
        {
            return 0.000001;
        }

        List<double> positiveDeltas = new List<double>();

        for (int i = 0; i < records.Count - 1; i++)
        {
            FrameHitRecord a = records[i];
            FrameHitRecord b = records[i + 1];

            if (b.sourceRowIndex != a.sourceRowIndex + 1 ||
                b.nodeID != a.nodeID ||
                double.IsNaN(a.rawTimestamp) ||
                double.IsNaN(b.rawTimestamp))
            {
                continue;
            }

            double delta = b.rawTimestamp - a.rawTimestamp;
            if (delta > 0.0 && !double.IsInfinity(delta))
            {
                positiveDeltas.Add(delta);
            }

            if (positiveDeltas.Count >= 500)
            {
                break;
            }
        }

        if (positiveDeltas.Count == 0)
        {
            return 1.0;
        }

        positiveDeltas.Sort();
        double medianDelta = CalculateMedianDouble(positiveDeltas);

        if (medianDelta > 1000.0)
        {
            return 0.000001;
        }

        if (medianDelta > 1.0)
        {
            return 0.001;
        }

        return 1.0;
    }

    private Dictionary<int, TrialNodeStats> BuildTrialNodeStatsFromRecords(
        SourceFileSelection source,
        string sceneID,
        string participantID,
        List<int> selectedNodes,
        List<FrameHitRecord> records
    )
    {
        Dictionary<int, TrialNodeStats> result =
            new Dictionary<int, TrialNodeStats>();

        for (int i = 0; i < selectedNodes.Count; i++)
        {
            int nodeID = selectedNodes[i];

            TrialNodeStats stats = new TrialNodeStats();
            stats.sceneID = sceneID;
            stats.taskOrder = source.taskOrder;
            stats.participantID = participantID;
            stats.sourceFile = Path.GetFileName(source.fullPath);
            stats.nodeID = nodeID;

            InitializeZeroEntries(stats);
            result[nodeID] = stats;
        }

        for (int i = 0; i < records.Count; i++)
        {
            FrameHitRecord record = records[i];
            TrialNodeStats stats;

            if (!result.TryGetValue(record.nodeID, out stats))
            {
                continue;
            }

            stats.totalSampleCount++;

            float dt = record.deltaTime;

            if (!record.gazeValid)
            {
                stats.invalidGazeDuration += dt;
                continue;
            }

            stats.validSampleCount++;
            stats.validGazeDuration += dt;

            if (record.hitStatus == "ValidHit")
            {
                stats.validHitSampleCount++;

                string objectKey = BuildObjectKey(
                    record.category,
                    record.objectName
                );

                AddToDictionary(stats.objectDurations, objectKey, dt);
                AddToDictionary(stats.objectHitCounts, objectKey, 1);
            }
            else
            {
                stats.noHitDuration += dt;
                stats.noHitSampleCount++;
            }
        }

        return result;
    }

    private void InitializeZeroEntries(TrialNodeStats stats)
    {
        for (int i = 0; i < aoiObjectDefinitions.Count; i++)
        {
            AOIObjectDefinition definition = aoiObjectDefinitions[i];
            string key = BuildObjectKey(
                definition.category,
                definition.objectName
            );

            if (!stats.objectDurations.ContainsKey(key))
            {
                stats.objectDurations.Add(key, 0f);
            }

            if (!stats.objectHitCounts.ContainsKey(key))
            {
                stats.objectHitCounts.Add(key, 0);
            }
        }
    }

    private void EnsureAllZeroEntries(TrialNodeStats stats)
    {
        InitializeZeroEntries(stats);
    }

    private void SaveFrameHitFiles(
        string sceneID,
        SourceFileSelection source,
        List<int> selectedNodes,
        List<FrameHitRecord> records
    )
    {
        Dictionary<int, List<FrameHitRecord>> byNode =
            new Dictionary<int, List<FrameHitRecord>>();

        for (int i = 0; i < selectedNodes.Count; i++)
        {
            byNode[selectedNodes[i]] = new List<FrameHitRecord>();
        }

        for (int i = 0; i < records.Count; i++)
        {
            FrameHitRecord record = records[i];
            List<FrameHitRecord> nodeRecords;

            if (byNode.TryGetValue(record.nodeID, out nodeRecords))
            {
                nodeRecords.Add(record);
            }
        }

        foreach (KeyValuePair<int, List<FrameHitRecord>> pair in byNode)
        {
            int nodeID = pair.Key;
            string path = GetGazeHitCachePath(sceneID, source, nodeID);

            if (File.Exists(path) && !overwriteExistingGazeHit)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (StreamWriter writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine(
                    "TaskOrder,ParticipantID,SourceFile,Timestamp,FrameIndex," +
                    "SceneID,NodeID,GazeValid,HitStatus,Category,ObjectName," +
                    "HitPointX,HitPointY,HitPointZ,HitDistance,DeltaTime," +
                    "GazeOriginX,GazeOriginY,GazeOriginZ," +
                    "GazeDirectionX,GazeDirectionY,GazeDirectionZ"
                );

                List<FrameHitRecord> nodeRecords = pair.Value;

                for (int i = 0; i < nodeRecords.Count; i++)
                {
                    FrameHitRecord r = nodeRecords[i];

                    writer.WriteLine(string.Join(",", new string[]
                    {
                        r.taskOrder.ToString(CultureInfo.InvariantCulture),
                        EscapeCsv(r.participantID),
                        EscapeCsv(r.sourceFile),
                        r.rawTimestamp.ToString("G17", CultureInfo.InvariantCulture),
                        r.frameIndex.ToString(CultureInfo.InvariantCulture),
                        EscapeCsv(r.sceneID),
                        r.nodeID.ToString(CultureInfo.InvariantCulture),
                        r.gazeValid ? "1" : "0",
                        EscapeCsv(r.hitStatus),
                        EscapeCsv(r.category),
                        EscapeCsv(r.objectName),
                        FloatToCsv(r.hitPoint.x),
                        FloatToCsv(r.hitPoint.y),
                        FloatToCsv(r.hitPoint.z),
                        FloatToCsv(r.hitDistance),
                        FloatToCsv(r.deltaTime),
                        FloatToCsv(r.origin.x),
                        FloatToCsv(r.origin.y),
                        FloatToCsv(r.origin.z),
                        FloatToCsv(r.direction.x),
                        FloatToCsv(r.direction.y),
                        FloatToCsv(r.direction.z)
                    }));
                }
            }
        }
    }

    private TrialNodeStats LoadTrialStatsFromCache(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
        {
            string headerLine = reader.ReadLine();
            if (headerLine == null)
            {
                return null;
            }

            List<string> header = ParseCsvLine(headerLine);

            int idxTask = FindColumnIndex(header, "TaskOrder");
            int idxParticipant = FindColumnIndex(header, "ParticipantID");
            int idxSource = FindColumnIndex(header, "SourceFile");
            int idxScene = FindColumnIndex(header, "SceneID");
            int idxNode = FindColumnIndex(header, "NodeID");
            int idxValid = FindColumnIndex(header, "GazeValid");
            int idxStatus = FindColumnIndex(header, "HitStatus");
            int idxCategory = FindColumnIndex(header, "Category");
            int idxObject = FindColumnIndex(header, "ObjectName");
            int idxDelta = FindColumnIndex(header, "DeltaTime");

            TrialNodeStats stats = new TrialNodeStats();
            bool initialized = false;

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                List<string> row = ParseCsvLine(line);

                if (!initialized)
                {
                    stats.taskOrder = ReadInt(row, idxTask, 0);
                    stats.participantID = SafeGet(row, idxParticipant);
                    stats.sourceFile = SafeGet(row, idxSource);
                    stats.sceneID = SafeGet(row, idxScene);
                    stats.nodeID = ReadInt(row, idxNode, 0);
                    initialized = true;
                }

                bool gazeValid = ReadInt(row, idxValid, 0) == 1;
                string hitStatus = SafeGet(row, idxStatus);
                string category = SafeGet(row, idxCategory);
                string objectName = SafeGet(row, idxObject);
                float delta = ReadFloat(row, idxDelta, 0f);

                stats.totalSampleCount++;

                if (!gazeValid)
                {
                    stats.invalidGazeDuration += delta;
                    continue;
                }

                stats.validSampleCount++;
                stats.validGazeDuration += delta;

                if (string.Equals(
                    hitStatus,
                    "ValidHit",
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    stats.validHitSampleCount++;

                    string objectKey = BuildObjectKey(category, objectName);
                    AddToDictionary(stats.objectDurations, objectKey, delta);
                    AddToDictionary(stats.objectHitCounts, objectKey, 1);
                }
                else
                {
                    stats.noHitDuration += delta;
                    stats.noHitSampleCount++;
                }
            }

            return initialized ? stats : null;
        }
    }

    private void WriteTrialSummary(string path)
    {
        using (StreamWriter writer = new StreamWriter(path, false, Encoding.UTF8))
        {
            writer.WriteLine(
                "SceneID,TaskOrder,ParticipantID,SourceFile,NodeID," +
                "Category,ObjectName,DwellDuration,DwellProportion," +
                "HitSampleCount,TotalSampleCount,ValidSampleCount,ValidHitSampleCount," +
                "ValidGazeDuration,NoHitDuration,NoHitProportion,InvalidGazeDuration"
            );

            for (int i = 0; i < allTrialNodeStats.Count; i++)
            {
                TrialNodeStats stats = allTrialNodeStats[i];

                for (int o = 0; o < aoiObjectDefinitions.Count; o++)
                {
                    AOIObjectDefinition definition = aoiObjectDefinitions[o];
                    string key = BuildObjectKey(
                        definition.category,
                        definition.objectName
                    );

                    float duration = GetDictionaryValue(
                        stats.objectDurations,
                        key
                    );

                    int hitCount = GetDictionaryValue(
                        stats.objectHitCounts,
                        key
                    );

                    WriteTrialSummaryRow(
                        writer,
                        stats,
                        definition.category,
                        definition.objectName,
                        duration,
                        hitCount
                    );
                }
            }
        }
    }

    private void WriteTrialSummaryRow(
        StreamWriter writer,
        TrialNodeStats stats,
        string category,
        string objectName,
        float duration,
        int hitCount
    )
    {
        float proportion = stats.validGazeDuration > 0f
            ? duration / stats.validGazeDuration
            : 0f;

        float noHitProportion = stats.validGazeDuration > 0f
            ? stats.noHitDuration / stats.validGazeDuration
            : 0f;

        writer.WriteLine(string.Join(",", new string[]
        {
            EscapeCsv(stats.sceneID),
            stats.taskOrder.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(stats.participantID),
            EscapeCsv(stats.sourceFile),
            stats.nodeID.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(category),
            EscapeCsv(objectName),
            FloatToCsv(duration),
            FloatToCsv(proportion),
            hitCount.ToString(CultureInfo.InvariantCulture),
            stats.totalSampleCount.ToString(CultureInfo.InvariantCulture),
            stats.validSampleCount.ToString(CultureInfo.InvariantCulture),
            stats.validHitSampleCount.ToString(CultureInfo.InvariantCulture),
            FloatToCsv(stats.validGazeDuration),
            FloatToCsv(stats.noHitDuration),
            FloatToCsv(noHitProportion),
            FloatToCsv(stats.invalidGazeDuration)
        }));
    }

    private void BuildNodeSummaryRows()
    {
        currentNodeSummaryRows.Clear();

        Dictionary<string, List<TrialMetricValue>> groups =
            new Dictionary<string, List<TrialMetricValue>>();

        for (int i = 0; i < allTrialNodeStats.Count; i++)
        {
            TrialNodeStats stats = allTrialNodeStats[i];

            for (int o = 0; o < aoiObjectDefinitions.Count; o++)
            {
                AOIObjectDefinition definition = aoiObjectDefinitions[o];
                string objectKey = BuildObjectKey(
                    definition.category,
                    definition.objectName
                );

                float duration = GetDictionaryValue(
                    stats.objectDurations,
                    objectKey
                );

                int hitCount = GetDictionaryValue(
                    stats.objectHitCounts,
                    objectKey
                );

                AddNodeSummaryMetric(
                    groups,
                    stats,
                    definition.category,
                    definition.objectName,
                    duration,
                    hitCount
                );
            }
        }

        foreach (KeyValuePair<string, List<TrialMetricValue>> pair in groups)
        {
            List<TrialMetricValue> values = pair.Value;

            if (values.Count == 0)
            {
                continue;
            }

            TrialMetricValue first = values[0];
            List<float> proportions = new List<float>();
            List<float> durations = new List<float>();

            int validTrialCount = 0;
            int hitTrialCount = 0;

            for (int i = 0; i < values.Count; i++)
            {
                TrialMetricValue value = values[i];

                if (!value.hasValidGaze)
                {
                    continue;
                }

                validTrialCount++;
                proportions.Add(value.proportion);
                durations.Add(value.duration);

                if (value.hit)
                {
                    hitTrialCount++;
                }
            }

            if (validTrialCount == 0)
            {
                continue;
            }

            proportions.Sort();
            durations.Sort();

            NodeSummaryRow row = new NodeSummaryRow();
            row.sceneID = first.sceneID;
            row.nodeID = first.nodeID;
            row.category = first.category;
            row.objectName = first.objectName;
            row.medianProportion = CalculateMedianFloat(proportions);
            row.meanProportion = CalculateMeanFloat(proportions);
            row.q1Proportion = CalculateQuantile(proportions, 0.25f);
            row.q3Proportion = CalculateQuantile(proportions, 0.75f);
            row.medianDuration = CalculateMedianFloat(durations);
            row.hitRate = hitTrialCount / (float)validTrialCount;
            row.trialCount = values.Count;
            row.validTrialCount = validTrialCount;

            currentNodeSummaryRows.Add(row);
        }

        currentNodeSummaryRows.Sort(CompareNodeSummaryRows);
    }

    private void AddNodeSummaryMetric(
        Dictionary<string, List<TrialMetricValue>> groups,
        TrialNodeStats stats,
        string category,
        string objectName,
        float duration,
        int hitCount
    )
    {
        string key =
            stats.sceneID + "|" +
            stats.nodeID + "|" +
            category + "|" +
            objectName;

        List<TrialMetricValue> list;

        if (!groups.TryGetValue(key, out list))
        {
            list = new List<TrialMetricValue>();
            groups.Add(key, list);
        }

        TrialMetricValue metric = new TrialMetricValue();
        metric.sceneID = stats.sceneID;
        metric.nodeID = stats.nodeID;
        metric.category = category;
        metric.objectName = objectName;
        metric.hasValidGaze = stats.validGazeDuration > 0f;
        metric.duration = duration;
        metric.proportion = stats.validGazeDuration > 0f
            ? duration / stats.validGazeDuration
            : 0f;
        metric.hit = hitCount > 0;

        list.Add(metric);
    }

    private int CompareNodeSummaryRows(NodeSummaryRow a, NodeSummaryRow b)
    {
        int sceneCompare = string.Compare(
            a.sceneID,
            b.sceneID,
            StringComparison.OrdinalIgnoreCase
        );

        if (sceneCompare != 0)
        {
            return sceneCompare;
        }

        int nodeCompare = a.nodeID.CompareTo(b.nodeID);
        if (nodeCompare != 0)
        {
            return nodeCompare;
        }


        int categoryCompare = string.Compare(
            a.category,
            b.category,
            StringComparison.OrdinalIgnoreCase
        );

        if (categoryCompare != 0)
        {
            return categoryCompare;
        }

        return string.Compare(
            a.objectName,
            b.objectName,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private void WriteNodeSummary(string path)
    {
        string selectedTaskOrders = JoinSelectedTaskOrders();
        string selectedNodeIDs = JoinSelectedNodeIDs();

        using (StreamWriter writer = new StreamWriter(path, false, Encoding.UTF8))
        {
            writer.WriteLine(
                "SceneID,SelectedTaskOrders,SelectedNodeIDs,NodeID," +
                "Category,ObjectName,MedianProportion,MeanProportion,Q1Proportion," +
                "Q3Proportion,MedianDuration,HitRate,TrialCount,ValidTrialCount"
            );

            for (int i = 0; i < currentNodeSummaryRows.Count; i++)
            {
                NodeSummaryRow row = currentNodeSummaryRows[i];

                writer.WriteLine(string.Join(",", new string[]
                {
                    EscapeCsv(row.sceneID),
                    EscapeCsv(selectedTaskOrders),
                    EscapeCsv(selectedNodeIDs),
                    row.nodeID.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(row.category),
                    EscapeCsv(row.objectName),
                    FloatToCsv(row.medianProportion),
                    FloatToCsv(row.meanProportion),
                    FloatToCsv(row.q1Proportion),
                    FloatToCsv(row.q3Proportion),
                    FloatToCsv(row.medianDuration),
                    FloatToCsv(row.hitRate),
                    row.trialCount.ToString(CultureInfo.InvariantCulture),
                    row.validTrialCount.ToString(CultureInfo.InvariantCulture)
                }));
            }
        }
    }

    private bool ValidateAOIModel()
    {
        ResolveAOIRoot();
        EnsureAOILayerMask();

        if (aoiRoot == null)
        {
            statusMessage = "Validation failed: AOI root not found.";
            Debug.LogError(statusMessage);
            return false;
        }

        if (aoiRoot.childCount == 0)
        {
            statusMessage = "Validation failed: AOI root contains no category objects.";
            Debug.LogError(statusMessage);
            return false;
        }

        Collider[] colliders = aoiRoot.GetComponentsInChildren<Collider>(true);

        if (colliders.Length == 0)
        {
            statusMessage = "Validation failed: no Collider found under AOI root.";
            Debug.LogError(statusMessage);
            return false;
        }

        int layerMismatchCount = 0;
        int hierarchyFailureCount = 0;
        int validColliderCount = 0;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];

            bool layerIncluded =
                (aoiLayerMask.value & (1 << col.gameObject.layer)) != 0;

            if (!layerIncluded)
            {
                layerMismatchCount++;
            }

            string category;
            string objectName;

            if (!TryResolveAOIHierarchy(
                col.transform,
                out category,
                out objectName
            ))
            {
                hierarchyFailureCount++;
            }
            else
            {
                validColliderCount++;
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("AOI validation completed.");
        sb.AppendLine("Categories: " + aoiRoot.childCount);
        sb.AppendLine("Colliders: " + colliders.Length);
        sb.AppendLine("Valid hierarchy colliders: " + validColliderCount);
        sb.AppendLine("Layer mismatches: " + layerMismatchCount);
        sb.AppendLine("Hierarchy failures: " + hierarchyFailureCount);

        statusMessage = sb.ToString().TrimEnd();

        if (layerMismatchCount > 0 || hierarchyFailureCount > 0)
        {
            Debug.LogWarning(statusMessage);
            return false;
        }

        Debug.Log(statusMessage);
        return true;
    }

    private void BuildAOIDefinitions()
    {
        aoiCategories.Clear();
        aoiObjectDefinitions.Clear();

        HashSet<string> categorySet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        HashSet<string> objectSet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Collider[] colliders = aoiRoot.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            string category;
            string objectName;

            if (!TryResolveAOIHierarchy(
                colliders[i].transform,
                out category,
                out objectName
            ))
            {
                continue;
            }

            if (categorySet.Add(category))
            {
                aoiCategories.Add(category);
            }

            string objectKey = BuildObjectKey(category, objectName);

            if (objectSet.Add(objectKey))
            {
                AOIObjectDefinition definition = new AOIObjectDefinition();
                definition.category = category;
                definition.objectName = objectName;
                aoiObjectDefinitions.Add(definition);
            }
        }

        // If a category has named child AOIs, remove a possible
        // Category/Category definition created by a Collider on the category parent.
        HashSet<string> categoriesWithNamedChildren =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < aoiObjectDefinitions.Count; i++)
        {
            AOIObjectDefinition definition = aoiObjectDefinitions[i];

            if (!string.Equals(
                definition.category,
                definition.objectName,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                categoriesWithNamedChildren.Add(definition.category);
            }
        }

        aoiObjectDefinitions.RemoveAll(
            definition =>
                categoriesWithNamedChildren.Contains(definition.category) &&
                string.Equals(
                    definition.category,
                    definition.objectName,
                    StringComparison.OrdinalIgnoreCase
                )
        );

        aoiCategories.Sort(StringComparer.OrdinalIgnoreCase);
        aoiObjectDefinitions.Sort(CompareAOIObjectDefinitions);
    }

    private int CompareAOIObjectDefinitions(
        AOIObjectDefinition a,
        AOIObjectDefinition b
    )
    {
        int categoryCompare = string.Compare(
            a.category,
            b.category,
            StringComparison.OrdinalIgnoreCase
        );

        if (categoryCompare != 0)
        {
            return categoryCompare;
        }

        return string.Compare(
            a.objectName,
            b.objectName,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private bool TryResolveAOIHierarchy(
        Transform hitTransform,
        out string category,
        out string objectName
    )
    {
        category = "";
        objectName = "";

        if (aoiRoot == null || hitTransform == null)
        {
            return false;
        }

        Transform cursor = hitTransform;
        Transform childBelowCategory = null;

        while (cursor != null && cursor != aoiRoot)
        {
            if (cursor.parent == aoiRoot)
            {
                category = cursor.name;
                objectName = childBelowCategory != null
                    ? childBelowCategory.name
                    : category;

                return true;
            }

            childBelowCategory = cursor;
            cursor = cursor.parent;
        }

        return false;
    }

    private List<SourceFileSelection> ResolveSelectedSourceFiles()
    {
        List<SourceFileSelection> result =
            new List<SourceFileSelection>();

        HashSet<string> addedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        StringBuilder warnings = new StringBuilder();

        for (int i = 0; i < taskSelections.Count; i++)
        {
            TaskDataSelection selection = taskSelections[i];
            int taskOrder = Mathf.Max(1, selection.taskOrder);

            string folder = Path.Combine(
                Application.streamingAssetsPath,
                dataRootFolderName
            );

            folder = Path.Combine(folder, "Task" + taskOrder);
            folder = Path.Combine(folder, dataSubFolderName);

            if (!Directory.Exists(folder))
            {
                warnings.AppendLine(
                    "Task" + taskOrder + " folder not found: " + folder
                );
                continue;
            }

            string[] files = Directory.GetFiles(
                folder,
                "*.csv",
                SearchOption.TopDirectoryOnly
            );

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            if (files.Length == 0)
            {
                warnings.AppendLine(
                    "Task" + taskOrder + " contains no CSV files."
                );
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
                selectedIndices = ParseIndexExpression(
                    selection.csvIndices,
                    files.Length
                );
            }

            for (int j = 0; j < selectedIndices.Count; j++)
            {
                int oneBasedIndex = selectedIndices[j];
                string fullPath = files[oneBasedIndex - 1];

                if (!addedPaths.Add(fullPath))
                {
                    continue;
                }

                SourceFileSelection item = new SourceFileSelection();
                item.taskOrder = taskOrder;
                item.csvIndex = oneBasedIndex;
                item.fullPath = fullPath;
                result.Add(item);
            }
        }

        if (warnings.Length > 0)
        {
            Debug.LogWarning(warnings.ToString());
        }

        return result;
    }

    private bool AreAllCachesAvailable(
        string sceneID,
        SourceFileSelection source,
        List<int> selectedNodes
    )
    {
        for (int i = 0; i < selectedNodes.Count; i++)
        {
            string path = GetGazeHitCachePath(
                sceneID,
                source,
                selectedNodes[i]
            );

            if (!File.Exists(path))
            {
                return false;
            }
        }

        return true;
    }

    private string GetGazeHitCachePath(
        string sceneID,
        SourceFileSelection source,
        int nodeID
    )
    {
        string folder = Path.Combine(
            Application.streamingAssetsPath,
            aoiOutputRootFolder
        );

        folder = Path.Combine(folder, gazeHitFolderName);
        folder = Path.Combine(folder, "Scene" + sceneID);
        folder = Path.Combine(folder, "Task" + source.taskOrder);

        string sourceBaseName =
            Path.GetFileNameWithoutExtension(source.fullPath);

        string fileName =
            SanitizeFileName(sourceBaseName) +
            "_Node" + nodeID +
            "_GazeHit.csv";

        return Path.Combine(folder, fileName);
    }

    private string GetSummaryOutputFolder(string sceneID)
    {
        string folder = Path.Combine(
            Application.streamingAssetsPath,
            aoiOutputRootFolder
        );

        folder = Path.Combine(folder, summaryFolderName);
        folder = Path.Combine(folder, "Scene" + sceneID);

        return folder;
    }

    private string BuildOutputSuffix()
    {
        string tasks = JoinSelectedTaskOrders().Replace(";", "-");
        string nodes = JoinSelectedNodeIDs().Replace(";", "-");

        string suffix =
            "Scene" + ResolveSceneID() +
            "_Tasks" + tasks +
            "_Nodes" + nodes;

        if (appendTimestampToSummaryFileName)
        {
            suffix += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        return SanitizeFileName(suffix);
    }

    private string JoinSelectedTaskOrders()
    {
        SortedSet<int> values = new SortedSet<int>();

        for (int i = 0; i < taskSelections.Count; i++)
        {
            values.Add(Mathf.Max(1, taskSelections[i].taskOrder));
        }

        return JoinIntegers(values);
    }

    private string JoinSelectedNodeIDs()
    {
        List<int> values = ParseIndexExpressionUnlimited(nodeIds);
        return JoinIntegers(values);
    }

    private string JoinIntegers(IEnumerable<int> values)
    {
        StringBuilder sb = new StringBuilder();

        foreach (int value in values)
        {
            if (sb.Length > 0)
            {
                sb.Append(";");
            }

            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private string ResolveSceneID()
    {
        if (!StringIsNullOrWhiteSpace(sceneIDOverride))
        {
            return NormalizeID(sceneIDOverride);
        }

        return ExtractDigits(SceneManager.GetActiveScene().name);
    }

    private List<int> ParseIndexExpression(
        string expression,
        int maxIndex
    )
    {
        SortedSet<int> result = new SortedSet<int>();

        if (StringIsNullOrWhiteSpace(expression) || maxIndex <= 0)
        {
            return new List<int>();
        }

        string normalized = NormalizeIndexExpression(expression);
        string[] tokens = normalized.Split(
            new char[] { ',' },
            StringSplitOptions.RemoveEmptyEntries
        );

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim();
            int dashIndex = token.IndexOf('-');

            if (dashIndex > 0 && dashIndex < token.Length - 1)
            {
                int start;
                int end;

                if (int.TryParse(
                    token.Substring(0, dashIndex).Trim(),
                    out start
                ) &&
                int.TryParse(
                    token.Substring(dashIndex + 1).Trim(),
                    out end
                ))
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

                if (int.TryParse(token, out index) &&
                    index >= 1 &&
                    index <= maxIndex)
                {
                    result.Add(index);
                }
            }
        }

        return new List<int>(result);
    }

    private List<int> ParseIndexExpressionUnlimited(string expression)
    {
        SortedSet<int> result = new SortedSet<int>();

        if (StringIsNullOrWhiteSpace(expression))
        {
            return new List<int>();
        }

        string normalized = NormalizeIndexExpression(expression);
        string[] tokens = normalized.Split(
            new char[] { ',' },
            StringSplitOptions.RemoveEmptyEntries
        );

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim();
            int dashIndex = token.IndexOf('-');

            if (dashIndex > 0 && dashIndex < token.Length - 1)
            {
                int start;
                int end;

                if (int.TryParse(
                    token.Substring(0, dashIndex).Trim(),
                    out start
                ) &&
                int.TryParse(
                    token.Substring(dashIndex + 1).Trim(),
                    out end
                ))
                {
                    if (start > end)
                    {
                        int temp = start;
                        start = end;
                        end = temp;
                    }

                    start = Mathf.Max(1, start);
                    end = Mathf.Max(1, end);

                    for (int index = start; index <= end; index++)
                    {
                        result.Add(index);
                    }
                }
            }
            else
            {
                int index;

                if (int.TryParse(token, out index) && index >= 1)
                {
                    result.Add(index);
                }
            }
        }

        return new List<int>(result);
    }

    private string NormalizeIndexExpression(string expression)
    {
        return expression
            .Replace('，', ',')
            .Replace('；', ',')
            .Replace(';', ',')
            .Replace(' ', ',');
    }

    private string BuildObjectKey(string category, string objectName)
    {
        return category + "||" + objectName;
    }

    private void AddToDictionary(
        Dictionary<string, float> dictionary,
        string key,
        float value
    )
    {
        if (StringIsNullOrWhiteSpace(key))
        {
            return;
        }

        float existing;

        if (dictionary.TryGetValue(key, out existing))
        {
            dictionary[key] = existing + value;
        }
        else
        {
            dictionary.Add(key, value);
        }
    }

    private void AddToDictionary(
        Dictionary<string, int> dictionary,
        string key,
        int value
    )
    {
        if (StringIsNullOrWhiteSpace(key))
        {
            return;
        }

        int existing;

        if (dictionary.TryGetValue(key, out existing))
        {
            dictionary[key] = existing + value;
        }
        else
        {
            dictionary.Add(key, value);
        }
    }

    private float GetDictionaryValue(
        Dictionary<string, float> dictionary,
        string key
    )
    {
        float value;
        return dictionary.TryGetValue(key, out value) ? value : 0f;
    }

    private int GetDictionaryValue(
        Dictionary<string, int> dictionary,
        string key
    )
    {
        int value;
        return dictionary.TryGetValue(key, out value) ? value : 0;
    }

    private float CalculateMeanFloat(List<float> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0f;
        }

        double sum = 0.0;

        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }

        return (float)(sum / values.Count);
    }

    private float CalculateMedianFloat(List<float> sortedValues)
    {
        if (sortedValues == null || sortedValues.Count == 0)
        {
            return 0f;
        }

        int middle = sortedValues.Count / 2;

        if (sortedValues.Count % 2 == 1)
        {
            return sortedValues[middle];
        }

        return (sortedValues[middle - 1] + sortedValues[middle]) * 0.5f;
    }

    private double CalculateMedianDouble(List<double> sortedValues)
    {
        if (sortedValues == null || sortedValues.Count == 0)
        {
            return 0.0;
        }

        int middle = sortedValues.Count / 2;

        if (sortedValues.Count % 2 == 1)
        {
            return sortedValues[middle];
        }

        return (sortedValues[middle - 1] + sortedValues[middle]) * 0.5;
    }

    private float CalculateQuantile(
        List<float> sortedValues,
        float probability
    )
    {
        if (sortedValues == null || sortedValues.Count == 0)
        {
            return 0f;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        probability = Mathf.Clamp01(probability);

        float position = (sortedValues.Count - 1) * probability;
        int lower = Mathf.FloorToInt(position);
        int upper = Mathf.CeilToInt(position);

        if (lower == upper)
        {
            return sortedValues[lower];
        }

        float t = position - lower;
        return Mathf.Lerp(sortedValues[lower], sortedValues[upper], t);
    }

    private string FloatToCsv(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return "";
        }

        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private string EscapeCsv(string value)
    {
        if (value == null)
        {
            return "";
        }

        bool requiresQuotes =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (!requiresQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private string SanitizeFileName(string value)
    {
        if (StringIsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();

        for (int i = 0; i < invalidChars.Length; i++)
        {
            value = value.Replace(invalidChars[i], '_');
        }

        return value;
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
                if (inQuotes &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
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

    private int FindColumnIndex(
        List<string> header,
        string columnName
    )
    {
        if (header == null || StringIsNullOrWhiteSpace(columnName))
        {
            return -1;
        }

        for (int i = 0; i < header.Count; i++)
        {
            string value = header[i];

            if (value == null)
            {
                continue;
            }

            value = value.Trim().Trim('\uFEFF');

            if (string.Equals(
                value,
                columnName,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return i;
            }
        }

        return -1;
    }

    private string SafeGet(List<string> row, int index)
    {
        if (row == null || index < 0 || index >= row.Count)
        {
            return "";
        }

        return row[index];
    }

    private float ReadFloat(
        List<string> row,
        int index,
        float defaultValue
    )
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

    private double ReadDouble(
        List<string> row,
        int index,
        double defaultValue
    )
    {
        double value;

        if (double.TryParse(
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

    private int ReadInt(
        List<string> row,
        int index,
        int defaultValue
    )
    {
        int value;

        if (int.TryParse(
            SafeGet(row, index),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value
        ))
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
            return value.ToString(CultureInfo.InvariantCulture);
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

    private class SourceFileSelection
    {
        public int taskOrder;
        public int csvIndex;
        public string fullPath;
    }

    private class AOIObjectDefinition
    {
        public string category;
        public string objectName;
    }

    private class FrameHitRecord
    {
        public int taskOrder;
        public string participantID;
        public string sourceFile;
        public int sourceRowIndex;
        public int frameIndex;
        public double rawTimestamp;
        public string sceneID;
        public int nodeID;
        public bool gazeValid;
        public string hitStatus = "";
        public string category = "";
        public string objectName = "";
        public Vector3 hitPoint = Vector3.zero;
        public float hitDistance = 0f;
        public float deltaTime = 0f;
        public Vector3 origin;
        public Vector3 direction;
    }

    private class TrialNodeStats
    {
        public string sceneID = "";
        public int taskOrder = 0;
        public string participantID = "";
        public string sourceFile = "";
        public int nodeID = 0;

        public int totalSampleCount = 0;
        public int validSampleCount = 0;
        public int validHitSampleCount = 0;
        public int noHitSampleCount = 0;

        public float validGazeDuration = 0f;
        public float noHitDuration = 0f;
        public float invalidGazeDuration = 0f;


        public readonly Dictionary<string, float> objectDurations =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public readonly Dictionary<string, int> objectHitCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private class TrialMetricValue
    {
        public string sceneID;
        public int nodeID;
        public string category;
        public string objectName;
        public bool hasValidGaze;
        public float duration;
        public float proportion;
        public bool hit;
    }

    private class NodeSummaryRow
    {
        public string sceneID;
        public int nodeID;
        public string category;
        public string objectName;
        public float medianProportion;
        public float meanProportion;
        public float q1Proportion;
        public float q3Proportion;
        public float medianDuration;
        public float hitRate;
        public int trialCount;
        public int validTrialCount;
    }
}
