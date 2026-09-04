using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

public class BatchGazeHitCalculator : EditorWindow
{
    // =========================
    // Path settings
    // =========================

    private string taskSceneMapPath =
        @"D:\UnityProject\SceneMerged_260708\Assets\StreamingAssets\ReadData\TaskList_151_Attribute.xlsx";

    private string inputRoot =
        @"D:\UnityProject\SceneMerged_260708\Assets\StreamingAssets\Reorder";

    private string outputRoot =
        @"D:\UnityProject\SceneMerged_260708\Assets\StreamingAssets\Reorder_GazeHit";

    private string scenePathTemplate = "Assets/04_Scenes/Scene{0}.unity";

    private string gazeHitLayerName = "GazeHit";

    // Column and tag settings for node-specific signage colliders.
    // Example hierarchy:
    // Signage / HS-02 / Node11
    // When current CSV row is Node11, only Signage colliders whose node variant is Node11 will be accepted.
    private string nodeIDColumnName = "NodeID";
    private string signageTagName = "Signage";
    private bool filterSignageByCurrentNode = true;

    // If enabled, the script activates only the signage child objects matching the current row NodeID.
    // Example: current NodeID = Node11 -> activate Signage/*/Node11 and deactivate Signage/*/Node1, Node2, etc.
    private string signageRootName = "Signage";
    private bool activateSignageVariantByCurrentNode = true;

    // =========================
    // Calculation settings
    // =========================

    private float maxRayDistance = 50f;

    private bool processAllScenes = true;

    // Supported format: 1,3,5 or 1-5,8,10
    private string selectedSceneIDs = "1-23";

    // true = empty values for no-hit / invalid gaze
    // false = write 0 for no-hit / invalid gaze
    private bool emptyHitValuesWhenNoHit = true;

    // =========================
    // Menu
    // =========================

    [MenuItem("Tools/Gaze/Batch Calculate Gaze Hit")]
    public static void ShowWindow()
    {
        GetWindow<BatchGazeHitCalculator>("Batch Gaze Hit");
    }

    private void OnGUI()
    {
        GUILayout.Label("Batch Gaze Hit Calculator", EditorStyles.boldLabel);

        GUILayout.Space(8);
        GUILayout.Label("Path Settings", EditorStyles.boldLabel);

        taskSceneMapPath = EditorGUILayout.TextField("Task-Scene Map", taskSceneMapPath);
        inputRoot = EditorGUILayout.TextField("Input Root", inputRoot);
        outputRoot = EditorGUILayout.TextField("Output Root", outputRoot);
        scenePathTemplate = EditorGUILayout.TextField("Scene Path Template", scenePathTemplate);
        gazeHitLayerName = EditorGUILayout.TextField("GazeHit Layer Name", gazeHitLayerName);

        GUILayout.Space(12);
        GUILayout.Label("Calculation Scope", EditorStyles.boldLabel);

        processAllScenes = EditorGUILayout.Toggle("Process All Scenes", processAllScenes);

        if (!processAllScenes)
        {
            selectedSceneIDs = EditorGUILayout.TextField("Selected Scene IDs", selectedSceneIDs);

            EditorGUILayout.HelpBox(
                "Input SceneID, for example: 1,3,5 or 1-5,8,10. This is SceneID, not TaskOrder.",
                MessageType.Info
            );
        }

        GUILayout.Space(12);
        GUILayout.Label("Node-specific Signage Settings", EditorStyles.boldLabel);

        filterSignageByCurrentNode = EditorGUILayout.Toggle("Filter Signage By NodeID", filterSignageByCurrentNode);
        activateSignageVariantByCurrentNode = EditorGUILayout.Toggle("Activate Signage Variant By NodeID", activateSignageVariantByCurrentNode);
        signageRootName = EditorGUILayout.TextField("Signage Root Name", signageRootName);
        nodeIDColumnName = EditorGUILayout.TextField("NodeID Column Name", nodeIDColumnName);
        signageTagName = EditorGUILayout.TextField("Signage Tag Name", signageTagName);

        EditorGUILayout.HelpBox(
            "Recommended ON: for each CSV row, the script reads NodeID and activates only Signage/*/that Node variant. During raycast, NodeID filtering is applied only to Signage hits; non-signage hits such as walls and floors are recorded normally.",
            MessageType.Info
        );

        GUILayout.Space(12);
        GUILayout.Label("Raycast Settings", EditorStyles.boldLabel);

        maxRayDistance = EditorGUILayout.FloatField("Max Ray Distance", maxRayDistance);

        if (maxRayDistance <= 0)
        {
            EditorGUILayout.HelpBox("Max Ray Distance must be greater than 0.", MessageType.Warning);
        }

        emptyHitValuesWhenNoHit = EditorGUILayout.Toggle("Empty Hit Values When No Hit", emptyHitValuesWhenNoHit);

        GUILayout.Space(16);

        if (GUILayout.Button("Start Batch Calculation"))
        {
            RunBatchCalculation();
        }
    }

