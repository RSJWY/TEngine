using UnityEditor;
using System.Diagnostics;
using System.IO;

public class OpenPowerShellEditor
{
    // 在 Unity 菜单栏中添加一个按钮：Tools -> 打开 PowerShell (管理员 / 项目根目录)
    [MenuItem("Tools/AI工具/OpenPowerShell _F4")]
    public static void LaunchPowerShellAsAdmin()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));

        // 首选 Windows Terminal（即"右键开始菜单 -> 终端"打开的那个窗口），使用其默认配置文件（本机为 PowerShell 7）
        string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        string wtPath = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(wtPath) && TryLaunch(wtPath, $"-d \"{projectRoot}\""))
            return;

        // 回退：直接启动 PowerShell 7（朴素控制台窗口，不经过 Windows Terminal）
        string shellArgs = $"-NoExit -Command \"Set-Location -LiteralPath '{projectRoot}'\"";
        foreach (string pwshPath in GetPwshCandidates())
        {
            if (TryLaunch(pwshPath, shellArgs))
                return;
        }

        // 最后回退：Windows 自带的 PowerShell 5.1
        if (!TryLaunch("powershell.exe", shellArgs))
            UnityEngine.Debug.LogWarning("OpenPowerShell: 所有终端候选路径均启动失败。");
    }

    private static bool TryLaunch(string fileName, string arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                // 通过 ShellExecute 的 Verb 触发 UAC 提权
                Verb = "runas",
                UseShellExecute = true,
                // 提权时 WorkingDirectory 可能被忽略，这里用 -NoExit + cd 定位到项目根目录
                Arguments = arguments
            };
            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 用户在 UAC 弹窗点了"否"（错误码 1223）：不再尝试其他候选
            if (ex.NativeErrorCode == 1223)
            {
                UnityEngine.Debug.Log("OpenPowerShell: 用户取消了 UAC 提权。");
                return true;
            }
            return false;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> GetPwshCandidates()
    {
        string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

        // winget / Microsoft Store（MSIX）版的应用执行别名
        string appExecutionAlias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "pwsh.exe");
        if (File.Exists(appExecutionAlias))
            yield return appExecutionAlias;

        // MSI 默认安装位置（机器级 / 用户级）
        yield return @"C:\Program Files\PowerShell\7\pwsh.exe";
        yield return Path.Combine(localAppData, "Programs", "PowerShell", "7", "pwsh.exe");
    }
}
