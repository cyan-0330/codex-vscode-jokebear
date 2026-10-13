# JokeBear · VS Code Codex 桌宠

一个适用于 **Windows x64 上的 VS Code Codex 扩展**的桌宠。它沿用 JokeBear
动画，提供与桌宠连接的审批说话气泡，以及紧凑的独立对话状态面板。

## 直接告诉 Codex 安装

把这个仓库的网址发给 **VS Code 中的 Codex**，并说：

> 请按照这个仓库的 AGENTS.md 和 INSTALL_FOR_CODEX.md，安装 JokeBear 桌宠。
> 使用 VS Code Codex 接入，保留我已有的 hooks；安装完成后启动并验证。

仓库包含可执行安装流程、源码、校验清单和测试。Codex 可以下载 Release 包、
校验并执行 `install.ps1`；也可以下载源码后自动编译安装。不需要 Node.js、
npx、预装 Python、独立 Codex App 或新的 VS Code 扩展。

首次安装有一个必须手动完成的步骤：**审阅并信任审批处理器**。这是
[Codex 官方 hooks 机制](https://developers.openai.com/codex/hooks)的要求。
安装完成后运行 `review-hooks.cmd`，在打开的 Codex 审阅会话中输入 `/hooks`，
审阅并信任 `CodexPet.Hook.exe` 的 **PermissionRequest** 处理器，然后重新加载
VS Code 窗口。对话状态面板不依赖其他 hooks 的信任。

## 与当前版本相同的功能

- **JokeBear 动画桌宠**：保留原生动画、拖动、宠物切换和设置。
- **一体审批气泡**：待审批时显示在宠物旁，随宠物移动；可允许一次、拒绝，
  或返回 VS Code 中对应的对话处理。
- **近 5 天的 Codex 对话**：每一行是一条对话，按对话 ID 区分，使用真实标题。
  同一个文件夹内的不同对话分别显示。
- **三色灯**：黄色表示进行中／待审批／待继续；绿色表示最新一轮回复完成；
  红色表示最新一轮出错或中断。继续发送消息后会重新显示工作状态。
- **单条隐藏与恢复**：× 只隐藏这一条对话，重启或出现新消息仍保持隐藏；
  可恢复，不删除聊天历史。
- **准确跳转**：点击标题打开这条 VS Code Codex 对话，不跳到其他对话。
- **紧凑界面**：隐藏原生上方信息框、三个控件和白色 Pet 菜单条；默认最多
  显示五行，多余对话可滚动；悬停可查看完整标题、状态和该对话的真实计划进度。
- **真实状态同步**：每 5 秒读取本机 VS Code 会话元数据和有限长度的 rollout。
  排除独立 App、CLI 和子代理的会话。
- **保持审批语义**：关闭、超时、断线或退出交回 Codex 原审批流程，不自动批准；
  并发请求排队且隔离。

## 手动安装

1. 安装 Windows 版 VS Code 和官方 OpenAI Codex 扩展，并登录 Codex。
2. 下载最新 Release 的 Windows ZIP 与 SHA256SUMS.txt，校验后解压。
3. 双击 `install.cmd`。默认安装到 `%LOCALAPPDATA%\CodexJokeBearPet`。
4. 完成上方的首次 hook 信任步骤，重新加载 VS Code。

指定目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -InstallDirectory 'E:\工具\CodexJokeBearPet'
```

安装器自动探测 VS Code、扩展、Codex 数据目录，并准备自带 SQLite 的 Python
运行时。Petdex Desktop **0.9.2** 和 JokeBear 图片固定版本且校验 SHA-256，
因此安装结果使用相同的动画资源。安装前会备份原生宠物设置和需要修改的 hooks；
保留其他 hooks、matcher 和配置。不会修改 Codex 审批策略或信任记录。

启动：安装目录中双击 `start.cmd`。如果误点 Close Pet，再双击 `start.cmd` 可以
重新显示。右下角 Petdex 的 Quit 是退出原生宠物；审批伴随组件有自己的托盘菜单。

升级：退出审批伴随组件后，将新版本安装到原目录；隐藏设置保存在 `data/progress.json`。
换目录后 hook 路径变化，需要重新审阅信任。

卸载接入：运行 `uninstall-hooks.ps1`，退出两项托盘程序，再删除安装目录。
只移除此组件的 handlers，保留其他 hooks、聊天记录及第三方宠物文件。

## 兼容与数据

支持本机 Windows x64 VS Code。安装时检查本机扩展的 `/local/:conversationId`
路由与 Windows Codex runtime；不兼容时会明确提示更新扩展。对话数据库按实际
schema 探测，不绑定单个数据库文件名。WSL、SSH、容器或独立 Codex App 不属于本包。

安装后的对话摘要、隐藏设置、连接令牌和本机路径都留在安装用户的电脑上；不会
上传至这个仓库。状态读取失败时保留最近成功结果。只支持由 Codex 发送到官方
PermissionRequest hook 的审批。

## 开发与验证

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-projects.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-bridge.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-gui.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-approval-gui.ps1
python .\tests\test_vscode_projects.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-package.ps1 -DependencyCache '<已下载并校验的依赖目录>'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-package.ps1
```

包测试在临时目录中完成全新安装、中文路径、依赖校验、用户配置生成、hooks 合并、
幂等安装和个人信息排除检查；不改测试者真实的 Codex 配置。GUI 测试只处理明确
标记的模拟审批。

本仓库伴随组件与安装器采用 MIT 协议。第三方软件及 JokeBear 作品权利见
[THIRD_PARTY.md](THIRD_PARTY.md)。