    // =========================
    // Main process
    // =========================

    private void RunBatchCalculation()
    {
        try
        {
            Debug.Log("========== Batch Gaze Hit Calculation Started ==========");

            if (!File.Exists(taskSceneMapPath))
            {
                Debug.LogError("Task-scene map file does not exist: " + taskSceneMapPath);
                return;
            }

            if (!Directory.Exists(inputRoot))
            {
                Debug.LogError("Input root does not exist: " + inputRoot);
                return;
            }

            if (!Directory.Exists(outputRoot))
            {
                Directory.CreateDirectory(outputRoot);
            }

            if (maxRayDistance <= 0)
            {
                Debug.LogError("Max Ray Distance must be greater than 0.");
                return;
            }

            int gazeHitLayer = LayerMask.NameToLayer(gazeHitLayerName);

            if (gazeHitLayer < 0)
            {
                Debug.LogError("Layer not found: " + gazeHitLayerName + ". Please create this layer in Unity first.");
                return;
            }

            int layerMask = LayerMask.GetMask(gazeHitLayerName);

            List<TaskSceneRecord> taskSceneRecords = ReadTaskSceneMapFromXlsx(taskSceneMapPath);

            if (taskSceneRecords == null || taskSceneRecords.Count == 0)
            {
                Debug.LogError("No valid TaskOrder-SceneID records were loaded.");
                return;
            }

            Debug.Log("Loaded task-scene records: " + taskSceneRecords.Count);

            if (!processAllScenes)
            {
                HashSet<int> selectedSceneSet = ParseSceneIDSelection(selectedSceneIDs);

                if (selectedSceneSet.Count == 0)
                {
                    Debug.LogError("No valid SceneID parsed. Example input: 1,3,5 or 1-5,8,10");
                    return;
                }

                taskSceneRecords = taskSceneRecords
                    .Where(r => selectedSceneSet.Contains(r.SceneID))
                    .ToList();

                if (taskSceneRecords.Count == 0)
                {
                    Debug.LogError("No task records found after SceneID filtering.");
                    return;
                }

                Debug.Log("Selected SceneIDs: " + string.Join(",", selectedSceneSet.OrderBy(x => x)));
                Debug.Log("Filtered task count: " + taskSceneRecords.Count);
            }

            var groupedByScene = taskSceneRecords
                .GroupBy(r => r.SceneID)
                .OrderBy(g => g.Key);

            int totalFiles = 0;
            int processedFiles = 0;

            foreach (var group in groupedByScene)
            {
                int sceneID = group.Key;
                string scenePath = string.Format(scenePathTemplate, sceneID);

                if (!File.Exists(scenePath))
                {
                    Debug.LogError("Scene file does not exist: " + scenePath);
                    continue;
                }

                Debug.Log("Opening scene: " + scenePath);
                EditorSceneManager.OpenScene(scenePath);
                Physics.SyncTransforms();

                if (activateSignageVariantByCurrentNode && GameObject.Find(signageRootName) == null)
                {
                    Debug.LogWarning("Signage root not found in current scene: " + signageRootName + ". Node-based activation will not work in this scene.");
                }

                foreach (TaskSceneRecord record in group.OrderBy(r => r.TaskOrder))
                {
                    string taskFolder = Path.Combine(inputRoot, "Task" + record.TaskOrder);
                    string eyeFolder = Path.Combine(taskFolder, "Eye");

                    if (!Directory.Exists(eyeFolder))
                    {
                        Debug.LogWarning("Eye folder not found, skipped: " + eyeFolder);
                        continue;
                    }

                    string outputTaskFolder = Path.Combine(outputRoot, "Task" + record.TaskOrder);
                    string outputEyeFolder = Path.Combine(outputTaskFolder, "Eye");

                    if (!Directory.Exists(outputEyeFolder))
                    {
                        Directory.CreateDirectory(outputEyeFolder);
                    }

                    string[] csvFiles = Directory.GetFiles(eyeFolder, "*.csv", SearchOption.TopDirectoryOnly);

                    if (csvFiles.Length == 0)
                    {
                        Debug.LogWarning("No CSV files found in Eye folder, skipped: " + eyeFolder);
                        continue;
                    }

                    Array.Sort(csvFiles, StringComparer.OrdinalIgnoreCase);

                    foreach (string csvPath in csvFiles)
                    {
                        totalFiles++;

                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(csvPath);
                        string outputFileName = fileNameWithoutExt + "_GazeHit.csv";
                        string outputPath = Path.Combine(outputEyeFolder, outputFileName);

                        bool success = ProcessEyeCsv(csvPath, outputPath, layerMask);

                        if (success)
                        {
                            processedFiles++;
                            Debug.Log("Done: " + outputPath);
                        }
                    }
                }
            }

            Debug.Log("========== Batch Gaze Hit Calculation Finished ==========");
            Debug.Log("Total CSV files found: " + totalFiles);
            Debug.Log("Successfully processed files: " + processedFiles);
        }
        catch (Exception ex)
        {
            Debug.LogError("Batch calculation failed: " + ex.Message + "\n" + ex.StackTrace);
        }
    }

