# McsMCP：游戏内 MCP 服务端

**本文是 McsMCP 的项目文档**（架构、实现、工具设计取舍与实测记录）。
安装、构建、工具清单见 [`McsMCP/README.md`](../McsMCP/README.md)。

**跨项目可复用的协议知识不在这里** —— 那些在
[`mcp-streamable-http.md`](mcp-streamable-http.md)（可整体交给别的项目参考）。

---

## 1. 结构

```
McsMCP/
├── McsMCPPlugin.cs          入口：BepInEx BaseUnityPlugin，启动两个传输、日志捕获
├── McsMCPConfig.cs          自身配置（端口、传输开关、日志开关）
├── Server/
│   ├── MCPServer.cs         JSON-RPC 分发、工具注册表、主线程编组 —— 两个传输共用
│   ├── MCPHttpServer.cs     Streamable HTTP 传输（本项目新增）
│   ├── MCPProtocol.cs       消息类型 + 协议版本常量
│   ├── UnityMainThreadDispatcher.cs
│   ├── MainThreadWatchdog.cs
│   ├── PatchIntrospection.cs
│   └── UnityHelper.cs       Unity 对象访问（反射为主）
├── Tools/                   44 个工具
└── tests/                   无游戏环境的验证工程（见 §6）
```

两个传输**收敛到同一个 `MCPServer.HandleRequest`**，所以 HTTP 层只加帧格式，工具注册表、
分发与主线程编组全部共享、零改动。

| 传输 | 端口 | 客户端要求 |
|---|---|---|
| Streamable HTTP（推荐） | 27016 `/mcp` | 支持 `streamable-http` 的客户端，**免 bridge** |
| TCP 换行分隔 JSON-RPC（遗留） | 27015 | 需要 bridge 脚本 |

两者配置独立可开关（`Transport/EnableHttp`、`Transport/EnableTcp`）。
**端口必须分开** —— 两个监听器无法绑定同一端口，而两者默认都开。

---

## 2. 从 MelonMCP 迁移时改了什么

本工程由 LongYinMods 的 **MelonMCP**（MelonLoader + IL2CPP）迁移而来。

### 2.1 移除：IL2CPP 专属的原生工具

`disasm` / `read_mem` / `resolve_jump` 整组删除，连同 `NativeMemory.cs`、`DisassemblyHelper.cs`
与 **Iced** 依赖。

它们存在的唯一理由是回答一个 IL2CPP 问题：*"原生 detour 到底装上没有？"* ——
IL2CPP 下托管 `MethodBase` 只是包装，Il2CppInterop 把原生入口改写成 `ff 25` 跳转，
真实代码在 `GameAssembly.dll` 里，所以只有读**运行中进程的内存**才能分辨。

Mono 下这些**全部不成立**，而且留着会**主动误导**：Harmony 直接改托管方法体，
打上补丁后原生入口字节**不变** —— 看到未修改的序言会被读成"没打上补丁"，
恰是 IL2CPP 版当初要防止的误判的反面。

`hook_patch_info` 因此保留但重写：报告 patcher 类型、有效性与完整补丁列表，
`entryBytes` 缺省时返回 `entryBytesUnavailable` 说明原因。

### 2.2 重写

| 子系统 | 原因 |
|---|---|
| **代码执行** | 换用 **net35 的 mcs.dll**（见 §3） |
| **日志捕获** | MelonLoader 是两个静态事件；BepInEx 是 `Logger.Sources` 列表且**懒创建**，需定时重扫 |
| **配置工具** | MelonPreferences → BepInEx `ConfigFile`，经 `Chainloader.PluginInfos` 枚举 |
| **关闭/暂停** | MelonLoader 的 `_definiteQuit`（为跨热重载保活）在 BepInEx 无对应物，删除 |

### 2.3 启用：8 个原本被禁用的工具

