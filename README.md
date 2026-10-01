# CncSim - Unity CNC 加工仿真

Unity3D 下的数控加工仿真系统，核心（解析、几何、运动学、材料去除、分析等）为纯 C#，
与引擎解耦；表现层用 UGUI 自绘界面，支持中英双语与手机/平板自适应。

## 目录结构

```
Assets/
  CncSim/
    Scripts/Core/     纯 C# 核心（无 UnityEngine 依赖）
    Scripts/Runtime/  引擎表现层（场景、渲染、仿真桥、UI）
    README.md         功能对照与集成文档
  Scenes/Main.unity   主场景（入口物体挂 CncSceneBootstrap）
  Editor/             构建脚本
Packages/             依赖清单（TextMeshPro、UGUI 等）
ProjectSettings/      Unity 工程设置（2022.3.62f1）
.github/workflows/    云端构建 APK
```

## 用 Unity 打开

1. 安装 Unity **2022.3.62f1**（含 Android Build Support）
2. 用 Unity Hub 打开本仓库根目录
3. 打开 `Assets/Scenes/Main.unity`，点 Play 即可运行

## 构建 Android APK

完整的手机可照做分步说明见 `当前工作区/BUILD_ANDROID.md`（含云构建、本地 Unity、命令行三种路线与常见问题）。

### 方式一：云端自动构建（GitHub Actions）
1. 仓库 `Settings > Secrets and variables > Actions` 添加 Unity 授权：
   - 个人版：`UNITY_LICENSE`（许可证文件内容）
   - 或：`UNITY_EMAIL` + `UNITY_PASSWORD`
2. push 到 `master`，或在 Actions 页面手动运行 `Build Android APK`
3. 运行完成后在该次运行的 **Artifacts** 里下载 `CncSim-Android-APK`

### 方式二：本地命令行
```bash
Unity -quit -batchmode -nographics -projectPath . \
  -executeMethod CncSim.EditorTools.BuildAndroid.Build -logFile build.log
```
产物：`build/Android/CncSim.apk`

### 方式三：Unity 编辑器
`File > Build Settings` → Android → `Build`。构建脚本会自动导入 TextMeshPro Essential Resources。

## 功能

覆盖 94 项 CNC 仿真功能：G 代码解析与高亮编辑、三/四/五轴运动学、材料去除、
刀具轨迹、碰撞检测、时间估算、参数推荐、后处理、录制回放、程序导入导出、项目管理、
教程与速查手册等。详见 `当前工作区/Assets/CncSim/README.md`。
