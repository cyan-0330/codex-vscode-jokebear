# Codex 可执行的安装流程

这份说明服务于用户说“请从这个 GitHub 网址安装桌宠”的场景。该用户指令授权
正常安装和启动操作；采用工具要求的网络或文件系统权限申请完成它。

## 取得可安装包

优先选择这个仓库最新的 GitHub Release：

- `codex-vscode-jokebear-<版本>-windows-x64.zip`
- `SHA256SUMS.txt`

通过同一个 Release 下载二者，使用 `Get-FileHash -Algorithm SHA256` 校验 ZIP。
解压到稳定目录，避免把长期使用的 hook executable 留在临时目录。也可下载
仓库源码，使用 Windows .NET Framework 4.x 内置 csc.exe 编译；无需安装 SDK。

## 安装

在 VS Code 正常 Windows 用户会话中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

默认目标：`%LOCALAPPDATA%\CodexJokeBearPet`。用户指定目标时加
`-InstallDirectory '<完整路径>'`，支持中文和空格。不要请求管理员身份，除非目标
目录本身需要；安装器使用当前用户的文件和 hook 配置。

非标准 VS Code 安装可加 `-CodeExecutable '<Code.exe 完整路径>'`；非标准扩展目录
可加 `-ExtensionDirectory '<openai.chatgpt 扩展目录>'`。`CODEX_HOME` 会被尊重，
也可显式传 `-CodexDirectory`。

安装器会：

1. 确认 Windows x64、本机 VS Code 与官方 Codex 扩展以及对话路由。
2. 编译或安装两个 companion executable。
3. 从 `dependencies.lock.json` 指定 HTTPS 地址取得固定版本 Python、Petdex、
   JokeBear sprite，逐项检查 SHA-256。
4. 为当前机器生成 `petdex-integration.json`；保留对话记录与隐藏设置。
5. 选择 JokeBear 和当前外观比例；备份并保留其他宠物设置。
6. 合并安装本组件的 hooks，备份原文件并保留第三方 handlers。
7. 启动桌宠及审批伴随组件。

安装器的 `-PrepareOnly` 供隔离测试准备依赖使用，不安装 hooks、不修改原生宠物
设置、不启动桌宠；正常用户安装不要使用它。`-NoStart` 仅用于延后启动。

## 首次手动信任

[官方 hooks 文档](https://developers.openai.com/codex/hooks)规定：
“Non-managed hooks must be reviewed and trusted before they run.”

运行安装目录的 `review-hooks.cmd`。它打开 **VS Code 扩展自带**的 Codex runtime，
用户在该审阅会话中输入 `/hooks`，审阅并信任指向本包 `bin/CodexPet.Hook.exe`
的 **PermissionRequest** command handler。之后退出审阅会话并重新加载 VS Code。
这是一次明确的用户信任动作，Codex 不应代写 `[hooks.state]` 或点击真实审批。
其他状态 handlers 可以保持未信任：对话状态由只读导入同步。

如果 `/hooks` 不存在，检查使用的是检测出的扩展 bundled runtime，提示更新
VS Code Codex 扩展；不要将不存在的命令声称为已经执行。

## 验证并交付

```powershell
& '<安装目录>\bin\CodexPet.Hook.exe' --ping
Get-Content -LiteralPath '<安装目录>\data\installation-report.json'
```

确认两个 pet/companion 进程、实际可见的 JokeBear 与对话面板。最近五天有多条
VS Code 对话时，分别显示真实标题；同一目录的对话不得合并。允许一次、拒绝和
返回 VS Code 由用户在真实审批气泡中操作。不要为验证而自动批准权限。

向用户报告安装路径、启动文件和首次 trust 状态。如果认证、网络、扩展或系统
阻止了某一步，明确报告已完成和剩余步骤，不声称完整安装已经成功。
