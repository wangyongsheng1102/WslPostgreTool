using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WslPostgreTool.Models;

namespace WslPostgreTool.Services;

/// <summary>
/// 設定の永続化サービス
/// </summary>
public class ConfigService
{
    private const string ConfigFileName = "connections.json";
    private readonly string _configPath;

    public ConfigService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WslPostgreTool");
        
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }
        
        _configPath = Path.Combine(appDataPath, ConfigFileName);
    }

    /// <summary>
    /// 接続設定を保存
    /// </summary>
    public void SaveConnections(List<DatabaseConnection> connections)
    {
        try
        {
            var json = JsonSerializer.Serialize(connections, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            throw new Exception($"設定の保存に失敗しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 接続設定を読み込み
    /// </summary>
    public List<DatabaseConnection> LoadConnections()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return new List<DatabaseConnection>();
            }

            var json = File.ReadAllText(_configPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<DatabaseConnection>();
            }

            var connections = JsonSerializer.Deserialize<List<DatabaseConnection>>(json);
            return connections ?? new List<DatabaseConnection>();
        }
        catch (Exception ex)
        {
            throw new Exception($"設定の読み込みに失敗しました: {ex.Message}", ex);
        }
    }
}

