using UnityEditor;
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using UnityEngine;

public class OpenPowerShellEditor
{
    // Windows Terminal 官方"默认终端应用程序"委派 GUID（微软文档公开值）
    // DelegationConsole = Terminal 包内 OpenConsole，DelegationTerminal = Windows Terminal
    private const string DelegationConsoleGuid = "{2EACA947-7F5F-4CFA-BA87-8F7FBEEFBE69}";
    private const string DelegationTerminalGuid = "{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}";

    // Tools -> AI工具 -> Opencode
    // F4 快捷键
    [MenuItem("Tools/AI工具/Opencode _F4")]
    public static void LaunchPowerShellAsAdmin()
    {
        string projectPath = Directory.GetParent(Application.dataPath).FullName;

        // PowerShell 7
        string pwshPath = FindPowerShell7();

        if (string.IsNullOrEmpty(pwshPath))
        {
            UnityEngine.Debug.LogError(
                "没有找到 PowerShell 7 (pwsh.exe)"
            );
            return;
        }

        // 若装有 Windows Terminal，把当前用户"默认终端应用程序"设为 Terminal。
        // 之后控制台窗口经 ConPTY 委派由 Terminal 渲染（含 runas 提权窗口），
        // 而 pwsh 仍是 Unity 直接启动的进程 —— 不经过 wt.exe 串链。
        if (!string.IsNullOrEmpty(FindWindowsTerminal()))
        {
            SetDefaultTerminalToWindowsTerminal();
        }

        // 关键：必须由 Unity 直接启动 pwsh。
        // 加密文件的透明解密授权跟随 Unity 的进程链，
        // 若先启动 wt.exe 再由 Terminal 拉起 pwsh，链路断开，只能读到密文。
        string arguments =
            $"-NoExit " +
            $"-Command \"Set-Location -LiteralPath '{EscapePowerShellPath(projectPath)}'\"";

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = pwshPath,
            Arguments = arguments,
            UseShellExecute = true,

            // 管理员权限
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                UnityEngine.Debug.LogWarning(
                    "用户取消了管理员权限请求。"
                );
            }
            else
            {
                UnityEngine.Debug.LogException(ex);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
        }
    }

    /// <summary>
    /// 将当前用户"默认终端应用程序"设为 Windows Terminal（HKCU\Console\%%Startup）
    /// </summary>
    private static void SetDefaultTerminalToWindowsTerminal()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Console\%%Startup"))
            {
                key.SetValue("DelegationConsole", DelegationConsoleGuid);
                key.SetValue("DelegationTerminal", DelegationTerminalGuid);
            }
        }
        catch (Exception ex)
        {
            // 设置失败不阻断启动，只是窗口退回传统控制台样式
            UnityEngine.Debug.LogWarning(
                $"设置默认终端为 Windows Terminal 失败（不影响启动）: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// 查找 Windows Terminal
    /// </summary>
    private static string FindWindowsTerminal()
    {
        string[] paths =
        {
            Environment.ExpandEnvironmentVariables(
                @"%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe"
            ),

            @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_*\wt.exe"
        };

        foreach (string path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        // PATH
        string pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                string wt = Path.Combine(
                    dir.Trim(),
                    "wt.exe"
                );

                if (File.Exists(wt))
                    return wt;
            }
        }

        return null;
    }

    /// <summary>
    /// 查找 PowerShell 7
    /// </summary>
    private static string FindPowerShell7()
    {
        string[] paths =
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",

            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
                ),
                @"Programs\PowerShell\7\pwsh.exe"
            )
        };

        foreach (string path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        string pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                string pwsh = Path.Combine(
                    dir.Trim(),
                    "pwsh.exe"
                );

                if (File.Exists(pwsh))
                    return pwsh;
            }
        }

        return null;
    }

    private static string EscapePowerShellPath(string path)
    {
        return path.Replace("'", "''");
    }
}