IL2CPP 下 Il2CppInterop 把所有组件塌陷成裸 `UnityEngine.Component`，
于是 `GetType().Name` 永不匹配，这些工具只能回答 `"Component 'X' not found"`：

`find_game_object`、`list_components`、`inspect_component`、`toggle_behaviour`、`set_property`、
`invoke_method`、`inspect_material`、`take_screenshot`。

**该塌陷在 Mono 下不存在。** 实机验证：

```
list_game_objects → "components":["Transform","NewGongGaoInput"]   ← 真实类型名
```

### 2.4 net472 / Mono 可移植性修正

以下都能在 IL2CPP/net6 上编译，在 net472 上失败（每处都在源码就地注释）：

| 问题 | 处理 |
|---|---|
| `Environment.TickCount64`（.NET Core 3.0+） | 改用 `TickCount`（int），跨 49.7 天回绕用 unchecked 减法 |
| `string.Contains(string, StringComparison)`（Core 2.1+） | `IndexOf(...) >= 0` |
| `Path.GetRelativePath`（Core 2.0+） | 自写 `MakeRelativePath` |
| `StreamReader(Stream, Encoding, bool)`（Core） | 完整 5 参数重载 |
| `GetComponentsInChildren<T>()` 返回 `T[]` 而非 `Il2CppReferenceArray<T>` | 用 `.Length` 而非 `.Count` |
| `BaseUnityPlugin.Logger` 是 protected | `public static ManualLogSource Log`（**不同名**，否则 CS0122） |
| 本游戏无 `UnityEngine.SceneManagementModule.dll` | `SceneManager` 在 `CoreModule` 内 |

---

## 3. `execute_csharp`：语言能力与 mcs.dll 版本

**问题**：为什么用 net35 的 mcs.dll，语言特性会不会不够？

### 3.1 两个独立开关

| 开关 | 决定什么 | 本工程 |
|---|---|---|
| `mcs.dll` 的**目标框架** | 能不能**加载**（依赖能否解析） | net35 —— 必须 |
| `CompilerSettings.LanguageVersion` | 能用**哪代语法** | `Experimental`（已是天花板） |

net35 只影响程序集引用，**不影响语法**。
`Mono.CSharp.LanguageVersion` 枚举最高是 `V_7_2`：

```
ISO_1 ISO_2 V_3 V_4 V_5 V_6 V_7 V_7_1 V_7_2 Experimental Default Latest
```

**所以选 net35 没有牺牲任何语言特性。** net6 的 mcs.dll 不会带来更多语法，
只会把依赖换成一堆 Unity Mono 里不存在的 contract 程序集：

| | 引用 |
|---|---|
| `lib/net6/mcs.dll`（原版） | `System.Runtime 6.0.0.0`、`System.Collections 6.0.0.0` 等 **16 个** contract 程序集 |
| `lib/net35/mcs.dll`（本工程） | `mscorlib`、`System`、`System.Core`、`MonoMod.RuntimeDetour` |

Unity Mono 4.x profile 没有那些 contract 程序集，加载会在类型解析阶段失败 ——
**症状只会是"execute_csharp 坏了"**，不会指出真正原因。
这与 UnityExplorer 给 BepInEx/Mono 构建配 net35 mcs.dll 是同一个理由。

### 3.2 实测语法矩阵

脚本 [`tests/mcs-lang/`](../McsMCP/tests/mcs-lang/)，24 项，可重复运行。

**能用**：auto-property、LINQ（方法/lambda 形式）、`dynamic`、表达式体成员、
auto-prop 初始化器、字符串插值、空条件 `?.`、`nameof`、tuple 与解构、`out var`、`is` 模式、
throw 表达式、`default` 字面量、readonly struct。

**不能用**：C# 8 的 `switch` 表达式 / `using` 声明 / `??=` / `^1` 索引、C# 9 的 `record` /
`init` / 目标类型 `new()`。

