using UnityEngine;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 多场景全景图参数测试脚本（适配Unity 2020.3，支持Scene1/Scene2/Scene3等）
/// 自动识别当前场景，动态加载对应Node的全景图与CSV参数
/// 当前版本：改为屏幕按钮切换Node，不再依赖键盘输入
/// </summary>
public class UniversalPanoramaTest : MonoBehaviour
{
    [Header("通用测试配置")]
    public Material targetMaterial;                  // 需赋值Node_General材质（与PanoramaMaterialManager一致）
    public string csvRelativePath = "ReadData/NodeInfo.csv"; // CSV相对StreamingAssets路径
    public string imgFolderRoot = "ImageFolder/";   // 全景图根路径（相对StreamingAssets）
    public string panoramaExt = ".jpg";             // 全景图格式（与PanoramaMaterialManager一致）

    [Header("GUI配置")]
    public int buttonColumns = 4;                   // 每行按钮数
    public float panelWidth = 320f;                 // 面板宽度
    public float panelHeight = 420f;                // 面板高度
    public float buttonWidth = 60f;                 // 按钮宽度
    public float buttonHeight = 32f;                // 按钮高度
    public float buttonSpacing = 8f;                // 按钮间距

    private string currentSceneID;                  // 自动识别的当前场景ID（如"1"/"2"/"3"）
    private int currentNodeID = 1;                  // 当前Node编号
    private Material defaultMaterial;               // 默认材质备份
    private bool isCSVLoaded = false;               // CSV加载状态标记
    private int currentSceneMaxNodeID;              // 当前场景的最大Node编号（从CSV自动获取）
    private Vector2 scrollPos = Vector2.zero;       // 按钮面板滚动位置
    private bool isPanelMinimized = false;
    public bool showGUI = true;
    public Rect panelRect = new Rect(20f, 20f, 420f, 520f);

    [Header("Signage Node Variant")]
    public bool switchSignageByNode = true;
    public Transform signageRoot;
    public string signageRootName = "Signage";

    [Header("Camera Follow Node")]
    public bool moveCameraWithNode = true;
    public Camera controlledCamera;
    public Vector3 cameraPositionOffset = Vector3.zero;
    public bool rotateCameraWithNode = false;
    public Vector3 cameraRotationOffsetEuler = Vector3.zero;

    // Shader属性名（与PanoramaMaterialManager完全一致，确保参数匹配）
    private const string mainTexProp = "_MainTex";
    private const string translateXProp = "_tX";
    private const string translateYProp = "_tY";
    private const string translateZProp = "_tZ";
    private const string scaleXProp = "_sX";
    private const string scaleYProp = "_sY";
    private const string scaleZProp = "_sZ";
    private const string rotateXProp = "_rX";
    private const string rotateYProp = "_rY";
    private const string rotateZProp = "_rZ";

    private Coroutine activePanoramaLoadCoroutine = null;

    private void Awake()
    {
        // 1. 校验材质赋值
        if (targetMaterial == null)
        {
            Debug.LogError("【全景测试脚本】请为 targetMaterial 赋值 Node_General 材质！");
            enabled = false;
            return;
        }

        // 2. 自动识别当前场景ID（从场景名提取，如"Scene1"→"1"）
        if (!TryGetSceneIDFromSceneName(out currentSceneID))
        {
            Debug.LogError("【全景测试脚本】当前场景名不符合格式（需为 Scene1 / Scene2 等），脚本已禁用！");
            enabled = false;
            return;
        }

        Debug.Log($"【全景测试脚本】已识别当前场景：Scene{currentSceneID}");

        // 3. 备份默认材质（退出/销毁时恢复用）
        defaultMaterial = new Material(targetMaterial);

        // 4. 加载CSV数据
        StartCoroutine(LoadCSVData(() =>
        {
            currentSceneMaxNodeID = GetCurrentSceneMaxNodeID();
            Debug.Log($"【全景测试脚本】Scene{currentSceneID} 最大Node编号：{currentSceneMaxNodeID}");
        }));
    }

