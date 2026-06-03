package parser

import (
	"bufio"
	"os"
	"regexp"
	"strings"
)

// LogEntry 表示一条日志记录
type LogEntry struct {
	Timestamp string   `json:"timestamp"` // 2026-06-03 09:15:04.4188
	Level     string   `json:"level"`     // Debug/Warning/Error
	Message   string   `json:"message"`   // 剥离富文本后的消息
	Stack     []string `json:"stack"`     // 堆栈行数组
	RawLevel  string   `json:"rawLevel"`  // 原始级别（用于未知级别）
}

var (
	// 日志头格式: 2026-06-03 09:15:04.4188 | Debug | <富文本>消息</富文本>
	headerRegex = regexp.MustCompile(`^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\s*\|\s*(\w+)\s*\|\s*(.*)$`)

	// Unity 富文本标签: <color=#XXX>, <b>, </color>, </b> 等
	richTextRegex = regexp.MustCompile(`<[^>]+>`)

	// 堆栈行特征（编辑器/打包通用）: 类名:方法名 或 类名.方法名
	stackLineRegex = regexp.MustCompile(`^[A-Za-z_][\w.<>]+[:.][\w<>]+`)
)

// ParseFile 解析日志文件，返回所有日志条目
func ParseFile(filePath string) ([]LogEntry, error) {
	file, err := os.Open(filePath)
	if err != nil {
		return nil, err
	}
	defer file.Close()

	var entries []LogEntry
	var currentEntry *LogEntry
	scanner := bufio.NewScanner(file)

	// 增大缓冲区以处理长行
	buf := make([]byte, 0, 64*1024)
	scanner.Buffer(buf, 1024*1024)

	for scanner.Scan() {
		line := scanner.Text()

		// 空行表示一条日志结束
		if strings.TrimSpace(line) == "" {
			if currentEntry != nil {
				entries = append(entries, *currentEntry)
				currentEntry = nil
			}
			continue
		}

		// 尝试匹配日志头
		if matches := headerRegex.FindStringSubmatch(line); matches != nil {
			// 保存上一条记录
			if currentEntry != nil {
				entries = append(entries, *currentEntry)
			}

			// 开始新记录
			timestamp := matches[1]
			level := matches[2]
			rawMessage := matches[3]

			// 剥离富文本标签和冗余前缀
			message := stripRichText(rawMessage)
			message = stripRedundantPrefix(message)

			currentEntry = &LogEntry{
				Timestamp: timestamp,
				Level:     normalizeLevel(level),
				RawLevel:  level,
				Message:   message,
				Stack:     []string{},
			}
		} else if currentEntry != nil {
			// 非日志头的行视为堆栈或消息续行
			trimmed := strings.TrimSpace(line)
			if stackLineRegex.MatchString(trimmed) || strings.Contains(line, ":") {
				currentEntry.Stack = append(currentEntry.Stack, line)
			} else if trimmed != "" {
				// 可能是多行消息
				currentEntry.Message += "\n" + trimmed
			}
		}
	}

	// 保存最后一条记录
	if currentEntry != nil {
		entries = append(entries, *currentEntry)
	}

	return entries, scanner.Err()
}

// stripRichText 移除 Unity 富文本标签
func stripRichText(text string) string {
	return richTextRegex.ReplaceAllString(text, "")
}

// stripRedundantPrefix 移除富文本剥离后残留的冗余前缀
// 典型残留形如: "[INFO] ►  - Unity Version..." 或 " - 消息"
func stripRedundantPrefix(text string) string {
	text = strings.TrimSpace(text)

	// 移除 [INFO] ► / [WARNING] ► / [ERROR] ► 等标记前缀（► 后可能有多个空格）
	markers := []string{"[INFO]", "[WARNING]", "[ERROR]", "[DEBUG]", "[FATAL]"}
	for _, marker := range markers {
		if strings.HasPrefix(text, marker) {
			text = strings.TrimPrefix(text, marker)
			text = strings.TrimSpace(text)
			// 去掉紧随的箭头符号
			text = strings.TrimPrefix(text, "►")
			text = strings.TrimSpace(text)
			break
		}
	}

	// 移除分隔符 "- "（富文本日志正文前的连接符）
	if strings.HasPrefix(text, "- ") {
		text = strings.TrimPrefix(text, "- ")
	} else if text == "-" {
		text = ""
	}

	return strings.TrimSpace(text)
}

// normalizeLevel 统一日志级别名称
func normalizeLevel(level string) string {
	level = strings.ToUpper(level)
	switch level {
	case "DEBUG", "TRACE":
		return "DEBUG"
	case "INFO", "INFORMATION":
		return "INFO"
	case "WARNING", "WARN":
		return "WARNING"
	case "ERROR", "FATAL", "CRITICAL":
		return "ERROR"
	default:
		return level
	}
}

// FilterEntries 按级别和关键词过滤日志
func FilterEntries(entries []LogEntry, levels map[string]bool, keyword string) []LogEntry {
	keyword = strings.ToLower(keyword)
	var result []LogEntry

	for _, entry := range entries {
		// 级别过滤
		if len(levels) > 0 && !levels[entry.Level] {
			continue
		}

		// 关键词过滤
		if keyword != "" {
			searchText := strings.ToLower(entry.Message + " " + strings.Join(entry.Stack, " "))
			if !strings.Contains(searchText, keyword) {
				continue
			}
		}

		result = append(result, entry)
	}

	return result
}