**⚠️ 三个"以为是 C# 7、实则不能用"的坑**（真正会撞到的）：

```
C# 7   switch case int i:   -> error CS0589: Internal compiler error ... NotImplementedException: type pattern matching
C# 7   case var x:          -> 同上（同一未实现分支）
C# 7   when 子句             -> error CS1525: Unexpected symbol `when'
```

`case int i:` 是**内部编译器错误**而非普通语法错误 —— 说明解析器有该分支但未实现。
反编译印证：mcs.dll 里**有** `RecursivePattern` / `PropertyPattern`（C# 8 的名字），
但走到 `NotImplementedException`。

**`is` 模式完全可用**（`if (o is string s)`），所以换写法即可。

另外 **LINQ 查询语法（`from n in ... select`）报 `CS1940`**，用方法链代替。

### 3.3 ★ 一个会静默搞死 `execute_csharp` 的依赖

**`mcs.dll` 的 `SkipVisibilityExt` 静态构造函数硬依赖 `MonoMod.RuntimeDetour 22.3.23.4`**，
而本游戏的 BepInEx 5.4.17 **只带 21.9.19.1**。

反编译该 `.cctor`：

```il
newobj MonoMod.RuntimeDetour.Hook::.ctor(AppDomain.GetAssemblies, SkipVisibilityExt.GetAssembliesPatch)
call   Hook::Apply()          // ← 类型初始化时就装 hook
```

它用 MonoMod **hook 掉 `AppDomain.GetAssemblies`**。因此：

- **第一次 `Evaluate()` 就触发，失败则每次调用都挂**；
- 报错是 `TypeInitializationException` + 内层 `FileNotFoundException`，
  看起来像"这个编译器什么都不支持"。

**真实游戏里不需要改代码** —— BepInEx/HarmonyX 已把该程序集加载进同一 AppDomain，
版本会被统一（实测 `Assembly.Load(byName 22.x)` 返回 21.9.19.1）。

但**独立 harness 里必须手动预加载**，否则得到"全部语法 FAIL"的假象 ——
这正是探测第一轮的遭遇（见 §6.1）。

> 独立旁证：**UnityExplorer 要求完全相同的 `22.3.23.4`**，且在本游戏工作正常。

---

## 4. 工具设计：面向"每次请求都付费"的读者

### 4.1 为什么这是硬约束，不是风格偏好

MCP 客户端的 `tools/list` 结果（工具名 + 描述 + `inputSchema`）会作为**工具定义**注入模型上下文，
所以：

> **工具描述不是文档，是提示词。成本按「每次请求」计，不按「每次阅读」计。**

这类问题**不会报错、不会失败、功能全部正常** —— 只是每一轮对话都在悄悄多花几千 token。
它跟"工具能不能跑通"完全正交，**在"跑一下试试"的验证下永远不会暴露。**

### 4.2 实测成本（42 个工具）

```
工具描述（description）            10,590 chars  ≈ 2,647 token
参数描述（inputSchema.properties）  8,844 chars  ≈ 2,211 token
──────────────────────────────────────────────────────────
每次请求合计                                ≈ 4,858 token
```

**参数描述也计入。** 只压 `description` 而不管 schema，最多只能省一半。

曾出现的最大单条：`execute_csharp` 的 2,832 chars（≈708 token）——
**一个工具的文本占了全量的 15%。**

动态核实命令（在 `McsMCP/` 下）：

```bash
python3 - <<'EOF'
import re,io,glob
d=p=0
for f in glob.glob('Tools/*.cs'):
    s=io.open(f,encoding='utf-8').read()
    d+=sum(len(m.group(1)) for m in re.finditer(r'Description => @"(.*?)";',s,re.S))
    for m in re.finditer(r'ToolPropertySchema\s*\{(.*?)\n\s{18,}\}',s,re.S):
        dm=re.search(r'Description\s*=\s*((?:"[^"]*"(?:\s*\+\s*)?)+)',m.group(1),re.S)
        if dm: p+=sum(len(x) for x in re.findall(r'"([^"]*)"',dm.group(1)))
