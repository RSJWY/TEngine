#if UNITY_EDITOR
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace TEngine
{
    /// <summary>
    /// 异步操作可视化监控组件（仅编辑器生效）。
    /// <remarks>挂载到任意物体（推荐 GameEntry 下），在 Inspector 中实时查看所有调度器上正在执行的异步操作。</remarks>
    /// </summary>
    [AddComponentMenu("TEngine/Async Operation Monitor")]
    public class AsyncOperationMonitor : MonoBehaviour
    {
        private class OperationRecord
        {
            public string OperationName;
            public string Description;
            public string SchedulerName;
            public uint Priority;
            public float Progress;
            public OperationStatus Status;
            public string Error;
            public int ElapsedFrames;
            public long ElapsedMilliseconds;
            public bool IsWaitForCompletion;
            public int StartFrame;
            public int CompletedFrame;
        }

        private class SchedulerRecord
        {
            public string Name;
            public uint Priority;
            public int RunningCount;
            public int PendingCount;
        }

        [Range(0.1f, 2f)]
        [Tooltip("刷新间隔（秒）")]
        public float refreshInterval = 0.5f;

        [Tooltip("操作完成后再显示几帧（便于观察结束状态）")]
        public int lingerFrames = 60;

        [Tooltip("是否记录已完成操作的历史")]
        public bool keepHistory = true;

        [Tooltip("历史记录上限")]
        public int maxHistory = 200;

        private float _nextRefreshTime;
        private readonly System.Collections.Generic.List<SchedulerRecord> _schedulers = new System.Collections.Generic.List<SchedulerRecord>(8);
        private readonly System.Collections.Generic.List<OperationRecord> _operations = new System.Collections.Generic.List<OperationRecord>(64);
        private readonly System.Collections.Generic.List<OperationRecord> _history = new System.Collections.Generic.List<OperationRecord>(64);
        private readonly StringBuilder _sb = new StringBuilder(512);

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshInterval;
            Snapshot();
        }

        private void Snapshot()
        {
            // 轮转：当前操作列表转历史
            if (keepHistory)
            {
                foreach (var op in _operations)
                {
                    if (op.Status == OperationStatus.Succeeded || op.Status == OperationStatus.Failed)
                    {
                        op.CompletedFrame = Time.frameCount;
                        _history.Insert(0, op);
                    }
                }

                while (_history.Count > maxHistory)
                {
                    _history.RemoveAt(_history.Count - 1);
                }
            }

            _operations.Clear();
            _schedulers.Clear();

            if (!ModuleSystemReady())
            {
                return;
            }

            var module = ModuleSystem.GetModule<IAsyncOperationModule>();
            if (module == null)
            {
                return;
            }

            foreach (var schedulerInfo in module.GetDebugInfo())
            {
                _schedulers.Add(new SchedulerRecord
                {
                    Name = schedulerInfo.Name,
                    Priority = schedulerInfo.Priority,
                    RunningCount = schedulerInfo.RunningCount,
                    PendingCount = schedulerInfo.PendingCount,
                });

                foreach (var opInfo in schedulerInfo.Operations)
                {
                    OperationRecord record = null;

                    // 历史续接：同一操作实例（同名+同起始帧）保留原有 StartFrame
                    if (keepHistory)
                    {
                        for (int i = _history.Count - 1; i >= 0; i--)
                        {
                            var h = _history[i];
                            if (h.OperationName == opInfo.OperationName && h.StartFrame > 0)
                            {
                                record = h;
                                _history.RemoveAt(i);
                                break;
                            }
                        }
                    }

                    if (record == null)
                    {
                        record = new OperationRecord();
                        record.StartFrame = Time.frameCount;
                    }

                    record.OperationName = opInfo.OperationName;
                    record.Description = opInfo.Description;
                    record.SchedulerName = opInfo.SchedulerName;
                    record.Priority = opInfo.Priority;
                    record.Progress = opInfo.Progress;
                    record.Status = opInfo.Status;
                    record.Error = opInfo.Error;
                    record.ElapsedFrames = opInfo.ElapsedFrames;
                    record.ElapsedMilliseconds = opInfo.ElapsedMilliseconds;
                    record.IsWaitForCompletion = opInfo.IsWaitForCompletion;

                    _operations.Add(record);
                }
            }
        }

        private static bool ModuleSystemReady()
        {
            try
            {
                var module = ModuleSystem.GetModule<IAsyncOperationModule>();
                return module != null;
            }
            catch
            {
                return false;
            }
        }

        private void PruneStaleHistory()
        {
            int cutoff = Time.frameCount - lingerFrames;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].CompletedFrame > 0 && _history[i].CompletedFrame < cutoff)
                {
                    _history.RemoveAt(i);
                }
            }
        }

        [CustomEditor(typeof(AsyncOperationMonitor))]
        private class AsyncOperationMonitorEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();

                var monitor = (AsyncOperationMonitor)target;
                monitor.PruneStaleHistory();

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Schedulers", EditorStyles.boldLabel);
                if (monitor._schedulers.Count == 0)
                {
                    EditorGUILayout.HelpBox("ModuleSystem 未初始化，或当前没有注册的调度器。", MessageType.None);
                }
                else
                {
                    var sb = monitor._sb;
                    sb.Clear();
                    foreach (var scheduler in monitor._schedulers)
                    {
                        sb.AppendLine($"{scheduler.Name}  (priority {scheduler.Priority})  running:{scheduler.RunningCount}  pending:{scheduler.PendingCount}");
                    }

                    EditorGUILayout.TextArea(sb.ToString(), GUILayout.ExpandHeight(false));
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"Active Operations ({monitor._operations.Count})", EditorStyles.boldLabel);
                if (monitor._operations.Count == 0)
                {
                    EditorGUILayout.HelpBox("当前没有正在执行的异步操作。", MessageType.None);
                }
                else
                {
                    foreach (var op in monitor._operations)
                    {
                        DrawOperation(op);
                    }
                }

                if (monitor.keepHistory)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField($"Completed History ({monitor._history.Count})", EditorStyles.boldLabel);
                    for (int i = 0; i < monitor._history.Count && i < 20; i++)
                    {
                        DrawOperation(monitor._history[i], dimmed: true);
                    }
                }

                Repaint();
            }

            private static void DrawOperation(OperationRecord op, bool dimmed = false)
            {
                var statusColor = op.Status switch
                {
                    OperationStatus.Processing => new Color(0.35f, 0.75f, 1f),
                    OperationStatus.Succeeded => new Color(0.4f, 0.85f, 0.4f),
                    OperationStatus.Failed => new Color(1f, 0.4f, 0.4f),
                    _ => Color.gray,
                };

                var originalColor = GUI.color;
                if (dimmed)
                {
                    GUI.color = new Color(0.75f, 0.75f, 0.75f);
                }

                EditorGUILayout.BeginVertical("Helpbox");
                var rect = EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"<color=#{ColorUtility.ToHtmlStringRGB(statusColor)}>●</color> <b>{op.OperationName}</b>  [{op.Status}]  {op.Progress:P0}  {op.ElapsedMilliseconds}ms ({op.ElapsedFrames}f)  P{op.Priority}  @{op.SchedulerName}", new GUIStyle(EditorStyles.label) { richText = true });
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(op.Description))
                {
                    EditorGUILayout.LabelField(" ", op.Description, EditorStyles.miniLabel);
                }

                if (!string.IsNullOrEmpty(op.Error))
                {
                    EditorGUILayout.LabelField("Error: " + op.Error, EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.EndVertical();
                GUI.color = originalColor;
            }
        }
    }
}
#endif