    // =========================
    // Process one Eye CSV
    // =========================

    private bool ProcessEyeCsv(string inputCsvPath, string outputCsvPath, int layerMask)
    {
        try
        {
            List<List<string>> table = ReadCsv(inputCsvPath);

            if (table == null || table.Count < 2)
            {
                Debug.LogWarning("CSV is empty or only has header, skipped: " + inputCsvPath);
                return false;
            }

            List<string> header = table[0];

            int idxOriginX = GetRequiredColumnIndex(header, "GazeOriginWorldX", inputCsvPath);
            int idxOriginY = GetRequiredColumnIndex(header, "GazeOriginWorldY", inputCsvPath);
            int idxOriginZ = GetRequiredColumnIndex(header, "GazeOriginWorldZ", inputCsvPath);

            int idxDirX = GetRequiredColumnIndex(header, "GazeDirectionWorldX", inputCsvPath);
            int idxDirY = GetRequiredColumnIndex(header, "GazeDirectionWorldY", inputCsvPath);
            int idxDirZ = GetRequiredColumnIndex(header, "GazeDirectionWorldZ", inputCsvPath);

            if (idxOriginX < 0 || idxOriginY < 0 || idxOriginZ < 0 ||
                idxDirX < 0 || idxDirY < 0 || idxDirZ < 0)
            {
                return false;
            }

            int idxHitX = EnsureColumn(header, table, "HitPointX");
            int idxHitY = EnsureColumn(header, table, "HitPointY");
            int idxHitZ = EnsureColumn(header, table, "HitPointZ");
            int idxHitDistance = EnsureColumn(header, table, "HitDistance");
            int idxHitObjectName = EnsureColumn(header, table, "HitObjectName");
            int idxHitObjectTag = EnsureColumn(header, table, "HitObjectTag");
            int idxHitObjectVariant = EnsureColumn(header, table, "HitObjectVariant");

            int idxCurrentNode = -1;
            if (filterSignageByCurrentNode)
            {
                idxCurrentNode = GetRequiredColumnIndex(header, nodeIDColumnName, inputCsvPath);
                if (idxCurrentNode < 0)
                {
                    return false;
                }
            }

            int idxHitNormalX = EnsureColumn(header, table, "HitNormalX");
            int idxHitNormalY = EnsureColumn(header, table, "HitNormalY");
            int idxHitNormalZ = EnsureColumn(header, table, "HitNormalZ");

            int idxGazeValid = EnsureColumn(header, table, "GazeValid");
            int idxGazeHit = EnsureColumn(header, table, "GazeHit");

            int validCount = 0;
            int hitCount = 0;
            string lastActivatedNodeName = "";

            for (int i = 1; i < table.Count; i++)
            {
                List<string> row = table[i];

                EnsureRowLength(row, header.Count);

                float ox = 0f;
                float oy = 0f;
                float oz = 0f;
                float dx = 0f;
                float dy = 0f;
                float dz = 0f;

                bool parsed = true;
                parsed &= TryParseFloat(row[idxOriginX], out ox);
                parsed &= TryParseFloat(row[idxOriginY], out oy);
                parsed &= TryParseFloat(row[idxOriginZ], out oz);
                parsed &= TryParseFloat(row[idxDirX], out dx);
                parsed &= TryParseFloat(row[idxDirY], out dy);
                parsed &= TryParseFloat(row[idxDirZ], out dz);

                if (!parsed)
                {
                    WriteInvalid(
                        row,
                        idxGazeValid,
                        idxGazeHit,
                        idxHitX,
                        idxHitY,
                        idxHitZ,
                        idxHitDistance,
                        idxHitObjectName,
                        idxHitObjectTag,
                        idxHitNormalX,
                        idxHitNormalY,
                        idxHitNormalZ
                    );
                    row[idxHitObjectVariant] = "";
                    continue;
                }

                Vector3 origin = new Vector3(ox, oy, oz);
                Vector3 direction = new Vector3(dx, dy, dz);

                bool gazeValid =
                    IsFinite(origin) &&
                    IsFinite(direction) &&
                    direction.sqrMagnitude > 0.000001f;

                if (!gazeValid)
                {
                    WriteInvalid(
                        row,
                        idxGazeValid,
                        idxGazeHit,
                        idxHitX,
                        idxHitY,
                        idxHitZ,
                        idxHitDistance,
                        idxHitObjectName,
                        idxHitObjectTag,
                        idxHitNormalX,
                        idxHitNormalY,
                        idxHitNormalZ
                    );
                    row[idxHitObjectVariant] = "";
                    continue;
                }

                validCount++;

                direction.Normalize();

                Ray ray = new Ray(origin, direction);

                string currentNodeName = "";
                if (filterSignageByCurrentNode && idxCurrentNode >= 0)
                {
                    currentNodeName = NormalizeNodeName(row[idxCurrentNode]);
                }

                if (activateSignageVariantByCurrentNode &&
                    !string.IsNullOrEmpty(currentNodeName) &&
                    !string.Equals(currentNodeName, lastActivatedNodeName, StringComparison.OrdinalIgnoreCase))
                {
                    ActivateSignageVariantForNode(currentNodeName);
                    Physics.SyncTransforms();
                    lastActivatedNodeName = currentNodeName;
                }

                if (TryGetFilteredHit(ray, maxRayDistance, layerMask, currentNodeName, out RaycastHit hit))
                {
                    hitCount++;

                    row[idxGazeValid] = "1";
                    row[idxGazeHit] = "1";

                    row[idxHitX] = FormatFloat(hit.point.x);
                    row[idxHitY] = FormatFloat(hit.point.y);
                    row[idxHitZ] = FormatFloat(hit.point.z);
                    row[idxHitDistance] = FormatFloat(hit.distance);

                    GameObject hitObject = hit.collider.gameObject;
                    bool isSignage = IsSignageObject(hitObject.transform);

                    // HitObjectName records the semantic object, e.g. HS-02.
                    // HitObjectVariant records the node-specific collider version, e.g. Node11.
                    row[idxHitObjectName] = isSignage ? GetSemanticObjectName(hitObject.transform) : hitObject.name;
                    row[idxHitObjectTag] = isSignage ? signageTagName : hitObject.tag;
                    row[idxHitObjectVariant] = isSignage ? GetNodeVariantName(hitObject.transform) : "";

                    row[idxHitNormalX] = FormatFloat(hit.normal.x);
                    row[idxHitNormalY] = FormatFloat(hit.normal.y);
                    row[idxHitNormalZ] = FormatFloat(hit.normal.z);
                }
                else
                {
                    row[idxGazeValid] = "1";
                    row[idxGazeHit] = "0";

                    WriteNoHit(
                        row,
                        idxHitX,
                        idxHitY,
                        idxHitZ,
                        idxHitDistance,
                        idxHitObjectName,
                        idxHitObjectTag,
                        idxHitNormalX,
                        idxHitNormalY,
                        idxHitNormalZ
                    );
                    row[idxHitObjectVariant] = "";
                }
            }

            WriteCsv(outputCsvPath, table);

            Debug.Log(
                "File processed: " + Path.GetFileName(inputCsvPath) +
                " | Valid gaze: " + validCount +
                " | Hit: " + hitCount
            );

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to process CSV: " + inputCsvPath + "\n" + ex.Message + "\n" + ex.StackTrace);
            return false;
        }
    }

