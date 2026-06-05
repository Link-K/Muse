# S2-009 设计规格：文件树 CRUD + 会话与崩溃恢复

日期：2026-06-05
任务 ID：S2-009
状态：设计通过，待实施

---

## 1. 背景与目标

Sprint 2 Day 1 已落地 Workspace 最小骨架（文件树读取、多标签、冲突检测、自动保存快照）。当前存在以下缺口：

1. **文件树仅支持只读**，无法创建/重命名/删除/移动文件和目录。
2. **会话恢复不完整**：没有独立的 `session.json` 记录正常退出时的标签列表；所有 `.muse/recovery/*.json` 被统一视为"崩溃残留"，正常退出重启也会触发误恢复提示。
3. **"最近关闭"缺失**：被删除或移动后文件不可见，没有"最近关闭"灰显区域供用户找回。

本任务覆盖 MVP 范围：文件树 CRUD（右键菜单 + 点击/拖拽）+ 会话优先 + recovery 静默保留 + "最近关闭"灰显项。

**不在本任务范围**：
- Markdown 内部链接重写（文件重命名后不自动更新文档内的相对路径引用）
- 跨工作区资产迁移
- 冲突合并 UI
- 撤销/重做（留口子，不实现）

---

## 2. 接口与类型

### 2.1 扩展 `IWorkspaceService`

```csharp
// 文件树 CRUD（同步）
WorkspaceMutationResult CreateNode(string parentPath, string name, bool isDirectory);
WorkspaceMutationResult RenameNode(string path, string newName);
WorkspaceMutationResult RemoveNode(string path);
WorkspaceMutationResult MoveNode(string sourcePath, string targetDirectoryPath);

// 软流程复合操作（用于 UI 处理打开中标签）
WorkspaceMutationResult CloseAndRemove(string path);
WorkspaceMutationResult CloseAndMove(string path, string targetDirectoryPath);

// 会话与恢复
WorkspaceSessionState? GetLastSession();
void FlushSession();
void InvalidateSession();

// 最近关闭（新增）
IReadOnlyList<RecentlyClosedEntry> GetRecentlyClosed();
void RemoveFromRecentlyClosed(string path);
```

### 2.2 新增类型

```csharp
public sealed record WorkspaceMutationResult(
    bool Succeeded,
    string? Code,        // "io_error" | "path_conflict" | "forbidden_path"
                         // | "outside_workspace" | "open_tab_unsaved" | "not_found" | "invalid_name"
    string? Message,
    string? AffectedPath  // 成功后返回新建/重命名/移动后的规范化绝对路径
);

public sealed record WorkspaceSessionState(
    string WorkspaceRoot,
    IReadOnlyList<string> OpenTabIds,
    DateTimeOffset SavedAt);

public sealed record RecentlyClosedEntry(
    string FilePath,
    string FileName,
    DateTimeOffset ClosedAt,
    long? LastKnownSizeBytes);  // 写盘时快照，单位：字节；仅存前 64KB 摘要时填 null
```

### 2.3 扩展 `WorkspaceTabState`

```csharp
public sealed record WorkspaceTabState(
    string DocumentId,
    string FilePath,
    bool IsDirty,
    DateTimeOffset LastTouchedAt,
    bool HasExternalConflict = false,
    string? ConflictMessage = null,
    bool HasUnsavedRecovery = false,   // 新增：session 内有草稿待恢复
    bool IsMissingOnDisk = false        // 新增：session 中记录但文件已不存在
);
```

---

## 3. 存储结构

| 文件 | 用途 | 格式 |
|---|---|---|
| `.muse/settings/session.json` | 正常退出会话 | `{ "workspaceRoot": "...", "openTabIds": [...], "savedAt": "..." }` |
| `.muse/settings/recently-closed.json` | 最近关闭列表（最多 20 条） | `RecentlyClosedEntry[]` |
| `.muse/recovery/*.json` | 未保存草稿（不删除，静默保留） | `WorkspaceRecoverySnapshot[]` |

**原子性**：session.json 写操作采用 "写 .tmp 再 File.Move 覆盖" 模式。

**降级**：session.json / recently-closed.json 损坏或不存在时，当作空数据处理，不阻断启动。

---

## 4. 数据流

### 4.1 文件树 CRUD 概述

