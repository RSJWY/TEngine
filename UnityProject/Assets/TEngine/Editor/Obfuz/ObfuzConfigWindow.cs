using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
#if OBFUZ_INSTALLED
using ObfuzGarbageCodeType = Obfuz.Settings.GarbageCodeType;
using ObfuzGarbageTask = Obfuz.Settings.GarbageCodeGenerationTask;
using ObfuzPassType = Obfuz.ObfusPasses.ObfuscationPassType;
using ObfuzProxyMode = Obfuz.Settings.ProxyMode;
using ObfuzRuntimeType = Obfuz.Settings.RuntimeType;
using ObfuzSettingsAsset = Obfuz.Settings.ObfuzSettings;
using ObfuzMenu = Obfuz.Unity.ObfuzMenu;
#endif

namespace TEngine
{
    /// <summary>
    /// TEngine 混淆配置窗口：以中文界面集中编辑 ProjectSettings/Obfuz.asset（ObfuzSettings），
    /// 提供健康检查、快速预设、密钥/加密VM/垃圾代码生成入口。仅编辑配置，不改动 Obfuz 包本身。
    /// </summary>
    public class ObfuzConfigWindow : OdinEditorWindow
    {
        private const string MenuPath = "TEngine/Build/混淆配置窗口";
        private const double SaveDelaySeconds = 0.6;

        private static Color OkColor => new Color(0.45f, 0.85f, 0.45f);
        private static Color WarnColor => new Color(0.95f, 0.7f, 0.25f);
        private static Color ErrorColor => new Color(0.9f, 0.4f, 0.35f);
        private static Color MutedColor => new Color(0.65f, 0.65f, 0.65f);

#if OBFUZ_INSTALLED
        private ObfuzSettingsAsset S => ObfuzSettingsAsset.Instance;

        [MenuItem(MenuPath, false, 51)]
        public static void ShowWindow()
        {
            var window = GetWindow<ObfuzConfigWindow>();
            window.titleContent = new GUIContent("TEngine 混淆配置", EditorGUIUtility.IconContent("d_SceneViewVisibility").image);
            window.minSize = new Vector2(700, 640);
            window.Show();
        }

        protected override void OnImGUI()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label("配置存储于 ProjectSettings/Obfuz.asset，修改后自动保存", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("立即保存", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    FlushSave();
                }
                if (GUILayout.Button("官方设置页", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    FlushSave();
                    SettingsService.OpenProjectSettings("Project/Obfuz");
                }
            }
            GUILayout.EndHorizontal();

            SirenixEditorGUI.DrawThickHorizontalSeparator();
            base.OnImGUI();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            EditorApplication.update -= FlushSaveWhenReady;
            FlushSave();
        }

        #region 总览

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/状态")]
        [DisplayAsString, LabelText("ENABLE_OBFUZ 宏（热更 DLL 混淆链路）"), GUIColor(nameof(DefineColor))]
        [ShowInInspector, PropertyOrder(0)]
        private string DefineStatus => BuildDLLCommand.IsObfuzActive ? "开" : "关";

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/状态")]
        [DisplayAsString, LabelText("Player Build 自动混淆回调"), GUIColor(nameof(BuildCallbackColor))]
        [ShowInInspector, PropertyOrder(1)]
        private string BuildCallbackStatus => S.buildPipelineSettings.enable ? "开" : "关";

        private Color DefineColor => BuildDLLCommand.IsObfuzActive ? OkColor : MutedColor;
        private Color BuildCallbackColor => S.buildPipelineSettings.enable ? OkColor : MutedColor;

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/状态")]
        [InfoBox("切换宏会触发脚本重编译；热更 DLL 混淆在 BuildAndCopyDlls 时生效。", InfoMessageType.None)]
        [HorizontalGroup("Pages/总览/状态/Actions")]
        [Button("$ObfuzToggleLabel", ButtonSizes.Medium), GUIColor(nameof(ObfuzToggleColor))]
        [PropertyOrder(2)]
        private void ToggleObfuz()
        {
            BuildDLLCommand.SetObfuzSafe(!BuildDLLCommand.IsObfuzActiveSafe);
            QueueSave();
        }

        private string ObfuzToggleLabel => BuildDLLCommand.IsObfuzActive ? "关闭混淆" : "开启混淆";
        private Color ObfuzToggleColor => BuildDLLCommand.IsObfuzActive ? WarnColor : OkColor;

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/健康检查")]
        [ShowInInspector, ReadOnly, HideLabel, PropertyOrder(3)]
        [TableList(ShowIndexLabels = false, AlwaysExpanded = true, IsReadOnly = true)]
        [ListDrawerSettings(IsReadOnly = true, DraggableItems = false, HideAddButton = true, HideRemoveButton = true, NumberOfItemsPerPage = 20)]
        private List<HealthItem> HealthReport => BuildHealthReport();

        private sealed class HealthItem
        {
            [TableColumnWidth(80, Resizable = false)]
            [LabelText("级别")]
            [GUIColor("$LevelColor")]
            public string Level;

            [HideLabel]
            [GUIColor("$LevelColor")]
            public string Message;

            private Color LevelColor => Level == "错误"
                ? new Color(0.95f, 0.55f, 0.5f)
                : Level == "警告"
                    ? new Color(0.95f, 0.8f, 0.45f)
                    : new Color(0.75f, 0.75f, 0.75f);
        }

