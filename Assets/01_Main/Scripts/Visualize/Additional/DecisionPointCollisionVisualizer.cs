using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Runtime visualizer for a single decision-point stay event.
/// - Locates Eye CSV by TaskOrder + csvIndex
/// - Splits consecutive NodeID rows into stay events
/// - Finds target stay by NodeID + PathSequence
/// - Visualizes:
///   1) cumulative ground projection sectors (small radius, for direction explanation)
///   2) cumulative 3D shell surfaces (semi-transparent, double-sided mesh)
///   3) potential direction arrows
///   4) highlighted visible Signage / TargetObjects accumulated over the stay
/// </summary>
public class DecisionPointCollisionVisualizer : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Base Reorder path, e.g. F:/.../Reorder")]
    public string baseReorderPath;
    [Tooltip("Relative to Assets folder. Example: StreamingAssets/ReadData/NodeVisible.csv")]
    public string nodeVisibleRelativePath =
    "ReadData/NodeVisible.csv";

    [Header("Select Trial / Stay Event")]
    public int taskOrder = 1;
    [Tooltip("1-based csv index in TaskX/Eye after alphabetical sort")]
    public int csvIndex = 1;
    public int targetNodeId = 1;
    [Tooltip("1-based stay order among consecutive same-Node stays in this Eye csv")]
    public int targetPathSequence = 1;
    public bool autoRefreshOnStart = true;

    [Header("Scene References")]
    public Transform signageRoot;
    public Transform targetObjectsRoot;
    public Transform nodesRoot;

    [Header("Visibility Parameters")]
    public float maxViewDistance = 25f;
    public float fallbackHorizontalFov = 98.17201f;
    public float fallbackVerticalFov = 104.0472f;
    public string surfaceTag = "surface";

    [Header("Projection / Shell Display")]
    [Tooltip("Ground projection radius. This is only for explaining which direction arrows fall inside, not the full 25m radius.")]
    public float groundProjectionRadius = 2.5f;
    [Tooltip("Ground projection Y offset relative to current node position.")]
    public float groundHeightOffset = 0.05f;
    public int horizontalArcSegments = 40;
    public int verticalArcSegments = 10;
    public Material shellMaterial;
    public Material groundSectorMaterial;
    public Color shellColor = new Color(0.1f, 0.95f, 1f, 0.16f);
    public Color groundSectorColor = new Color(0.1f, 0.95f, 1f, 0.22f);

    [Header("Direction Visualization")]
    public float directionArrowLength = 2.5f;
    public float arrowHeadLength = 0.35f;
    public float arrowHeadAngleDeg = 22f;
    public Material directionVisibleMaterial;
    public Material directionHiddenMaterial;
    public Color visibleDirectionColor = new Color(0.2f, 0.9f, 0.3f, 1f);
    public Color hiddenDirectionColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Object Highlight")]
    public Material signageHighlightMaterial;
    public Material targetHighlightMaterial;
    public bool highlightSignage = true;
    public bool highlightTargets = true;

    [Header("Debug / Overlay")]
    public bool drawForwardRay = true;
    public float forwardRayLength = 1.5f;
    public Color forwardColor = new Color(0.1f, 0.95f, 1f, 1f);
    public bool showOverlay = true;
    public Vector2 overlayPosition = new Vector2(20, 20);
    public Vector2 overlaySize = new Vector2(560, 280);

    private readonly List<EyeFrame> _frames = new List<EyeFrame>();
    private readonly List<NodeStaySegment> _segments = new List<NodeStaySegment>();
    private readonly Dictionary<int, List<int>> _nodeConnections = new Dictionary<int, List<int>>();
    private readonly Dictionary<int, Transform> _nodeTransforms = new Dictionary<int, Transform>();

    private readonly List<AngleInterval> _mergedIntervals = new List<AngleInterval>();
    private readonly List<SignageCandidate> _signageCandidates = new List<SignageCandidate>();
    private readonly List<TargetCandidate> _targetCandidates = new List<TargetCandidate>();

    private readonly Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
    private readonly HashSet<string> _visibleSignageIds = new HashSet<string>();
    private readonly HashSet<string> _visibleTargetIds = new HashSet<string>();
    private readonly HashSet<int> _visibleDirectionIds = new HashSet<int>();

    private NodeStaySegment _currentSegment;
    private string _currentCsvPath = string.Empty;
    private bool _isReady = false;

    private Vector3 _avgPosition;
    private Vector3 _avgForward;
    private float _avgHfov;
    private float _avgVfov;

    private Transform _visualRoot;
    private readonly List<GameObject> _generatedVisuals = new List<GameObject>();

    private void Awake()
    {
        baseReorderPath = ProjectPathConfig.ReorderDataRoot;
    }

    private void Start()
    {
        if (autoRefreshOnStart) Refresh();
    }

    private void Update()
    {
        if (!_isReady) return;
        if (drawForwardRay)
        {
            Debug.DrawLine(_avgPosition, _avgPosition + _avgForward.normalized * forwardRayLength, forwardColor);
        }
    }

    private void OnDisable()
    {
        RestoreHighlightedMaterials();
        ClearGeneratedVisuals();
    }

    private void OnDestroy()
    {
        RestoreHighlightedMaterials();
        ClearGeneratedVisuals();
    }

    private void OnGUI()
    {
        if (!showOverlay || !_isReady || _currentSegment == null) return;

        Rect r = new Rect(overlayPosition.x, overlayPosition.y, overlaySize.x, overlaySize.y);
        GUI.Box(r, "Decision Point Collision Visualizer");

        GUILayout.BeginArea(new Rect(r.x + 12, r.y + 26, r.width - 24, r.height - 36));
        GUILayout.Label($"TaskOrder: {taskOrder}");
        GUILayout.Label($"CSV Index: {csvIndex}");
        GUILayout.Label($"CSV File: {Path.GetFileName(_currentCsvPath)}");
        GUILayout.Label($"NodeID: {targetNodeId}");
        GUILayout.Label($"PathSequence: {targetPathSequence}");
        GUILayout.Label($"Frames in stay: {_currentSegment.FrameCount}");
        GUILayout.Space(8);
        GUILayout.Label($"Visible Signage Count: {_visibleSignageIds.Count}");
        GUILayout.Label(string.Join(", ", _visibleSignageIds.OrderBy(s => s)));
        GUILayout.Space(5);
        GUILayout.Label($"Visible Target Count: {_visibleTargetIds.Count}");
        GUILayout.Label(string.Join(", ", _visibleTargetIds.OrderBy(s => s)));
        GUILayout.Space(5);
        GUILayout.Label($"Visible Direction Count: {_visibleDirectionIds.Count}");
        GUILayout.Label(string.Join(", ", _visibleDirectionIds.OrderBy(v => v).Select(v => $"Node{v}")));
        GUILayout.EndArea();
    }

    [ContextMenu("Refresh Visualizer")]
    public void Refresh()
    {
        RestoreHighlightedMaterials();
        ClearGeneratedVisuals();

        _isReady = false;
        _frames.Clear();
        _segments.Clear();
        _nodeConnections.Clear();
        _nodeTransforms.Clear();
        _mergedIntervals.Clear();
        _signageCandidates.Clear();
        _targetCandidates.Clear();
        _visibleSignageIds.Clear();
        _visibleTargetIds.Clear();
        _visibleDirectionIds.Clear();
        _currentSegment = null;
        _currentCsvPath = string.Empty;

        if (!ValidateSetup()) return;

        CacheNodeTransforms();
        LoadNodeConnections();

        if (!ResolveEyeCsvPath(out _currentCsvPath))
        {
            Debug.LogError("[DecisionPointCollisionVisualizer] Failed to resolve Eye CSV path.");
            return;
        }

        try
        {
            ReadEyeCsv(_currentCsvPath, _frames);
            BuildNodeStaySegments(_frames, _segments);

            _currentSegment = _segments.FirstOrDefault(s => s.NodeId == targetNodeId && s.PathSequence == targetPathSequence);
            if (_currentSegment == null)
            {
                Debug.LogError($"[DecisionPointCollisionVisualizer] Stay event not found. NodeID={targetNodeId}, PathSequence={targetPathSequence}");
                return;
            }

            ComputeSegmentAverages(_currentSegment);
            BuildCumulativeHorizontalIntervals(_currentSegment);
            BuildCandidatesForCurrentNode();
            EvaluateAccumulatedVisibility(_currentSegment);
            ApplyHighlights();
            BuildVisualGeometry();

            _isReady = true;
            Debug.Log($"[DecisionPointCollisionVisualizer] Ready. Signage={_visibleSignageIds.Count}, Target={_visibleTargetIds.Count}, Direction={_visibleDirectionIds.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DecisionPointCollisionVisualizer] Refresh failed: {ex}");
        }
    }

    #region Validation / Resolve

    private bool ValidateSetup()
    {
        if (string.IsNullOrWhiteSpace(baseReorderPath) || !Directory.Exists(baseReorderPath))
        {
            Debug.LogError($"[DecisionPointCollisionVisualizer] Invalid baseReorderPath: {baseReorderPath}");
            return false;
        }

        string nodeVisibleFullPath = GetNodeVisibleFullPath();
        if (string.IsNullOrWhiteSpace(nodeVisibleFullPath) || !File.Exists(nodeVisibleFullPath))
        {
            Debug.LogError($"[DecisionPointCollisionVisualizer] Invalid NodeVisible path: {nodeVisibleFullPath}");
            return false;
        }

        if (signageRoot == null || targetObjectsRoot == null || nodesRoot == null)
        {
            Debug.LogError("[DecisionPointCollisionVisualizer] Please assign signageRoot, targetObjectsRoot and nodesRoot.");
            return false;
        }

        if (taskOrder < 1 || csvIndex < 1 || targetNodeId < 1 || targetPathSequence < 1)
        {
            Debug.LogError("[DecisionPointCollisionVisualizer] taskOrder, csvIndex, targetNodeId and targetPathSequence must be >= 1.");
            return false;
        }

        return true;
    }

    private string GetNodeVisibleFullPath()
    {
        return Path.Combine(
            Application.streamingAssetsPath,
            nodeVisibleRelativePath
        );
    }

    private bool ResolveEyeCsvPath(out string csvPath)
    {
        csvPath = string.Empty;
        string eyeFolder = Path.Combine(baseReorderPath, $"Task{taskOrder}", "Eye");
        if (!Directory.Exists(eyeFolder))
        {
            Debug.LogError($"[DecisionPointCollisionVisualizer] Eye folder not found: {eyeFolder}");
            return false;
        }

        string[] files = Directory.GetFiles(eyeFolder, "*.csv");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        int idx = csvIndex - 1;
        if (idx < 0 || idx >= files.Length)
        {
            Debug.LogError($"[DecisionPointCollisionVisualizer] csvIndex={csvIndex} out of range, file count={files.Length}");
            return false;
        }

        csvPath = files[idx];
        return true;
    }

    #endregion

    #region CSV / Segments

    private void ReadEyeCsv(string path, List<EyeFrame> output)
    {
        output.Clear();

        string[] lines = File.ReadAllLines(path);
        if (lines.Length <= 1) throw new Exception("CSV has no data rows.");

        string[] header = ParseCsvLine(lines[0]);
        int idxNode = FindColumnIndex(header, "NodeID");
        int idxPx = FindColumnIndex(header, "HMDPositionX");
        int idxPy = FindColumnIndex(header, "HMDPositionY");
        int idxPz = FindColumnIndex(header, "HMDPositionZ");
        int idxRx = FindColumnIndex(header, "HMDRotationEulerX");
        int idxRy = FindColumnIndex(header, "HMDRotationEulerY");
        int idxRz = FindColumnIndex(header, "HMDRotationEulerZ");
        int idxHfov = FindColumnIndex(header, "CameraFOV_H", false);
        int idxVfov = FindColumnIndex(header, "CameraFOV_V", false);

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] parts = ParseCsvLine(lines[i]);

            int nodeId = ParseInt(parts, idxNode, -1);
            if (nodeId < 0) continue;

            Vector3 pos = new Vector3(
                ParseFloat(parts, idxPx, 0f),
                ParseFloat(parts, idxPy, 0f),
                ParseFloat(parts, idxPz, 0f)
            );

            Vector3 euler = new Vector3(
                ParseFloat(parts, idxRx, 0f),
                ParseFloat(parts, idxRy, 0f),
                ParseFloat(parts, idxRz, 0f)
            );

            Quaternion rot = Quaternion.Euler(euler);
            Vector3 forward = (rot * Vector3.forward).normalized;
            if (forward.sqrMagnitude < 1e-6f) continue;

            float hfov = idxHfov >= 0 ? ParseFloat(parts, idxHfov, fallbackHorizontalFov) : fallbackHorizontalFov;
            float vfov = idxVfov >= 0 ? ParseFloat(parts, idxVfov, fallbackVerticalFov) : fallbackVerticalFov;

            output.Add(new EyeFrame
            {
                RowIndex = i,
                NodeId = nodeId,
                Position = pos,
                RotationEuler = euler,
                Forward = forward,
                HorizontalFov = hfov,
                VerticalFov = vfov
            });
        }

        if (output.Count == 0) throw new Exception("No valid Eye frames parsed.");
    }

    private void BuildNodeStaySegments(List<EyeFrame> frames, List<NodeStaySegment> output)
    {
        output.Clear();
        if (frames.Count == 0) return;

        int staySeq = 0;
        int start = 0;
        int currentNode = frames[0].NodeId;

        for (int i = 1; i <= frames.Count; i++)
        {
            bool endSeg = i == frames.Count || frames[i].NodeId != currentNode;
            if (!endSeg) continue;

            staySeq++;
            output.Add(new NodeStaySegment
            {
                NodeId = currentNode,
                StartIndex = start,
                EndIndex = i - 1,
                PathSequence = staySeq
            });

            if (i < frames.Count)
            {
                start = i;
                currentNode = frames[i].NodeId;
            }
        }
    }

    private void ComputeSegmentAverages(NodeStaySegment seg)
    {
        Vector3 posSum = Vector3.zero;
        Vector3 forwardSum = Vector3.zero;
        float hfovSum = 0f;
        float vfovSum = 0f;
        int count = 0;

        for (int i = seg.StartIndex; i <= seg.EndIndex; i++)
        {
            EyeFrame f = _frames[i];
            posSum += f.Position;
            forwardSum += f.Forward;
            hfovSum += f.HorizontalFov;
            vfovSum += f.VerticalFov;
            count++;
        }

        _avgPosition = posSum / Mathf.Max(1, count);
        _avgForward = (forwardSum / Mathf.Max(1, count)).normalized;
        _avgHfov = hfovSum / Mathf.Max(1, count);
        _avgVfov = vfovSum / Mathf.Max(1, count);
    }

    #endregion

    #region Scene Caches

    private void CacheNodeTransforms()
    {
        _nodeTransforms.Clear();
        foreach (Transform child in nodesRoot.GetComponentsInChildren<Transform>(true))
        {
            int nodeId = ParseNodeIdFromName(child.name);
            if (nodeId > 0 && !_nodeTransforms.ContainsKey(nodeId))
                _nodeTransforms[nodeId] = child;
        }
    }

    private void LoadNodeConnections()
    {
        _nodeConnections.Clear();
        string nodeVisiblePath = GetNodeVisibleFullPath();

        string[] lines = File.ReadAllLines(nodeVisiblePath);
        if (lines.Length <= 1) return;

        string[] header = ParseCsvLine(lines[0]);
        int idxNode = FindColumnIndex(header, "NodeID");
        int idxVisibleNodes = FindColumnIndex(header, "VisibleNodes");

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] parts = ParseCsvLine(lines[i]);
            int nodeId = ParseInt(parts, idxNode, -1);
            if (nodeId < 0) continue;
            _nodeConnections[nodeId] = ParseIntList(GetPart(parts, idxVisibleNodes));
        }
    }

    private void BuildCandidatesForCurrentNode()
    {
        _signageCandidates.Clear();
        _targetCandidates.Clear();

        foreach (Transform signRoot in signageRoot)
        {
            GameObject actualGo = ResolveSignageObjectForNode(signRoot, targetNodeId);
            if (actualGo == null) continue;

            Renderer[] renderers = actualGo.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;

            _signageCandidates.Add(new SignageCandidate
            {
                LogicalId = signRoot.name,
                TargetObject = actualGo,
                Renderers = renderers.ToList()
            });
        }

        foreach (Transform child in targetObjectsRoot)
        {
            Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;

            _targetCandidates.Add(new TargetCandidate
            {
                LogicalId = child.name,
                TargetObject = child.gameObject,
                Renderers = renderers.ToList()
            });
        }
    }

    private GameObject ResolveSignageObjectForNode(Transform signRoot, int nodeId)
    {
        List<Transform> nodeChildren = new List<Transform>();
        foreach (Transform c in signRoot)
        {
            if (ParseNodeIdFromName(c.name) > 0)
                nodeChildren.Add(c);
        }

        if (nodeChildren.Count == 0)
            return signRoot.gameObject;

        string wanted = $"Node{nodeId}";
        Transform match = nodeChildren.FirstOrDefault(c => string.Equals(c.name, wanted, StringComparison.OrdinalIgnoreCase));
        return match != null ? match.gameObject : null;
    }

    #endregion

    #region Visibility Evaluation

    private void EvaluateAccumulatedVisibility(NodeStaySegment seg)
    {
        _visibleSignageIds.Clear();
        _visibleTargetIds.Clear();
        _visibleDirectionIds.Clear();

        if (_nodeConnections.TryGetValue(targetNodeId, out List<int> connectedNodes) &&
            _nodeTransforms.TryGetValue(targetNodeId, out Transform currentNodeTf))
        {
            Vector3 currentNodePos = currentNodeTf.position;
            foreach (int nextNodeId in connectedNodes)
            {
                if (!_nodeTransforms.TryGetValue(nextNodeId, out Transform nextTf)) continue;

                Vector3 flatDir = Vector3.ProjectOnPlane(nextTf.position - currentNodePos, Vector3.up);
                if (flatDir.sqrMagnitude < 1e-6f) continue;

                float yaw = YawFromDirection(flatDir.normalized);
                if (IsAngleInMergedIntervals(yaw))
                    _visibleDirectionIds.Add(nextNodeId);
            }
        }

        for (int i = seg.StartIndex; i <= seg.EndIndex; i++)
        {
            EyeFrame frame = _frames[i];

            foreach (var signage in _signageCandidates)
            {
                if (_visibleSignageIds.Contains(signage.LogicalId)) continue;
                if (IsObjectVisible3D(frame, signage.TargetObject))
                    _visibleSignageIds.Add(signage.LogicalId);
            }

            foreach (var target in _targetCandidates)
            {
                if (_visibleTargetIds.Contains(target.LogicalId)) continue;
                if (IsObjectVisible3D(frame, target.TargetObject))
                    _visibleTargetIds.Add(target.LogicalId);
            }
        }
    }

    private bool IsObjectVisible3D(EyeFrame frame, GameObject obj)
    {
        if (obj == null) return false;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return false;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        Vector3 center = b.center;
        Vector3 toCenter = center - frame.Position;
        float dist = toCenter.magnitude;
        if (dist > maxViewDistance || dist < 1e-6f) return false;
        if (!IsDirectionInsideFrameFov(frame, toCenter.normalized)) return false;

        foreach (Vector3 sample in BuildBoundsSamplePoints(b))
        {
            Vector3 toSample = sample - frame.Position;
            float d = toSample.magnitude;
            if (d > maxViewDistance || d < 1e-6f) continue;

            Vector3 dir = toSample / d;
            if (!IsDirectionInsideFrameFov(frame, dir)) continue;
            if (RayHitsTargetBeforeSurface(frame.Position, dir, d + 0.02f, obj)) return true;
        }

        return false;
    }

    private bool IsDirectionInsideFrameFov(EyeFrame frame, Vector3 worldDir)
    {
        Quaternion lookRot = Quaternion.LookRotation(frame.Forward.normalized, Vector3.up);
        Vector3 local = Quaternion.Inverse(lookRot) * worldDir.normalized;

        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;

        return Mathf.Abs(yaw) <= frame.HorizontalFov * 0.5f &&
               Mathf.Abs(pitch) <= frame.VerticalFov * 0.5f;
    }

    private bool RayHitsTargetBeforeSurface(Vector3 origin, Vector3 direction, float maxDistance, GameObject targetRoot)
    {
        Ray ray = new Ray(origin, direction);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance);
        if (hits == null || hits.Length == 0) return false;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            GameObject go = hit.collider.gameObject;

            if (go.CompareTag(surfaceTag))
                return false;

            if (go.transform.IsChildOf(targetRoot.transform) || go == targetRoot)
                return true;
        }

        return false;
    }

    private List<Vector3> BuildBoundsSamplePoints(Bounds b)
    {
        List<Vector3> pts = new List<Vector3>(9);
        Vector3 c = b.center;
        Vector3 e = b.extents;

        pts.Add(c);
        pts.Add(c + new Vector3(e.x, e.y, e.z));
        pts.Add(c + new Vector3(e.x, e.y, -e.z));
        pts.Add(c + new Vector3(e.x, -e.y, e.z));
        pts.Add(c + new Vector3(e.x, -e.y, -e.z));
        pts.Add(c + new Vector3(-e.x, e.y, e.z));
        pts.Add(c + new Vector3(-e.x, e.y, -e.z));
        pts.Add(c + new Vector3(-e.x, -e.y, e.z));
        pts.Add(c + new Vector3(-e.x, -e.y, -e.z));
        return pts;
    }

    #endregion

    #region Horizontal Intervals

    private void BuildCumulativeHorizontalIntervals(NodeStaySegment seg)
    {
        _mergedIntervals.Clear();
        List<AngleInterval> raw = new List<AngleInterval>();

        for (int i = seg.StartIndex; i <= seg.EndIndex; i++)
        {
            EyeFrame f = _frames[i];
            Vector3 flatForward = Vector3.ProjectOnPlane(f.Forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-6f) continue;

            float yaw = YawFromDirection(flatForward.normalized);
            float half = f.HorizontalFov * 0.5f;
            AddWrappedInterval(raw, yaw - half, yaw + half);
        }

        if (raw.Count == 0) return;
        raw.Sort((a, b) => a.Start.CompareTo(b.Start));

        AngleInterval cur = raw[0];
        for (int i = 1; i < raw.Count; i++)
        {
            AngleInterval next = raw[i];
            if (next.Start <= cur.End + 0.01f)
                cur.End = Mathf.Max(cur.End, next.End);
            else
            {
                _mergedIntervals.Add(cur);
                cur = next;
            }
        }
        _mergedIntervals.Add(cur);
    }

    private void AddWrappedInterval(List<AngleInterval> list, float start, float end)
    {
        start = NormalizeAngle(start);
        end = NormalizeAngle(end);

        if (start <= end)
        {
            list.Add(new AngleInterval(start, end));
        }
        else
        {
            list.Add(new AngleInterval(start, 180f));
            list.Add(new AngleInterval(-180f, end));
        }
    }

    private bool IsAngleInMergedIntervals(float angle)
    {
        angle = NormalizeAngle(angle);
        foreach (var iv in _mergedIntervals)
        {
            if (angle >= iv.Start && angle <= iv.End)
                return true;
        }
        return false;
    }

    #endregion

    #region Build Visual Geometry

    private void BuildVisualGeometry()
    {
        ClearGeneratedVisuals();

        _visualRoot = new GameObject("DP_Visualizer_RuntimeRoot").transform;
        _visualRoot.SetParent(transform, false);

        BuildGroundProjectionMeshes();
        BuildShellMeshes();
        BuildDirectionArrowMeshes();
    }

    private void ClearGeneratedVisuals()
    {
        if (_visualRoot != null)
        {
            if (Application.isPlaying) Destroy(_visualRoot.gameObject);
            else DestroyImmediate(_visualRoot.gameObject);
            _visualRoot = null;
        }

        foreach (var go in _generatedVisuals)
        {
            if (go == null) continue;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        _generatedVisuals.Clear();
    }

    private void BuildGroundProjectionMeshes()
    {
        if (groundSectorMaterial == null) return;

        Vector3 center = GetNodeProjectionCenter();
        foreach (var iv in _mergedIntervals)
        {
            Mesh m = BuildGroundSectorMesh(center, iv.Start, iv.End, Mathf.Max(0.1f, groundProjectionRadius), horizontalArcSegments);
            CreateMeshObject("GroundSector", m, groundSectorMaterial, groundSectorColor, _visualRoot);
        }
    }

    private void BuildShellMeshes()
    {
        if (shellMaterial == null) return;

        foreach (var iv in _mergedIntervals)
        {
            Mesh m = BuildShellMeshDoubleSided(_avgPosition, iv.Start, iv.End, maxViewDistance, _avgVfov * 0.5f, horizontalArcSegments, verticalArcSegments);
            CreateMeshObject("ViewShell", m, shellMaterial, shellColor, _visualRoot);
        }
    }

    private void BuildDirectionArrowMeshes()
    {
        if (!_nodeConnections.TryGetValue(targetNodeId, out List<int> connectedNodes)) return;
        if (!_nodeTransforms.TryGetValue(targetNodeId, out Transform currentNodeTf)) return;

        Vector3 start = GetNodeProjectionCenter();

        foreach (int nextNodeId in connectedNodes)
        {
            if (!_nodeTransforms.TryGetValue(nextNodeId, out Transform nextTf)) continue;

            Vector3 flatDir = Vector3.ProjectOnPlane(nextTf.position - currentNodeTf.position, Vector3.up);
            if (flatDir.sqrMagnitude < 1e-6f) continue;
            flatDir.Normalize();

            Color c = _visibleDirectionIds.Contains(nextNodeId) ? visibleDirectionColor : hiddenDirectionColor;
            Material mat = _visibleDirectionIds.Contains(nextNodeId) ? directionVisibleMaterial : directionHiddenMaterial;

            GameObject arrow = BuildArrowObject($"Dir_{nextNodeId}", start, flatDir, directionArrowLength, arrowHeadLength, arrowHeadAngleDeg, c, mat);
            arrow.transform.SetParent(_visualRoot, true);
            _generatedVisuals.Add(arrow);
        }
    }

    private Vector3 GetNodeProjectionCenter()
    {
        if (_nodeTransforms.TryGetValue(targetNodeId, out Transform nodeTf))
        {
            Vector3 p = nodeTf.position;
            return new Vector3(p.x, p.y + groundHeightOffset, p.z);
        }

        return new Vector3(_avgPosition.x, groundHeightOffset, _avgPosition.z);
    }

    private Mesh BuildGroundSectorMesh(Vector3 center, float startDeg, float endDeg, float radius, int segments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "GroundSectorMesh";

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        verts.Add(center);
        for (int i = 0; i <= segments; i++)
        {
            float t = segments == 0 ? 0f : i / (float)segments;
            float a = Mathf.Lerp(startDeg, endDeg, t);
            Vector3 p = center + DirectionFromYaw(a) * radius;
            verts.Add(p);
        }

        for (int i = 1; i < verts.Count - 1; i++)
        {
            tris.Add(0);
            tris.Add(i);
            tris.Add(i + 1);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildShellMeshDoubleSided(Vector3 apex, float startDeg, float endDeg, float radius, float halfVfovDeg, int hSeg, int vSeg)
    {
        Mesh mesh = new Mesh();
        mesh.name = "ViewShellMeshDoubleSided";

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        // outer curved surface vertices
        for (int iy = 0; iy <= vSeg; iy++)
        {
            float pt = vSeg == 0 ? 0f : iy / (float)vSeg;
            float pitch = Mathf.Lerp(-halfVfovDeg, halfVfovDeg, pt);

            for (int ix = 0; ix <= hSeg; ix++)
            {
                float yt = hSeg == 0 ? 0f : ix / (float)hSeg;
                float yaw = Mathf.Lerp(startDeg, endDeg, yt);

                Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                verts.Add(apex + dir.normalized * radius);
            }
        }

        int row = hSeg + 1;
        for (int iy = 0; iy < vSeg; iy++)
        {
            for (int ix = 0; ix < hSeg; ix++)
            {
                int a = iy * row + ix;
                int b = a + 1;
                int c = a + row;
                int d = c + 1;

                AddDoubleSidedQuad(tris, a, b, c, d);
            }
        }

        // top face fan to apex
        int topStart = vSeg * row;
        int apexTopIndex = verts.Count;
        verts.Add(apex);
        for (int ix = 0; ix < hSeg; ix++)
        {
            int a = topStart + ix;
            int b = topStart + ix + 1;
            AddDoubleSidedTriangle(tris, apexTopIndex, a, b);
        }

        // bottom face fan to apex
        int bottomStart = 0;
        int apexBottomIndex = verts.Count;
        verts.Add(apex);
        for (int ix = 0; ix < hSeg; ix++)
        {
            int a = bottomStart + ix;
            int b = bottomStart + ix + 1;
            AddDoubleSidedTriangle(tris, apexBottomIndex, b, a);
        }

        // left curtain to apex
        int apexLeftIndex = verts.Count;
        verts.Add(apex);
        for (int iy = 0; iy < vSeg; iy++)
        {
            int a = iy * row + 0;
            int b = (iy + 1) * row + 0;
            AddDoubleSidedTriangle(tris, apexLeftIndex, b, a);
        }

        // right curtain to apex
        int apexRightIndex = verts.Count;
        verts.Add(apex);
        for (int iy = 0; iy < vSeg; iy++)
        {
            int a = iy * row + hSeg;
            int b = (iy + 1) * row + hSeg;
            AddDoubleSidedTriangle(tris, apexRightIndex, a, b);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddDoubleSidedQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(c); tris.Add(b);
        tris.Add(b); tris.Add(c); tris.Add(d);

        tris.Add(b); tris.Add(c); tris.Add(a);
        tris.Add(d); tris.Add(c); tris.Add(b);
    }

    private void AddDoubleSidedTriangle(List<int> tris, int a, int b, int c)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(b);
    }

    private GameObject CreateMeshObject(string name, Mesh mesh, Material baseMaterial, Color color, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, true);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        mf.sharedMesh = mesh;

        Material matInstance = new Material(baseMaterial);
        if (matInstance.HasProperty("_Color"))
            matInstance.color = color;

        mr.sharedMaterial = matInstance;
        _generatedVisuals.Add(go);
        return go;
    }

    private GameObject BuildArrowObject(string name, Vector3 start, Vector3 dir, float length, float headLength, float headAngleDeg, Color color, Material lineMaterial)
    {
        GameObject root = new GameObject(name);

        LineRenderer lr = root.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 5;
        lr.widthMultiplier = 0.03f;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;

        Material m = lineMaterial != null ? new Material(lineMaterial) : new Material(Shader.Find("Sprites/Default"));
        if (m.HasProperty("_Color")) m.color = color;
        lr.sharedMaterial = m;
        lr.startColor = color;
        lr.endColor = color;

        Vector3 end = start + dir * length;
        Vector3 rightHead = Quaternion.AngleAxis(180f - headAngleDeg, Vector3.up) * dir;
        Vector3 leftHead = Quaternion.AngleAxis(180f + headAngleDeg, Vector3.up) * dir;

        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.SetPosition(2, end + rightHead * headLength);
        lr.SetPosition(3, end);
        lr.SetPosition(4, end + leftHead * headLength);

        return root;
    }

    #endregion

    #region Highlight

    private void ApplyHighlights()
    {
        if (highlightSignage && signageHighlightMaterial != null)
        {
            foreach (var signage in _signageCandidates)
            {
                if (!_visibleSignageIds.Contains(signage.LogicalId)) continue;
                foreach (Renderer r in signage.Renderers)
                    OverrideRendererMaterials(r, signageHighlightMaterial);
            }
        }

        if (highlightTargets && targetHighlightMaterial != null)
        {
            foreach (var target in _targetCandidates)
            {
                if (!_visibleTargetIds.Contains(target.LogicalId)) continue;
                foreach (Renderer r in target.Renderers)
                    OverrideRendererMaterials(r, targetHighlightMaterial);
            }
        }
    }

    private void OverrideRendererMaterials(Renderer renderer, Material mat)
    {
        if (renderer == null || mat == null) return;

        if (!_originalMaterials.ContainsKey(renderer))
            _originalMaterials[renderer] = renderer.sharedMaterials;

        Material[] replacement = new Material[renderer.sharedMaterials.Length];
        for (int i = 0; i < replacement.Length; i++)
            replacement[i] = mat;

        renderer.sharedMaterials = replacement;
    }

    private void RestoreHighlightedMaterials()
    {
        foreach (var kv in _originalMaterials)
        {
            if (kv.Key != null)
                kv.Key.sharedMaterials = kv.Value;
        }
        _originalMaterials.Clear();
    }

    #endregion

    #region Parsing / Utility

    private int FindColumnIndex(string[] header, string name, bool required = true)
    {
        for (int i = 0; i < header.Length; i++)
        {
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        if (required) throw new Exception($"Missing column: {name}");
        return -1;
    }

    private string GetPart(string[] parts, int idx)
    {
        return idx >= 0 && idx < parts.Length ? parts[idx] : string.Empty;
    }

    private int ParseInt(string[] parts, int idx, int fallback)
    {
        if (idx < 0 || idx >= parts.Length) return fallback;
        if (int.TryParse(parts[idx], NumberStyles.Any, CultureInfo.InvariantCulture, out int v)) return v;
        if (float.TryParse(parts[idx], NumberStyles.Any, CultureInfo.InvariantCulture, out float vf)) return Mathf.RoundToInt(vf);
        return fallback;
    }

    private float ParseFloat(string[] parts, int idx, float fallback)
    {
        if (idx < 0 || idx >= parts.Length) return fallback;
        if (float.TryParse(parts[idx], NumberStyles.Any, CultureInfo.InvariantCulture, out float v)) return v;
        return fallback;
    }

    private string[] ParseCsvLine(string line)
    {
        List<string> result = new List<string>();
        bool inQuotes = false;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
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
            else if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Length = 0;
            }
            else
            {
                sb.Append(ch);
            }
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    private List<int> ParseIntList(string text)
    {
        List<int> values = new List<int>();
        if (string.IsNullOrWhiteSpace(text)) return values;
        string[] tokens = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            if (int.TryParse(token.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out int v))
                values.Add(v);
        }
        return values;
    }

    private int ParseNodeIdFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        string digits = new string(name.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return -1;
        return int.TryParse(digits, out int v) ? v : -1;
    }

    private float NormalizeAngle(float deg)
    {
        while (deg > 180f) deg -= 360f;
        while (deg < -180f) deg += 360f;
        return deg;
    }

    private float YawFromDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return 0f;
        dir.Normalize();
        return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }

    private Vector3 DirectionFromYaw(float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    #endregion

    #region Nested Types

    [Serializable]
    private class EyeFrame
    {
        public int RowIndex;
        public int NodeId;
        public Vector3 Position;
        public Vector3 RotationEuler;
        public Vector3 Forward;
        public float HorizontalFov;
        public float VerticalFov;
    }

    [Serializable]
    private class NodeStaySegment
    {
        public int NodeId;
        public int StartIndex;
        public int EndIndex;
        public int PathSequence;
        public int FrameCount => EndIndex - StartIndex + 1;
    }

    private struct AngleInterval
    {
        public float Start;
        public float End;
        public AngleInterval(float start, float end)
        {
            Start = start;
            End = end;
        }
    }

    private class SignageCandidate
    {
        public string LogicalId;
        public GameObject TargetObject;
        public List<Renderer> Renderers;
    }

    private class TargetCandidate
    {
        public string LogicalId;
        public GameObject TargetObject;
        public List<Renderer> Renderers;
    }

    #endregion
}
