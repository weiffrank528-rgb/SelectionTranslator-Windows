# Security Policy

## 支持范围

当前仅维护最新 Alpha 版本。发现安全或隐私问题后，请先在最新版复现。

## 报告方式

仓库发布后，请优先使用 GitHub 的 **Private vulnerability reporting**；如果尚未启用，请创建一个不包含利用细节和敏感数据的普通 Issue，请求维护者提供私下联系方式。

请勿公开发布真实 API Key、私人选中文本、剪贴板内容或包含个人信息的崩溃转储。

## 安全边界

- 自动翻译会把选中文本发送给当前配置的翻译服务。
- API Key 使用 Windows DPAPI 当前用户范围加密，配置位于 `%APPDATA%\SelectionTranslator`，不会写入项目目录。
- 密码输入框会尽量通过 UI Automation 检测并跳过，但第三方应用可能不正确标记敏感控件。
- 项目不应以管理员权限常驻运行，除非用户理解完整性级别带来的风险。
