# XLua集成

## 兼容性问题

xLua 注册 C# 类型时大量使用 `Type.FullName`、`Type.Namespace`。符号混淆后这些属性返回混淆名称，而 Lua 代码通常仍以原始名称访问，导致 `type not found`。

简单地给所有 Lua 导出类型保名可以工作，但会放弃大范围符号保护。官方推荐使用 `ObfuscationTypeMapper` 返回原始类型名。

## 修改xLua类型名读取

把 xLua 源码中需要协议名称的：

```csharp
type.FullName
```

替换为：

```csharp
ObfuscationTypeMapper.GetOriginalTypeFullNameOrCurrent(type)
```

Namespace 同理需要按官方样例 diff 处理。官方指出修改点接近十处，不能只改一个注册入口；应对照 Obfuz Sample 的 WorkWithXLua 与当前 xLua 版本逐处审计。

不要机械替换所有 `Type.FullName`：有些位置需要当前混淆名用于真实 CLR 查询，只有“面向 Lua 协议/注册名”的位置才应返回原始名。

## 注册类型映射

理论上：

```csharp
ObfuscationTypeMapper.RegisterType<MyType>("My.Namespace.MyType");
```

但字符串易因重构过期。推荐：

```csharp
ObfuscationInstincts.RegisterReflectionType<MyType>();
```

Obfuz 在混淆时从泛型 Type 提取原始名字并生成正确映射。

## 注册时机

官方要求在创建 `LuaEnv` 前注册全部类型映射。否则 xLua 初始化缓存了混淆名称后，再注册映射可能无法修复已有表。

推荐顺序：

```text
初始化Encryption Scope
  -> 加载热更新程序集
  -> 执行全部RegisterReflectionType生成/注册逻辑
  -> 创建LuaEnv
  -> 加载Lua脚本
```

## 类型清单生成

大型项目不应手写每个注册：

- 从 xLua `LuaCallCSharp`/`CSharpCallLua` 配置生成；
- 从项目 Attribute 扫描；
- 从代码生成器输出集中注册方法；
- CI 比较导出类型与注册清单；
- 对泛型封闭类型和嵌套类型单独处理。

生成代码应编译进会被 Obfuz 分析的程序集，以便 Instinct Pass 识别。

## 其他风险

- Lua 中硬编码方法名、字段名、property 名；
- `CS.Namespace.Type` 路径；
- 反射调用和 overload 选择；
- 热更新后新增导出类型未注册；
- xLua 代码生成 wrapper 处于不同程序集，需加入第 4 类引用重写列表；
- delegate/event 名称被改写；
- JSON/Lua 配置存储 CLR 全名。

若 Lua 直接按成员名访问，除了 TypeMapper，还需要保留成员名或修改 xLua 的成员映射生成方式。TypeMapper 只解决 Type 与原始类型全名，不自动恢复所有成员名。

## TEngine现状

当前工程扫描未发现 xLua 包或集成代码。本章作为未来接入指南，不代表项目已经使用 xLua。若后续加入：

1. 先确定 xLua 分支和代码生成方式。
2. 对照官方 WorkWithXLua 样例修改当前版本源码。
3. 将生成 wrapper/注册程序集纳入 Obfuz 程序集依赖审计。
4. 建立 Lua 导出 API 混淆策略。
5. 执行 Lua 全量脚本冒烟。

## 官方来源

- [与XLua协同工作](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/manual/xlua/work-with-xlua.md)
- [Obfuz+XLua入门](https://github.com/focus-creative-games/obfuz-doc/blob/main/docs/beginner/work-with-xlua.md)

