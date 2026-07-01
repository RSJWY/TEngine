using System;

namespace GameLogic
{
    /// <summary>
    /// 标记一个普通数据类型需要生成数据绑定器。
    /// <para>这是自定义数据绑定入口，不表示 UI 绑定。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class DataBindingModelAttribute : Attribute
    {
    }

    /// <summary>
    /// 标记字段或属性不参与数据绑定生成。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class DataBindIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// 为高频数值字段设置容差，避免微小抖动反复触发刷新。
    /// </summary>
    /// <remarks>
    /// 当前支持 float、double、Vector2、Vector3、Vector4、Quaternion，其他类型回退到默认相等比较。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class DataBindToleranceAttribute : Attribute
    {
        public DataBindToleranceAttribute(float tolerance)
        {
            Tolerance = tolerance;
        }

        /// <summary>
        /// 相等判断容差。Quaternion 使用角度作为单位。
        /// </summary>
        public float Tolerance { get; }
    }

    /// <summary>
    /// 将 bool 字段或属性生成为一次性信号。
    /// </summary>
    /// <remarks>
    /// 生成器按 false 到 true 的边沿触发信号，持续 true 不会重复触发。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class DataBindSignalAttribute : Attribute
    {
    }
}
