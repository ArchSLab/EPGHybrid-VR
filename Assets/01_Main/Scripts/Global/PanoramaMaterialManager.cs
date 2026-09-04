using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.Collections.Generic; // 新增：用于字典缓存

/// <summary>
/// 管理全景图加载与材质参数更新（适配Unity 2020.3）
/// </summary>
public class PanoramaMaterialManager : MonoBehaviour
{
    [Header("基础配置")]
    public Material nodeGeneralMaterial; // 引用Node_General材质
    public string panoramaExt = ".jpg";  // 全景图格式（如.png/.jpg）
    public string imgFolderPath = "ImageFolder/"; // 全景图相对StreamingAssets的路径

    [Header("Shader属性名（需与材质完全匹配！）")]
    public string mainTexProp = "_MainTex";       // MainTex属性名
    public string translateXProp = "_TranslateX"; // TranslateX属性名
    public string translateYProp = "_TranslateY"; // TranslateY属性名
    public string translateZProp = "_TranslateZ"; // TranslateZ属性名
    public string scaleXProp = "_ScaleX";         // ScaleX属性名
    public string scaleYProp = "_ScaleY";         // ScaleY属性名
    public string scaleZProp = "_ScaleZ";         // ScaleZ属性名
    public string rotateXProp = "_RotateX";       // RotateX属性名
    public string rotateYProp = "_RotateY";       // RotateY属性名
    public string rotateZProp = "_RotateZ";       // RotateZ属性名

    public string sceneFolder = "";

    private Dictionary<string, Texture2D> _panoramaCache = new Dictionary<string, Texture2D>();

    [Header("AssetBundle配置")]
    public string abRootPath;
    public string abFolderPath = ""; // 清空子目录（或根据需要填写）
    public string abExt = ".bundle"; // 注意：打包时用的是.bundle扩展名（见PanoramaABBuilder）
    private Dictionary<string, AssetBundle> _abCache = new Dictionary<string, AssetBundle>();

    /// <summary>
    /// 单例实例（方便其他脚本调用）
    /// </summary>
    public static PanoramaMaterialManager Instance { get; private set; }

    public List<string> GetCachedNodeIDs()
    {
        return new List<string>(_panoramaCache.Keys);
    }

    private void Awake()
    {

        abRootPath = ProjectPathConfig.PanoramaAssetBundleRoot;

        // 关键修改：确保实例跨场景存在
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // 新增：使物体在场景切换时不被销毁

        if (nodeGeneralMaterial == null)
            Debug.LogError("PanoramaMaterialManager: 请引用Node_General材质！");
    }

    private void OnDestroy()
    {
        // 卸载所有AB包
        foreach (var ab in _abCache.Values)
        {
            ab.Unload(true);
        }
        _abCache.Clear();
        _panoramaCache.Clear();
    }

    // 新增：预加载指定节点的全景图
    //public IEnumerator PreloadPanorama(string nodeID, string sceneID)
    //{
    //    // 原键：string cacheKey = nodeID;
    //    string cacheKey = $"{sceneID}_{nodeID}"; // 新键：场景ID+节点ID（如Scene1_Node1）
    //    if (_panoramaCache.ContainsKey(cacheKey))
    //    {
    //        yield break; // 已缓存则跳过
    //    }

    //    // 2. 生成与UpdatePanorama完全一致的场景文件夹名（Scene+sceneID，如"Scene1"）
    //    string sceneFolder = $"Scene{sceneID}"; // 关键：与UpdatePanorama的sceneFolder生成逻辑统一

    //    // 3. 生成正确的图片文件名（Node+nodeID+后缀，如"Node1.jpg"）
    //    string imgFileName = $"{nodeID}{panoramaExt}"; // 关键：补充"Node"前缀，与实际图片名匹配

    //    // 4. 使用Path.Combine拼接路径（自动处理斜杠问题，更可靠）
    //    string imgFullPath = Path.Combine(Application.streamingAssetsPath, imgFolderPath, sceneFolder, imgFileName);

    //    //// 打印路径日志，方便调试
    //    //Debug.Log($"📥 开始预加载：{imgFullPath}（缓存键：{cacheKey}）");

    //    // 5. 加载纹理（复用原有逻辑）
    //    UnityWebRequest req = UnityWebRequestTexture.GetTexture(imgFullPath);
    //    yield return req.SendWebRequest();

    //    // 错误处理
    //    if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
    //    {
    //        Debug.LogError($"❌ 预加载失败：{req.error}，路径：{imgFullPath}");
    //        yield break;
    //    }

    //    // 解析纹理
    //    byte[] textureData = req.downloadHandler.data;
    //    Texture2D panoramaTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
    //    bool loadSuccess = panoramaTexture.LoadImage(textureData);
    //    if (!loadSuccess)
    //    {
    //        Debug.LogError($"❌ 预加载解析失败：{imgFileName}");
    //        yield break;
    //    }

