# Contributing

感谢帮助改进 SelectionTranslator。当前最需要的是不同 Windows 应用的兼容性反馈，以及可重复的取词失败步骤。

## 开发环境

- Windows 11
- .NET Framework 4.8
- Visual Studio 2022（“.NET 桌面开发”工作负载），或系统自带的 .NET Framework C# 编译器

## 构建与测试

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -Configuration Release
.\test.ps1 -SkipNetwork
```

在线 MyMemory 测试会向公共翻译服务发送测试文本，只有在确实需要时才运行：

```powershell
.\test.ps1
```

## 提交兼容性问题

请包含：

- Windows 版本和应用名称/版本。
- 目标应用进程名，例如 `wps.exe`、`WINWORD.exe`。
- UI Automation 是否成功，或浮窗底部显示的取词方式。
- 是否启用了剪贴板兜底/WPS 兼容。
- 最小复现步骤和不含私人内容的截图。

请不要在 Issue 中粘贴 API Key、私人文档、聊天内容或其他敏感信息。

## Pull Request

- 保持全局鼠标钩子只做最小事件记录，并始终调用 `CallNextHookEx`。
- 任何剪贴板变更都必须有清晰的所有权和恢复策略。
- 新增原生结构或 P/Invoke 时应补充 32/64 位布局测试。
- 提交前运行离线烟雾测试。
