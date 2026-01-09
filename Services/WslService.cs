using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WslPostgreTool.Services;

/// <summary>
/// WSL インタラクションサービス
/// </summary>
public class WslService
{
    /// <summary>
    /// WSL ディストリビューションリストを取得
    /// </summary>
    public async Task<List<string>> GetWslDistributionsAsync()
    {
        var distributions = new List<string>();

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-l -q",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
                return distributions;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // 出力を解析してディストリビューション名を抽出
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Replace('\0', ' ').Trim()) ||
                    line.StartsWith("NAME", StringComparison.OrdinalIgnoreCase)) continue;
                var cleaned = Regex.Replace(line, @"[\0\s]", "");
                distributions.Add(cleaned.Trim());
            }
        }
        catch (Exception)
        {
            // WSL が利用できない場合は空リストを返す
        }

        return distributions;
    }
}