    //    // 设置纹理属性
    //    panoramaTexture.Compress(false);
    //    panoramaTexture.wrapMode = TextureWrapMode.Clamp;
    //    panoramaTexture.filterMode = FilterMode.Bilinear;

    //    // 6. 存入缓存（键与UpdatePanorama完全一致）
    //    _panoramaCache[cacheKey] = panoramaTexture;
    //    //Debug.Log($"✅ 预加载完成并缓存：{cacheKey}（路径：{imgFullPath}）");

    //}

    public IEnumerator PreloadPanorama(string nodeID, string sceneID)
    {
        string cacheKey = $"{sceneID}_{nodeID}"; // 保持原缓存键格式
        if (_panoramaCache.ContainsKey(cacheKey))
        {
            yield break; // 已缓存则跳过
        }

        // 1. 构建AB包路径（关键修复：从D盘加载，而非StreamingAssets）
        string sceneFolder = $"Scene{sceneID}";
        string abName = $"{sceneFolder.ToLower()}{abExt}"; // 如"scene1.bundle"
                                                           // 拼接D盘完整路径（含平台目录，与打包输出路径匹配）
        string abFullPath = Path.Combine(abRootPath, abName);
        string assetName = nodeID;

        // 2. 加载AB包（后续逻辑不变，仅修改路径）
        AssetBundle ab;
        if (!_abCache.TryGetValue(abName, out ab))
        {
            // 从本地文件加载（推荐用LoadFromFile，比UnityWebRequest更高效，避免HTTP请求错误）
            ab = AssetBundle.LoadFromFile(abFullPath);
            if (ab == null)
            {
                Debug.LogError($"❌ AB包加载失败：路径不存在或格式错误，路径：{abFullPath}");
                yield break;
            }
            _abCache[abName] = ab;
            Debug.Log($"✅ 加载并缓存AB包：{abName}");
        }

        // 3. 从AB包加载纹理
        Texture2D panoramaTexture = ab.LoadAsset<Texture2D>(assetName);
        if (panoramaTexture == null)
        {
            Debug.LogError($"❌ 从AB包加载纹理失败：{assetName}（AB包：{abName}）");
            yield break;
        }

        // 4. 纹理属性设置（保持原有逻辑）
        panoramaTexture.Compress(false);
        panoramaTexture.wrapMode = TextureWrapMode.Clamp;
        panoramaTexture.filterMode = FilterMode.Bilinear;

        // 5. 存入纹理缓存
        _panoramaCache[cacheKey] = panoramaTexture;
        Debug.Log($"✅ 预加载AB纹理完成：{cacheKey}（资源：{assetName}）");
    }


    /// <summary>
    /// 异步更新全景图与材质参数
    /// </summary>
    public IEnumerator UpdatePanorama(string targetNodeID, string sceneID)
    {
        // 👇 这个 compositeKey 只用于查 CSV 数据（坐标、旋转等参数），和缓存无关！
        string compositeKey = $"{sceneID}_{targetNodeID}";

        if (!CSVReader.AllNodeData.ContainsKey(compositeKey))
        {
            Debug.LogError($"UpdatePanorama失败：节点{compositeKey}不存在于CSV数据中");
            yield break;
        }

        NodeData node = CSVReader.AllNodeData[compositeKey];
        Texture2D panoramaTexture;

        string cacheKey = $"{sceneID}_Node{targetNodeID}"; // 新键

        //Debug.Log($"[UpdatePanorama] 使用缓存键: {cacheKey}");

        if (_panoramaCache.TryGetValue(cacheKey, out panoramaTexture))
        {
            Debug.Log($"✅ 缓存命中：{cacheKey}"); // 命中就直接用，不加载！
        }
        else
        {
         

            // 缓存未命中时从AB包加载（替代原临时文件加载逻辑）
            string sceneFolder = $"Scene{sceneID}";
            string abName = $"{sceneFolder.ToLower()}{abExt}";
            string abFullPath = Path.Combine(abRootPath, abName);
            string assetName = targetNodeID;

            // 加载AB包（复用缓存逻辑）
            AssetBundle ab;
            if (!_abCache.TryGetValue(abName, out ab))
            {
                UnityWebRequest abReq = UnityWebRequestAssetBundle.GetAssetBundle(abFullPath);
                yield return abReq.SendWebRequest();

                if (abReq.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"❌ 临时加载AB包失败：{abReq.error}，路径：{abFullPath}");
                    yield break;
                }

                ab = DownloadHandlerAssetBundle.GetContent(abReq);
                _abCache[abName] = ab;
            }

            // 从AB包加载纹理
            panoramaTexture = ab.LoadAsset<Texture2D>(assetName);
            if (panoramaTexture == null)
            {
                Debug.LogError($"❌ 临时加载AB纹理失败：{assetName}（AB包：{abName}）");
                yield break;
            }

            // 纹理属性设置
            panoramaTexture.Compress(false);
            panoramaTexture.wrapMode = TextureWrapMode.Clamp;
            panoramaTexture.filterMode = FilterMode.Bilinear;

            // 存入缓存
            _panoramaCache[cacheKey] = panoramaTexture;
            Debug.Log($"✅ 临时加载AB纹理完成：{cacheKey}");
        }


