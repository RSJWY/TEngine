using System;
using Cysharp.Threading.Tasks;

namespace GameLogic
{
    /// <summary>
    /// Toast辅助类 - 提供全局静态调用接口
    /// 内部异步等待窗口加载完成后，通过 OnRefresh 驱动显示，
    /// 避免旧实现里 ShowUI + GetUIAsync 回调的竞态与重复调用问题。
    /// </summary>
    public static class ToastHelper
    {
        /// <summary>
        /// 显示Toast提示（自动上浮消失，不拦截背景点击）
        /// </summary>
        /// <param name="message">提示文本</param>
        /// <param name="duration">停留时长（秒）</param>
        /// <param name="moveDistance">上浮距离（像素）</param>
        public static void ShowToast(string message, float duration = 2f, float moveDistance = 100f)
        {
            ShowAsync(new ToastData
            {
                mode = ToastMode.Toast,
                message = message,
                duration = duration,
                moveDistance = moveDistance,
            }).Forget();
            
        }

        /// <summary>
        /// 显示确认对话框（单按钮，背景被遮罩拦截）
        /// </summary>
        /// <param name="message">对话框消息</param>
        /// <param name="onConfirm">确认回调</param>
        /// <param name="confirmText">确认按钮文本</param>
        public static void ShowConfirm(string message, Action onConfirm = null, string confirmText = "确认")
        {
            ShowAsync(new ToastData
            {
                mode = ToastMode.Confirm,
                message = message,
                confirmText = confirmText,
                onConfirm = onConfirm,
                maskClickable = false,
            }).Forget();
        }

        /// <summary>
        /// 显示确认取消对话框（双按钮，背景被遮罩拦截，点击遮罩触发取消）
        /// </summary>
        /// <param name="message">对话框消息</param>
        /// <param name="onConfirm">确认回调</param>
        /// <param name="onCancel">取消回调</param>
        /// <param name="confirmText">确认按钮文本</param>
        /// <param name="cancelText">取消按钮文本</param>
        /// <param name="maskClickable">点击遮罩是否触发取消（默认true）</param>
        public static void ShowConfirmCancel(
            string message,
            Action onConfirm = null,
            Action onCancel = null,
            string confirmText = "确认",
            string cancelText = "取消",
            bool maskClickable = true)
        {
            ShowAsync(new ToastData
            {
                mode = ToastMode.ConfirmCancel,
                message = message,
                confirmText = confirmText,
                cancelText = cancelText,
                onConfirm = onConfirm,
                onCancel = onCancel,
                maskClickable = maskClickable,
            }).Forget();
        }

        /// <summary>
        /// 显示三按钮对话框（确认 + 取消 + 附加），背景被遮罩拦截，点击遮罩触发取消。
        /// 附加按钮由调用方决定是否显示，用于需要三选一的业务场景。
        /// </summary>
        /// <param name="message">对话框消息</param>
        /// <param name="onConfirm">确认回调</param>
        /// <param name="onCancel">取消回调（含点击遮罩）</param>
        /// <param name="onExtra">附加回调</param>
        /// <param name="confirmText">确认按钮文本</param>
        /// <param name="cancelText">取消按钮文本</param>
        /// <param name="extraText">附加按钮文本</param>
        /// <param name="showExtra">是否显示附加按钮（默认 true）</param>
        /// <param name="maskClickable">点击遮罩是否触发取消（默认true）</param>
        public static void ShowConfirmCancelExtra(
            string message,
            Action onConfirm = null,
            Action onCancel = null,
            Action onExtra = null,
            string confirmText = "确认",
            string cancelText = "取消",
            string extraText = "附加",
            bool showExtra = true,
            bool maskClickable = true)
        {
            ShowAsync(new ToastData
            {
                mode = ToastMode.ConfirmCancelExtra,
                message = message,
                confirmText = confirmText,
                cancelText = cancelText,
                extraText = extraText,
                showExtra = showExtra,
                onConfirm = onConfirm,
                onCancel = onCancel,
                onExtra = onExtra,
                maskClickable = maskClickable,
            }).Forget();
        }

        /// <summary>
        /// 异步显示Toast（可 await 等待窗口加载完成）
        /// </summary>
        public static async UniTask ShowAsync(ToastData data)
        {
            // ShowUIAsyncAwait 内部：已存在则 Pop/Push 并重新触发 OnRefresh；否则加载后触发
            await UIModule.Instance.ShowUIAsyncAwait<CommonToastUI>(data);
        }

        /// <summary>
        /// 关闭Toast窗口（如果存在）
        /// </summary>
        public static void Close()
        {
            if (UIModule.Instance.HasWindow<CommonToastUI>())
            {
                UIModule.Instance.CloseUI<CommonToastUI>();
            }
        }
    }
}