        private List<HealthItem> BuildHealthReport()
        {
            var report = new List<HealthItem>();
            void Add(string level, string message) => report.Add(new HealthItem { Level = level, Message = message });

            var secret = S.secretSettings;
            var vm = S.encryptionVMSettings;
            var passMask = S.obfuscationPassSettings.enabledPasses;
            bool encryptOn = passMask.HasFlag(ObfuzPassType.ConstEncrypt) || passMask.HasFlag(ObfuzPassType.FieldEncrypt);

            if (string.IsNullOrEmpty(secret.defaultStaticSecretKey) || secret.defaultStaticSecretKey == "Code Philosophy-Static")
            {
                Add("错误", "静态密钥仍为官方默认值，正式发布前必须替换（加密与密钥页）");
            }
            if (string.IsNullOrEmpty(secret.defaultDynamicSecretKey) || secret.defaultDynamicSecretKey == "Code Philosophy-Dynamic")
            {
                Add("错误", "动态密钥仍为官方默认值，正式发布前必须替换（加密与密钥页）");
            }
            if (string.IsNullOrEmpty(vm.codeGenerationSecretKey) || vm.codeGenerationSecretKey == "Obfuz")
            {
                Add("错误", "加密 VM 代码生成密钥仍为官方默认值，必须替换（加密与密钥页）");
            }
            if (!IsPowerOfTwo(vm.encryptionOpCodeCount) || vm.encryptionOpCodeCount < 64)
            {
                Add("错误", $"加密指令数 {vm.encryptionOpCodeCount} 无效：必须是 2 的幂且不小于 64（默认值为 256，建议不要超过 1024）");
            }
            else if (vm.encryptionOpCodeCount > 1024)
            {
                Add("警告", $"加密指令数 {vm.encryptionOpCodeCount} 为合法值，但官方建议不要超过 1024");
            }
            if (S.assemblySettings.assembliesToObfuscate == null || S.assemblySettings.assembliesToObfuscate.Length == 0)
            {
                Add("错误", "待混淆程序集列表为空（程序集页）");
            }
            if (encryptOn && !File.Exists(vm.codeOutputPath))
            {
                Add("警告", $"加密 VM 代码尚未生成：{vm.codeOutputPath}（加密与密钥页点击生成）");
            }
            if (encryptOn && !File.Exists(secret.staticSecretKeyOutputPath))
            {
                Add("警告", $"静态密钥文件不存在：{secret.staticSecretKeyOutputPath}");
            }
            if (encryptOn && !File.Exists(secret.dynamicSecretKeyOutputPath))
            {
                Add("警告", $"动态密钥文件不存在：{secret.dynamicSecretKeyOutputPath}");
            }
            if (passMask == (ObfuzPassType)(-1))
            {
                Add("警告", "混淆通道为全部启用（All），生产建议按成本显式选择通道（混淆通道页有预设）");
            }
            if (!S.assemblySettings.obfuscateObfuzRuntime)
            {
                Add("警告", "未混淆 Obfuz.Runtime，官方强烈建议开启以避免暴露解密基础设施");
            }
            if (S.polymorphicDllSettings.enable &&
                (string.IsNullOrEmpty(S.polymorphicDllSettings.codeGenerationSecretKey) || S.polymorphicDllSettings.codeGenerationSecretKey == "obfuz-polymorphic-key"))
            {
                Add("警告", "多态 DLL 密钥仍为官方默认值，启用多态 DLL 前必须替换（高级页）");
            }
            if (secret.randomSeed == 0)
            {
                Add("提示", "随机种子为 0：可按版本设置非零种子提升产物差异（可选）");
            }
            if (File.Exists(S.symbolObfusSettings.GetSymbolMappingFile()))
            {
                Add("提示", $"已存在符号映射文件：{S.symbolObfusSettings.GetSymbolMappingFile()}（务必纳入版本管理）");
            }
            else
            {
                Add("提示", "尚无符号映射文件：首次混淆后生成，用于堆栈还原与稳定改名");
            }
            if (report.All(item => item.Level == "提示"))
            {
                Add("提示", "未发现阻塞性问题");
            }
            return report;
        }

        private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/快捷操作")]
        [HorizontalGroup("Pages/总览/快捷操作/Row1")]
        [Button("生成加密 VM", ButtonSizes.Medium)]
        private void GenerateVmButton()
        {
            FlushSave();
            ObfuzMenu.GenerateEncryptionVM();
        }

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/快捷操作")]
        [HorizontalGroup("Pages/总览/快捷操作/Row1")]
        [Button("生成密钥文件", ButtonSizes.Medium)]
        private void GenerateSecretButton()
        {
            FlushSave();
            ObfuzMenu.SaveSecretFile();
        }

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/快捷操作")]
        [HorizontalGroup("Pages/总览/快捷操作/Row1")]
        [Button("打开混淆产物目录", ButtonSizes.Medium)]
        private void OpenObfuzOutput()
        {
            FlushSave();
            var root = S.ObfuzRootDir;
            if (Directory.Exists(root))
            {
                EditorUtility.RevealInFinder(root);
            }
            else
            {
                EditorUtility.DisplayDialog("TEngine 混淆配置", $"目录不存在：{root}\n执行一次混淆后生成。", "知道了");
            }
        }

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/快捷操作")]
        [HorizontalGroup("Pages/总览/快捷操作/Row2")]
        [Button("生成垃圾代码", ButtonSizes.Medium)]
        private void GenerateGarbageButton()
        {
            FlushSave();
            ObfuzMenu.GenerateGarbageCodes();
        }

        [TabGroup("Pages", "总览")]
        [BoxGroup("Pages/总览/快捷操作")]
        [HorizontalGroup("Pages/总览/快捷操作/Row2")]
        [Button("清理垃圾代码", ButtonSizes.Medium)]
        private void CleanGarbageButton()
        {
            ObfuzMenu.CleanGeneratedGarbageCodes();
        }

