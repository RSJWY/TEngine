using UnityEditor;
using System.Diagnostics;

public class OpenPowerShellEditor
{
    // 在 Unity 菜单栏中添加一个按钮：Tools -> 打开 PowerShell (管理员 / 项目根目录)
    [MenuItem("Tools/AI工具/OpenPowerShell _F4")]
    public static void LaunchPowerShellAsAdmin()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            // 通过 ShellExecute 的 Verb 触发 UAC 提权
            Verb = "runas",
            UseShellExecute = true,
            // 提权时 WorkingDirectory 可能被忽略，这里用 -NoExit + cd 定位到项目根目录
            Arguments = $"-NoExit -Command \"Set-Location -LiteralPath '{UnityEngine.Application.dataPath}/..'\""
        };
        Process.Start(startInfo);
    }
}
