# 路径映射：Wine/Proton 环境下游戏与宿主是两个文件系统

本文是 AGENTS.md §1 的展开。**触发条件**：任何需要把文件路径交给游戏进程（或被游戏进程返回）
的操作 —— MCP 工具传参、mod 读写配置、从外部读游戏日志、放截图。

---

## 1. 为什么需要知道这个

目标是 **Windows 版觅长生**，在 Linux 上用 **Proton** 运行。因此：

> **游戏进程看到的路径 ≠ 你在 shell 里看到的路径。**

两者不是"格式不同"，而是**真的指向不同的位置**。忽略这一点不会报错，只会把文件放到别处。

---

## 2. 盘符映射（实测）

在游戏进程内查询 `Directory.GetLogicalDrives()`，只有三个盘：

| 盘符 | 映射到 |
|---|---|
| `C:` | Wine prefix |
| `S:` | **Steam 库根目录** — `/run/media/huang/Games/SteamLibrary` |
| `Z:` | **Linux 根目录** — `/` |

实测依据：

```
cwd            : S:\steamapps\common\觅长生        ← 游戏 cwd 在 S: 下，极具误导性
S:\ 一级目录   : home | steamapps | tmp
Z:\ 一级目录   : bin | etc | home | lib | lib32 | lib64 | mnt | opt | run | sbin | srv | tmp | usr | var
```

`Z:\` 是标准 Linux 根布局 → 确认 `Z: = /`。

### 换算规则

```
宿主 /home/huang/project/MCSPlugins/output/x.png
游戏 Z:\home\huang\project\MCSPlugins\output\x.png
```

即 **`/` 前面加 `Z:`，斜杠转反斜杠**。

**本仓库位于 `/home/huang/project/MCSPlugins` → 游戏内是 `Z:\home\huang\project\MCSPlugins`。**

---

## 3. 实例：一次真实的误用

给 `take_screenshot` 传了 `S:\home\huang\project\MCSPlugins\output\_shot.png`。**没有任何报错**：

1. Windows 会自动创建不存在的父目录；
2. 文件被真实写到 `/run/media/huang/Games/SteamLibrary/home/…`
   —— 在 Steam 库里凭空长出一棵 `home/` 树；
3. 工具返回成功，文件确实是有效 PNG（4.3 MB，3840×2160）。

**缺的只是一个正确的盘符，代价是文件根本不在预期的位置。**

### 3.1 为什么"验证"没抓到它

当时的验证是让游戏执行：

```csharp
File.Exists(@"S:\home\huang\project\MCSPlugins\output\_shot.png")   // → true
```

**这是循环论证**：它只证明**游戏刚刚创建的那个文件存在**，永远无法暴露盘符写错 ——
写入和验证用的是同一个错误路径。

对照实验（用另一种盘符读回）：

```
Z: 写入 → Z: 读回 = fromZ      且宿主侧能看到该文件
Z: 写入 → S: 读回 = false      ← 二者确实是不同位置
```

### 3.2 正确的验证方式

```
✓ 宿主侧断言   existsSync('/home/huang/project/MCSPlugins/output/_shot.png')
✓ 问目录映射   Directory.GetDirectories(@"S:\")     // 出现不该有的 home 即露馅
✗ 自证         File.Exists(<刚写进去的同一个路径>)
```

已落到该 MCP 服务端的端到端检查里（那份工程现维护在 UnityMCP 仓库）：
**同时**断言"游戏确认写了"**和**"文件出现在宿主预期路径"，两者都过才算通过。

用错误盘符反向验证过它的区分度：

```
MCS_SHOT_PATH='S:\home\...' node live-check.mjs
  ok   the game confirms the file exists at the requested path   ← 仍通过（证明该探针无用）
  FAIL file reached the requested HOST path ...                  ← 正确探针失败
```

---

## 4. 沙箱与游戏的文件系统也不同（另一个独立因素）

某些工作环境（如带私有 `/tmp` 的沙箱）里，宿主 shell 与游戏进程可见的目录并不一致：

- **仓库目录通常是共享的**（bind mount）→ 跨进程传文件优先用仓库内路径；
- **`/tmp` 往往不是** → 用 `tmpdir()` 得到的路径，游戏写进去的东西你在沙箱里看不到。

这与盘符问题是**两个独立原因**，都可能表现为"文件不见了"。排查时先分清是哪一类：
看路径是不是 `Z:` 开头（盘符问题），或者换到仓库目录下再试（沙箱问题）。

---

## 5. 游戏日志的位置

```
steamapps/workshop/content/1189490/2824349934/BepInEx/LogOutput.log
```

仓库内的 `steamapps` 是指向 `/run/media/huang/Games/SteamLibrary/steamapps` 的符号链接，
所以 `S:\steamapps\...` 与仓库内的 `steamapps/...` **是同一处** —— 这是唯一一个两种视角都方便的地方。
