using System;
using UnityEngine;

[Serializable]
public class NodeData
{
    public string SceneID;         // 场景ID
    public string NodeID;          // 节点ID（如Node1）
    public float Pos_X;            // 相机位置X
    public float Pos_Y;            // 相机位置Y
    public float Pos_Z;            // 相机位置Z
    public float TranslateX;       // 全景平移X
    public float TranslateY;       // 全景平移Y
    public float TranslateZ;       // 全景平移Z
    public float ScaleX;           // 全景缩放X
    public float ScaleY;           // 全景缩放Y
    public float ScaleZ;           // 全景缩放Z
    public float RotateX;          // 全景旋转X
    public float RotateY;          // 全景旋转Y
    public float RotateZ;          // 全景旋转Z

    /// <summary>
    /// 从CSV行数据初始化（带异常处理）
    /// </summary>
    public NodeData(string[] csvRow)
    {
        if (csvRow == null || csvRow.Length == 0)
            throw new ArgumentNullException("csvRow", "CSV行数据不能为空");

        // 解析SceneID（第0列）
        SceneID = csvRow[0].Trim();
        if (string.IsNullOrEmpty(SceneID))
            throw new ArgumentException("SceneID不能为空（第0列）");

        // 解析NodeID（第1列）
        NodeID = csvRow[1].Trim();
        if (string.IsNullOrEmpty(NodeID))
            throw new ArgumentException("NodeID不能为空（第0列）");

        // 解析位置坐标（第2-4列）
        Pos_X = ParseFloat(csvRow[2], "Pos_X");
        Pos_Y = ParseFloat(csvRow[3], "Pos_Y");
        Pos_Z = ParseFloat(csvRow[4], "Pos_Z");

        // 解析平移参数（第5-7列）
        TranslateX = ParseFloat(csvRow[5], "TranslateX");
        TranslateY = ParseFloat(csvRow[6], "TranslateY");
        TranslateZ = ParseFloat(csvRow[7], "TranslateZ");

        // 解析缩放参数（第8-10列）
        ScaleX = ParseFloat(csvRow[8], "ScaleX");
        ScaleY = ParseFloat(csvRow[9], "ScaleY");
        ScaleZ = ParseFloat(csvRow[10], "ScaleZ");

        // 解析旋转参数（第11-13列）
        RotateX = ParseFloat(csvRow[11], "RotateX");
        RotateY = ParseFloat(csvRow[12], "RotateY");
        RotateZ = ParseFloat(csvRow[13], "RotateZ");
    }

    /// <summary>
    /// 安全解析float值（失败时抛异常，方便定位问题）
    /// </summary>
    private float ParseFloat(string value, string paramName)
    {
        if (float.TryParse(value, out float result))
            return result;

        throw new ArgumentException($"参数{paramName}解析失败，值：{value}");
    }
}