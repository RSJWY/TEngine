using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 数据绑定生成代码使用的相等比较工具。
    /// </summary>
    public static class DataBindingComparison
    {
        /// <summary>
        /// 按容差判断新旧值是否等价。
        /// </summary>
        /// <remarks>
        /// 对 float、double 和 Unity 常用向量类型使用容差判断；其他类型回退到默认相等比较。
        /// </remarks>
        public static bool AreEqual<T>(T oldValue, T newValue, float tolerance)
        {
            object oldObject = oldValue;
            object newObject = newValue;

            if (oldObject is float oldFloat && newObject is float newFloat)
            {
                return Mathf.Abs(oldFloat - newFloat) <= tolerance;
            }

            if (oldObject is double oldDouble && newObject is double newDouble)
            {
                return Math.Abs(oldDouble - newDouble) <= tolerance;
            }

            if (oldObject is Vector2 oldVector2 && newObject is Vector2 newVector2)
            {
                return (oldVector2 - newVector2).sqrMagnitude <= tolerance * tolerance;
            }

            if (oldObject is Vector3 oldVector3 && newObject is Vector3 newVector3)
            {
                return (oldVector3 - newVector3).sqrMagnitude <= tolerance * tolerance;
            }

            if (oldObject is Vector4 oldVector4 && newObject is Vector4 newVector4)
            {
                return (oldVector4 - newVector4).sqrMagnitude <= tolerance * tolerance;
            }

            if (oldObject is Quaternion oldQuaternion && newObject is Quaternion newQuaternion)
            {
                return Quaternion.Angle(oldQuaternion, newQuaternion) <= tolerance;
            }

            return EqualityComparer<T>.Default.Equals(oldValue, newValue);
        }
    }
}
