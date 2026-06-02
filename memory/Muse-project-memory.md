- 开发中所有文档统一放在 files/ 目录。
- 模块开发完成后必须同步更新开发文档。
- 前端设计实现需遵循 skills/apple-design/DESIGN.md 中的设计主题。
- 所有模块的开发要遵循：高内聚、低耦合的思想。
- 所有方案文档只保留单一版本，文件名不带版本号，后续在原文档直接更新。

- 2026-06-02: 已移除 `MainView.axaml` 中文件树的展开按钮与文件的打开按钮，并移除左侧“打开标签”区域以精简交互（更依赖顶部标签栏）。
- 2026-06-02: 新增单元测试 `FileTree_ExpandedState_ShouldBePreservedAcrossWorkspaceRefresh`，验证 `MainViewModel` 在 Workspace 刷新时保持文件树展开状态。
- 2026-06-02: 已在本地运行 `dotnet build` 与 `dotnet test`，所有测试通过（64/64）。
- 2026-06-02: 已为标签拖拽新增左右插入指示器与平滑淡入淡出动画（`BoolToOpacityConverter` + `DoubleTransition`）。
- 2026-06-02: 修复双击打开文件时触发的 `MainView.axaml` DataTemplate 运行时空引用，做法为：将过渡定义移至资源并显式使用 `Visual.OpacityProperty`。
- 2026-06-02: 最新本地验证通过：`dotnet build` 成功，`dotnet test` 全绿（65/65）。

## 2026-06 开发经验总结（S2-006）
- 标签拖拽实现建议：拖拽状态（`IsDropTarget`/`IsDropBefore`/`IsDropAfter`）放在 `WorkspaceTabViewModel`，View 仅负责指针事件与状态切换，避免在 ViewModel 中耦合 UI 坐标逻辑。
- DataTemplate 稳定性建议：模板中尽量减少复杂内联对象创建；动画过渡优先使用资源复用，并显式使用 Avalonia 属性（如 `Visual.OpacityProperty`），可降低延迟构建期空引用风险。
- 命令绑定建议：标签项操作（激活/关闭）优先用“每项命令代理”而不是模板内跨层级引用根 DataContext，运行时更稳定、测试更容易。
- 标签持久化策略：`MoveTab` 成功后立即写 `.muse/settings/tabs.json`；`OpenWorkspace` 恢复时采用“recovery 快照优先 + tabs 顺序重排 + 缺失文件忽略”策略，兼顾恢复率与健壮性。
- 诊断与修复方法：出现 XAML 异常时先精确定位行号，再以最小改动替换可疑节点；每次修复后执行 `dotnet build` + `dotnet test` 双重回归，防止只修编译不修运行时。
- Windows 环境命令稳定性：终端 PATH 可能漂移，必要时使用绝对路径 `C:\Program Files\dotnet\dotnet.exe` 运行测试，避免误判为代码问题。
- 协作流程经验：功能改动完成后同步更新 `files/变更说明-*.md`、`files/实施计划.md` 和本记忆文件，减少后续交接与回溯成本。

## 2026-06 文档流程闭环（固定执行）
- 固定流程：`任务卡模板 -> 开发实现 -> 变更说明模板 -> 回滚说明模板（按需） -> 实施计划更新 -> 项目记忆更新`。
- 固定规范：使用 `files/开发检查清单.md` 作为每次任务的执行对照表。
- 固定要求：每次开发流程完成后，必须做一次“文档与验证核对”，不跳过。

### 每次开发完成后核对清单（必做）
- [ ] 代码层：功能主流程与异常路径已验证。
- [ ] 构建层：已执行 `dotnet build` 并确认通过。
- [ ] 测试层：已执行 `dotnet test --no-build` 并确认通过。
- [ ] 任务文档：`files/任务卡模板.md` 对应任务已填写或补全。
- [ ] 变更文档：已更新对应 `files/变更说明-*.md`（含验证记录与经验沉淀）。
- [ ] 回滚文档：存在风险时已补全 `files/回滚说明模板.md` 对应条目。
- [ ] 计划文档：已更新 `files/实施计划.md` 最新执行进展。
- [ ] 项目记忆：已更新本文件，沉淀可复用经验与坑位。

### 执行提醒
- 如任一核对项未完成，视为“流程未完成”，不得标记任务真正完成。
- 后续新任务默认沿用本核对清单，除非明确说明不适用项及原因。