所有 CRUD 操作走以下共同路径：
1. 名称校验（`invalid_name`）：非空、不含 `/\:*?"<>|`、不以 `.` 开头
2. 路径白名单校验：`IsInternalWorkspacePath == false` 且在 `WorkspaceRoot` 之下（`outside_workspace`）
3. 存在性校验：目标已存在（`path_conflict`）、源不存在（`not_found`）
4. 打开中标签保护：源路径对应打开中的 tab → 返回 `open_tab_unsaved`，由 UI 弹出软流程对话框
5. 文件系统操作（`io_error`）
6. `RefreshWorkspaceFromDisk()` 同步文件树
7. `WorkspaceChanged` 事件通知 UI

### 4.2 删除软流程（带打开标签）

```
RemoveNode(path)
  ├─ 找出所有 FilePath == path 的 OpenTabs
  ├─ 若有脏 tab：
  │    返回 open_tab_unsaved，UI 弹三选一：
  │    ├─ "保存后删除" → SaveDocument + CloseAndRemove
  │    ├─ "丢弃并删除" → CloseAndRemove
  │    └─ "取消" → 不操作
  ├─ 若有干净 tab 无脏 tab：
  │    UI 弹"该文件已打开，关闭后删除？"
  │    ├─ "确定" → CloseAndRemove
  │    └─ "取消"
  └─ 无打开 tab → 直接 RemoveNode
```

### 4.3 重命名软流程（带打开标签）

```
RenameNode(path, newName)
  ├─ 目标路径已在 Tab 中 → UI 弹"该文件已打开，是否同时更新标签？"
  │    ├─ "确定" → CloseAndRename 原子操作
  │    └─ "取消"
  └─ 无打开中标签 → 直接 RenameNode
```

### 4.4 移动软流程（带打开标签）

同删除软流程，由 `CloseAndMove` 原子执行。

### 4.5 会话与崩溃恢复流程

#### 正常退出（写 session）

```
App.OnFrameworkShutdownCompleted
  → MainViewModel.SyncWorkspaceState()
  → IWorkspaceService.FlushSession()
      ├─ 序列化 WorkspaceSessionState（OpenTabIds 取当前 _state.OpenTabs）
      ├─ 原子写 .muse/settings/session.json
      └─ 保留 .muse/recovery/*.json（不删除，作为"未保存草稿层"）
```

#### 启动恢复（OpenWorkspace）

```
1. LoadSession(root)
   - 不存在 / 解析失败 → session = null

2. LoadRecoveryTabs(root)
   - 遍历 .muse/recovery/*.json → 暂存草稿到 _draftContents

3. 合并（session 优先）
   - session 存在：基础 tabs = session.OpenTabIds 中 File.Exists 的项
     ├─ 属于 session 且有 recovery 草稿的 tab：IsDirty=true, HasUnsavedRecovery=true
     └─ 属于 session 但无 recovery 的 tab：IsDirty=false
   - session == null：基础 tabs = 空，recovery 全部按"未保存草稿"提示

4. 缺失文件灰显
   - session 中有但 File.Exists==false 的 tabId：
     ├─ OpenTabs 保留为 IsMissingOnDisk=true
     ├─ recently-closed.json 追加该条（首次发现时）
     └─ UI 灰显 + tooltip "文件已被移动或删除"
```

### 4.6 "最近关闭"面板

```
打开"最近关闭"面板
  → 读 .muse/settings/recently-closed.json
  → 每条渲染为灰显项（FileName + 关闭时间）
  → "重新打开"：如果 File.Exists，重走 OpenDocument
  → "从历史移除"：从 recently-closed.json 移除该条（不操作磁盘）
  → 写入策略：每次 CloseDocument / RemoveNode 成功后追加，超过 20 条 LRU 淘汰
```

---

## 5. 错误处理

| 错误码 | 含义 | UI 处理 |
|---|---|---|
| `invalid_name` | 名称为空/含非法字符/以 `.` 开头 | 行内 TextBox 红色边框 + 提示 |
| `path_conflict` | 目标路径已存在 | 弹窗"已存在同名 X" |
| `forbidden_path` | 操作 `.muse` 内部路径 | 弹窗"不允许操作工作区内部目录" |
| `outside_workspace` | 操作越出 WorkspaceRoot | 弹窗"目标位置不在当前工作区" |
| `open_tab_unsaved` | 打开且未保存的标签被删/移 | 三选一弹窗（保存后删除 / 丢弃并删除 / 取消） |
| `not_found` | 源文件/目录不存在 | 行内提示 |
| `io_error` | IO 异常（权限、磁盘、共享冲突） | 顶部红条 + 详情可复制 + 记录到 ConflictEvent |