        #endregion

        #region 程序集

        [TabGroup("Pages", "程序集")]
        [BoxGroup("Pages/程序集/待混淆程序集")]
        [InfoBox("真正被混淆的程序集名（不带 .dll）。不要手动添加 Obfuz.Runtime，由下方开关控制。", InfoMessageType.None)]
        [LabelText("混淆程序集")]
        [ValueDropdown(nameof(HotUpdateAssemblyOptions))]
        [ListDrawerSettings(DefaultExpandedState = true, DraggableItems = true)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] AssembliesToObfuscate
        {
            get => S.assemblySettings.assembliesToObfuscate ?? Array.Empty<string>();
            set
            {
                S.assemblySettings.assembliesToObfuscate = (value ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
                MarkDirty();
            }
        }

        private ValueDropdownList<string> HotUpdateAssemblyOptions()
        {
            var list = new ValueDropdownList<string> { { "(无候选)", "" } };
#if ENABLE_HYBRIDCLR
            list.Clear();
            foreach (var name in HybridCLR.Editor.SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
            {
                list.Add(name, name);
            }
#endif
            return list;
        }

        [TabGroup("Pages", "程序集")]
        [BoxGroup("Pages/程序集/待混淆程序集")]
        [HorizontalGroup("Pages/程序集/待混淆程序集/Fill")]
        [Button("从 HybridCLR 热更程序集填充", ButtonSizes.Small)]
        private void FillFromHotUpdateAssemblies()
        {
#if ENABLE_HYBRIDCLR
            var current = new HashSet<string>(AssembliesToObfuscate, StringComparer.Ordinal);
            int added = 0;
            foreach (var name in HybridCLR.Editor.SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
            {
                if (current.Add(name))
                {
                    added++;
                }
            }
            S.assemblySettings.assembliesToObfuscate = current.ToArray();
            MarkDirty();
            Debug.Log($"[ObfuzConfig] 已从 HybridCLR 热更程序集填充，新增 {added} 个");
#else
            Debug.LogWarning("[ObfuzConfig] 未启用 ENABLE_HYBRIDCLR，无法自动填充");
#endif
        }

        [TabGroup("Pages", "程序集")]
        [BoxGroup("Pages/程序集/引用程序集")]
        [InfoBox("自身不混淆、但 IL 中引用了被混淆程序集类型的程序集，混淆时会同步改写其中的调用点。", InfoMessageType.None)]
        [LabelText("引用跟随程序集")]
        [ListDrawerSettings(DefaultExpandedState = true)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] NonObfuscatedReferencing
        {
            get => S.assemblySettings.nonObfuscatedButReferencingObfuscatedAssemblies ?? Array.Empty<string>();
            set
            {
                S.assemblySettings.nonObfuscatedButReferencingObfuscatedAssemblies = (value ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "程序集")]
        [BoxGroup("Pages/程序集/搜索路径")]
        [LabelText("附加程序集搜索路径")]
        [ListDrawerSettings(DefaultExpandedState = false)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] AdditionalAssemblySearchPaths
        {
            get => S.assemblySettings.additionalAssemblySearchPaths ?? Array.Empty<string>();
            set
            {
                S.assemblySettings.additionalAssemblySearchPaths = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "程序集")]
        [BoxGroup("Pages/程序集/运行时")]
        [LabelText("混淆 Obfuz.Runtime")]
        [ToggleLeft]
        [Tooltip("官方强烈建议开启：不混淆会暴露解密基础设施")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool ObfuscateObfuzRuntime
        {
            get => S.assemblySettings.obfuscateObfuzRuntime;
            set
            {
                S.assemblySettings.obfuscateObfuzRuntime = value;
                MarkDirty();
            }
        }

        #endregion

        #region 混淆通道

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/预设")]
        [InfoBox("预设会整体覆盖下方通道开关。全局通道是硬上限：未启用的通道，规则文件也无法重新开启。", InfoMessageType.None)]
        [ShowInInspector, DisplayAsString, HideLabel]
        private string PresetTip => string.Empty;

        [TabGroup("Pages", "混淆通道")]
        [HorizontalGroup("Pages/混淆通道/预设/Row")]
        [Button("最小", ButtonSizes.Medium)]
        private void PresetMinimal()
        {
            S.obfuscationPassSettings.enabledPasses = ObfuzPassType.SymbolObfus | ObfuzPassType.RemoveConstField;
            MarkDirty();
        }

        [TabGroup("Pages", "混淆通道")]
        [HorizontalGroup("Pages/混淆通道/预设/Row")]
        [Button("均衡", ButtonSizes.Medium)]
        private void PresetBalanced()
        {
            S.obfuscationPassSettings.enabledPasses = ObfuzPassType.SymbolObfus | ObfuzPassType.RemoveConstField
                | ObfuzPassType.ConstEncrypt | ObfuzPassType.ExprObfus;
            MarkDirty();
        }

        [TabGroup("Pages", "混淆通道")]
        [HorizontalGroup("Pages/混淆通道/预设/Row")]
        [Button("强化", ButtonSizes.Medium)]
        private void PresetHardened()
        {
            S.obfuscationPassSettings.enabledPasses = ObfuzPassType.SymbolObfus | ObfuzPassType.RemoveConstField
                | ObfuzPassType.ConstEncrypt | ObfuzPassType.ExprObfus
                | ObfuzPassType.CallObfus | ObfuzPassType.ControlFlowObfus | ObfuzPassType.FieldEncrypt;
            MarkDirty();
        }

        [TabGroup("Pages", "混淆通道")]
        [HorizontalGroup("Pages/混淆通道/预设/Row")]
        [Button("全部", ButtonSizes.Medium), GUIColor(nameof(WarnColor))]
        private void PresetAll()
        {
            S.obfuscationPassSettings.enabledPasses = (ObfuzPassType)(-1);
            MarkDirty();
        }

        private bool GetPass(ObfuzPassType flag) => S.obfuscationPassSettings.enabledPasses.HasFlag(flag);

        private void SetPass(ObfuzPassType flag, bool value)
        {
            var current = S.obfuscationPassSettings.enabledPasses;
            S.obfuscationPassSettings.enabledPasses = value ? current | flag : current & ~flag;
            MarkDirty();
        }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("符号混淆 SymbolObfus"), ToggleLeft]
        [Tooltip("类型/字段/方法/参数/属性/事件改名，运行时几乎无成本")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassSymbol { get => GetPass(ObfuzPassType.SymbolObfus); set => SetPass(ObfuzPassType.SymbolObfus, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("常量加密 ConstEncrypt"), ToggleLeft]
        [Tooltip("数值/字符串/数组常量加密，读取时有解密与缓存成本")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassConst { get => GetPass(ObfuzPassType.ConstEncrypt); set => SetPass(ObfuzPassType.ConstEncrypt, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("字段加密 FieldEncrypt"), ToggleLeft]
        [Tooltip("字段以密文存储、读写时转换，成本较高，建议仅用于关键字段")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassField { get => GetPass(ObfuzPassType.FieldEncrypt); set => SetPass(ObfuzPassType.FieldEncrypt, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("调用混淆 CallObfus"), ToggleLeft]
        [Tooltip("Dispatch/Delegate 间接化调用目标，有间接调用与首次解密成本")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassCall { get => GetPass(ObfuzPassType.CallObfus); set => SetPass(ObfuzPassType.CallObfus, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("表达式混淆 ExprObfus"), ToggleLeft]
        [Tooltip("算术/位运算等价重写，增加 IL 与执行指令")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassExpr { get => GetPass(ObfuzPassType.ExprObfus); set => SetPass(ObfuzPassType.ExprObfus, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("控制流混淆 ControlFlowObfus"), ToggleLeft]
        [Tooltip("基本块平坦化为状态机，分支与体积成本较高")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassControlFlow { get => GetPass(ObfuzPassType.ControlFlowObfus); set => SetPass(ObfuzPassType.ControlFlowObfus, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("执行栈混淆 EvalStackObfus"), ToggleLeft]
        [Tooltip("执行栈与临时值扰动；当前 Obfuz 版本默认 Builder 未注册该 Pass")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassEvalStack { get => GetPass(ObfuzPassType.EvalStackObfus); set => SetPass(ObfuzPassType.EvalStackObfus, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("移除常量字段 RemoveConstField"), ToggleLeft]
        [Tooltip("移除可内联的 const 元数据字段，成本很低")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassRemoveConst { get => GetPass(ObfuzPassType.RemoveConstField); set => SetPass(ObfuzPassType.RemoveConstField, value); }

        [TabGroup("Pages", "混淆通道")]
        [BoxGroup("Pages/混淆通道/通道开关")]
        [LabelText("水印 WaterMark"), ToggleLeft]
        [Tooltip("注入元数据/RVA/指令水印，成本低到中")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PassWatermark { get => GetPass(ObfuzPassType.WaterMark); set => SetPass(ObfuzPassType.WaterMark, value); }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则", Expanded = false)]
        [LabelText("总 Pass 规则文件")]
        [Tooltip("决定某程序集/类型/成员最终启用哪些通道的 XML 规则")]
        [ListDrawerSettings(DefaultExpandedState = false)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] GlobalPassRuleFiles
        {
            get => S.obfuscationPassSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.obfuscationPassSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("常量加密等级"), PropertyRange(1, 4)]
        [Tooltip("1-4 级，等级即参与加密的指令条数；官方建议 1")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int ConstEncryptLevel
        {
            get => S.constEncryptSettings.encryptionLevel;
            set
            {
                S.constEncryptSettings.encryptionLevel = Mathf.Clamp(value, 1, 4);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("常量加密规则")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] ConstEncryptRuleFiles
        {
            get => S.constEncryptSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.constEncryptSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("字段加密等级"), PropertyRange(1, 4)]
        [Tooltip("官方建议 1；字段加密成本高，建议配合规则文件只加密关键字段")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int FieldEncryptLevel
        {
            get => S.fieldEncryptSettings.encryptionLevel;
            set
            {
                S.fieldEncryptSettings.encryptionLevel = Mathf.Clamp(value, 1, 4);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("字段加密规则")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] FieldEncryptRuleFiles
        {
            get => S.fieldEncryptSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.fieldEncryptSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("调用代理模式")]
        [Tooltip("Dispatch：共用分发方法，体积小；Delegate：生成委托，更间接")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private ObfuzProxyMode CallProxyMode
        {
            get => S.callObfusSettings.proxyMode;
            set
            {
                S.callObfusSettings.proxyMode = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("调用混淆等级"), PropertyRange(1, 4)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int CallObfusLevel
        {
            get => S.callObfusSettings.obfuscationLevel;
            set
            {
                S.callObfusSettings.obfuscationLevel = Mathf.Clamp(value, 1, 4);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("每分发方法代理上限"), MinValue(1)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int CallDispatchCapacity
        {
            get => S.callObfusSettings.maxProxyMethodCountPerDispatchMethod;
            set
            {
                S.callObfusSettings.maxProxyMethodCountPerDispatchMethod = Mathf.Max(1, value);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("混淆 mscorlib 调用"), ToggleLeft]
        [Tooltip("会明显影响性能，谨慎开启")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool CallObfuscateMscorlib
        {
            get => S.callObfusSettings.obfuscateCallToMethodInMscorlib;
            set
            {
                S.callObfusSettings.obfuscateCallToMethodInMscorlib = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("调用混淆规则")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] CallObfusRuleFiles
        {
            get => S.callObfusSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.callObfusSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("控制流最小基本块指令数"), MinValue(1)]
        [Tooltip("小于该指令数的基本块不做平坦化；越大越保守")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int ControlFlowMinBlockInstructions
        {
            get => S.controlFlowObfusSettings.minInstructionCountOfBasicBlockToObfuscate;
            set
            {
                S.controlFlowObfusSettings.minInstructionCountOfBasicBlockToObfuscate = Mathf.Max(1, value);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("控制流混淆规则")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] ControlFlowRuleFiles
        {
            get => S.controlFlowObfusSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.controlFlowObfusSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("表达式混淆规则")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] ExprRuleFiles
        {
            get => S.exprObfusSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.exprObfusSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("移除常量字段规则")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] RemoveConstRuleFiles
        {
            get => S.removeConstFieldSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.removeConstFieldSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "混淆通道")]
        [FoldoutGroup("Pages/混淆通道/参数与规则")]
        [LabelText("执行栈混淆规则")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] EvalStackRuleFiles
        {
            get => S.evalStackObfusSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.evalStackObfusSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        #endregion

        #region 加密与密钥

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/加密 VM")]
        [LabelText("VM 代码生成密钥")]
        [DelayedProperty]
        [InlineButton(nameof(RandomizeVmKey), "随机")]
        [InfoBox("仍为官方默认值，必须替换。主包发布后不可再变更（VM 固化在 AOT）。", InfoMessageType.Error, VisibleIf = nameof(VmKeyIsDefault))]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string VmCodeGenerationKey
        {
            get => S.encryptionVMSettings.codeGenerationSecretKey;
            set
            {
                S.encryptionVMSettings.codeGenerationSecretKey = value;
                MarkDirty();
            }
        }

        private bool VmKeyIsDefault => string.IsNullOrEmpty(S.encryptionVMSettings.codeGenerationSecretKey) || S.encryptionVMSettings.codeGenerationSecretKey == "Obfuz";

        private static string GenerateSecretText() => $"TEngine-{Guid.NewGuid().ToString("N").Substring(0, 20)}";

        private void RandomizeSecret(Func<string> getter, Action<string> setter, string displayName, bool frozen)
        {
            string current = getter();
            string frozenTip = frozen ? "\n注意：该密钥为冻结参数，主包发布后不可再变更！" : string.Empty;
            if (!string.IsNullOrEmpty(current) &&
                !EditorUtility.DisplayDialog("TEngine 混淆配置",
                    $"将用随机值覆盖「{displayName}」的当前值。\n当前：{current}{frozenTip}",
                    "覆盖", "取消"))
            {
                return;
            }
            setter(GenerateSecretText());
            MarkDirty();
        }

        private void RandomizeVmKey() => RandomizeSecret(
            () => S.encryptionVMSettings.codeGenerationSecretKey,
            value => S.encryptionVMSettings.codeGenerationSecretKey = value,
            "VM 代码生成密钥", true);

        private void RandomizeStaticKey() => RandomizeSecret(
            () => S.secretSettings.defaultStaticSecretKey,
            value => S.secretSettings.defaultStaticSecretKey = value,
            "静态密钥", true);

        private void RandomizeDynamicKey() => RandomizeSecret(
            () => S.secretSettings.defaultDynamicSecretKey,
            value => S.secretSettings.defaultDynamicSecretKey = value,
            "动态密钥", false);

        private void RandomizePolymorphicKey() => RandomizeSecret(
            () => S.polymorphicDllSettings.codeGenerationSecretKey,
            value => S.polymorphicDllSettings.codeGenerationSecretKey = value,
            "多态 DLL 密钥", true);

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/加密 VM")]
        [LabelText("加密指令数"), MinValue(64)]
        [InfoBox("$OpCodeCountError", InfoMessageType.Error, VisibleIf = nameof(OpCodeCountInvalid))]
        [InfoBox("$OpCodeCountExceedsWarning", InfoMessageType.Warning, VisibleIf = nameof(OpCodeCountExceedsRecommend))]
        [InlineButton(nameof(StepDownOpCodeCount), "调小")]
        [InlineButton(nameof(StepUpOpCodeCount), "调大")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int EncryptionOpCodeCount
        {
            get => S.encryptionVMSettings.encryptionOpCodeCount;
            set
            {
                S.encryptionVMSettings.encryptionOpCodeCount = value;
                MarkDirty();
            }
        }

        private bool OpCodeCountInvalid
        {
            get
            {
                int count = S.encryptionVMSettings.encryptionOpCodeCount;
                return !IsPowerOfTwo(count) || count < 64;
            }
        }

        private string OpCodeCountError => $"当前值 {S.encryptionVMSettings.encryptionOpCodeCount} 无效：必须是 2 的幂且不小于 64（默认值为 256，建议不要超过 1024）";

        private bool OpCodeCountExceedsRecommend => !OpCodeCountInvalid && S.encryptionVMSettings.encryptionOpCodeCount > 1024;

        private string OpCodeCountExceedsWarning => $"当前值 {S.encryptionVMSettings.encryptionOpCodeCount} 合法，但官方建议不要超过 1024";

        private void StepDownOpCodeCount() => StepOpCodeCount(-1);

        private void StepUpOpCodeCount() => StepOpCodeCount(1);

        // 步进仅在文档建议的取值集合 64/128/256/512/1024 内进行，非法或越界的当前值会收敛到最近的合法值
        private void StepOpCodeCount(int direction)
        {
            int[] steps = { 64, 128, 256, 512, 1024 };
            int current = S.encryptionVMSettings.encryptionOpCodeCount;
            int result = direction < 0 ? steps[0] : steps[steps.Length - 1];
            foreach (int step in steps)
            {
                if (direction < 0 ? step < current : step > current)
                {
                    result = step;
                    if (direction > 0)
                    {
                        break;
                    }
                }
            }
            S.encryptionVMSettings.encryptionOpCodeCount = result;
            MarkDirty();
        }

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/加密 VM")]
        [LabelText("VM 代码输出路径")]
        [InlineButton(nameof(OpenVmOutputDir), "打开目录")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string VmCodeOutputPath
        {
            get => S.encryptionVMSettings.codeOutputPath;
            set
            {
                S.encryptionVMSettings.codeOutputPath = value;
                MarkDirty();
            }
        }

        private void OpenVmOutputDir()
        {
            var directory = Path.GetDirectoryName(S.encryptionVMSettings.codeOutputPath);
            if (Directory.Exists(directory))
            {
                EditorUtility.RevealInFinder(directory + Path.DirectorySeparatorChar);
            }
        }

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/加密 VM")]
        [Button("生成加密 VM 代码", ButtonSizes.Medium)]
        private void GenerateVmCode()
        {
            if (OpCodeCountInvalid)
            {
                EditorUtility.DisplayDialog("TEngine 混淆配置", "加密指令数无效，请先修正。", "知道了");
                return;
            }
            FlushSave();
            ObfuzMenu.GenerateEncryptionVM();
        }

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/密钥文件")]
        [InfoBox("参数冻结：VM 密钥/加密指令数/静态密钥发布 App 后请不要修改（同主包热更同样禁止）；动态密钥与随机种子可随热更轮换。", InfoMessageType.None)]
        [LabelText("静态密钥（AOT/启动早期）")]
        [DelayedProperty]
        [InlineButton(nameof(RandomizeStaticKey), "随机")]
        [InfoBox("仍为官方默认值，必须替换。", InfoMessageType.Error, VisibleIf = nameof(StaticKeyIsDefault))]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string StaticSecretKey
        {
            get => S.secretSettings.defaultStaticSecretKey;
            set
            {
                S.secretSettings.defaultStaticSecretKey = value;
                MarkDirty();
            }
        }

        private bool StaticKeyIsDefault => string.IsNullOrEmpty(S.secretSettings.defaultStaticSecretKey) || S.secretSettings.defaultStaticSecretKey == "Code Philosophy-Static";

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/密钥文件")]
        [LabelText("动态密钥（热更程序集）")]
        [DelayedProperty]
        [InlineButton(nameof(RandomizeDynamicKey), "随机")]
        [InfoBox("仍为官方默认值，必须替换。", InfoMessageType.Error, VisibleIf = nameof(DynamicKeyIsDefault))]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string DynamicSecretKey
        {
            get => S.secretSettings.defaultDynamicSecretKey;
            set
            {
                S.secretSettings.defaultDynamicSecretKey = value;
                MarkDirty();
            }
        }

        private bool DynamicKeyIsDefault => string.IsNullOrEmpty(S.secretSettings.defaultDynamicSecretKey) || S.secretSettings.defaultDynamicSecretKey == "Code Philosophy-Dynamic";

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/密钥文件")]
        [LabelText("静态密钥输出路径")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string StaticSecretKeyPath
        {
            get => S.secretSettings.staticSecretKeyOutputPath;
            set
            {
                S.secretSettings.staticSecretKeyOutputPath = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/密钥文件")]
        [LabelText("动态密钥输出路径")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string DynamicSecretKeyPath
        {
            get => S.secretSettings.dynamicSecretKeyOutputPath;
            set
            {
                S.secretSettings.dynamicSecretKeyOutputPath = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/密钥文件")]
        [LabelText("使用动态密钥的程序集")]
        [ValueDropdown(nameof(HotUpdateAssemblyOptions))]
        [ListDrawerSettings(DefaultExpandedState = false)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] AssembliesUsingDynamicKeys
        {
            get => S.secretSettings.assembliesUsingDynamicSecretKeys ?? Array.Empty<string>();
            set
            {
                S.secretSettings.assembliesUsingDynamicSecretKeys = (value ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/密钥文件")]
        [LabelText("随机种子")]
        [Tooltip("驱动 ops/salt 等确定性随机过程；按版本记录以便复现构建")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int SecretRandomSeed
        {
            get => S.secretSettings.randomSeed;
            set
            {
                S.secretSettings.randomSeed = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "加密与密钥")]
        [BoxGroup("Pages/加密与密钥/密钥文件")]
        [Button("生成密钥文件", ButtonSizes.Medium)]
        private void GenerateSecretFiles()
        {
            FlushSave();
            ObfuzMenu.SaveSecretFile();
        }

        #endregion

        #region 符号与映射

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/符号混淆")]
        [LabelText("Debug 模式")]
        [Tooltip("开启后名字稳定改为 $原名，便于定位；忽略正式 mapping 输入与规则效果，仅调试用")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool SymbolDebug
        {
            get => S.symbolObfusSettings.debug;
            set
            {
                S.symbolObfusSettings.debug = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/符号混淆")]
        [LabelText("混淆名前缀")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string ObfuscatedNamePrefix
        {
            get => S.symbolObfusSettings.obfuscatedNamePrefix;
            set
            {
                S.symbolObfusSettings.obfuscatedNamePrefix = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/符号混淆")]
        [LabelText("同名命名空间统一混淆"), ToggleLeft]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool ConsistentNamespace
        {
            get => S.symbolObfusSettings.useConsistentNamespaceObfuscation;
            set
            {
                S.symbolObfusSettings.useConsistentNamespaceObfuscation = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/符号混淆")]
        [LabelText("反射兼容检测"), ToggleLeft]
        [Tooltip("扫描字符串反射（Type.GetType/Enum.Parse 等）的潜在风险并告警")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool DetectReflectionCompat
        {
            get => S.symbolObfusSettings.detectReflectionCompatibility;
            set
            {
                S.symbolObfusSettings.detectReflectionCompatibility = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/符号混淆")]
        [LabelText("mapping 保留未知符号"), ToggleLeft]
        [Tooltip("避免 Unity 裁剪导致 mapping 记录不稳定，建议保持开启")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool KeepUnknownSymbol
        {
            get => S.symbolObfusSettings.keepUnknownSymbolInSymbolMappingFile;
            set
            {
                S.symbolObfusSettings.keepUnknownSymbolInSymbolMappingFile = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/符号混淆")]
        [LabelText("符号规则文件")]
        [ListDrawerSettings(DefaultExpandedState = false)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] SymbolRuleFiles
        {
            get => S.symbolObfusSettings.ruleFiles ?? Array.Empty<string>();
            set
            {
                S.symbolObfusSettings.ruleFiles = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/符号混淆")]
        [LabelText("自定义改名策略类型")]
        [ListDrawerSettings(DefaultExpandedState = false)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string[] CustomRenamePolicyTypes
        {
            get => S.symbolObfusSettings.customRenamePolicyTypes ?? Array.Empty<string>();
            set
            {
                S.symbolObfusSettings.customRenamePolicyTypes = value ?? Array.Empty<string>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/映射文件")]
        [InfoBox("正式 mapping 必须纳入版本管理并按发布版本归档，用于堆栈还原与稳定改名。", InfoMessageType.None)]
        [LabelText("正式映射文件"), DelayedProperty]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string SymbolMappingFile
        {
            get => S.symbolObfusSettings.symbolMappingFile;
            set
            {
                S.symbolObfusSettings.symbolMappingFile = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "符号与映射")]
        [BoxGroup("Pages/符号与映射/映射文件")]
        [LabelText("Debug 映射文件"), DelayedProperty]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string DebugSymbolMappingFile
        {
            get => S.symbolObfusSettings.debugSymbolMappingFile;
            set
            {
                S.symbolObfusSettings.debugSymbolMappingFile = value;
                MarkDirty();
            }
        }

        #endregion

        #region 垃圾代码

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/生成设置")]
        [InfoBox("垃圾代码用于降低二进制相似度，不等于核心防护强度；生成产物需纳入版本管理。", InfoMessageType.None)]
        [LabelText("生成密钥")]
        [DelayedProperty]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string GarbageCodeSecret
        {
            get => S.garbageCodeGenerationSettings.codeGenerationSecret;
            set
            {
                S.garbageCodeGenerationSettings.codeGenerationSecret = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("命名空间")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string GarbageNamespace
        {
            get => GarbageTask.classNamespace;
            set
            {
                GarbageTask.classNamespace = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("类名前缀")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string GarbageClassPrefix
        {
            get => GarbageTask.classNamePrefix;
            set
            {
                GarbageTask.classNamePrefix = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("生成类数"), MinValue(0)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int GarbageClassCount
        {
            get => GarbageTask.classCount;
            set
            {
                GarbageTask.classCount = Mathf.Max(0, value);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("每类方法数"), MinValue(0)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int GarbageMethodsPerClass
        {
            get => GarbageTask.methodCountPerClass;
            set
            {
                GarbageTask.methodCountPerClass = Mathf.Max(0, value);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("每类字段数"), MinValue(0)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int GarbageFieldsPerClass
        {
            get => GarbageTask.fieldCountPerClass;
            set
            {
                GarbageTask.fieldCountPerClass = Mathf.Max(0, value);
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("代码风格")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private ObfuzGarbageCodeType GarbageCodeType
        {
            get => GarbageTask.garbageCodeType;
            set
            {
                GarbageTask.garbageCodeType = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("随机种子")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int GarbageRandomSeed
        {
            get => GarbageTask.codeGenerationRandomSeed;
            set
            {
                GarbageTask.codeGenerationRandomSeed = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/默认任务")]
        [LabelText("输出路径")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string GarbageOutputPath
        {
            get => GarbageTask.outputPath;
            set
            {
                GarbageTask.outputPath = value;
                MarkDirty();
            }
        }

        private ObfuzGarbageTask GarbageTask => S.garbageCodeGenerationSettings.defaultTask ?? (S.garbageCodeGenerationSettings.defaultTask = new ObfuzGarbageTask());

        [TabGroup("Pages", "垃圾代码")]
        [BoxGroup("Pages/垃圾代码/附加任务")]
        [LabelText("附加任务列表")]
        [ListDrawerSettings(DefaultExpandedState = false)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private ObfuzGarbageTask[] GarbageAdditionalTasks
        {
            get => S.garbageCodeGenerationSettings.additionalTasks ?? Array.Empty<ObfuzGarbageTask>();
            set
            {
                S.garbageCodeGenerationSettings.additionalTasks = value ?? Array.Empty<ObfuzGarbageTask>();
                MarkDirty();
            }
        }

        [TabGroup("Pages", "垃圾代码")]
        [HorizontalGroup("Pages/垃圾代码/操作")]
        [Button("生成垃圾代码", ButtonSizes.Medium)]
        private void GenerateGarbageCodes()
        {
            FlushSave();
            ObfuzMenu.GenerateGarbageCodes();
        }

        [TabGroup("Pages", "垃圾代码")]
        [HorizontalGroup("Pages/垃圾代码/操作")]
        [Button("清理垃圾代码", ButtonSizes.Medium)]
        private void CleanGarbageCodes()
        {
            ObfuzMenu.CleanGeneratedGarbageCodes();
        }

        #endregion

        #region 高级

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/构建回调")]
        [LabelText("link.xml 处理回调顺序")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int LinkXmlCallbackOrder
        {
            get => S.buildPipelineSettings.linkXmlProcessCallbackOrder;
            set
            {
                S.buildPipelineSettings.linkXmlProcessCallbackOrder = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/构建回调")]
        [LabelText("混淆处理回调顺序")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int ObfuscationCallbackOrder
        {
            get => S.buildPipelineSettings.obfuscationProcessCallbackOrder;
            set
            {
                S.buildPipelineSettings.obfuscationProcessCallbackOrder = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/兼容性")]
        [LabelText("目标运行时")]
        [Tooltip("一般保持“当前激活脚本后端”；仅在独立混淆需要指定目标时修改")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private ObfuzRuntimeType TargetRuntime
        {
            get => S.compatibilitySettings.targetRuntime;
            set
            {
                S.compatibilitySettings.targetRuntime = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/多态 DLL")]
        [InfoBox("依赖 HybridCLR 8.4.0+ 自定义 DLL 结构；密钥与主包结构强绑定，主包发布后不可变更。开启后执行“HybridCLR/ObfuzExtension/GenerateAll”以实现注入修改支持多态加载", InfoMessageType.None)]
        [LabelText("启用多态 DLL"), ToggleLeft]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PolymorphicEnable
        {
            get => S.polymorphicDllSettings.enable;
            set
            {
                S.polymorphicDllSettings.enable = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/多态 DLL")]
        [LabelText("多态 DLL 密钥")]
        [DelayedProperty]
        [InlineButton(nameof(RandomizePolymorphicKey), "随机")]
        [InfoBox("仍为官方默认值，启用多态 DLL 前必须替换。", InfoMessageType.Error, VisibleIf = nameof(PolymorphicKeyIsDefault))]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string PolymorphicKey
        {
            get => S.polymorphicDllSettings.codeGenerationSecretKey;
            set
            {
                S.polymorphicDllSettings.codeGenerationSecretKey = value;
                MarkDirty();
            }
        }

        private bool PolymorphicKeyIsDefault => string.IsNullOrEmpty(S.polymorphicDllSettings.codeGenerationSecretKey) || S.polymorphicDllSettings.codeGenerationSecretKey == "obfuz-polymorphic-key";

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/多态 DLL")]
        [LabelText("禁用标准 DLL 加载"), ToggleLeft]
        [Tooltip("初期保持关闭更利于调试与回滚，成熟后再评估")]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private bool PolymorphicDisableStandardDll
        {
            get => S.polymorphicDllSettings.disableLoadStandardDll;
            set
            {
                S.polymorphicDllSettings.disableLoadStandardDll = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/水印")]
        [LabelText("水印文本")]
        [DelayedProperty]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private string WatermarkText
        {
            get => S.watermarkSettings.text;
            set
            {
                S.watermarkSettings.text = value;
                MarkDirty();
            }
        }

        [TabGroup("Pages", "高级")]
        [BoxGroup("Pages/高级/水印")]
        [LabelText("签名字节长度"), MinValue(1)]
        [OnValueChanged(nameof(MarkDirty))]
        [ShowInInspector]
        private int WatermarkSignatureLength
        {
            get => S.watermarkSettings.signatureLength;
            set
            {
                S.watermarkSettings.signatureLength = Mathf.Max(1, value);
                MarkDirty();
            }
        }

        #endregion

        #region 保存

        private bool _saveQueued;
        private double _nextSaveTime;

        private void MarkDirty()
        {
            Repaint();
            QueueSave();
        }

        private void QueueSave()
        {
            _nextSaveTime = EditorApplication.timeSinceStartup + SaveDelaySeconds;
            if (_saveQueued)
            {
                return;
            }
            _saveQueued = true;
            EditorApplication.update += FlushSaveWhenReady;
        }

        private void FlushSaveWhenReady()
        {
            if (EditorApplication.timeSinceStartup < _nextSaveTime)
            {
                return;
            }
            EditorApplication.update -= FlushSaveWhenReady;
            _saveQueued = false;
            ObfuzSettingsAsset.Save();
        }

        private void FlushSave()
        {
            if (!_saveQueued)
            {
                return;
            }
            EditorApplication.update -= FlushSaveWhenReady;
            _saveQueued = false;
            ObfuzSettingsAsset.Save();
        }

        #endregion

#else
        [MenuItem(MenuPath, false, 51)]
        public static void ShowWindow()
        {
            var window = GetWindow<ObfuzConfigWindow>();
            window.titleContent = new GUIContent("TEngine 混淆配置");
            window.minSize = new Vector2(460, 220);
            window.Show();
        }

        [InfoBox("未安装 Obfuz 包（com.code-philosophy.obfuz）。请先通过 Package Manager 安装 Obfuz 及 Obfuz4HybridCLR，编译通过后重新打开本窗口。", InfoMessageType.Warning)]
        [ShowInInspector, ReadOnly]
        private string Placeholder => "Obfuz 未安装";
#endif
    }
}
