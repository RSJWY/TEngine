using System;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    /// <summary>
    /// 流程 => 启动器。
    /// </summary>
    public class ProcedureLaunch : ProcedureBase
    {
        public override bool UseNativeDialog => true;

        private IAudioModule _audioModule;

        private bool _deployConfigLoaded;

        protected override void OnInit(ProcedureOwner procedureOwner)
        {
            _audioModule = ModuleSystem.GetModule<IAudioModule>();
            base.OnInit(procedureOwner);
        }

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            
            //热更新UI初始化
            LauncherMgr.Initialize();

#if ENABLE_OBFUZ && !UNITY_EDITOR
            // Obfuz 静态密钥在 AfterAssembliesLoaded 已尝试初始化；此处 UI 就绪后报告失败并阻断流程。
            if (ObfuzRuntimeInitializer.CheckFailureAndReport())
            {
                return;
            }
#endif

            // 语言配置：设置当前使用的语言，如果不设置，则默认使用操作系统语言
            InitLanguageSettings();

            // 声音配置：根据用户配置数据，设置即将使用的声音选项
            InitSoundSettings();

            // 画质档位：解析 -picture-quality <索引>，未传参或越界时保持 QualitySettings 默认
            ApplyQualityFromCommandLine();

            // 桌面多开：解析命令行实例标识，资源包初始化前注入（未传参时保持单开默认行为）
            ApplyMultiInstanceFromCommandLine();

            // 资源初始化前加载运行时配置（现场可覆盖热更地址），主包侧通过 ModuleSystem 访问
            LoadDeployConfigAsync().Forget();
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            // 等待部署配置加载完成后再进入 Splash，确保资源初始化时已读到现场地址
            if (!_deployConfigLoaded)
            {
                return;
            }

            ChangeState<ProcedureSplash>(procedureOwner);
        }

        private async UniTaskVoid LoadDeployConfigAsync()
        {
            try
            {
                await ModuleSystem.GetModule<IRuntimeConfigModule>().LoadAllAsync();
                ApplyDebuggerConfig();
                ApplyResourcePlayModeConfig();
            }
            catch (Exception exception)
            {
                Log.Error("Load deploy config failed, fallback to UpdateSetting defaults. reason {0}", exception.ToString());
            }

            _deployConfigLoaded = true;
        }

        /// <summary>
        /// 读取 DeployConfig 的调试器激活策略并现场覆盖 Debugger 组件。
        /// 未配置该字段、解析失败或场景无 Debugger 时保留 Inspector 默认行为。
        /// </summary>
        private void ApplyDebuggerConfig()
        {
            if (Debugger.Instance == null)
            {
                return;
            }

            IRuntimeConfigModule configModule = ModuleSystem.GetModule<IRuntimeConfigModule>();
            if (configModule == null
                || !configModule.TryGet<DeployConfig>(out DeployConfig deployConfig, "DeployConfig")
                || deployConfig == null
                || string.IsNullOrWhiteSpace(deployConfig.DebuggerActiveWindow))
            {
                return;
            }

            if (Enum.TryParse(deployConfig.DebuggerActiveWindow.Trim(), true, out DebuggerActiveWindowType type))
            {
                Debugger.Instance.ApplyActiveWindowType(type);
                Log.Info("[Debugger] 已按 DeployConfig 应用激活策略：{0}。", type);
            }
            else
            {
                Log.Warning("[Debugger] DeployConfig.DebuggerActiveWindow 取值无法解析：{0}，保留 Inspector 配置。", deployConfig.DebuggerActiveWindow);
            }
        }

        /// <summary>
        /// 读取 DeployConfig 的资源初始化模式覆盖并注入资源模块。
        /// 仅桌面平台（Windows/OSX/Linux Standalone Player）且非编辑器时生效——编辑器保持 EditorPrefs/模拟模式，
        /// 移动端与 WebGL 保持打包内置模式。取值 Offline / Host（Web 模式与桌面平台不适用，不做白名单），
        /// 留空或无法解析时保留内置模式。
        /// 时序上先于 Splash 后的 InitPackage（真正按模式初始化资源包的位置），覆盖必然生效。
        /// </summary>
        private void ApplyResourcePlayModeConfig()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer
                && Application.platform != RuntimePlatform.OSXPlayer
                && Application.platform != RuntimePlatform.LinuxPlayer)
            {
                return;
            }

            IRuntimeConfigModule configModule = ModuleSystem.GetModule<IRuntimeConfigModule>();
            if (configModule == null
                || !configModule.TryGet<DeployConfig>(out DeployConfig deployConfig, "DeployConfig")
                || deployConfig == null
                || string.IsNullOrWhiteSpace(deployConfig.YooPlayMode))
            {
                return;
            }

            string value = deployConfig.YooPlayMode.Trim();
            EPlayMode? playMode;
            switch (value.ToLowerInvariant())
            {
                case "offline":
                    playMode = EPlayMode.OfflinePlayMode;
                    break;
                case "host":
                    playMode = EPlayMode.HostPlayMode;
                    break;
                default:
                    playMode = null;
                    break;
            }

            if (playMode == null)
            {
                Log.Warning("[Resource] DeployConfig.YooPlayMode 取值无法解析：{0}（仅支持 Offline / Host），保留内置模式。", value);
                return;
            }

            _resourceModule.PlayMode = playMode.Value;
            Log.Info("[Resource] 已按 DeployConfig 覆盖资源初始化模式：{0}。", playMode.Value);
        }

        private void InitLanguageSettings()
        {
            if (_resourceModule.PlayMode == EPlayMode.EditorSimulateMode && RootModule.Instance.EditorLanguage == Language.Unspecified)
            {
                // 编辑器资源模式直接使用 Inspector 上设置的语言
                return;
            }
            
            ILocalizationModule localizationModule = ModuleSystem.GetModule<ILocalizationModule>();
            Language language = localizationModule.Language;
            if (Utility.PlayerPrefs.HasSetting(Constant.Setting.Language))
            {
                try
                {
                    string languageString = Utility.PlayerPrefs.GetString(Constant.Setting.Language);
                    language = (Language)System.Enum.Parse(typeof(Language), languageString);
                }
                catch(System.Exception exception)
                {
                    Log.Error("Init language error, reason {0}",exception.ToString());
                }
            }
            
            if (language != Language.English
                && language != Language.ChineseSimplified
                && language != Language.ChineseTraditional)
            {
                // 若是暂不支持的语言，则使用英语
                language = Language.English;
            
                Utility.PlayerPrefs.SetString(Constant.Setting.Language, language.ToString());
                Utility.PlayerPrefs.Save();
            }
            
            localizationModule.Language = language;
            Log.Info("Init language settings complete, current language is '{0}'.", language.ToString());
        }

        private void InitSoundSettings()
        {
            _audioModule.MusicEnable = !Utility.PlayerPrefs.GetBool(Constant.Setting.MusicMuted, false);
            _audioModule.MusicVolume = Utility.PlayerPrefs.GetFloat(Constant.Setting.MusicVolume, 1f);
            _audioModule.SoundEnable = !Utility.PlayerPrefs.GetBool(Constant.Setting.SoundMuted, false);
            _audioModule.SoundVolume = Utility.PlayerPrefs.GetFloat(Constant.Setting.SoundVolume, 1f);
            _audioModule.UISoundEnable = !Utility.PlayerPrefs.GetBool(Constant.Setting.UISoundMuted, false);
            _audioModule.UISoundVolume = Utility.PlayerPrefs.GetFloat(Constant.Setting.UISoundVolume, 1f);
            Log.Info("Init sound settings complete.");
        }

        /// <summary>
        /// 解析命令行参数 -picture-quality 并应用画质档位。
        /// 只接受数字索引（0 .. QualitySettings.names.Length-1），不依赖档位名称，换项目更换档位名无需改代码。
        /// 支持 "-picture-quality 3" 与 "-picture-quality=3" 两种写法。
        /// 未传参、越界或非数字时保持 QualitySettings 现有设置并告警，不打断启动流程。
        /// </summary>
        private void ApplyQualityFromCommandLine()
        {
            const string Key = "-picture-quality";

            string value = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == Key)
                {
                    value = i + 1 < args.Length ? args[i + 1] : string.Empty;
                    break;
                }

                if (args[i].StartsWith(Key + "=", StringComparison.Ordinal))
                {
                    value = args[i].Substring(Key.Length + 1);
                    break;
                }
            }

            if (value == null)
            {
                return;
            }

            if (!int.TryParse(value, out int level))
            {
                Log.Warning("[Quality] {0} 只接受数字索引（未使用名字匹配），忽略取值：{1}", Key, value);
                return;
            }

            int count = QualitySettings.names.Length;
            if (level < 0 || level >= count)
            {
                Log.Warning("[Quality] {0} 取值越界：{1}，可用范围 0-{2}，保持当前档位。", Key, level, count - 1);
                return;
            }

            // 启动早期场景与资源均未加载，applyExpensiveChanges 设为 true 可让后续加载的一切都按新档位生效
            QualitySettings.SetQualityLevel(level, true);
            Log.Info("[Quality] 已按命令行切换画质档位：{0} ({1})", level, QualitySettings.names[level]);
        }

        /// <summary>
        /// 解析命令行参数 --yoo-instance 并把实例标识注入资源模块，实现桌面多开的缓存隔离。
        /// 未传参时保持单开默认行为。
        /// </summary>
        private void ApplyMultiInstanceFromCommandLine()
        {
            string instanceId = MultiInstanceLauncher.ResolveInstanceId();
            if (string.IsNullOrEmpty(instanceId))
            {
                return;
            }

            _resourceModule.InstanceId = instanceId;
            Log.Info($"桌面多开实例标识：{instanceId}，资源缓存将隔离到 instance-{instanceId} 目录。");
        }
    }
}