print(f'desc {d:,}  schema {p:,}  total ≈{(d+p)//4:,} token/请求')
EOF
```

### 4.3 两条规则

1. **只写"违反预期"的部分。** 符合预期的能力（C# 7.2 语言）、符合预期的缺失（C# 8）
   都不必枚举 —— 它们由一句"up to 7.2"定义完了。**只有偏离标准的行为才值得点名。**
2. **参数说明交给 schema。** 描述里不要再复述参数 —— schema 本来就是干这个的，
   写两遍等于付两遍钱。

### 4.4 实测：一次返回值代价失控的改造

`take_screenshot` 原本把 PNG 内联在返回值里。实机测量：

```
base64 chars        : 12,321,176
decoded PNG bytes   : 9,240,880
approx tokens       : 3,080,294      ← 比任何模型窗口都大
```

**不是"有点吵"，而是工具实际不可用**：调用一次就爆上下文，且它"成功"返回，
agent 没有错误可恢复、无法预知代价、无法中途取消。

改后形态 `take_screenshot(path, cropX?, cropY?, cropWidth?, cropHeight?, width?)`：

- `path` **必填且必须绝对** —— 服务器工作目录是游戏的，相对路径会静静落到意外之处；
- **不再内联**，返回短 JSON（`path` / 尺寸 / `bytes` / `cropped` / `scaled`）；
- **裁剪坐标用左上原点**，与其他工具报的坐标一致（Unity 内部是左下，在 helper 内转换）；
- **先裁后缩**（先缩会采样即将丢弃的像素）；**降采样用最近邻**（均值滤波会糊掉要读的小字）；
- `width` **只降不升** —— 放大截图只加字节不加信息。

### 4.5 两个必须避开的实现陷阱

- **写文件失败必须报错，不能只 log。** 原实现写失败只 `LogWarning` 然后照常返回图片，
  于是路径不可写的调用方拿到"成功"结果而磁盘上什么都没有 —— 对专职写文件的工具是最坏结果。
- **每次都要 `Object.Destroy(texture)`。** 截图工具会被反复调用，不释放 GPU 纹理会持续累积。
  需要两层 `finally`：内层释放临时纹理，外层释放 `CaptureScreenshotAsTexture()` 返回的那张
  —— **后者不释放就是每次泄漏一张全屏纹理**。

### 4.6 体检结论（`read_logs` / `read_pseudocode_file` / `search_pseudocode`）

**先测再改**：`read_logs` 看着可疑（无过滤返回 1000 行），实测最大 ~22k token，**健康，没动**。

但同一工具里有个真 bug：`level` + `filter` **静默返回空**。代码构造正则样式的字符串
（`"[ERROR].*{filter}|{filter}.*[ERROR]"`）传给做**字面子串匹配**的 `GetLogs` ——
字面串里不会有 `.*` 或 `|`，所以从不匹配。

```
{"level":"warning"}                 -> 750 lines
{"filter":"Lua"}                    ->   8 lines
{"level":"warning","filter":"Lua"}  ->   0 lines   ← 应为 8 的子集
```

失败不可见，因为"没有匹配项"本身是合法答案。改法：让两个条件**独立**，
并在设了 level 时先取整个 buffer 再过滤。

`read_pseudocode_file` 三个缺陷：

- **`lineCount` 只写在 schema 里，代码从不 clamp** —— `Maximum` 是给客户端的提示，
  服务端不能依赖。真实反编译单文件 ~560 KB / **~140k token**，`lineCount=100000` 会整个读回。
- **路径包含检查是前缀测试**（`StartsWith(base)`）：`/decomp` 是 `/decompiled-secret` 的前缀，
  同级目录可逃逸；且默认区分大小写，在 Windows 上会**误拒**。
  改为 `StartsWith(base + 分隔符, OrdinalIgnoreCase)`。
- **截断不上报** —— 现在返回 `truncated` / `truncatedBy` / `returnedLines` 与续读 `startLine`。

`search_pseudocode`：`maxResults × (2×contextLines+1)` 行源码一次返回，两者相乘且都没 clamp，
已在代码里封顶。

### 4.7 现行约束

整理后的状态（对应 §4.2 的测法）：

```
描述全部 ≤ 500 字符；实测最长 498（add_game_knowledge）
平均 252 字符
```

**500 字符是硬上限，不是目标值。** 加新工具时按 §4.3 两条规则写；
改完用 §4.2 的命令复核。参照量级：一句简短的规则约 50–120 字符，
超过 500 基本意味着混进了"符合预期的"内容或参数说明。

---

## 5. 配置工具（重写自 MelonPreferences）

MelonPreferences → BepInEx `ConfigFile`。设计意图不变：**走活的内存模型，不手改 .cfg**
（`ConfigFile.Save()` 从内存重写整个文件，手改会被静默覆盖）。

BepInEx **没有活的 config 文件注册表**，经 `Chainloader.PluginInfos` 枚举 ——
每个 `BaseUnityPlugin` 带自己的 `Config`。**插件 GUID 作 category 标识**，
条目用 `Section.Key` 寻址（BepInEx 的 section 名是自由字符串，两个 mod 都可能有 `General`）。

### 5.1 实机发现：`ConfigDescription.ToString()` 没用

`get_config` 的 `description` 字段每条都返回字面量 `"BepInEx.Configuration.ConfigDescription"` ——
该类**没有重写 `ToString()`**，继承 `Object.ToString()` 返回类型名。

真正的文本在两个字段里：

| 字段 | 内容 |
|---|---|
| `<Description>` | mod 传给构造函数的描述串 |
| `<AcceptableValues>` | `AcceptableValueRange` 等，**它的 `ToString()` 反而好用**（`Range: 1024 to 65535`） |

两个都取 —— 约束信息才是防止 agent 写一个会被静默 clamp 的值的关键。

> 教训：**写 BepInEx 反射代码时不要相信 `ToString()`。**

---

## 6. 无游戏环境下的验证

### 6.1 三个坑（都是**测试**的错，工具本身正确）

1. **桩必须模拟真实初始化。** 首次探测所有用例全 FAIL，报同一个 `TypeInitializationException`
   —— 因为桩没把 server 实例挂到 plugin 上（真实由 `Awake` 完成），
   于是协商值取不到。**整齐地全部失败比单个失败更可疑**，先怀疑环境。
2. **游戏的文件系统与宿主不同。** 见 [`path-mapping.md`](path-mapping.md) ——
   盘符（`Z:` vs `S:`）和沙箱是两个独立原因。
3. **别用会自证的探针。** 问游戏 `File.Exists(<刚写进去的路径>)` 只能证明写入成功，
   永远抓不到路径写错。

### 6.2 测试工程

| 目录 | 用途 |
|---|---|
| [`tests/http-transport/`](../McsMCP/tests/http-transport/) | 编译**真实**传输层源码，无需游戏即可跑；`live-check.mjs` 是连接**真实游戏**的 22 项端到端检查 |
| [`tests/mcs-lang/`](../McsMCP/tests/mcs-lang/) | C# 语言特性矩阵（§3.2） |

---

## 7. 与 DSH 对接

```yaml
- insert:
    - id: mcp-mcs
      name: "@deepseek-ai/dsh-mcp-client"
      config:
        serverName: mcs
        transport: streamable-http
        url: http://127.0.0.1:27016/mcp
```

工具以 `mcp__mcs__<tool>` 暴露。协议侧细节见
[`mcp-streamable-http.md`](mcp-streamable-http.md)。
