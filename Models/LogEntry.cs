using CommunityToolkit.Mvvm.ComponentModel;

namespace WslPostgreTool.Models;

/// <summary>
/// 日志条目模型，支持分级和颜色
/// </summary>
public enum LogLevel
{
    Info,      // 信息 - 蓝色
    Warning,   // 警告 - 黄色
    Error,     // 错误 - 红色
    Success    // 成功 - 绿色
}

public partial class LogEntry : ObservableObject
{
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private LogLevel _level = LogLevel.Info;

    [ObservableProperty]
    private string _timestamp = string.Empty;

    public LogEntry(string message, LogLevel level = LogLevel.Info)
    {
        Message = message;
        Level = level;
        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