**审计**：所有失败通过 `AppendConflictEvent` 进入现有 `ConflictEvent` 审计流，UI 顶部"最近 5 条"可展开面板可见。

---

## 6. UI 交互

### 6.1 文件树节点右键菜单

```
点击节点 → 显示 ContextMenu：
  - "新建 Markdown 文件"     → CreateNode(parent, name, isDirectory: false)
  - "新建文件夹"             → CreateNode(parent, name, isDirectory: true)
  - "重命名"                 → 节点进入 inline 编辑模式，回车提交
  - "删除"                   → HandleRemove(path)
  - 分隔线
  - "在资源管理器中打开"     → System.Diagnostics.Process.Start("explorer.exe", "/select," + path)
```

### 6.2 拖拽到编辑区

```
将文件树节点拖入 Markdown 编辑区 → 在光标位置插入：
  - 图片文件（.png/.jpg/.gif/.webp/.svg）：`![](相对路径)`
  - 其他文件：`[文件名](相对路径)`
```

### 6.3 "最近关闭"区域

- 位置：侧栏底部或独立面板，折叠/展开切换
- 灰显样式：`Opacity=0.5`，tooltip 显示 `ClosedAt` 与 `LastKnownSizeBytes`
- 命令："重新打开"、"从历史移除"

### 6.4 行内错误反馈

- 成功：3 秒淡出，路径自动展开高亮
- 失败：`FileTreeNodeViewModel.LastError: string?`，模板 `Border.ToolTip` 绑定

---

## 7. 测试用例

### 7.1 单元测试（`Muse.Workspace.Tests`）

| ID | 用例 |
|---|---|
| T1-01 | CreateNode_成功创建文件 |
| T1-02 | CreateNode_成功创建目录 |
| T1-03 | CreateNode_名称含非法字符返回 invalid_name |
| T1-04 | CreateNode_以 `.` 开头返回 invalid_name |
| T1-05 | CreateNode_目标已存在返回 path_conflict |
| T1-06 | CreateNode_在 `.muse` 内部返回 forbidden_path |
| T1-07 | CreateNode_越出工作区根返回 outside_workspace |
| T1-08 | RenameNode_重命名 + OpenTabs 路径跟随 |
| T1-09 | RenameNode_重命名目录 + 子树 tab 跟随 |
| T1-10 | RemoveNode_删除无打开 tab 的文件 → recently-closed 追加 |
| T1-11 | RemoveNode_删除有打开但干净的 tab 走 CloseAndRemove |
| T1-12 | RemoveNode_脏 tab 返回 open_tab_unsaved（不删） |
| T1-13 | CloseAndRemove_脏 tab 自动保存后删除 |
| T1-14 | CloseAndRemove_保存失败回滚（tab 状态和磁盘均保留） |
| T1-15 | MoveNode_跨目录移动 + 路径同步 |
| T1-16 | MoveNode_目标位置已存在返回 path_conflict |
| T1-17 | MoveNode_源 == 目标后代返回 outside_workspace |
| T1-18 | FlushSession_写原子性（写 .tmp 再 Move） |
| T1-19 | LoadSession_文件不存在返回 null |
| T1-20 | LoadSession_JSON 损坏返回 null（不抛异常） |
| T1-21 | LoadSession_含已不存在的 tabId → 该项进 recently-closed |
| T1-22 | OpenWorkspace_session 优先于 recovery |
| T1-23 | OpenWorkspace_session 列表内的 recovery 标 HasUnsavedRecovery |
| T1-24 | RecentlyClosed_LRU 淘汰（超过 20 条时最早条目被剔除） |
| T1-25 | RecentlyClosed_JSON 损坏返回空列表（不抛异常） |
| T1-26 | InvalidateSession_下次启动不恢复 |

### 7.2 集成测试（`Muse.Tests`）

