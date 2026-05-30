---
name: workspace-save-feedback-gate
description: 为 Workspace 保存链路建立结果模型与 UI 反馈，并通过测试和质量门禁完成闭环验证。
---

# Workspace Save Feedback Gate

## 适用场景
- 需要把“保存命令”从状态更新升级为可观测结果（成功/失败）。
- 需要在 UI 显示保存成功/失败提示，避免静默失败。
- 需要保证改动后测试和门禁稳定通过。

## 执行步骤
1. 在 Workspace 层新增保存结果模型（含 succeeded、code、message、tab）。
2. 升级 `IWorkspaceService.SaveDocument` 返回结果模型，而不是仅返回 tab。
3. 在实现层补齐失败分支（如 `invalid_document_id`、`document_not_found`）。
4. 在 ViewModel 消费结果模型并设置 UI 反馈字段（消息+错误态）。
5. 在视图层绑定反馈字段，显示成功/失败提示。
6. 补充测试：成功保存清脏、保存失败路径、UI 反馈断言。
7. 运行门禁：build + 边界检查 + 分层测试 + 冒烟构建。

## 通过标准
- `SaveDocument` 必须返回统一结果对象。
- 失败路径必须有稳定错误码和可读错误信息。
- UI 必须可见保存反馈，不允许失败静默。
- 相关测试全部通过，质量门禁通过。

## 常见回归点
- 修改接口后未同步 Fake/Mock 导致测试编译失败。
- 保存失败时误清脏，导致 UI 状态与真实结果不一致。
- 成功/失败反馈未在编辑后清理，出现旧提示残留。
