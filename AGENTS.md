# AGENTS.md — 觅长生 Mod 开发

面向 AI 助手与开发者的**工作手册入口**：环境、构建、纪律、到专题文档的索引。

> **本文件只保留「每次都要知道」的内容 + 索引。** 展开细节在 `docs/` 下分专题存放。
> 这是**渐进披露**：先读本文件，需要时再按 §5 的索引跳到对应专题。

---

## 1. 这是什么项目

**觅长生**（觅长生 / MCS）的 **BepInEx 5 + Unity Mono** mod 集合。不是游戏本体开发，
而是通过 Harmony 补丁与运行时探查扩展一个已发行 Unity 游戏的行为。

- 游戏：Unity **2018.4.36f1** / **Mono** / **x64**（**不是 IL2CPP**）
- Mod 框架：**BepInEx 5.4.17** + **HarmonyX 2.5.5**（`net472`）
- 运行方式：Linux 下通过 **Proton**

> ⚠️ **不要照搬 LongYinMods 的 IL2CPP 经验。** 本项目是纯托管 Mono：没有 `GameAssembly.dll`，
> 没有代理类型塌陷，Harmony 直接改托管方法体。IL2CPP 专属的做法（原生内存改写、detour、
> entry bytes 检查）在这里**不适用**。

### 1.1 文档分工（写东西前先看这条）

| 内容 | 写在哪 |
|---|---|
| 环境、构建命令、通用踩坑、项目约定 | `AGENTS.md`（简述）+ `docs/<专题>.md`（详情） |
| 某个 mod 的设计与取舍 | `docs/<项目>.md` |
| **游戏本身**的知识（世界观、数值规则、机制） | MCP 知识库 `add_game_knowledge` —— 与 mod 开发无关，且不随构建变化 |

---

## 2. 目录结构

```
MCSPlugins/
├── AGENTS.md              本文件 —— 入口 + 索引
├── README.md              项目简介
├── docs/                  专题文档（见 §5 索引）
├── steamapps -> ...       指向 SteamLibrary 的软链接（跨工作区边界，见 §4）
├── FastPaimai/            极速拍卖
├── LunDaoScrollable/      论道条可滚动
├── ModPatches/            多 mod 兼容补丁
└── output/                本地临时产物，不进 git
```

**新增 mod 工程时参考 [`FastPaimai/FastPaimai.csproj`](FastPaimai/FastPaimai.csproj)** —— 它是本项目
验证过可用的最小模板（引用路径、`GameDir` / `WorkShopDir` 写法都从它抄）。

---

## 3. 构建

```bash
dotnet build <Project>/<Project>.csproj -c Release
```

- 目标框架 **`net472`**；游戏程序集直接引用安装目录，**不复制到输出**（`Private=false`）。
- **NuGet 缓存重定向**：部分工程带 `nuget.config` 把包目录指向工程内 `.nuget/`。
  本机用户级缓存是只读挂载，用默认路径会以 `Read-only file system` 失败。
  （原先举的例是 McsMCP 的 `nuget.config`；那份工程已迁出，本仓库目前没有别的例子。）
- **改完代码先确认产物**（md5 / 时间戳）再去读日志找 bug，否则会在正确的代码里找不存在的错误。

---

## 4. 五条最贵重的纪律

这些都是**真金白银换来的**，且**跨 mod、跨任务都成立**：

1. **交付给游戏的路径必须用 `Z:`，不是 `S:`。** 游戏跑在 Proton 里，`S:` 是 Steam 库、
   `Z:` 才是 Linux 根。用错**不会报错** —— 文件会真实地写到别处。
   见 [`docs/path-mapping.md`](docs/path-mapping.md)。

2. **验证"文件写对了地方"，必须从宿主侧断言。**
   问游戏 `File.Exists(<刚写进去的路径>)` 是**循环论证**，永远抓不到路径错误。
   见 [`docs/path-mapping.md`](docs/path-mapping.md) §3。

3. **改完 mod 必须重启游戏才生效。** 托管插件不会热重载；日志里出现 `Loading [<你的 mod>]`
   才算真的加载了。

4. **MCP 工具描述是提示词，不是文档。** `tools/list` 的结果按**每次请求**重付，
   当前 42 个工具的描述 + schema 约 **4,858 token / 请求**。
   两条规则：只写**偏离预期**的部分（符合预期的能力与缺失都不写），参数说明交给 schema。
   **单个描述 ≤500 字符。**

5. **先测再改，并且要测「使用代价」。** 直觉上"肯定有问题"的地方，实测可能完全健康；
   反过来，功能正常但**返回体积 / 描述长度失控**的问题，在"跑一下试试"的验证下全部会通过。

---

## 5. 专题文档索引

**按「我现在要做什么」查**：

| 我要…… | 读 |
|---|---|
| 给游戏方法挂 Harmony 补丁、读写 mod 配置 | 参考现有 mod：[`FastPaimai/Main.cs`](FastPaimai/Main.cs) |
| **文件路径 / 拿不到游戏写的文件** | [`docs/path-mapping.md`](docs/path-mapping.md) |

---

## 6. McsMCP（已迁出）

McsMCP 的源码、构建与部署说明、工具文档、验证 harness **现在都维护在 UnityMCP 仓库**；
本仓库不再包含它的工程文件，速查表也随源码一起搬走了（那张表里原本还有一条已废弃的 TCP 端口）。

---

## 7. 各 mod 文档

| 项目 | 说明 | 文档 |
|---|---|---|
| FastPaimai | 极速拍卖 | [`FastPaimai/`](FastPaimai/) |
| LunDaoScrollable | 论道条可滚动 | [`LunDaoScrollable/`](LunDaoScrollable/) |
| ModPatches | 多 mod 兼容补丁 | [`ModPatches/`](ModPatches/) |

> **通用内容写 `AGENTS.md` / `docs/<专题>.md`，项目内容写 `docs/<项目>.md`，
> 新发现随代码改动一起更新（不是以后补）。**
