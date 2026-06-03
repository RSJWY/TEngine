// 导入 Wails 生成的后端绑定与运行时事件
import { LoadLogFile, OpenFileDialog, OpenDefaultLogDir } from './wailsjs/go/main/App.js';
import { EventsOn } from './wailsjs/runtime/runtime.js';

// 全局状态
let allLogs = [];
let currentFilePath = '';

// DOM 元素
const logContainer = document.getElementById('logContainer');
const fileInfo = document.getElementById('fileInfo');
const statusText = document.getElementById('statusText');
const logCount = document.getElementById('logCount');
const searchInput = document.getElementById('searchInput');

// 级别过滤复选框
const filterDebug = document.getElementById('filterDebug');
const filterInfo = document.getElementById('filterInfo');
const filterWarning = document.getElementById('filterWarning');
const filterError = document.getElementById('filterError');

// 按钮
const openFileBtn = document.getElementById('openFileBtn');
const openDefaultBtn = document.getElementById('openDefaultBtn');

// 初始化
window.addEventListener('DOMContentLoaded', () => {
    // 绑定按钮
    openFileBtn.addEventListener('click', openFile);
    openDefaultBtn.addEventListener('click', openDefaultLogDir);

    // 绑定过滤器变化
    filterDebug.addEventListener('change', applyFilters);
    filterInfo.addEventListener('change', applyFilters);
    filterWarning.addEventListener('change', applyFilters);
    filterError.addEventListener('change', applyFilters);

    // 绑定搜索输入（防抖）
    let searchTimeout;
    searchInput.addEventListener('input', () => {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(applyFilters, 300);
    });

    // 监听后端文件拖拽事件
    EventsOn('file:dropped', (filePath) => {
        if (filePath) {
            loadLogFile(filePath);
        }
    });

    // 阻止浏览器默认拖拽行为，并提供视觉反馈
    setupDragVisual();
});

// 打开文件对话框（由后端弹出原生对话框）
async function openFile() {
    try {
        const filePath = await OpenFileDialog();
        if (filePath) {
            loadLogFile(filePath);
        }
    } catch (err) {
        showError('打开文件失败: ' + err);
    }
}

// 在文件管理器中打开默认日志目录
async function openDefaultLogDir() {
    try {
        await OpenDefaultLogDir();
    } catch (err) {
        showError('打开目录失败: ' + err);
    }
}

// 加载日志文件
async function loadLogFile(filePath) {
    try {
        statusText.textContent = '加载中...';
        logContainer.innerHTML = '<div class="empty-state"><p>解析日志中...</p></div>';

        const entries = await LoadLogFile(filePath);

        allLogs = entries || [];
        currentFilePath = filePath;

        const fileName = filePath.split(/[\\/]/).pop();
        fileInfo.textContent = `${fileName} (${allLogs.length} 条)`;

        applyFilters();
        statusText.textContent = `已加载 ${allLogs.length} 条日志`;
    } catch (err) {
        showError('加载日志失败: ' + err);
        logContainer.innerHTML = `<div class="empty-state"><p style="color: var(--error-color);">加载失败: ${escapeHtml(String(err))}</p></div>`;
    }
}

// 应用过滤器
function applyFilters() {
    const levels = {
        'DEBUG': filterDebug.checked,
        'INFO': filterInfo.checked,
        'WARNING': filterWarning.checked,
        'ERROR': filterError.checked
    };

    const keyword = searchInput.value.trim().toLowerCase();

    const filtered = allLogs.filter(entry => {
        // 级别过滤（未知级别默认显示）
        if (entry.level in levels && !levels[entry.level]) return false;

        // 关键词过滤
        if (keyword) {
            const stackText = entry.stack ? entry.stack.join(' ') : '';
            const searchText = (entry.message + ' ' + stackText).toLowerCase();
            if (!searchText.includes(keyword)) return false;
        }

        return true;
    });

    renderLogs(filtered);
    logCount.textContent = `显示 ${filtered.length} / ${allLogs.length} 条`;
}

// 渲染日志列表
function renderLogs(entries) {
    if (entries.length === 0) {
        logContainer.innerHTML = '<div class="empty-state"><p>无匹配的日志</p></div>';
        return;
    }

    // 用 DocumentFragment 批量插入，提升大日志渲染性能
    const fragment = document.createDocumentFragment();
    entries.forEach((entry, index) => {
        fragment.appendChild(createLogElement(entry, index));
    });

    logContainer.innerHTML = '';
    logContainer.appendChild(fragment);
}

// 创建单条日志元素
function createLogElement(entry, index) {
    const div = document.createElement('div');
    const levelClass = (entry.level || '').toLowerCase();
    div.className = `log-entry ${levelClass}`;
    div.dataset.index = index;

    const header = document.createElement('div');
    header.className = 'log-header';

    // 时间戳
    const timestamp = document.createElement('span');
    timestamp.className = 'log-timestamp';
    timestamp.textContent = entry.timestamp;

    // 级别
    const level = document.createElement('span');
    level.className = `log-level level-badge ${levelClass}`;
    level.textContent = entry.level;

    // 消息
    const message = document.createElement('div');
    message.className = 'log-message';
    message.innerHTML = highlightKeyword(escapeHtml(entry.message), searchInput.value);

    header.appendChild(timestamp);
    header.appendChild(level);
    header.appendChild(message);

    const hasStack = entry.stack && entry.stack.length > 0;

    // 堆栈切换提示
    if (hasStack) {
        const toggle = document.createElement('span');
        toggle.className = 'stack-toggle';
        toggle.textContent = `▸ ${entry.stack.length} 行`;
        header.appendChild(toggle);
    }

    div.appendChild(header);

    // 堆栈（默认隐藏）
    if (hasStack) {
        const stack = document.createElement('div');
        stack.className = 'log-stack hidden';
        entry.stack.forEach(line => {
            const stackLine = document.createElement('div');
            stackLine.className = 'log-stack-line';
            stackLine.textContent = line;
            stack.appendChild(stackLine);
        });
        div.appendChild(stack);

        // 点击头部切换堆栈显示
        header.addEventListener('click', () => {
            stack.classList.toggle('hidden');
            const toggle = header.querySelector('.stack-toggle');
            if (toggle) {
                toggle.textContent = stack.classList.contains('hidden')
                    ? `▸ ${entry.stack.length} 行`
                    : `▾ ${entry.stack.length} 行`;
            }
        });
    }

    return div;
}

// 高亮关键词
function highlightKeyword(text, keyword) {
    if (!keyword) return text;
    const regex = new RegExp(`(${escapeRegex(keyword)})`, 'gi');
    return text.replace(regex, '<mark>$1</mark>');
}

// 转义 HTML
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// 转义正则特殊字符
function escapeRegex(text) {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// 显示错误
function showError(message) {
    statusText.textContent = message;
    statusText.style.color = 'var(--error-color)';
    setTimeout(() => {
        statusText.style.color = '';
    }, 5000);
}

// 拖拽视觉反馈（实际文件路径由后端 OnFileDrop 提供）
function setupDragVisual() {
    window.addEventListener('dragover', (e) => {
        e.preventDefault();
        logContainer.classList.add('drag-over');
    });

    window.addEventListener('dragleave', (e) => {
        // 仅当离开窗口时移除
        if (e.relatedTarget === null) {
            logContainer.classList.remove('drag-over');
        }
    });

    window.addEventListener('drop', (e) => {
        e.preventDefault();
        logContainer.classList.remove('drag-over');
    });
}