    private void Start()
    {
#if UNITY_EDITOR
        FocusGameViewDelayed();
#endif

        // 初始加载当前场景Node1的全景图和参数（处理CSV加载延迟）
        if (isCSVLoaded && currentSceneMaxNodeID > 0)
        {
            SwitchToNode(currentNodeID);
        }
        else
        {
            Debug.LogWarning($"【全景测试脚本】CSV未加载完成，延迟1秒初始化 Scene{currentSceneID}-Node1...");
            Invoke(nameof(InitCurrentSceneNode1), 1f);
        }
    }

#if UNITY_EDITOR
    private void FocusGameViewDelayed()
    {
        EditorApplication.delayCall += FocusGameView;
    }

    private void FocusGameView()
    {
        var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType != null)
        {
            EditorWindow.GetWindow(gameViewType).Focus();
            Debug.Log("【全景测试脚本】已自动聚焦 Game 视图");
        }
    }
#endif

    /// <summary>
    /// 初始化当前场景Node1（CSV加载延迟备用）
    /// </summary>
    private void InitCurrentSceneNode1()
    {
        if (isCSVLoaded)
        {
            currentSceneMaxNodeID = GetCurrentSceneMaxNodeID();
            if (currentSceneMaxNodeID > 0)
            {
                SwitchToNode(1);
                Debug.Log($"【全景测试脚本】初始化 Scene{currentSceneID}-Node1 完成");
            }
            else
            {
                Debug.LogError($"【全景测试脚本】Scene{currentSceneID} 在CSV中无有效Node数据，无法初始化！");
            }
        }
        else
        {
            Debug.LogError($"【全景测试脚本】CSV加载失败，Scene{currentSceneID} 初始化终止！");
        }
    }

    private void OnGUI()
    {
        if (!showGUI)
        {
            return;
        }

        if (!Application.isPlaying || !isCSVLoaded || currentSceneMaxNodeID <= 0)
        {
            return;
        }

        panelRect.width = panelWidth;
        panelRect.height = isPanelMinimized
            ? 42f
            : Mathf.Min(panelHeight, Screen.height - 40f);

        panelRect = GUI.Window(
            20260511,
            panelRect,
            DrawPanoramaWindow,
            ""
        );
    }

    private void DrawPanoramaWindow(int windowID)
    {
        float actualPanelHeight = panelRect.height;

        if (isPanelMinimized)
        {
            GUI.Box(new Rect(0, 0, panelWidth, 42f), $"Panorama Test - Scene{currentSceneID}");

            GUI.Label(
                new Rect(12f, 18f, panelWidth - 100f, 20f),
                $"Node{currentNodeID} / {currentSceneMaxNodeID}"
            );

            if (GUI.Button(new Rect(panelWidth - 82f, 14f, 70f, 24f), "Expand"))
            {
                isPanelMinimized = false;
            }

            GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));
            return;
        }

        GUI.Box(new Rect(0, 0, panelWidth, actualPanelHeight), $"Panorama Test - Scene{currentSceneID}");

        if (GUI.Button(new Rect(panelWidth - 82f, 6f, 70f, 24f), "Minimize"))
        {
            isPanelMinimized = true;
            GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));
            return;
        }

        GUI.Label(new Rect(15f, 30f, panelWidth - 110f, 22f),
            $"Current Node: Node{currentNodeID} / Total {currentSceneMaxNodeID}");

        if (GUI.Button(new Rect(15f, 58f, 80f, 28f), "Prev"))
        {
            if (currentNodeID > 1)
            {
                SwitchToNode(currentNodeID - 1);
            }
        }

        if (GUI.Button(new Rect(105f, 58f, 80f, 28f), "Next"))
        {
            if (currentNodeID < currentSceneMaxNodeID)
            {
                SwitchToNode(currentNodeID + 1);
            }
        }

        if (GUI.Button(new Rect(195f, 58f, 110f, 28f), "Back Node1"))
        {
            SwitchToNode(1);
        }

        float scrollX = 15f;
        float scrollY = 95f;
        float scrollWidth = panelWidth - 30f;
        float scrollHeight = actualPanelHeight - 110f;

        int columns = Mathf.Max(1, buttonColumns);
        int rows = Mathf.CeilToInt(currentSceneMaxNodeID / (float)columns);

        float contentWidth = columns * buttonWidth + (columns - 1) * buttonSpacing;
        float contentHeight = rows * buttonHeight + (rows - 1) * buttonSpacing;

        scrollPos = GUI.BeginScrollView(
            new Rect(scrollX, scrollY, scrollWidth, scrollHeight),
            scrollPos,
            new Rect(0, 0, Mathf.Max(scrollWidth - 20f, contentWidth), contentHeight)
        );

        for (int node = 1; node <= currentSceneMaxNodeID; node++)
        {
            int index = node - 1;
            int row = index / columns;
            int col = index % columns;

            float x = col * (buttonWidth + buttonSpacing);
            float y = row * (buttonHeight + buttonSpacing);

            bool isCurrent = node == currentNodeID;
            string buttonText = isCurrent ? $"Node{node}*" : $"Node{node}";

            bool oldEnabled = GUI.enabled;
            GUI.enabled = !isCurrent;

            if (GUI.Button(new Rect(x, y, buttonWidth, buttonHeight), buttonText))
            {
                SwitchToNode(node);
            }

            GUI.enabled = oldEnabled;
        }

        GUI.EndScrollView();

        GUI.DragWindow(new Rect(0, 0, panelRect.width, panelRect.height));
    }
    /// <summary>
    /// 对外统一的切换入口
    /// </summary>
    private void SwitchToNode(int nodeID)
    {
        if (!isCSVLoaded)
        {
            Debug.LogWarning("【全景测试脚本】CSV尚未加载完成，暂时无法切换节点");
            return;
        }

        if (nodeID < 1 || nodeID > currentSceneMaxNodeID)
        {
            Debug.LogError($"【全景测试脚本】无效Node编号：{nodeID}，请输入 1-{currentSceneMaxNodeID}");
            return;
        }

        currentNodeID = nodeID;

        string compositeKey = $"{currentSceneID}_{nodeID}";
        if (CSVReader.AllNodeData.ContainsKey(compositeKey))
        {
            NodeData nodeData = CSVReader.AllNodeData[compositeKey];
            ApplyNodeCameraTransform(nodeData);
            UpdateSignageForNode(nodeID);
        }

        UpdatePanoramaAndParams(currentNodeID);
        Debug.Log($"【全景测试脚本】已切换到 Scene{currentSceneID}-Node{currentNodeID}");
    }
    public void SwitchToNodeFromReplay(string nodeIDText)
    {
        if (string.IsNullOrEmpty(nodeIDText))
        {
            Debug.LogWarning("[Replay] NodeID is empty, cannot switch panorama.");
            return;
        }

        string digits = new string(nodeIDText.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(digits))
        {
            digits = nodeIDText.Trim();
        }

        int nodeID;
        if (!int.TryParse(digits, out nodeID))
        {
            Debug.LogWarning("[Replay] Cannot parse NodeID: " + nodeIDText);
            return;
        }

        SwitchToNode(nodeID);
    }

    /// <summary>
    /// 更新当前场景目标Node的全景图和材质参数
    /// </summary>
    private void UpdatePanoramaAndParams(int nodeID)
    {
        string compositeKey = $"{currentSceneID}_{nodeID}";
        if (!CSVReader.AllNodeData.ContainsKey(compositeKey))
        {
            Debug.LogError($"【全景测试脚本】Scene{currentSceneID}：未找到 Node{nodeID} 的CSV数据（键：{compositeKey}）");
            return;
        }

        NodeData nodeData = CSVReader.AllNodeData[compositeKey];
        Debug.Log($"【全景测试脚本】开始加载 Node{nodeID}：CSV参数已获取（TranslateX={nodeData.TranslateX}）");
        StartPanoramaLoad(nodeData);
    }

    private void StartPanoramaLoad(NodeData nodeData)
    {
        if (activePanoramaLoadCoroutine != null)
        {
            StopCoroutine(activePanoramaLoadCoroutine);
            activePanoramaLoadCoroutine = null;
        }

        activePanoramaLoadCoroutine = StartCoroutine(LoadPanoramaAndUpdateTracked(nodeData));
    }

    private IEnumerator LoadPanoramaAndUpdateTracked(NodeData nodeData)
    {
        yield return StartCoroutine(LoadPanoramaAndUpdate(nodeData));
        activePanoramaLoadCoroutine = null;
    }

    public IEnumerator SwitchToNodeAndWait(string nodeIDText)
    {
        if (string.IsNullOrEmpty(nodeIDText))
        {
            Debug.LogWarning("[Export] NodeID is empty, cannot switch panorama.");
            yield break;
        }

        string digits = new string(nodeIDText.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(digits))
        {
            digits = nodeIDText.Trim();
        }

        int nodeID;
        if (!int.TryParse(digits, out nodeID))
        {
            Debug.LogWarning("[Export] Cannot parse NodeID: " + nodeIDText);
            yield break;
        }

        if (!isCSVLoaded)
        {
            Debug.LogWarning("[Export] CSV is not loaded yet, cannot switch panorama.");
            yield break;
        }

        if (nodeID < 1 || nodeID > currentSceneMaxNodeID)
        {
            Debug.LogWarning("[Export] Invalid NodeID: " + nodeID);
            yield break;
        }

        string compositeKey = $"{currentSceneID}_{nodeID}";

        if (!CSVReader.AllNodeData.ContainsKey(compositeKey))
        {
            Debug.LogWarning("[Export] Cannot find node data: " + compositeKey);
            yield break;
        }

        currentNodeID = nodeID;

        NodeData nodeData = CSVReader.AllNodeData[compositeKey];

        ApplyNodeCameraTransform(nodeData);
        UpdateSignageForNode(nodeID);

        if (activePanoramaLoadCoroutine != null)
        {
            StopCoroutine(activePanoramaLoadCoroutine);
            activePanoramaLoadCoroutine = null;
        }

        yield return StartCoroutine(LoadPanoramaAndUpdate(nodeData));

        // Wait one extra frame so material and texture can be rendered correctly.
        yield return null;
        yield return new WaitForEndOfFrame();

        Debug.Log("[Export] Panorama ready: Scene" + currentSceneID + "-Node" + nodeID);
    }

    /// <summary>
    /// 加载全景图并更新材质
    /// </summary>
    private IEnumerator LoadPanoramaAndUpdate(NodeData nodeData)
    {
        // 1. 构建全景图路径
        string sceneFolder = $"Scene{currentSceneID}";
        string imgFileName = $"Node{nodeData.NodeID}{panoramaExt}";
        string imgFullPath = Path.Combine(Application.streamingAssetsPath, imgFolderRoot, sceneFolder, imgFileName);
        Debug.Log($"【全景测试脚本】尝试加载全景图：{imgFullPath}");

        // 2. 加载全景图纹理
        UnityWebRequest req = UnityWebRequestTexture.GetTexture(imgFullPath);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"【全景测试脚本】Scene{currentSceneID}-Node{nodeData.NodeID}：全景图加载失败，错误：{req.error}，路径：{imgFullPath}");
            yield break;
        }

        // 3. 解析纹理
        byte[] textureData = req.downloadHandler.data;
        Texture2D panoramaTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool loadSuccess = panoramaTexture.LoadImage(textureData);
        if (!loadSuccess)
        {
            Debug.LogError($"【全景测试脚本】Scene{currentSceneID}-Node{nodeData.NodeID}：全景图解析失败（文件：{imgFileName}）");
            yield break;
        }

        // 4. 设置纹理属性
        panoramaTexture.Compress(false);
        panoramaTexture.wrapMode = TextureWrapMode.Clamp;
        panoramaTexture.filterMode = FilterMode.Bilinear;

        // 5. 更新材质主纹理
        if (targetMaterial.HasProperty(mainTexProp))
        {
            targetMaterial.SetTexture(mainTexProp, panoramaTexture);
            Debug.Log($"【全景测试脚本】Node{nodeData.NodeID} 主纹理更新完成（属性：{mainTexProp}）");
        }
        else
        {
            Debug.LogError($"【全景测试脚本】材质缺少 {mainTexProp} 属性，无法更新全景图！");
        }

        // 6. 更新材质变换参数
        SetMaterialProperty(translateXProp, nodeData.TranslateX, "平移X");
        SetMaterialProperty(translateYProp, nodeData.TranslateY, "平移Y");
        SetMaterialProperty(translateZProp, nodeData.TranslateZ, "平移Z");
        SetMaterialProperty(scaleXProp, nodeData.ScaleX, "缩放X");
        SetMaterialProperty(scaleYProp, nodeData.ScaleY, "缩放Y");
        SetMaterialProperty(scaleZProp, nodeData.ScaleZ, "缩放Z");
        SetMaterialProperty(rotateXProp, nodeData.RotateX, "旋转X");
        SetMaterialProperty(rotateYProp, nodeData.RotateY, "旋转Y");
        SetMaterialProperty(rotateZProp, nodeData.RotateZ, "旋转Z");
    }

    /// <summary>
    /// 设置材质参数
    /// </summary>
    private void SetMaterialProperty(string propertyName, float value, string paramDesc)
    {
        if (targetMaterial.HasProperty(propertyName))
        {
            targetMaterial.SetFloat(propertyName, value);
        }
        else
        {
            Debug.LogWarning($"【全景测试脚本】材质属性不存在：{propertyName}（{paramDesc}），参数 {value} 未生效");
        }
    }

    /// <summary>
    /// 加载CSV数据
    /// </summary>
    private IEnumerator LoadCSVData(System.Action onLoadComplete)
    {
        yield return CSVReader.LoadCSV(csvRelativePath, () =>
        {
            isCSVLoaded = true;
            Debug.Log($"【全景测试脚本】CSV数据加载完成，共解析 {CSVReader.AllNodeData.Count} 个节点");
            onLoadComplete?.Invoke();
        });
    }

    /// <summary>
    /// 自动识别当前场景ID
    /// </summary>
    private bool TryGetSceneIDFromSceneName(out string sceneID)
    {
        sceneID = null;
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        if (!currentSceneName.StartsWith("Scene"))
        {
            Debug.LogError($"【全景测试脚本】当前场景名「{currentSceneName}」不符合格式，需以 \"Scene\" 开头（如 Scene1 / Scene2）");
            return false;
        }

        string numberPart = new string(currentSceneName.Skip("Scene".Length).TakeWhile(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(numberPart))
        {
            Debug.LogError($"【全景测试脚本】当前场景名「{currentSceneName}」格式错误，\"Scene\" 后需跟数字（如 Scene3）");
            return false;
        }

        sceneID = numberPart;
        return true;
    }

    /// <summary>
    /// 获取当前场景的最大Node编号
    /// </summary>
    private int GetCurrentSceneMaxNodeID()
    {
        int maxNodeID = 0;

        var currentSceneKeys = CSVReader.AllNodeData.Keys
            .Where(key => key.StartsWith($"{currentSceneID}_"))
            .ToList();

        if (currentSceneKeys.Count == 0)
        {
            Debug.LogWarning($"【全景测试脚本】CSV中未找到 Scene{currentSceneID} 的任何Node数据");
            return 0;
        }

        foreach (var key in currentSceneKeys)
        {
            string nodeIDStr = key.Split('_')[1];
            if (int.TryParse(nodeIDStr, out int nodeID) && nodeID > maxNodeID)
            {
                maxNodeID = nodeID;
            }
        }

        return maxNodeID;
    }

    /// <summary>
    /// 恢复材质默认参数
    /// </summary>
    private void RestoreDefaultMaterial()
    {
        if (defaultMaterial != null && targetMaterial != null)
        {
            targetMaterial.CopyPropertiesFromMaterial(defaultMaterial);
        }
    }

    /// <summary>
    /// 场景切换/脚本销毁时恢复默认材质
    /// </summary>
    private void OnDestroy()
    {
        RestoreDefaultMaterial();

        if (defaultMaterial != null)
        {
            Destroy(defaultMaterial);
        }
    }

    /// <summary>
    /// 切换场景时触发
    /// </summary>
    private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
    {
        if (scene.name == $"Scene{currentSceneID}")
        {
            RestoreDefaultMaterial();
            Debug.Log($"【全景测试脚本】Scene{currentSceneID} 已卸载，恢复默认材质");
        }
    }

    private void ApplyNodeCameraTransform(NodeData nodeData)
    {
        if (!moveCameraWithNode)
        {
            return;
        }

        if (controlledCamera == null)
        {
            controlledCamera = Camera.main;
        }

        if (controlledCamera == null)
        {
            Debug.LogWarning("【全景测试脚本】未找到可控制的 Camera，无法同步相机位置");
            return;
        }

        // NodeData 中 Translate 是材质平移参数，实际相机位置要取反。
        // 这和导图脚本中使用 -TranslateX/Y/Z 的逻辑一致。
        Vector3 cameraPos = new Vector3(
            -nodeData.TranslateX,
            -nodeData.TranslateY,
            -nodeData.TranslateZ
        );

        controlledCamera.transform.position = cameraPos + cameraPositionOffset;

        if (rotateCameraWithNode)
        {
            Quaternion baseRot = Quaternion.Euler(
                nodeData.RotateX,
                nodeData.RotateY,
                nodeData.RotateZ
            );

            controlledCamera.transform.rotation =
                baseRot * Quaternion.Euler(cameraRotationOffsetEuler);
        }
    }

    private void UpdateSignageForNode(int nodeID)
    {
        if (!switchSignageByNode)
        {
            return;
        }

        if (signageRoot == null)
        {
            GameObject rootObj = GameObject.Find(signageRootName);

            if (rootObj != null)
            {
                signageRoot = rootObj.transform;
            }
        }

        if (signageRoot == null)
        {
            Debug.LogWarning("【全景测试脚本】未找到 Signage 根物体，无法切换标识牌可见性");
            return;
        }

        string targetNodeName = "Node" + nodeID;

        foreach (Transform signage in signageRoot)
        {
            if (signage == null)
            {
                continue;
            }

            int childCount = signage.childCount;

            // 没有子物体：说明这是默认标识牌，始终显示
            if (childCount == 0)
            {
                signage.gameObject.SetActive(true);
                continue;
            }

            // 有子物体：父物体保留 active，但只显示当前 Node 对应子物体
            signage.gameObject.SetActive(true);

            bool hasCurrentNodeVariant = false;

            for (int i = 0; i < childCount; i++)
            {
                Transform child = signage.GetChild(i);

                bool isCurrent =
                    string.Equals(child.name, targetNodeName, System.StringComparison.OrdinalIgnoreCase);

                child.gameObject.SetActive(isCurrent);

                if (isCurrent)
                {
                    hasCurrentNodeVariant = true;
                }
            }

            // 如果这个标识牌有 Node 子版本，但没有当前 Node 对应版本，
            // 则整个标识牌不显示，避免错误版本参与导图/碰撞。
            if (!hasCurrentNodeVariant)
            {
                signage.gameObject.SetActive(false);
            }
        }

        Debug.Log("【全景测试脚本】已根据 Node" + nodeID + " 切换 Signage 可见性");
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }
}