using System;
using System.IO;
using UnityEngine;

[Serializable]
public class PathConfigData
{
    public string panoramaAssetBundleRoot = "ImageFolder/AssetBundles";
    public string recordingSaveRoot = "SaveData/Recordings";
    public string gazeHitDataRoot = "Reorder_GazeHit";
    public string reorderDataRoot = "Reorder";
    public string decisionPointExportRoot = "SaveData/DecisionPointViews";
}

public static class ProjectPathConfig
{
    private static PathConfigData config;
    private static bool isLoaded = false;

    private static string ConfigFilePath =>
        Path.Combine(
            Application.streamingAssetsPath,
            "Config",
            "PathConfig.json"
        );

    private static void LoadConfig()
    {
        if (isLoaded)
            return;

        isLoaded = true;

        if (!File.Exists(ConfigFilePath))
        {
            Debug.LogWarning(
                "[ProjectPathConfig] PathConfig.json not found. " +
                "Default paths will be used.\n" +
                ConfigFilePath
            );

            config = new PathConfigData();
            return;
        }

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            config = JsonUtility.FromJson<PathConfigData>(json);

            if (config == null)
                config = new PathConfigData();

            Debug.Log(
                "[ProjectPathConfig] Path configuration loaded:\n" +
                ConfigFilePath
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[ProjectPathConfig] Failed to load PathConfig.json:\n" +
                e.Message
            );

            config = new PathConfigData();
        }
    }

    private static string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Application.streamingAssetsPath;

        configuredPath = configuredPath.Trim().Trim('"');

        // 如果已经是绝对路径，直接使用
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        // 相对路径默认相对于 StreamingAssets
        return Path.GetFullPath(
            Path.Combine(
                Application.streamingAssetsPath,
                configuredPath
            )
        );
    }

    public static string PanoramaAssetBundleRoot
    {
        get
        {
            LoadConfig();
            return ResolvePath(config.panoramaAssetBundleRoot);
        }
    }

    public static string RecordingSaveRoot
    {
        get
        {
            LoadConfig();
            return ResolvePath(config.recordingSaveRoot);
        }
    }

    public static string GazeHitDataRoot
    {
        get
        {
            LoadConfig();
            return ResolvePath(config.gazeHitDataRoot);
        }
    }

    public static string ReorderDataRoot
    {
        get
        {
            LoadConfig();
            return ResolvePath(config.reorderDataRoot);
        }
    }

    public static string DecisionPointExportRoot
    {
        get
        {
            LoadConfig();
            return ResolvePath(config.decisionPointExportRoot);
        }
    }
}