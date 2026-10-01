# 手机端获取 APK 操作清单

本文档只讲一件事：**在只有手机的情况下，如何用 GitHub Actions 云端构建出 CncSim.apk 并下载。**

工程已完成验证，配置正确。你只需完成两个你才能做的动作：把代码推到 GitHub、填 Unity 授权。
其余构建步骤由云端自动完成。

---

## 零、你需要准备的东西

| 物品 | 说明 |
|---|---|
| GitHub 账号 | 用于托管代码、跑云构建 |
| Unity 账号 | 免费个人版即可，用于提供构建授权 |
| 一个 Git 客户端 App | Android 用 Termux；iOS 用 Working Copy |
| 手机浏览器 | 操作 GitHub 网页 |

---

## 一、把工程推到 GitHub

### 1.1 在 GitHub 建仓库

1. 手机浏览器打开 `https://github.com`
2. 右上角 `+` → `New repository`
3. `Repository name` 填 `cnc-sim`
4. 选 `Public`（私有仓库也能跑 Actions，但免费额度有限）
5. **不要**勾 `Add a README file`（避免冲突）
6. 点 `Create repository`

### 1.2 生成访问令牌（当密码用）

1. 打开 `https://github.com/settings/tokens`
2. `Generate new token` → `Generate new token (classic)`
3. `Note` 随便填，如 `phone-push`
4. `Expiration` 选 30 天
5. 勾选 `repo` 这一整项
6. 拉到底点 `Generate token`
7. **立刻复制保存**这串 `ghp_...`，页面关掉就再也看不到

### 1.3 用 App 推送代码

**Android（Termux）：**

```bash
# 安装 git
pkg install git -y

# 克隆你刚建的仓库
git clone https://github.com/<你的用户名>/cnc-sim.git
cd cnc-sim
```

然后把本工程的这些内容复制进 `cnc-sim` 目录：`Assets/`、`Packages/`、`ProjectSettings/`、`.github/`、`.gitignore`、`README.md`、`BUILD_ANDROID.md`。

```bash
git add .
git commit -m "feat: CncSim unity project"
git push -u origin master
# 提示输入用户名 -> 填 GitHub 用户名
# 提示输入密码   -> 粘贴刚才的 ghp_... 令牌
```

**iOS（Working Copy）：**
1. 克隆 `https://github.com/<你的用户名>/cnc-sim.git`
2. 从「文件」App 把工程内容粘贴进仓库目录
3. Commit 并 Push，密码用 `ghp_...` 令牌

> 若你的默认分支是 `main`，把 `git push -u origin master` 改成 `main`。
> workflow 已同时监听 `master` 和 `main`，两者都会触发。

---

## 二、配置 Unity 授权（关键，缺了必失败）

任选一种方式，**推荐方式 A**。

### 方式 A：账号密码（最简单）

1. 打开你的 GitHub 仓库网页
2. `Settings` → 左侧 `Secrets and variables` → `Actions`
3. 点 `New repository secret`，添加两条：

| Name | Secret 值 |
|---|---|
| `UNITY_EMAIL` | 你的 Unity 账号邮箱 |
| `UNITY_PASSWORD` | 你的 Unity 账号密码 |

### 方式 B：许可证文件

1. 电脑上装 Unity Hub 与 2022.3.62f1，登录一次
2. 找到许可证文件：
   - Windows：`C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS：`/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux：`~/.local/share/unity3d/Unity/Unity_lic.ulf`
3. 用文本编辑器打开，**全文复制**
4. 在 GitHub 新增 Secret：Name 填 `UNITY_LICENSE`，值粘贴许可证全文

> 若你的 Unity 账号开了两步验证，方式 A 会失败，请用方式 B。

---

## 三、触发构建

### 方式一：自动触发

推送代码到 `master` 或 `main` 时自动开始。

### 方式二：手动触发

1. 打开仓库网页
2. 顶部 `Actions` 标签
3. 左侧点 `Build Android APK`
4. 右侧 `Run workflow` → 绿色按钮 `Run workflow`

---

## 四、下载 APK

1. 进入本次运行记录（`Actions` 里点最新那条）
2. 等待进度条变绿（首次约 10-20 分钟）
3. 页面滚动到底部 `Artifacts` 区域
4. 点 `CncSim-Android-APK` 下载（得到 zip）
5. 解压 zip，里面是 `CncSim.apk`

---

## 五、安装到手机

1. 把 `CncSim.apk` 传到目标手机
2. 用文件管理器点击它
3. 系统提示时允许「安装未知来源应用」
4. 完成安装，启动 `CncSim`

---

## 六、构建失败怎么办

### 看日志定位

1. `Actions` → 点失败的运行
2. 点左边红色的步骤 `Build Android APK`
3. 展开报错行，把关键错误行找出来

### 常见错误对照

| 报错关键词 | 原因 | 处理 |
|---|---|---|
| `License is not valid` / `No valid Unity license` | 授权没配或配错 | 检查 `UNITY_EMAIL`/`UNITY_PASSWORD` 或 `UNITY_LICENSE` |
| `Please provide a license` | 一条 Secret 都没配 | 按第二节配置 |
| `GameCI` 报版本 | Unity 版本不匹配 | 确认 `ProjectVersion.txt` 是 `2022.3.62f1` |
| `Editor return code 1` | 脚本编译错误 | 看日志中第一条 `error CS` |
| `TextMeshPro` 相关 | TMP 资源未导入 | 构建脚本会自动导入，失败则参考 `BUILD_ANDROID.md` |
| `No APK found` | 产物路径不符 | 本 workflow 已用 find 自动定位，若仍失败请看 `build/` 目录列表 |
| `Cannot find project` | 推到仓库的目录层级不对 | 仓库根目录应直接含 `Assets/`、`ProjectSettings/` |

---

## 七、可选：配置自动签名 Release

若想让 APK 直接通过 Release 长期保存并生成下载链接：

1. 生成 keystore（需要电脑或 Termux 装 JDK）
2. 在 `Settings → Secrets` 添加：
   - `ANDROID_KEYSTORE_BASE64`（keystore 文件的 base64）
   - `ANDROID_KEYSTORE_PASS`
   - `ANDROID_KEYALIAS_NAME`
   - `ANDROID_KEYALIAS_PASS`
3. 在 workflow 中按 game-ci 文档补 `androidKeystoreName` 等参数

不配置签名也能出 APK，用于测试安装足够。上架 Google Play 才必须正式签名。

---

## 快速对照表

| 项目 | 值 |
|---|---|
| Unity 版本 | 2022.3.62f1 (LTS) |
| 构建入口 | `CncSim.EditorTools.BuildAndroid.Build` |
| 主场景 | `Assets/Scenes/Main.unity` |
| 脚本后端 | IL2CPP |
| 目标架构 | ARM64 |
| 最低 API | 24 |
| 包名 | `com.monkeycode.cncsim` |
| workflow 文件 | `.github/workflows/android.yml` |
| 产物下载 | Actions → Artifacts → `CncSim-Android-APK` |
| 必须的 Secret | `UNITY_EMAIL` + `UNITY_PASSWORD`（或 `UNITY_LICENSE`） |
