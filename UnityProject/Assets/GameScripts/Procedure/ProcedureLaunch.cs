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

        private bool _licenseChecked;

        private bool _licenseChecking;

        private RockeyArmModule _rockeyArm;

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

            // 桌面多开：解析命令行实例标识，资源包初始化前注入（未传参时保持单开默认行为）
            string instanceId = MultiInstanceLauncher.ResolveInstanceId();
            if (!string.IsNullOrEmpty(instanceId))
            {
                _resourceModule.InstanceId = instanceId;
                Log.Info($"桌面多开实例标识：{instanceId}，资源缓存将隔离到 instance-{instanceId} 目录。");
            }

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

            // 加密锁授权闸门：验证通过（并启动看门狗）后才放行进入 Splash
            if (!_licenseChecked)
            {
                if (!_licenseChecking)
                {
                    _licenseChecking = true;
                    CheckLicenseAsync().Forget();
                }
                return;
            }

            ChangeState<ProcedureSplash>(procedureOwner);
        }

        /// <summary>
        /// 加密锁授权验证：通过后启动看门狗并放行流程；失败弹启动器消息框（确定重试 / 取消退出）。
        /// 注册的是 TEngine 适配层 RockeyArmModule（Assets/RockeyARM/TEngine，包装零框架核心 RockeyArm.RockeyArmManager），
        /// 模块实例注册进 ModuleSystem，热更域后续通过 GameModule.RockeyArm 访问同一实例。
        /// </summary>
        private async UniTaskVoid CheckLicenseAsync()
        {
            if (_rockeyArm == null)
            {
                _rockeyArm = new RockeyArmModule();
                ModuleSystem.RegisterModule<IRockeyArmModule>(_rockeyArm);
            }

            bool pass = await _rockeyArm.CheckPermissionsAsync();
            if (pass)
            {
                _rockeyArm.StartWatchdog().Forget();
                _licenseChecked = true;
                return;
            }

            // 失败后保持 _licenseChecking = true，流程停在本状态、不再自动重试；
            // 仅当用户在消息框点「确定」时解锁，由 OnUpdate 下一拍重新发起验证。
            string message = _rockeyArm.LastError?.UserMessage ?? "未知授权错误!";
            Log.Warning("[ProcedureLaunch] 授权验证失败：{0}", message);
            LauncherMgr.ShowMessageBox(
                $"{message}\n\n请插入加密锁后点击确定重试，点击取消退出应用。",
                onConfirm: () => _licenseChecking = false,
                onCancel: Application.Quit);
        }

        private async UniTaskVoid LoadDeployConfigAsync()
        {
            try
            {
                await ModuleSystem.GetModule<IRuntimeConfigModule>().LoadAllAsync();
                ApplyDebuggerConfig();
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
    }
}
