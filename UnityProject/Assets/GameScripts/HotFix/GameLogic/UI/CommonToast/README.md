# CommonToast 通用弹窗组件

通用 Toast 弹窗 UI 系统，支持四种显示模式。**对话框模式下遮罩会全屏拦截背景点击，确保用户必须先点按钮才能继续操作。**

## 功能特性

1. **Toast 提示模式** - 纯信息显示，自动上浮渐隐消失，**不拦截背景点击**
2. **确认对话框** - 全屏遮罩拦截背景，单个确认按钮
3. **确认取消对话框** - 全屏遮罩拦截背景，确认和取消双按钮，**点击遮罩触发取消回调**
4. **确认取消附加对话框** - 全屏遮罩拦截背景，确认/取消/附加三按钮，**附加按钮由调用方通过 showExtra 决定是否显示**，用于需要三选一的业务场景

## 核心机制（重要）

- 对话框的拦截依赖 `Mask`（全屏 `Image`，`raycastTarget=true`）+ `DialogRoot` 上的 `CanvasGroup`（`blocksRaycasts=true`）。
- **根节点 `CommonToastUI` 的 `RectTransform` 必须是全屏拉伸（anchorMin=0,0 / anchorMax=1,1 / sizeDelta=0,0）**，否则 `Mask` 无法覆盖屏幕、无法拦截点击。
- `ToastHelper` 通过 `UIModule.ShowUIAsyncAwait<CommonToastUI>(data)` 传入 `ToastData`，窗口在 `OnRefresh` 中根据 `UserData` 驱动显示，避免旧实现里 `ShowUI` + `GetUIAsync` 回调的竞态和重复调用问题。
- Toast 模式下 `ToastRoot` 的 `CanvasGroup` 设置为 `interactable=false / blocksRaycasts=false`，完全放行点击。

## 使用方式

### 1. Toast 提示（推荐日常使用）

```csharp
// 基础用法
ToastHelper.ShowToast("操作成功");

// 自定义停留时长和上浮距离
ToastHelper.ShowToast("数据已保存", duration: 3f, moveDistance: 120f);
```

### 2. 确认对话框（单按钮）

```csharp
// 无回调
ToastHelper.ShowConfirm("请注意飞行安全");

// 带回调
ToastHelper.ShowConfirm("确定要重置设置吗？", () => 
{
    Debug.Log("用户确认了操作");
});

// 自定义按钮文本
ToastHelper.ShowConfirm("任务已完成", () => { }, confirmText: "知道了");
```

### 3. 确认取消对话框（双按钮）

```csharp
ToastHelper.ShowConfirmCancel(
    message: "确定要退出房间吗？",
    onConfirm: () => ExitRoom(),
    onCancel: () => Debug.Log("取消退出"),
    confirmText: "退出",
    cancelText: "留下"
);

// 禁用“点击遮罩=取消”（强制用户点按钮）
ToastHelper.ShowConfirmCancel("确定删除？", onConfirm: Delete, maskClickable: false);
```

### 4. 确认取消附加对话框（三按钮）

```csharp
// 典型场景：需要三选一的确认流程
ToastHelper.ShowConfirmCancelExtra(
    message: "有未保存的修改，是否保存？",
    onConfirm: () => SaveAndExit(),   // 确认
    onCancel: () => Debug.Log("用户取消"), // 取消
    onExtra: () => ExitWithoutSave(), // 附加
    confirmText: "保存",
    cancelText: "取消",
    extraText: "不保存"
);

// 临时只显示双按钮（关闭附加按钮）
ToastHelper.ShowConfirmCancelExtra(
    "确定删除？",
    onConfirm: Delete,
    extraText: "永久删除",
    showExtra: false  // 附加按钮不显示，等同双按钮
);
```

### 4. 异步等待窗口加载完成（高级）

```csharp
// 可 await，等待窗口加载并显示完成
await ToastHelper.ShowAsync(new ToastData
{
    mode = ToastMode.ConfirmCancel,
    message = "确定要退出游戏吗？",
    onConfirm = () => Application.Quit(),
    confirmText = "退出",
    cancelText = "取消",
});
```

### 5. 主动关闭

```csharp
ToastHelper.Close();
```

## 文件结构

```
Assets/GameScripts/HotFix/GameLogic/UI/CommonToast/
├── CommonToastUI.cs          # UI窗口逻辑类（含 ToastData / ToastMode）
├── ToastHelper.cs            # 静态辅助调用类
├── CommonToastUICreator.cs   # 预制体一键生成工具（Editor）
├── CommonToastTest.cs        # 测试脚本（挂任意GameObject，按T/C/B键测试）
└── README.md

Assets/AssetRaw/UI/
└── CommonToastUI.prefab      # UI预制体
```

