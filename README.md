# RMUC 2026 模拟器（Net 分支）

这是 **实现了局域网联机** 的源码分支。

- 仓库：https://github.com/CityWithoutMe/rmuc-2026-simulator
- 联机代码在 **`Net`**。`main` 仍是更早的初始提交，没有这套联网。
- 不装 Unity、直接玩：下载 [局域网 Windows 客户端](https://github.com/CityWithoutMe/rmuc-2026-simulator/releases/tag/lan-client)（解压后双击 `RMNetwork.exe`）。

```bash
git clone -b Net https://github.com/CityWithoutMe/rmuc-2026-simulator.git
```

克隆前请安装 [Git LFS](https://git-lfs.com)，否则场地 FBX、天空盒等大文件只会下到指针。

## 仓库完整吗？

**源码完整。** 可执行客户端在 GitHub **Release**，不在 git 树里（避免把 100MB+ 资源塞进历史）。

`Net` 里有 Unity 工程（场景、脚本、资源、联机模块、测试和联机说明）。下面这些**故意没有放进源码**：

| 内容 | 原因 |
|------|------|
| `Build/` 打包结果（含 `RMNetwork.exe`） | 体积大，且每次构建都会变；已用 Release 发布 |
| `Library/`、`Temp/`、`Logs/` | Unity 生成缓存，对方用编辑器打开后会重建 |

想直接玩：打开 [Releases / lan-client](https://github.com/CityWithoutMe/rmuc-2026-simulator/releases/tag/lan-client)，下载 `RMNetwork-Windows-Lan.zip`。

## 打包后的游戏在哪打开？

### 从 GitHub 下载（推荐）

1. 打开 https://github.com/CityWithoutMe/rmuc-2026-simulator/releases/tag/lan-client
2. 下载 `RMNetwork-Windows-Lan.zip` 并解压
3. 双击其中的 `RMNetwork.exe`

不要只复制 exe，必须带着同目录的 `RMNetwork_Data`、`UnityPlayer.dll` 等一起用。

本机多开测试（四个窗口）：在解压目录里执行：

```powershell
1..4 | ForEach-Object { Start-Process .\RMNetwork.exe }
```

一个窗口点「创建房间」，其余窗口 IP 填 `127.0.0.1`、端口 `7777` 加入。

没焦点的窗口可能会暂停，多开时请把窗口并排放着。

### 本机已经构建过

在这台电脑上，当前可执行文件是：

`E:\模拟器开发\Build\LanBuild\RMNetwork.exe`

### 从源码自己构建

1. 安装 **Unity 6000.3.22f1**（或同大版本的 Unity 6）
2. 用 Unity Hub 打开克隆下来的工程，等 `Library` 导入完成
3. 菜单 **联机 → 构建 Windows 局域网客户端**
4. 构建完成后打开：`项目目录/Build/LanBuild/RMNetwork.exe`

也可用编辑器从 **MainMenu** 场景点 Play，走主菜单的「联网游戏」或「本地跑图」。不要只打开空的 SampleScene 当联机入口。

## 怎么联机

1～4 人都可以开局，一人当主机。详细步骤见 [README/局域网联机.md](README/局域网联机.md)。

摘要：

1. 同一局域网、同一版本客户端
2. 主机：主菜单 → 联网游戏 → 创建房间（默认端口 7777）
3. 其他人填主机 IPv4 和端口 → 通过 IP 加入
4. 各选一辆空闲车并准备；主机点开始
5. 防火墙放行专用网络；校园网客户端隔离可能导致连不上
6. 没有公网匹配、NAT 穿透或断线重连

## 工程结构（联机相关）

- `Assets/Script/Networking/`：房间、TCP 传输、主机权威同步
- `Assets/Scenes/MainMenu.unity`：联机入口
- `Assets/Scenes/SampleScene.unity`：对局场地
- `Tests/LanNetworking/`：不依赖 Unity 的传输/房间测试

```powershell
dotnet run --project Tests/LanNetworking/LanNetworking.csproj
```
