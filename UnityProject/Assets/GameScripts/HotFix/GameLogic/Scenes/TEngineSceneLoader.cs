using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using TEngine; // 确保引用了 TEngine
using Cysharp.Threading.Tasks; // UniTask 必须引用这个


namespace GameLogic
{

    public class TEngineBatchLoader : MonoBehaviour
    {
        public List<string> prefabNames; // 填入预制体名字
        public int batchSize = 2; // 每帧生成的数量

        private CancellationTokenSource _cts;

        private async void Start()
        {
            _cts = new CancellationTokenSource();
            await LoadAllPrefabs(_cts.Token);
        }

        private async UniTask LoadAllPrefabs(CancellationToken token)
        {
            int count = 0;

            foreach (var name in prefabNames)
            {
                try
                {
                    // 1. 直接 await 框架的方法
                    // 注意：由于返回 UniTask<GameObject>，它会自动实例化
                    GameObject go = await GameModule.Resource.LoadGameObjectAsync(name, parent: null, cancellationToken: token);

                    if (go != null)
                    {
                        // 预制体自带坐标，实例化后会自动出现在保存时的位置
                        go.name = name;
                    }

                    // 2. 分批控制：每加载 batchSize 个物体，等一帧
                    count++;
                    if (count >= batchSize)
                    {
                        count = 0;
                        // UniTask 的等一帧写法，等同于 yield return null
                        await UniTask.Yield(PlayerLoopTiming.Update, token);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"加载资源 {name} 出错: {e.Message}");
                }
            }

            Debug.Log("1.8GB 资源已分批加载完毕，帧率平稳。");
        }

        private void OnDestroy()
        {
            // 记得取消未完成的加载，防止切换场景时报错
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
