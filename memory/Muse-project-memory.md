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

## 2026-06-04: 图片粘贴/资产保存 开发经验（S2-007）
- 问题症结：在分屏场景下写盘成功且 ViewModel 设置 ImageFilePath，但 Avalonia 预览不显示，主要原因是跨线程/Bitmap 创建与文件句柄锁定导致的渲染失败（Binding 被触发但渲染层抛或静默失败）。
- 关键修复：
	- 在 `PreviewBlockViewModel` 中实现 `INotifyPropertyChanged` 并确保 `PropertyChanged` 在 UI 线程触发，避免后台线程直接触发导致绑定未更新或异常。
	- 在 `PathToImageConverter` 中读取字节后在 UI 线程创建 `Bitmap`（避免文件锁与跨线程渲染问题）。
	- 对 Win32 剪贴板读取路径增加降级与 filename 清洗，避免 `FileGroupDescriptorW` 返回控制字符使原始文件名失效。
- 经验要点：
	- 当 UI 看起来“没刷新”时，分两条线索：PropertyChanged 是否在正确实例/线程触发；渲染器（Bitmap）是否在 UI 线程并且没有被文件锁住。
	- 在复杂回退查找（多候选目录）时记录每一步的候选与结果，便于排查分屏/工作区上下文差异。
- 验证与后续：已在 Windows 环境手工复现并验证通过；建议补充集成测试并把 Win32 特定逻辑用平台判定保护。

## 2026-06-05: 图片粘贴注入修复与进度（S2-008）
- 目标：保证粘贴/拖拽保存的图片在预览中立即显示，避免因 PreviewBlockViewModel 被重建导致的竞态（VmId mismatch）。
- 已完成要点：
	- 在 `MainViewModel` 中新增待注入队列（`_pendingSavedImages`）与线程安全注册方法 `RegisterPendingSavedImage(relativePath, absolutePath)`，用于在保存图片后记录待注入项。
	- 将 `BuildPreviewBlocks` 由静态改为实例方法；在构建 `PreviewBlockViewModel` 列表后，快照 pending 队列并尝试根据 markdown 中包含的相对路径匹配对应的 `PreviewBlockViewModel`，若匹配则在构造阶段调用 `AssignImagePath(abs)` 进行原子注入并从队列移除。
	- 在 `Views/MainView.axaml.cs` 的粘贴与拖拽流程中，在 `SaveImageAsync` 返回后调用 `mvm.RegisterPendingSavedImage(rel, absPath)` 注册待注入项（同时保留原有短路重试逻辑作为补救）。
	- 已本地运行 `dotnet build`，解决方案成功构建（若干 warning，无错误）。
- 日志与验证：
	- 建议在运行时观察 `files/error-copy.txt` 中条目，确认 `SaveImage returned rel=...`、`BuildPreviewBlocks mappings (Line:VmId): ...` 与 `ImageBitmap assigned` 日志的 VmId 是否一致，从而验证赋值命中活动 VM。
- 后续建议与待办：
	- 运行应用并在编辑器执行粘贴/拖拽图片以产生运行时日志；若仍见不稳定情况，可将 pending 注入逻辑增强为“基于行号或更精确的行内锚点匹配”，或在 Preview 构建阶段将 image 字节直接交由同步创建 Bitmap（更大改动）。
	- 为粘贴场景添加集成测试（可模拟保存、刷新 preview、断言预览 VM 已包含 Bitmap），并在 CI 上运行以防回归。
	- 考虑将短路重试逻辑作为补救手段保留，但首要策略应为在 VM 构造时注入以减少竞态窗口。

### 本次变更影响的文件（关键）
- Muse/ViewModels/MainViewModel.cs — 新增 `_pendingSavedImages`、`RegisterPendingSavedImage`，并将 `BuildPreviewBlocks` 改为实例方法以支持注入逻辑。
- Muse/Views/MainView.axaml.cs — 在粘贴（Paste）与拖拽（Drop）处理处注册 pending saved image。
- Muse/ViewModels/PreviewBlockViewModel.cs — 保持 `AssignImagePath`/`SetImagePathAndBitmap` 逻辑（UI 线程创建 Bitmap）。

### 风险与注意事项
- 若文档中的相对路径格式或编辑器插入位置与预期不符，匹配失败的概率会上升；建议在插入时保证 `![](<rel>)` 的格式与保存返回的一致（当前以 `assets/filename` 为常见形式）。
- 多光标/并发编辑场景下，插入位置可能被并发修改，尚需小心并发冲突。

### 结论
- 已完成大部分代码改动并成功构建；下一步请在应用中实际粘贴图片并把新日志提供给我，我将进一步分析 VmId 是否命中并收尾测试用例与文档。
