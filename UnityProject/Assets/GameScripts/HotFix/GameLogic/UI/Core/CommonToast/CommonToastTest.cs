using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// CommonToast测试脚本 - 挂载到场景任意GameObject上测试
    /// 使用方法：
    /// 1. 在场景中创建空GameObject
    /// 2. 添加此脚本
    /// 3. 运行游戏后按对应按键测试：
    ///    - T键：显示Toast提示
    ///    - C键：显示确认对话框
    ///    - B键：显示确认取消对话框
    /// </summary>
    public class CommonToastTest : MonoBehaviour
    {
        private void Update()
        {
            // 按T键测试Toast
            if (Input.GetKeyDown(KeyCode.T))
            {
                Debug.Log("测试Toast提示");
                ToastHelper.ShowToast("这是一条Toast提示消息！", 2f);
            }

            // 按C键测试确认对话框
            if (Input.GetKeyDown(KeyCode.C))
            {
                Debug.Log("测试确认对话框");
                ToastHelper.ShowConfirm(
                    "确定要执行此操作吗？",
                    onConfirm: () => Debug.Log("用户点击了确认"),
                    confirmText: "确定"
                );
            }

            // 按B键测试确认取消对话框
            if (Input.GetKeyDown(KeyCode.B))
            {
                Debug.Log("测试确认取消对话框");
                ToastHelper.ShowConfirmCancel(
                    "确定要退出游戏吗？",
                    onConfirm: () => Debug.Log("用户点击了确认"),
                    onCancel: () => Debug.Log("用户点击了取消"),
                    confirmText: "退出",
                    cancelText: "取消"
                );
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("=== CommonToast测试面板 ===");
            GUILayout.Space(10);

            if (GUILayout.Button("显示Toast提示 (或按T键)", GUILayout.Height(40)))
            {
                ToastHelper.ShowToast("操作成功！", 2f);
            }

            if (GUILayout.Button("显示确认对话框 (或按C键)", GUILayout.Height(40)))
            {
                ToastHelper.ShowConfirm(
                    "确定要重置设置吗？",
                    onConfirm: () => Debug.Log("确认重置"),
                    confirmText: "确定"
                );
            }

            if (GUILayout.Button("显示确认取消对话框 (或按B键)", GUILayout.Height(40)))
            {
                ToastHelper.ShowConfirmCancel(
                    "确定要删除此项吗？",
                    onConfirm: () => Debug.Log("确认删除"),
                    onCancel: () => Debug.Log("取消删除"),
                    confirmText: "删除",
                    cancelText: "取消"
                );
            }

            GUILayout.EndArea();
        }
    }
}
