using System;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
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

            // 语言配置：设置当前使用的语言，如果不设置，则默认使用操作系统语言
            InitLanguageSettings();

            // 声音配置：根据用户配置数据，设置即将使用的声音选项
            InitSoundSettings();

            // 资源初始化前加载部署配置（现场可覆盖热更地址），主包侧通过 ModuleSystem 访问
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
                await ModuleSystem.GetModule<IJsonConfigModule>().LoadAllAsync();
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

            IJsonConfigModule configModule = ModuleSystem.GetModule<IJsonConfigModule>();
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
