# 从零导出 Android APK（手机可照做的分步说明）

本工程已经是完整 Unity 工程，包含主场景、工程设置、依赖清单、构建脚本和云构建配置。
下面给你三条路线，按你的设备条件选一条。

---

## 前置：把代码推到 GitHub

本机沙箱无法连接 GitHub，所以推送需要你在自己的网络环境里执行一次。

方式 A（有电脑 / 能用终端）：

```bash
git clone <你的仓库地址>
cd cnc-sim
git push -u origin master
```

方式 B（只有手机）：
1. 安装一个 Git 客户端 App（Android 推荐 "Termux" 或 "GitHub 官方 App 无法提交"，iOS 推荐 "Working Copy"）
2. 用 App 克隆 `https://github.com/jsonbin2026/cnc-sim.git`
3. 把本仓库文件覆盖进去后提交并推送
4. 用户名 `jsonbin2026`，密码用 Personal Access Token
   - 打开 `https://github.com/settings/tokens` → Generate new token (classic) → 勾 `repo` → 生成并复制

> 如果你本来的仓库就是 `jsonbin2026/cnc-sim`，直接推送即可。

---

## 路线一：GitHub Actions 云构建（推荐，全程手机可完成）

不需要电脑，也不需要本地装 Unity，编译在云端完成。

### 第 1 步：配置 Unity 授权
打开仓库 → `Settings` → 左侧 `Secrets and variables` → `Actions` → `New repository secret`。

**个人版 Unity（免费）** 需要一份许可证文件内容，作为 `UNITY_LICENSE`：
1. 电脑上安装 Unity Hub 与对应版本 Unity，登录一次
2. 找到许可证文件：
   - Windows：`C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS：`/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux：`~/.local/share/unity3d/Unity/Unity_lic.ulf`
3. 用文本编辑器打开，**整段内容**复制
4. 在 GitHub 新增 Secret，名字 `UNITY_LICENSE`，值粘贴许可证全文

**或改用账号密码**（更简单，但需谨慎）：
新增两个 Secret：`UNITY_EMAIL`（你的 Unity 账号邮箱）、`UNITY_PASSWORD`（密码）。

### 第 2 步：触发构建
- 推送到 `master` 分支会自动触发；或
- 打开仓库 `Actions` 标签 → 左侧 `Build Android APK` → 右侧 `Run workflow`

### 第 3 步：下载 APK
构建完成后（首次约 10-20 分钟）：
1. 进入该次运行记录
2. 页面底部 `Artifacts` 区域
3. 下载 `CncSim-Android-APK`（是个 zip，解压得到 `CncSim.apk`）

### 第 4 步：安装到手机
把 `CncSim.apk` 传到手机，点击安装（需在系统设置里允许"安装未知来源应用"）。

---

## 路线二：本地 Unity 编辑器导出（需要一台电脑）

### 第 1 步：安装 Unity
1. 下载 Unity Hub
2. 在 Hub 里安装 **Unity 2022.3.62f1**（与本工程 `ProjectSettings/ProjectVersion.txt` 一致）
3. 安装时勾选 **Android Build Support**，并勾选其下的
   **Android SDK & NDK Tools**、**OpenJDK**（否则无法打包）

### 第 2 步：打开工程
1. Unity Hub → `Open` → 选择本仓库根目录（含 `Assets`、`ProjectSettings` 的那层）
2. 首次打开会导入资源，等待完成

### 第 3 步：确认 TMP 资源
菜单 `Window > TextMeshPro > Import TMP Essential Resources`，点 Import。
（构建脚本也会自动导入，但手动做一次最稳，否则界面文字不显示。）

### 第 4 步：切换平台
`File > Build Settings` → 左侧选 `Android` → 点 `Switch Platform`，等进度条走完。

### 第 5 步：Player 设置（一般已配好，核对即可）
`Player Settings` 里确认：
- `Company Name` / `Product Name`（可自定义，如 MonkeyCode / CncSim）
- `Other Settings > Scripting Backend` = **IL2CPP**
- `Other Settings > Target Architectures` 勾 **ARM64**（必选，可加 ARMv7）
- `Other Settings > Minimum API Level` = **24** 及以上
- `Resolution and Presentation > Default Orientation` = Auto Rotation

### 第 6 步：场景列表
`Build Settings` 的 `Scenes In Build` 里确认有 `Assets/Scenes/Main.unity` 且前面打勾。
没有就点 `Add Open Scenes`。

### 第 7 步：打包
点 `Build`：
- 若勾选 `Build App Bundle (Google Play)` → 出 `.aab`（上架 Google Play 用）
- 不勾 → 出 `.apk`（直接安装用）
选好保存路径，等待编译完成。

### 第 8 步：安装
把 APK 传到手机安装，或电脑接 USB 后执行：

```bash
adb install -r CncSim.apk
```

---

## 路线三：本地命令行打包（有电脑且想自动化）

配好 Unity 与 Android SDK 环境变量后，在工程根目录执行：

```bash
Unity -quit -batchmode -nographics -projectPath . \
  -executeMethod CncSim.EditorTools.BuildAndroid.Build -logFile build.log
```

产物固定在 `build/Android/CncSim.apk`，日志在 `build.log`。

macOS 上 Unity 可执行文件通常在：
`/Applications/Unity/Hub/Editor/2022.3.62f1/Unity.app/Contents/MacOS/Unity`

---

## 常见问题

**Q：构建报错说找不到 TextMeshPro / 字体？**
未导入 TMP Essential Resources。执行 `Window > TextMeshPro > Import TMP Essential Resources`。

**Q：界面中文显示成方块？**
默认字体不含中文字形。需要：下载一个中文字体（如思源黑体）→
`Window > TextMeshPro > Font Asset Creator` 生成 TMP Font Asset →
在 `ProjectSettings` 的 `TMP Settings` 里设为默认字体。

**Q：想换应用包名 / 名称？**
`Edit > Project Settings > Player`：`Product Name` 改名称；
`Other Settings > Package Name` 改包名（如 `com.yourname.cncsim`，不能有中文）。

**Q：APK 装不上？**
确认是 `.apk` 不是 `.aab`；`.aab` 是上架格式，不能直接安装。

**Q：构建卡在 "Building Scene" 很久？**
首次 IL2CPP 编译较慢属正常，耐心等待。

**Q：Actions 报 Unity license 错误？**
检查 `UNITY_LICENSE` Secret 是否粘贴了完整的 `.ulf` 文件内容，且版本与 `2022.3.62f1` 匹配。

---

## 快速对照表

| 项目 | 值 |
|---|---|
| Unity 版本 | 2022.3.62f1 (LTS) |
| 主场景 | `Assets/Scenes/Main.unity` |
| 入口组件 | `CncSceneBootstrap`（挂在场景唯一物体 `CncBootstrap` 上） |
| 脚本后端 | IL2CPP |
| 目标架构 | ARM64 |
| 最低 API | 24 |
| APK 输出 | `build/Android/CncSim.apk` |
| 云构建产物 | Actions Artifacts → `CncSim-Android-APK` |