    // =========================
    // Node-specific signage hit filtering
    // =========================

    private bool TryGetFilteredHit(
        Ray ray,
        float maxDistance,
        int layerMask,
        string currentNodeName,
        out RaycastHit selectedHit)
    {
        selectedHit = default(RaycastHit);

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            maxDistance,
            layerMask,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        // Keep the physical occlusion order:
        // the first acceptable collider along the ray is the final hit.
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        string normalizedCurrentNode = NormalizeNodeName(currentNodeName);
        bool hasCurrentNode = !string.IsNullOrEmpty(normalizedCurrentNode);

        foreach (RaycastHit candidate in hits)
        {
            Transform hitTransform = candidate.collider.transform;
            bool isSignage = IsSignageObject(hitTransform);

            // Key rule:
            // NodeID filtering is applied ONLY to Signage objects.
            // Non-signage objects such as walls, columns, floors, railings, shopfronts,
            // etc. should be recorded normally and must not be discarded.
            if (isSignage && filterSignageByCurrentNode)
            {
                // If this row has no valid NodeID, this signage hit cannot be
                // matched to a node-specific signage variant, so skip this
                // signage candidate only and continue checking farther hits.
                if (!hasCurrentNode)
                {
                    continue;
                }

                string hitNodeVariant = NormalizeNodeName(GetNodeVariantName(hitTransform));

                if (!string.Equals(hitNodeVariant, normalizedCurrentNode, StringComparison.OrdinalIgnoreCase))
                {
                    // Wrong signage variant, e.g. current row is Node11 but hit is Signage/*/Node7.
                    // Skip this signage candidate and continue to the next hit.
                    continue;
                }
            }

            selectedHit = candidate;
            return true;
        }

        return false;
    }

