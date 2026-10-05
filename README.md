# 觅长生mod源码

## LunDaoScrollable

[论道条可滚动](https://steamcommunity.com/sharedfiles/filedetails/?id=3335111171)

## ModPatches

[多mod兼容](https://steamcommunity.com/sharedfiles/filedetails/?id=3335773793)

## FastPaimai

[极速拍卖](https://steamcommunity.com/sharedfiles/filedetails/?id=3493753835)

## McsMCP

在游戏内运行的 [Model Context Protocol](https://modelcontextprotocol.io/) 服务端，让 AI 助手可以
实时检视和操作运行中的游戏（读取日志、执行 C#、查找对象、改配置、查补丁冲突等）。

本工程由 MelonLoader + IL2CPP 版本的 **MelonMCP** 迁移而来，适配本作的
**BepInEx 5.4.17 + Unity Mono** 环境。

构建与使用说明、以及迁移中做了哪些改动（为什么删掉了 disasm 系列工具、为什么换成
net35 的 mcs.dll、启用了哪些原本被禁用的工具）见 [McsMCP/README.md](McsMCP/README.md)。

```bash
cd McsMCP
# 必须 -c Release；产物是单文件 bin/Release/net472/McsMCP.dll
dotnet build -c Release
```