        // 验证材质属性是否存在
        if (!nodeGeneralMaterial.HasProperty(mainTexProp))
        {
            Debug.LogError($"材质缺少属性：{mainTexProp}，请检查Shader属性名配置");
            yield break;
        }

        nodeGeneralMaterial.SetTexture(mainTexProp, panoramaTexture);

        // 更新材质变换参数（平移/缩放/旋转）
        SetMaterialProperty(translateXProp, node.TranslateX);
        SetMaterialProperty(translateYProp, node.TranslateY);
        SetMaterialProperty(translateZProp, node.TranslateZ);
        SetMaterialProperty(scaleXProp, node.ScaleX);
        SetMaterialProperty(scaleYProp, node.ScaleY);
        SetMaterialProperty(scaleZProp, node.ScaleZ);
        SetMaterialProperty(rotateXProp, node.RotateX);
        SetMaterialProperty(rotateYProp, node.RotateY);
        SetMaterialProperty(rotateZProp, node.RotateZ);
    }


    /// <summary>
    /// 安全设置材质Float参数（带错误检测）
    /// </summary>
    private void SetMaterialProperty(string propertyName, float value)
    {
        if (nodeGeneralMaterial.HasProperty(propertyName))
        {
            nodeGeneralMaterial.SetFloat(propertyName, value);
        }
        else
        {
            Debug.LogWarning($"材质属性不存在：{propertyName}，参数{value}未生效");
        }
    }

    /// <summary>
    /// 清理指定场景的AB包缓存
    /// </summary>
    /// <param name="sceneID">场景ID（如"1"）</param>
    public void ClearSceneABCache(string sceneID)
    {
        string abName = $"scene{sceneID.ToLower()}.bundle"; // 原逻辑不变，兼容现有AB包命名
        if (_abCache.ContainsKey(abName))
        {
            _abCache[abName].Unload(true); // 卸载AB包并释放所有资源
            _abCache.Remove(abName);
            Debug.Log($"清理场景[{sceneID}]的AB包缓存：{abName}");
        }
        else
        {
            Debug.Log($"场景[{sceneID}]无对应的AB包缓存：{abName}");
        }
    }

    // 配套：PanoramaMaterialManager.cs 中完善 ClearSceneCache 接口（增加返回值便于日志统计）
    public int ClearSceneCache(string sceneID)
    {
        if (string.IsNullOrEmpty(sceneID))
        {
            Debug.LogError("场景ID为空，无法清理缓存！");
            return 0;
        }

        List<string> keysToRemove = new List<string>();
        // 兼容两种缓存键格式："场景ID_NodeX" 和 "场景ID_X"（X为数字）
        foreach (var key in _panoramaCache.Keys)
        {
            // 匹配以 "场景ID_" 开头的所有键（无论后续格式）
            if (key.StartsWith($"{sceneID}_"))
            {
                keysToRemove.Add(key);
            }
        }

        // 卸载纹理并移除缓存
        int clearedCount = 0;
        foreach (var key in keysToRemove)
        {
            if (_panoramaCache.TryGetValue(key, out Texture2D texture))
            {
                Resources.UnloadAsset(texture); // 卸载纹理资源
                clearedCount++;
            }
            _panoramaCache.Remove(key);
        }

        Debug.Log($"清理场景[{sceneID}]的全景图缓存，共移除 {clearedCount} 个纹理");
        return clearedCount;
    }

    // PanoramaMaterialManager.cs
    public void ForceClearAllCache()
    {
        // 清空纹理缓存
        foreach (var texture in _panoramaCache.Values)
        {
            if (texture != null)
            {
                Resources.UnloadAsset(texture);
            }
        }
        _panoramaCache.Clear();
        Debug.Log("[缓存重置] 所有纹理缓存已清空");

        // 清空AB包缓存
        foreach (var ab in _abCache.Values)
        {
            if (ab != null)
            {
                ab.Unload(true);
            }
        }
        _abCache.Clear();
        Debug.Log("[缓存重置] 所有AB包缓存已清空");
    }
}