    private void ActivateSignageVariantForNode(string currentNodeName)
    {
        string normalizedCurrentNode = NormalizeNodeName(currentNodeName);

        if (string.IsNullOrEmpty(normalizedCurrentNode))
        {
            return;
        }

        GameObject signageRoot = GameObject.Find(signageRootName);

        if (signageRoot == null)
        {
            Debug.LogWarning("Signage root not found: " + signageRootName);
            return;
        }

        Transform[] allTransforms = signageRoot.GetComponentsInChildren<Transform>(true);

        int activeCount = 0;
        int inactiveCount = 0;

        foreach (Transform t in allTransforms)
        {
            if (!IsNodeVariantName(t.name))
            {
                continue;
            }

            string nodeVariantName = NormalizeNodeName(t.name);
            bool shouldBeActive = string.Equals(
                nodeVariantName,
                normalizedCurrentNode,
                StringComparison.OrdinalIgnoreCase
            );

            if (t.gameObject.activeSelf != shouldBeActive)
            {
                t.gameObject.SetActive(shouldBeActive);
            }

            if (shouldBeActive)
            {
                activeCount++;
            }
            else
            {
                inactiveCount++;
            }
        }

        // Use this message for debugging only. It appears when the active NodeID changes.
        Debug.Log(
            "Activated signage variant: " + normalizedCurrentNode +
            " | Active: " + activeCount +
            " | Inactive: " + inactiveCount
        );
    }

