using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Internal;
using UnityEngineInternal;

#pragma warning disable CS0618

namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>
        /// Unity相关的实用函数。
        /// </summary>
        public static partial class Unity
        {
            private static IUpdateDriver _updateDriver;

            #region 控制协程Coroutine

            public static GameCoroutine StartCoroutine(string name, IEnumerator routine, MonoBehaviour bindBehaviour)
            {
                if (bindBehaviour == null)
                {
                    Log.Error("StartCoroutine {0} failed, bindBehaviour is null", name);
                    return null;
                }

                var behaviour = bindBehaviour;
                return StartCoroutine(behaviour, name, routine);
            }

            public static GameCoroutine StartCoroutine(string name, IEnumerator routine, GameObject bindGo)
            {
                if (bindGo == null)
                {
                    Log.Error("StartCoroutine {0} failed, BindGo is null", name);
                    return null;
                }

                var behaviour = GetDefaultBehaviour(bindGo);
                return StartCoroutine(behaviour, name, routine);
            }

            public static GameCoroutine StartGlobalCoroutine(string name, IEnumerator routine)
            {
                var coroutine = StartCoroutine(routine);
                var gameCoroutine = new GameCoroutine();
                gameCoroutine.Coroutine = coroutine;
                gameCoroutine.Name = name;
                gameCoroutine.BindBehaviour = null;
                return gameCoroutine;
            }

            public static void StopCoroutine(GameCoroutine coroutine)
            {
                if (coroutine.Coroutine != null)
                {
                    var behaviour = coroutine.BindBehaviour;
                    if (behaviour != null)
                    {
                        behaviour.StopCoroutine(coroutine.Coroutine);
                    }

                    coroutine.Coroutine = null;
                    coroutine.BindBehaviour = null;
                }
            }

            private static GameCoroutine StartCoroutine(MonoBehaviour behaviour, string name, IEnumerator routine)
            {
                var coroutine = behaviour.StartCoroutine(routine);
                var gameCoroutine = new GameCoroutine();
                gameCoroutine.Coroutine = coroutine;
                gameCoroutine.Name = name;
                gameCoroutine.BindBehaviour = behaviour;
                return gameCoroutine;
            }

            private static GameCoroutineAgent GetDefaultBehaviour(GameObject bindGameObject)
            {
                if (bindGameObject != null)
                {
                    if (bindGameObject.TryGetComponent(out GameCoroutineAgent coroutineBehaviour))
                    {
                        return coroutineBehaviour;
                    }

                    return bindGameObject.AddComponent<GameCoroutineAgent>();
                }

                return null;
            }


            public static Coroutine StartCoroutine(string methodName)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return null;
                }

                _MakeEntity();
                return _updateDriver.StartCoroutine(methodName);
            }

            public static Coroutine StartCoroutine(IEnumerator routine)
            {
                if (routine == null)
                {
                    return null;
                }

                _MakeEntity();
                return _updateDriver.StartCoroutine(routine);
            }

            public static Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return null;
                }

                _MakeEntity();
                return _updateDriver.StartCoroutine(methodName, value);
            }

            public static void StopCoroutine(string methodName)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return;
                }

                _MakeEntity();
                _updateDriver.StopCoroutine(methodName);
            }

            public static void StopCoroutine(IEnumerator routine)
            {
                if (routine == null)
                {
                    return;
                }

                _MakeEntity();
                _updateDriver.StopCoroutine(routine);
            }

            public static void StopCoroutine(Coroutine routine)
            {
                if (routine == null)
                {
                    return;
                }

                _MakeEntity();
                _updateDriver.StopCoroutine(routine);
                routine = null;
            }

            public static void StopAllCoroutines()
            {
                _MakeEntity();
                _updateDriver.StopAllCoroutines();
            }

            #endregion

            #region 注入UnityUpdate/FixedUpdate/LateUpdate

            /// <summary>
            /// 为给外部提供的 添加帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddUpdateListener(Action fun)
            {
                _MakeEntity();
                AddUpdateListenerImp(fun).Forget();
            }

            private static async UniTaskVoid AddUpdateListenerImp(Action fun)
            {
                await UniTask.Yield( /*PlayerLoopTiming.LastPreUpdate*/);
                _updateDriver.AddUpdateListener(fun);
            }

            /// <summary>
            /// 为给外部提供的 添加物理帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddFixedUpdateListener(Action fun)
            {
                _MakeEntity();
                AddFixedUpdateListenerImp(fun).Forget();
            }

            private static async UniTaskVoid AddFixedUpdateListenerImp(Action fun)
            {
                await UniTask.Yield(PlayerLoopTiming.LastEarlyUpdate);
                _updateDriver.AddFixedUpdateListener(fun);
            }

            /// <summary>
            /// 为给外部提供的 添加Late帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddLateUpdateListener(Action fun)
            {
                _MakeEntity();
                AddLateUpdateListenerImp(fun).Forget();
            }

            private static async UniTaskVoid AddLateUpdateListenerImp(Action fun)
            {
                await UniTask.Yield( /*PlayerLoopTiming.LastPreLateUpdate*/);
                _updateDriver.AddLateUpdateListener(fun);
            }

            /// <summary>
            /// 移除帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveUpdateListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveUpdateListener(fun);
            }

            /// <summary>
            /// 移除物理帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveFixedUpdateListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveFixedUpdateListener(fun);
            }

            /// <summary>
            /// 移除Late帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveLateUpdateListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveLateUpdateListener(fun);
            }

            #endregion

            #region Unity Events 注入

            /// <summary>
            /// 为给外部提供的Destroy注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddDestroyListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.AddDestroyListener(fun);
            }

            /// <summary>
            /// 为给外部提供的Destroy反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveDestroyListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveDestroyListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnDrawGizmos注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddOnDrawGizmosListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.AddOnDrawGizmosListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnDrawGizmos反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveOnDrawGizmosListener(Action fun)
            {
                _MakeEntity();
                _updateDriver.RemoveOnDrawGizmosListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationPause注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddOnApplicationPauseListener(Action<bool> fun)
            {
                _MakeEntity();
                _updateDriver.AddOnApplicationPauseListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationPause反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveOnApplicationPauseListener(Action<bool> fun)
            {
                _MakeEntity();
                _updateDriver.RemoveOnApplicationPauseListener(fun);
            }

            #endregion

            private static void _MakeEntity()
            {
                if (_updateDriver != null)
                {
                    return;
                }

                _updateDriver = ModuleSystem.GetModule<IUpdateDriver>();
            }

            #region FindObjectOfType
            public static T FindObjectOfType<T>() where T : UnityEngine.Object
            {
#if UNITY_6000_0_OR_NEWER
                return UnityEngine.Object.FindFirstObjectByType<T>();
#else
                return UnityEngine.Object.FindObjectOfType<T>();

#endif
            }

            #endregion

            #region 自定义组件事件管理

            /// <summary>
            /// 添加自定义事件监听器到指定控件的 EventTrigger 上。
            /// </summary>
            /// <param name="control">要添加监听器的控件。</param>
            /// <param name="type">事件类型。</param>
            /// <param name="action">回调函数。</param>
            public static void AddCustomEventListener(UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> action)
            {
                EventTrigger trigger = AddMonoBehaviour<EventTrigger>(control);
                EventTrigger.Entry entry = new EventTrigger.Entry
                {
                    eventID = type
                };
                if (entry.callback == null)
                {
                    entry.callback = new EventTrigger.TriggerEvent();
                }
                entry.callback.AddListener(action);
                trigger.triggers?.Add(entry);
            }

            /// <summary>
            /// 移除自定义事件监听器。
            /// </summary>
            /// <param name="control">要移除监听器的控件。</param>
            /// <param name="type">事件类型。</param>
            /// <param name="action">回调函数。</param>
            public static void RemoveCustomEventListener(UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> action)
            {
                EventTrigger trigger = control.GetComponent<EventTrigger>();
                if (trigger?.triggers != null)
                {
                    EventTrigger.Entry entry;
                    for (int i = 0; i < trigger.triggers.Count; i++)
                    {
                        entry = trigger.triggers[i];
                        if (entry?.callback == null)
                        {
                            continue;
                        }
                        if (entry.eventID == type && entry.callback.GetPersistentMethodName(0) == action.Method.Name)
                        {
                            trigger.triggers.RemoveAt(i);
                            break;
                        }
                    }
                    trigger.triggers.RemoveAll(e => e?.callback == null || e.callback?.GetPersistentEventCount() == 0);
                }
            }

            #endregion

            #region AddComponent

            /// <summary>
            /// 添加（或复用）指定类型的组件到 GameObject。
            /// </summary>
            /// <param name="type">组件类型。</param>
            /// <param name="go">目标 GameObject。</param>
            /// <returns>已存在或新添加的组件。</returns>
            [TypeInferenceRule(TypeInferenceRules.TypeReferencedByFirstArgument)]
            public static Component AddMonoBehaviour(Type type, GameObject go)
            {
                if (!go.TryGetComponent(type, out var comp))
                {
                    comp = go.AddComponent(type);
                }

                return comp;
            }

            /// <summary>
            /// 添加（或复用）指定类型的组件到组件所在 GameObject。
            /// </summary>
            /// <typeparam name="T">组件类型。</typeparam>
            /// <param name="comp">参照组件。</param>
            /// <returns>已存在或新添加的组件。</returns>
            public static T AddMonoBehaviour<T>(Component comp) where T : Component
            {
                if (!comp.TryGetComponent<T>(out var ret))
                {
                    ret = comp.gameObject.AddComponent<T>();
                }
                return ret;
            }

            /// <summary>
            /// 添加（或复用）指定类型的组件到 GameObject。
            /// </summary>
            /// <typeparam name="T">组件类型。</typeparam>
            /// <param name="go">目标 GameObject。</param>
            /// <returns>已存在或新添加的组件。</returns>
            public static T AddMonoBehaviour<T>(GameObject go) where T : Component
            {
                if (!go.TryGetComponent<T>(out var comp))
                {
                    comp = go.AddComponent<T>();
                }
                return comp;
            }

            /// <summary>
            /// 移除指定类型的组件。
            /// </summary>
            /// <param name="type">组件类型。</param>
            /// <param name="go">目标 GameObject。</param>
            [TypeInferenceRule(TypeInferenceRules.TypeReferencedByFirstArgument)]
            public static void RmvMonoBehaviour(Type type, GameObject go)
            {
                if (go.TryGetComponent(type, out var comp))
                {
                    UnityEngine.Object.Destroy(comp);
                }
            }

            /// <summary>
            /// 移除指定类型的组件。
            /// </summary>
            /// <typeparam name="T">组件类型。</typeparam>
            /// <param name="go">目标 GameObject。</param>
            public static void RmvMonoBehaviour<T>(GameObject go) where T : Component
            {
                if (go.TryGetComponent<T>(out var comp))
                {
                    UnityEngine.Object.Destroy(comp);
                }
            }

            /// <summary>
            /// 移除指定类型的组件（Editor 下防止销毁 Asset）。
            /// </summary>
            /// <typeparam name="T">组件类型。</typeparam>
            /// <param name="go">参照组件。</param>
            public static void RmvMonoBehaviour<T>(Component go) where T : Component
            {
                if (go.TryGetComponent<T>(out var comp))
                {
#if UNITY_EDITOR
                    string assetPath = UnityEditor.AssetDatabase.GetAssetPath(comp);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        Debug.LogError($"试图销毁 Asset: {assetPath}");
                        return;
                    }
#endif
                    UnityEngine.Object.DestroyImmediate(comp);
                }
            }

            #endregion

            #region 查找子节点

            /// <summary>
            /// 按路径查找子节点。
            /// </summary>
            /// <param name="transform">起始 Transform。</param>
            /// <param name="path">路径。</param>
            /// <returns>找到的 Transform，未找到返回 null。</returns>
            public static Transform FindChild(Transform transform, string path)
            {
                var findTrans = transform.Find(path);
                if (findTrans != null)
                {
                    return findTrans;
                }

                return null;
            }

            /// <summary>
            /// 递归按名字查找子节点。
            /// </summary>
            /// <param name="transform">起始 Transform。</param>
            /// <param name="name">节点名。</param>
            /// <returns>找到的 Transform，未找到返回 null。</returns>
            public static Transform FindChildByName(Transform transform, string name)
            {
                if (transform == null)
                {
                    return null;
                }

                for (int i = 0; i < transform.childCount; i++)
                {
                    var childTrans = transform.GetChild(i);
                    if (childTrans.name == name)
                    {
                        return childTrans;
                    }

                    var find = FindChildByName(childTrans, name);
                    if (find != null)
                    {
                        return find;
                    }
                }

                return null;
            }

            /// <summary>
            /// 按路径查找子节点上的组件。
            /// </summary>
            /// <param name="type">组件类型。</param>
            /// <param name="transform">起始 Transform。</param>
            /// <param name="path">路径。</param>
            /// <returns>找到的组件，未找到返回 null。</returns>
            [TypeInferenceRule(TypeInferenceRules.TypeReferencedByFirstArgument)]
            public static Component FindChildComponent(Type type, Transform transform, string path)
            {
                var findTrans = transform.Find(path);
                if (findTrans != null)
                {
                    return findTrans.gameObject.TryGetComponent(type, out var comp) ? comp : null;
                }

                return null;
            }

            /// <summary>
            /// 按路径查找子节点上的组件。
            /// </summary>
            /// <typeparam name="T">组件类型。</typeparam>
            /// <param name="transform">起始 Transform。</param>
            /// <param name="path">路径。</param>
            /// <returns>找到的组件，未找到返回 null。</returns>
            public static T FindChildComponent<T>(Transform transform, string path) where T : Component
            {
                var findTrans = transform.Find(path);
                if (findTrans != null)
                {
                    return findTrans.gameObject.TryGetComponent<T>(out var comp) ? comp : null;
                }

                return null;
            }

            #endregion

            #region Layer

            /// <summary>
            /// 递归设置 GameObject 及其所有子物体的 Layer。
            /// </summary>
            /// <param name="go">目标 GameObject。</param>
            /// <param name="layer">Layer 值。</param>
            public static void SetLayer(GameObject go, int layer)
            {
                if (go == null)
                {
                    return;
                }
                SetLayer(go.transform, layer);
            }

            /// <summary>
            /// 递归设置 Transform 及其所有子物体的 Layer。
            /// </summary>
            /// <param name="trans">目标 Transform。</param>
            /// <param name="layer">Layer 值。</param>
            public static void SetLayer(Transform trans, int layer)
            {
                if (trans == null)
                {
                    return;
                }
                var allTrans = trans.GetComponentsInChildren<Transform>();

                for (int i = 0; i < allTrans.Length; i++)
                {
                    allTrans[i].gameObject.layer = layer;
                }
            }

            #endregion

            #region 随机数

            /// <summary>
            /// 返回 [min, max) 范围内的随机整数。
            /// </summary>
            /// <param name="min">最小值（含）。</param>
            /// <param name="max">最大值（不含）。</param>
            /// <returns>随机整数。</returns>
            public static int RandomRangeInt(int min, int max)
                => UnityEngine.Random.Range(min, max);

            /// <summary>
            /// 返回 [min, max] 范围内的随机浮点数。
            /// </summary>
            /// <param name="min">最小值（含）。</param>
            /// <param name="max">最大值（含）。</param>
            /// <returns>随机浮点数。</returns>
            public static float RandomRangeFloat(float min, float max)
                => UnityEngine.Random.Range(min, max);

            /// <summary>
            /// 返回圆内的随机二维点。
            /// </summary>
            /// <param name="radius">半径。</param>
            /// <returns>随机点。</returns>
            public static Vector2 RandomInsideCircle(float radius)
                => UnityEngine.Random.insideUnitCircle * radius;

            #endregion

            #region 数组创建

            /// <summary>
            /// 创建指定类型和长度的数组。
            /// </summary>
            /// <param name="type">元素类型。</param>
            /// <param name="length">长度。</param>
            /// <returns>数组实例，length 为负返回 null。</returns>
            [TypeInferenceRule(TypeInferenceRules.TypeReferencedByFirstArgument)]
            public static Array CreateUnityArray(Type type, int length)
                => length >= 0 ? Array.CreateInstance(type, length) : null;

            /// <summary>
            /// 创建指定类型和长度的数组。
            /// </summary>
            /// <typeparam name="T">元素类型。</typeparam>
            /// <param name="length">长度。</param>
            /// <returns>数组实例，length 为负返回 null。</returns>
            public static T[] CreateUnityArray<T>(int length)
                => length >= 0 ? new T[length] : null;

            #endregion

            #region 实例化

            /// <summary>
            /// 实例化 GameObject。
            /// </summary>
            /// <param name="go">源 GameObject。</param>
            /// <returns>新实例，源为 null 返回 null。</returns>
            public static GameObject Instantiate(GameObject go)
                => go == null ? null : UnityEngine.Object.Instantiate(go);

            /// <summary>
            /// 实例化 Unity 对象。
            /// </summary>
            /// <typeparam name="T">对象类型。</typeparam>
            /// <param name="go">源对象。</param>
            /// <returns>新实例，源为 null 返回 null。</returns>
            public static T Instantiate<T>(T go) where T : UnityEngine.Object
                => go == null ? null : UnityEngine.Object.Instantiate(go);

            #endregion

            #region 物理

            /// <summary>
            /// 射线检测。
            /// </summary>
            /// <param name="ray">射线。</param>
            /// <param name="hitInfo">命中信息。</param>
            /// <param name="maxDistance">最大距离。</param>
            /// <param name="layerMask">层级掩码。</param>
            /// <returns>是否命中。</returns>
            public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask)
                => Physics.Raycast(ray, out hitInfo, maxDistance, layerMask);

            #endregion

            #region 正则

            /// <summary>
            /// 获取正则匹配的分组值列表。
            /// </summary>
            /// <param name="pattern">正则模式。</param>
            /// <param name="input">输入字符串。</param>
            /// <returns>分组值列表。</returns>
            public static List<string> GetRegexMatchGroups(string pattern, string input)
            {
                if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
                {
                    return new List<string>();
                }
                var match = Regex.Match(input, pattern);
                var results = new List<string>(match.Groups.Count);
                foreach (Group group in match.Groups)
                {
                    results.Add(group.Value);
                }
                return results;
            }

            #endregion

            #region 材质

            /// <summary>
            /// 设置材质的 Vector3 属性。
            /// </summary>
            /// <param name="mat">材质。</param>
            /// <param name="nameId">属性名 ID。</param>
            /// <param name="val">值。</param>
            public static void SetMaterialVector3(Material mat, int nameId, Vector3 val)
            {
                mat.SetVector(nameId, val);
            }

            #endregion

            #region 触摸

            /// <summary>
            /// 按 fingerId 查找触摸点。
            /// </summary>
            /// <param name="fingerId">手指 ID。</param>
            /// <param name="findTouch">输出的触摸信息。</param>
            /// <returns>是否找到。</returns>
            public static bool TryGetTouchByFingerId(int fingerId, out Touch findTouch)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.fingerId == fingerId)
                    {
                        findTouch = touch;
                        return true;
                    }
                }

                findTouch = default;
                return false;
            }

            #endregion

            #region HashCode

            /// <summary>
            /// 获取字符串的 HashCode。
            /// </summary>
            /// <param name="str">字符串。</param>
            /// <returns>HashCode。</returns>
            public static int GetHashCodeByString(string str)
                => str.GetHashCode();

            #endregion

            #region ResolutionHelper

            private static Resolution[] s_resolutions;

            /// <summary>
            /// 获取当前设备支持的分辨率列表（缓存）。
            /// </summary>
            /// <returns>分辨率数组。</returns>
            public static Resolution[] GetResolutions()
                => s_resolutions != null ? s_resolutions : s_resolutions = Screen.resolutions;

            /// <summary>
            /// 设置屏幕分辨率。
            /// </summary>
            /// <param name="width">宽。</param>
            /// <param name="height">高。</param>
            /// <param name="fullscreen">是否全屏。</param>
            public static void SetScreenResolution(int width, int height, bool fullscreen)
            {
                Screen.SetResolution(width, height, fullscreen);
            }

            /// <summary>
            /// 设置屏幕分辨率（指定全屏模式）。
            /// </summary>
            /// <param name="width">宽。</param>
            /// <param name="height">高。</param>
            /// <param name="mode">全屏模式。</param>
            public static void SetScreenResolutionWithMode(int width, int height, FullScreenMode mode)
            {
                Screen.SetResolution(width, height, mode);
            }

            /// <summary>
            /// 按分辨率索引设置屏幕分辨率。
            /// </summary>
            /// <param name="index">分辨率索引。</param>
            /// <param name="fullscreen">是否全屏。</param>
            public static void SetScreenResolution(int index, bool fullscreen)
            {
                if (index < 0 || index >= Screen.resolutions.Length)
                {
                    return;
                }
                var resolution = Screen.resolutions[index];
                Screen.SetResolution(resolution.width, resolution.height, fullscreen);
            }

            /// <summary>
            /// 按分辨率索引设置屏幕分辨率（指定全屏模式）。
            /// </summary>
            /// <param name="index">分辨率索引。</param>
            /// <param name="mode">全屏模式。</param>
            public static void SetScreenResolutionWithMode(int index, FullScreenMode mode)
            {
                if (index < 0 || index >= Screen.resolutions.Length)
                {
                    return;
                }
                var resolution = Screen.resolutions[index];
                Screen.SetResolution(resolution.width, resolution.height, mode);
            }

            #endregion
        }

        public class GameCoroutine
        {
            public string Name;
            public Coroutine Coroutine;
            public MonoBehaviour BindBehaviour;
        }

        class GameCoroutineAgent : MonoBehaviour
        {
        }
    }
}