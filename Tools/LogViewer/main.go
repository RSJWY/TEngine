package main

import (
	"context"
	"embed"
	"logviewer/parser"
	"os"
	"path/filepath"
	goruntime "runtime"

	"github.com/wailsapp/wails/v2"
	"github.com/wailsapp/wails/v2/pkg/options"
	"github.com/wailsapp/wails/v2/pkg/options/assetserver"
	"github.com/wailsapp/wails/v2/pkg/options/windows"
	wailsruntime "github.com/wailsapp/wails/v2/pkg/runtime"
)

//go:embed all:frontend
var assets embed.FS

// App 结构体
type App struct {
	ctx context.Context
}

// NewApp 创建一个新的 App 实例
func NewApp() *App {
	return &App{}
}

// startup 在应用启动时调用
func (a *App) startup(ctx context.Context) {
	a.ctx = ctx

	// 监听前端文件拖拽，拖入后直接读取首个文件
	wailsruntime.OnFileDrop(ctx, func(x, y int, paths []string) {
		if len(paths) == 0 {
			return
		}
		wailsruntime.EventsEmit(a.ctx, "file:dropped", paths[0])
	})
}

// LoadLogFile 加载日志文件
func (a *App) LoadLogFile(filePath string) ([]parser.LogEntry, error) {
	return parser.ParseFile(filePath)
}

// FilterLogs 过滤日志
func (a *App) FilterLogs(entries []parser.LogEntry, levels map[string]bool, keyword string) []parser.LogEntry {
	return parser.FilterEntries(entries, levels, keyword)
}

// OpenFileDialog 打开文件选择对话框，返回选中的文件路径（取消返回空字符串）
func (a *App) OpenFileDialog() (string, error) {
	// 默认目录不存在时传空，避免对话框报错
	defaultDir := a.GetDefaultLogPath()
	if defaultDir != "" {
		if info, err := os.Stat(defaultDir); err != nil || !info.IsDir() {
			defaultDir = ""
		}
	}

	return wailsruntime.OpenFileDialog(a.ctx, wailsruntime.OpenDialogOptions{
		Title:            "选择日志文件",
		DefaultDirectory: defaultDir,
		Filters: []wailsruntime.FileFilter{
			{DisplayName: "日志文件 (*.log;*.txt)", Pattern: "*.log;*.txt"},
			{DisplayName: "所有文件 (*.*)", Pattern: "*.*"},
		},
	})
}

// OpenDefaultLogDir 在系统文件管理器中打开默认日志目录
func (a *App) OpenDefaultLogDir() error {
	path := a.GetDefaultLogPath()
	if path == "" {
		return nil
	}
	wailsruntime.BrowserOpenURL(a.ctx, "file:///"+filepath.ToSlash(path))
	return nil
}

// GetDefaultLogPath 获取默认日志目录
func (a *App) GetDefaultLogPath() string {
	if goruntime.GOOS == "windows" {
		// Windows: %LOCALAPPDATA%\DefaultCompany\hotUnity\Logs
		return filepath.Join(getLocalAppData(), "DefaultCompany", "hotUnity", "Logs")
	}
	return ""
}

func getLocalAppData() string {
	if goruntime.GOOS == "windows" {
		// 尝试获取 LOCALAPPDATA 环境变量
		if localAppData := os.Getenv("LOCALAPPDATA"); localAppData != "" {
			return localAppData
		}
		// 回退到 USERPROFILE\AppData\Local
		if userProfile := os.Getenv("USERPROFILE"); userProfile != "" {
			return filepath.Join(userProfile, "AppData", "Local")
		}
	}
	return ""
}

func main() {
	// 创建 app 实例
	app := NewApp()

	// 创建应用配置
	err := wails.Run(&options.App{
		Title:  "TEngine Log Viewer",
		Width:  1280,
		Height: 800,
		AssetServer: &assetserver.Options{
			Assets: assets,
		},
		BackgroundColour: &options.RGBA{R: 30, G: 30, B: 30, A: 1},
		OnStartup:        app.startup,
		// 启用拖拽文件功能
		DragAndDrop: &options.DragAndDrop{
			EnableFileDrop: true,
		},
		Bind: []interface{}{
			app,
		},
		Windows: &windows.Options{
			WebviewIsTransparent: false,
			WindowIsTranslucent:  false,
			DisableWindowIcon:    false,
		},
	})

	if err != nil {
		println("Error:", err.Error())
	}
}