    private bool IsSignageObject(Transform transform)
    {
        Transform current = transform;

        while (current != null)
        {
            if (current.CompareTag(signageTagName))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private string GetNodeVariantName(Transform transform)
    {
        Transform current = transform;

        while (current != null)
        {
            if (IsNodeVariantName(current.name))
            {
                return NormalizeNodeName(current.name);
            }

            current = current.parent;
        }

        return "";
    }

    private string GetSemanticObjectName(Transform transform)
    {
        Transform current = transform;

        while (current != null)
        {
            if (IsNodeVariantName(current.name))
            {
                if (current.parent != null)
                {
                    return current.parent.name;
                }

                return current.name;
            }

            current = current.parent;
        }

        return transform.gameObject.name;
    }

    private bool IsNodeVariantName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        string s = rawName.Trim();

        if (!s.StartsWith("Node", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = s.Substring(4).Trim();

        if (suffix.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < suffix.Length; i++)
        {
            if (!char.IsDigit(suffix[i]))
            {
                return false;
            }
        }

        return true;
    }

    private string NormalizeNodeName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "";
        }

        string s = rawName.Trim();

        if (s.StartsWith("Node", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = s.Substring(4).Trim();

            int nodeNumber;
            if (int.TryParse(suffix, out nodeNumber))
            {
                return "Node" + nodeNumber;
            }

            return s;
        }

        int intNodeNumber;
        if (int.TryParse(s, out intNodeNumber))
        {
            return "Node" + intNodeNumber;
        }

        float floatNodeNumber;
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out floatNodeNumber))
        {
            int rounded = Mathf.RoundToInt(floatNodeNumber);
            if (Mathf.Abs(floatNodeNumber - rounded) < 0.0001f)
            {
                return "Node" + rounded;
            }
        }

        return s;
    }

    // =========================
    // Invalid / no-hit writers
    // =========================

    private void WriteInvalid(
        List<string> row,
        int idxGazeValid,
        int idxGazeHit,
        int idxHitX,
        int idxHitY,
        int idxHitZ,
        int idxHitDistance,
        int idxHitObjectName,
        int idxHitObjectTag,
        int idxHitNormalX,
        int idxHitNormalY,
        int idxHitNormalZ)
    {
        row[idxGazeValid] = "0";
        row[idxGazeHit] = "0";

        if (emptyHitValuesWhenNoHit)
        {
            row[idxHitX] = "";
            row[idxHitY] = "";
            row[idxHitZ] = "";
            row[idxHitDistance] = "";

            row[idxHitNormalX] = "";
            row[idxHitNormalY] = "";
            row[idxHitNormalZ] = "";
        }
        else
        {
            row[idxHitX] = "0";
            row[idxHitY] = "0";
            row[idxHitZ] = "0";
            row[idxHitDistance] = "0";

            row[idxHitNormalX] = "0";
            row[idxHitNormalY] = "0";
            row[idxHitNormalZ] = "0";
        }

        row[idxHitObjectName] = "InvalidGaze";
        row[idxHitObjectTag] = "InvalidGaze";
    }

    private void WriteNoHit(
        List<string> row,
        int idxHitX,
        int idxHitY,
        int idxHitZ,
        int idxHitDistance,
        int idxHitObjectName,
        int idxHitObjectTag,
        int idxHitNormalX,
        int idxHitNormalY,
        int idxHitNormalZ)
    {
        if (emptyHitValuesWhenNoHit)
        {
            row[idxHitX] = "";
            row[idxHitY] = "";
            row[idxHitZ] = "";
            row[idxHitDistance] = "";

            row[idxHitNormalX] = "";
            row[idxHitNormalY] = "";
            row[idxHitNormalZ] = "";
        }
        else
        {
            row[idxHitX] = "0";
            row[idxHitY] = "0";
            row[idxHitZ] = "0";
            row[idxHitDistance] = "0";

            row[idxHitNormalX] = "0";
            row[idxHitNormalY] = "0";
            row[idxHitNormalZ] = "0";
        }

        row[idxHitObjectName] = "NoHit";
        row[idxHitObjectTag] = "NoHit";
    }

    // =========================
    // Read TaskOrder - SceneID from xlsx
    // =========================