## UI 预制体创建

### 方法一：使用自动工具（推荐）

1. 在 Unity 场景中任意 GameObject 上添加 `CommonToastUICreator` 组件
2. 在 Inspector 面板点击 **"Create CommonToastUI Prefab"** 按钮
3. 预制体自动创建到 `Assets/AssetRaw/UI/CommonToastUI.prefab`
4. 创建完成后删除该组件和脚本

### 方法二：手动创建 / 检查现有预制体

预制体结构：

```
CommonToastUI (Canvas + GraphicRaycaster + CanvasScaler)
  ├── RectTransform: anchorMin=(0,0) anchorMax=(1,1) sizeDelta=(0,0)  ← 必须全屏拉伸！
  │
  ├── ToastRoot (用于Toast模式)
  │   ├── CanvasGroup (interactable=false, blocksRaycasts=false)  ← 不拦截点击
  │   ├── Image (raycastTarget=false, 半透明背景)
  │   └── ToastMessage (TextMeshProUGUI, raycastTarget=false)
  │
  └── DialogRoot (用于对话框模式)
      ├── CanvasGroup (interactable=true, blocksRaycasts=true)  ← 拦截点击
      ├── Mask (Image, raycastTarget=true, 全屏拉伸, 0,0,0,0.7 半透明黑)
      │   └── Button (transition=None, onClick→点击遮罩触发取消)
      └── Panel (对话框面板)
          ├── Image (raycastTarget=false)
          ├── DialogMessage (TextMeshProUGUI, raycastTarget=false)
          └── ButtonGroup (HorizontalLayoutGroup)
              ├── CancelButton (Button + Image)
              │   └── Text (TextMeshProUGUI, raycastTarget=false)
              └── ConfirmButton (Button + Image)
                  └── Text (TextMeshProUGUI, raycastTarget=false)
```

### 组件要求（核对清单）

- **根节点 RectTransform**: `anchorMin=(0,0)`, `anchorMax=(1,1)`, `sizeDelta=(0,0)` —— **遮罩拦截的前提**
- **Canvas**: 根节点上，`renderMode=ScreenSpaceOverlay`（运行时由 UIWindow 强制 `overrideSorting=true`）
- **GraphicRaycaster**: 根节点上
- **ToastRoot.CanvasGroup**: `interactable=false`, `blocksRaycasts=false`
- **DialogRoot.CanvasGroup**: `interactable=true`, `blocksRaycasts=true`
- **Mask.Image**: `raycastTarget=true`，全屏拉伸

## 技术细节

- **框架集成**: 继承自 `UIWindow`，使用 `[Window(UILayer.Tips)]` 特性，层级高于普通UI
- **动画系统**: 使用 UniTask 实现淡入/停留/上浮淡出（Toast）和淡入（Dialog）
- **生命周期管理**: `OnDestroy` 自动取消动画与计时器，清空回调引用，防止内存泄漏
- **多次调用**: 通过 `UIModule` 复用窗口实例，`OnRefresh` 重新驱动显示，避免重复加载
- **遮罩点击**: ConfirmCancel 模式下点击遮罩触发 `onCancel`；Confirm 模式遮罩 Button 禁用，只能点确认

## 注意事项

1. 确保项目已导入 **TextMeshPro** 与 **UniTask** 包
2. Toast 模式会在动画结束后自动关闭窗口
3. Dialog 模式点击按钮（或可点击遮罩）后自动关闭窗口
4. 多次显示会自动覆盖前一次内容（复用同一窗口实例）
5. **若发现遮罩挡不住背景点击，首先检查根节点 RectTransform 是否全屏拉伸**

## 示例场景

```csharp
// 场景1：保存成功提示
public void OnSaveButtonClick()
{
    SaveData();
    ToastHelper.ShowToast("保存成功");
}

// 场景2：退出确认
public void OnExitButtonClick()
{
    ToastHelper.ShowConfirmCancel(
        "确定要退出吗？未保存的数据将丢失",
        onConfirm: () => Application.Quit(),
        confirmText: "退出",
        cancelText: "取消"
    );
}

// 场景3：任务完成通知
public void OnMissionComplete()
{
    ToastHelper.ShowConfirm(
        "恭喜完成训练任务！",
        onConfirm: () => ReturnToMainMenu(),
        confirmText: "返回主菜单"
    );
}
```