| ID | 用例 |
|---|---|
| T2-01 | FileTree_NewFileCommand_成功 → FileTree 刷新 + 节点选中 |
| T2-02 | FileTree_RenameCommand_冲突路径 → LastError 含 path_conflict |
| T2-03 | FileTree_DeleteCommand_脏 tab → UI 收到 open_tab_unsaved |
| T2-04 | FileTree_DeleteCommand_保存后删除 → 落盘 + tab 关闭 + 文件删除 |
| T2-05 | FileTree_DragToEditor_插入相对路径 |
| T2-06 | RecentlyClosedPanel_显示灰显项（session 缺失文件） |
| T2-07 | RecentlyClosedPanel_重新打开成功（文件已恢复时） |
| T2-08 | App_OnExit_FlushSession → session.json 含当前 OpenTabIds |
| T2-09 | App_启动_无 session.json → 不抛异常，正常打开空工作区 |
| T2-10 | App_启动_有 session + 部分文件丢失 → 正常 tab + 灰显"最近关闭"项 |

### 7.3 边缘场景

- 快速连续操作（5 次新建）、并发（CRUD 与 RefreshWorkspaceFromDisk 同时触发）、特殊字符文件名（含空格/括号/中文/Emoji）、路径分隔符混用（`/` 和 `\`）、空工作区、大文件树（1000 节点，性能冒烟）、session 与 recovery 同名冲突。

---

## 8. 文档更新

| 文件 | 变更 |
|---|---|
| `files/项目架构文档.md` | 追加 2.12 节"文件树 CRUD 与会话恢复基线" |
| `files/Sprint2-任务卡.md` | 追加 S2-009 任务卡详情 |
| `files/变更说明-S2-009.md` | **新增**：变更背景、接口摘要、验证记录、回滚说明 |
| `files/实施计划.md` | 第 10.1 节追加 S2-009 完成项 |
| `files/开发检查清单.md` | 追加"S2-009 经验"小节 |
| `memory/Muse-project-memory.md` | 追加 3 条关键决策记录 |

---

## 9. 关键决策记录

| 决策 | 原因 |
|---|---|
| 文件树 CRUD 走服务层（`IWorkspaceService`），不在 ViewModel 直接 `File.*` | 保持架构约束（UI 不直接读写文件系统），可单元测试 |
| 删除/移动打开中标签走软流程（UI 弹确认框） | 用户体验优先，减少操作路径；服务层提供 `CloseAndRemove` / `CloseAndMove` 原子操作 |
| session 优先 + recovery 静默保留 | session.json 反映正常退出意图；recovery 作为"未保存草稿层"不打扰用户 |
| "最近关闭"独立 `recently-closed.json`，最多 20 条 LRU | 解耦 session（仅当前打开项）与历史（已关闭项）；文件恢复后可从灰显项重新打开 |
| `session.json` / `recently-closed.json` 损坏时降级为空白 | 不阻断启动；写入采用 .tmp + Move 原子性保证 |

---

## 10. 验收标准

1. 文件树节点可右键新建/重命名/删除/移动（文件与目录）。
2. 删除/移动打开中的标签时，UI 弹出确认框（三选一/二选一）。
3. 正常退出后重启，标签顺序与内容恢复（session 优先）。
4. `recovery/*.json` 在 session 存在时静默保留，不弹窗不打断启动。
5. session 中已不存在的文件在 UI 显示为灰显"最近关闭"项。
6. 所有 26 个单元测试 + 10 个集成测试通过。
7. 文档同步完成（`files/变更说明-S2-009.md`、`项目架构文档.md` 2.12 节、`实施计划.md` 进展、`开发检查清单.md` 经验条、`memory/Muse-project-memory.md` 决策记录）。

---

## 11. 回滚方案

如需回滚：
1. 从 `main` 执行 `git revert <commit>` 撤销 S2-009 相关提交。
2. 恢复点：`IWorkspaceService` 恢复到 4 个 CRUD 方法添加之前的状态；删除 `WorkspaceMutationResult`、`WorkspaceSessionState`、`RecentlyClosedEntry` 类型文件；`WorkspaceTabState` 移除 `HasUnsavedRecovery` 和 `IsMissingOnDisk`。
3. 验证：`dotnet build` 通过，`dotnet test` 不引入新失败。