    private List<TaskSceneRecord> ReadTaskSceneMapFromXlsx(string xlsxPath)
    {
        List<TaskSceneRecord> records = new List<TaskSceneRecord>();

        using (FileStream zipStream = new FileStream(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            List<string> sharedStrings = ReadSharedStrings(archive);

            ZipArchiveEntry sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");

            if (sheetEntry == null)
            {
                Debug.LogError("Cannot find xl/worksheets/sheet1.xml in the xlsx file.");
                return records;
            }

            List<List<string>> rows = ReadSheetRows(sheetEntry, sharedStrings);

            if (rows.Count < 2)
            {
                Debug.LogError("Task-scene map has insufficient rows.");
                return records;
            }

            List<string> header = rows[0];

            int idxTaskOrder = FindColumnIndex(header, "TaskOrder");
            int idxSceneID = FindColumnIndex(header, "SceneID");

            if (idxTaskOrder < 0 || idxSceneID < 0)
            {
                Debug.LogError("Cannot find TaskOrder or SceneID columns in the task-scene map.");
                return records;
            }

            for (int i = 1; i < rows.Count; i++)
            {
                List<string> row = rows[i];

                if (idxTaskOrder >= row.Count || idxSceneID >= row.Count)
                {
                    continue;
                }

                int taskOrder;
                int sceneID;

                if (int.TryParse(row[idxTaskOrder], out taskOrder) &&
                    int.TryParse(row[idxSceneID], out sceneID))
                {
                    records.Add(new TaskSceneRecord
                    {
                        TaskOrder = taskOrder,
                        SceneID = sceneID
                    });
                }
            }
        }

        return records;
    }

    private List<string> ReadSharedStrings(ZipArchive archive)
    {
        List<string> sharedStrings = new List<string>();

        ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");

        if (entry == null)
        {
            return sharedStrings;
        }

        XmlDocument doc = new XmlDocument();

        using (Stream stream = entry.Open())
        {
            doc.Load(stream);
        }

        XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("a", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        XmlNodeList siNodes = doc.SelectNodes("//a:si", ns);

        foreach (XmlNode si in siNodes)
        {
            XmlNodeList textNodes = si.SelectNodes(".//a:t", ns);
            StringBuilder sb = new StringBuilder();

            foreach (XmlNode t in textNodes)
            {
                sb.Append(t.InnerText);
            }

            sharedStrings.Add(sb.ToString());
        }

        return sharedStrings;
    }

    private List<List<string>> ReadSheetRows(ZipArchiveEntry sheetEntry, List<string> sharedStrings)
    {
        Dictionary<int, Dictionary<int, string>> cellTable = new Dictionary<int, Dictionary<int, string>>();

        XmlDocument doc = new XmlDocument();

        using (Stream stream = sheetEntry.Open())
        {
            doc.Load(stream);
        }

        XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("a", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        XmlNodeList rowNodes = doc.SelectNodes("//a:sheetData/a:row", ns);

        int maxRow = 0;
        int maxCol = 0;

        foreach (XmlNode rowNode in rowNodes)
        {
            XmlAttribute rowAttribute = rowNode.Attributes["r"];
            if (rowAttribute == null)
            {
                continue;
            }

            int rowIndex;
            if (!int.TryParse(rowAttribute.Value, out rowIndex))
            {
                continue;
            }

            if (!cellTable.ContainsKey(rowIndex))
            {
                cellTable[rowIndex] = new Dictionary<int, string>();
            }

            XmlNodeList cellNodes = rowNode.SelectNodes("a:c", ns);

            foreach (XmlNode cellNode in cellNodes)
            {
                XmlAttribute refAttribute = cellNode.Attributes["r"];
                if (refAttribute == null)
                {
                    continue;
                }

                string cellRef = refAttribute.Value;
                int colIndex = GetColumnIndexFromCellRef(cellRef);

                string cellType = cellNode.Attributes["t"] != null ? cellNode.Attributes["t"].Value : "";
                string value = "";

                if (cellType == "inlineStr")
                {
                    XmlNode textNode = cellNode.SelectSingleNode("a:is/a:t", ns);
                    if (textNode != null)
                    {
                        value = textNode.InnerText;
                    }
                }
                else
                {
                    XmlNode valueNode = cellNode.SelectSingleNode("a:v", ns);

                    if (valueNode != null)
                    {
                        value = valueNode.InnerText;

                        if (cellType == "s")
                        {
                            int sharedIndex;
                            if (int.TryParse(value, out sharedIndex) &&
                                sharedIndex >= 0 &&
                                sharedIndex < sharedStrings.Count)
                            {
                                value = sharedStrings[sharedIndex];
                            }
                        }
                    }
                }

                cellTable[rowIndex][colIndex] = value;

                maxRow = Mathf.Max(maxRow, rowIndex);
                maxCol = Mathf.Max(maxCol, colIndex);
            }
        }

        List<List<string>> rows = new List<List<string>>();

        for (int r = 1; r <= maxRow; r++)
        {
            List<string> row = new List<string>();

            for (int c = 1; c <= maxCol; c++)
            {
                if (cellTable.ContainsKey(r) && cellTable[r].ContainsKey(c))
                {
                    row.Add(cellTable[r][c]);
                }
                else
                {
                    row.Add("");
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    private int GetColumnIndexFromCellRef(string cellRef)
    {
        int col = 0;

        foreach (char ch in cellRef)
        {
            if (char.IsLetter(ch))
            {
                col = col * 26 + (char.ToUpper(ch) - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return col;
    }

    // =========================
    // CSV read and write
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

    private void WriteCsv(string path, List<List<string>> table)
    {
        using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            foreach (List<string> row in table)
            {
                writer.WriteLine(string.Join(",", row.Select(EscapeCsvValue).ToArray()));
            }
        }
    }

    private string EscapeCsvValue(string value)
    {
        if (value == null)
        {
            return "";
        }

        bool needQuote =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (needQuote)
        {
            value = value.Replace("\"", "\"\"");
            return "\"" + value + "\"";
        }

        return value;
    }

    // =========================
    // SceneID selection
    // =========================

    private HashSet<int> ParseSceneIDSelection(string input)
    {
        HashSet<int> result = new HashSet<int>();

        if (string.IsNullOrEmpty(input) || input.Trim().Length == 0)
        {
            return result;
        }

        string[] parts = input.Split(',');

        foreach (string rawPart in parts)
        {
            string part = rawPart.Trim();

            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            if (part.Contains("-"))
            {
                string[] rangeParts = part.Split('-');

                if (rangeParts.Length != 2)
                {
                    Debug.LogWarning("Cannot parse SceneID range: " + part);
                    continue;
                }

                int start;
                int end;

                if (int.TryParse(rangeParts[0].Trim(), out start) &&
                    int.TryParse(rangeParts[1].Trim(), out end))
                {
                    if (start > end)
                    {
                        int temp = start;
                        start = end;
                        end = temp;
                    }

                    for (int i = start; i <= end; i++)
                    {
                        result.Add(i);
                    }
                }
                else
                {
                    Debug.LogWarning("Cannot parse SceneID range: " + part);
                }
            }
            else
            {
                int sceneID;

                if (int.TryParse(part, out sceneID))
                {
                    result.Add(sceneID);
                }
                else
                {
                    Debug.LogWarning("Cannot parse SceneID: " + part);
                }
            }
        }

        return result;
    }

    // =========================
    // Utility functions
    // =========================

    private int GetRequiredColumnIndex(List<string> header, string columnName, string filePath)
    {
        int index = FindColumnIndex(header, columnName);

        if (index < 0)
        {
            Debug.LogError("Missing required column: " + columnName + "\nFile: " + filePath);
        }

        return index;
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

    private int EnsureColumn(List<string> header, List<List<string>> table, string columnName)
    {
        int index = FindColumnIndex(header, columnName);

        if (index >= 0)
        {
            return index;
        }

        header.Add(columnName);
        int newIndex = header.Count - 1;

        for (int i = 1; i < table.Count; i++)
        {
            table[i].Add("");
        }

        return newIndex;
    }

    private void EnsureRowLength(List<string> row, int length)
    {
        while (row.Count < length)
        {
            row.Add("");
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

    private string FormatFloat(float value)
    {
        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private bool IsFinite(Vector3 v)
    {
        return
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }

    private class TaskSceneRecord
    {
        public int TaskOrder;
        public int SceneID;
    }
}