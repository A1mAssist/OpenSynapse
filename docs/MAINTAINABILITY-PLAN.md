# OpenSynapse 结构治理计划

一句话：**把"结构靠自觉"换成"结构靠会红的检查"，再把唯一在加速长胖的那一层拆开。**

## 0. 判断

现在不是屎山。三条证据：分层零违规（Core 不引用 Windows/App，Windows 不引用 App）、全仓库只有 1 处硬编码 PID、`MainWindow*.cs` 里对设备服务的直接调用是 0。

唯一的前兆：**App 层在加速长胖，而它是唯一没有自动化验收的层。** 9 天 318 个 commit，`MainWindow.xaml` 被改 128 次。热点文件和验证能力错位，这是所有屎山的第一阶段。

所以这份计划不做"架构重写"，只做两件事：给增量装刹车，把已经长歪的那一个类型拆开。

## 1. 基线（2026-10-29 实测，棘轮起点）

| 指标 | 当前 | 目标 |
|---|---|---|
| Core / Windows / App 行数 | 7 243 / 55 040 / 40 772 | Core 占比继续上升，不设上限 |
| 单文件 > 700 行 | Core 1 · Windows 14 · App 13 | 全仓库 ≤ 3（协议表与数据表除外） |
| `MainViewModel` | 25 个 partial、10 504 行、468 个 public、32 个文件引用 | ≤ 1 500 行、≤ 60 public |
| `MainWindow` | 23 个 cs（4 265 行）+ 1 733 行 XAML、181 个事件处理器 | MainWindow.xaml ≤ 500 行、code-behind ≤ 1 200 行 |
| `MainWindow.xaml` 里的 `DevicesPage` | 1 225 行（占全文 70%） | 独立 UserControl |
| XAML 绑定面 | 1 313 处 `{Binding}`、21 处 `x:Bind`、934 条不同路径 | 路径数随页面拆分下降，绑定方式不变 |
| 测试 | 1 077 个 `[Fact]/[Theory]` → 1 643 用例；测试项目硬链 8 个 App 文件 | Compile Include 归零 |
| 能力 id | `RazerDeviceRegistry` 里 123 个字符串 + 39 份 Blade manifest | 只用常量 |
| 分层违规 | 0 | 保持 0 |

## 2. 唯一有效的机制：预算棘轮

不禁止你现在有债，只禁止你**偷偷加债**。

- `.structure-budget.json` 记录每个超标文件的行数上限、每个超大类型的 public 成员上限。
- `StructureBudgetTests` 在每次 `dotnet test` 里读实际值：**涨了就红；降了就绿（并提示可以把预算改小）**。
- 新文件超过 600 行直接红，不进白名单。

这条规则对 agent、对人、对赶工都一样有效，因为它不依赖任何人记得规矩。

```csharp
// tests/OpenSynapse.Core.Tests/StructureBudgetTests.cs 形状
[Fact]
public void NoFileExceedsItsBudget()
{
    var budget = Load(".structure-budget.json");        // 相对仓库根定位
    foreach (var (path, limit) in budget.Files)
        Assert.True(CountLines(path) <= limit, $"{path} 超预算：{CountLines(path)} > {limit}");
}
```

## 3. 阶段 0：把规矩变成检查（1 天）

只产出检查，不产出"规范文档"。

| 新增 | 检查什么 |
|---|---|
| `tests/OpenSynapse.Core.Tests/StructureBudgetTests.cs` | 单文件行数、单类型 public 成员数只降不升 |
| `.structure-budget.json` | 上述两类的白名单与上限 |
| `tests/OpenSynapse.Core.Tests/LayeringTests.cs` | Core 不得 `using OpenSynapse.Windows/App`；Windows 不得 `using OpenSynapse.App` |
| `tests/OpenSynapse.Core.Tests/TestProjectHygieneTests.cs` | 测试 csproj 不得出现指向 `src/OpenSynapse.App` 的 `<Compile Include>`（阶段 2 完成后启用） |
| `scripts/check-structure.ps1` | 本地一次跑完，输出表格式清单 + 退出码 |

验收：`dotnet test` 全绿；**故意给某个超预算文件加一行，测试必须红**；删掉加的那行，必须绿。这一步不做完不进阶段 1——否则整套机制没有证明过它会响。

## 4. 阶段 1：能力契约常量化（1 天）

- 新增 `src/OpenSynapse.Core/Devices/CapabilityIds.cs`，123 个 `const string`。
- `RazerDeviceRegistry` 全部改用常量；manifest JSON 保持字符串不变（它是数据）。
- 新增 `ManifestCapabilityTests`：
  - 每个常量至少在一个 manifest 或 family contract 里出现；
  - 每份 manifest 的每个 key 都能找到常量；
  - 反向也查：manifest 有、代码没有 → 红。

效果：能力 id 拼错从"设备莫名少一个能力"变成"编译不过"。

做法：机械替换用 FastCtx `replace`，一个文件一次 build，不要一次改完再编。

## 5. 阶段 2：测试工程归位（1 天）

测试 csproj 现在硬链了 8 个 App 源文件才能测，这份手写清单会随新文件漂移。

| 文件 | 去处 |
|---|---|
| `AppBehaviorSettings.cs` | Core（纯设置模型） |
| `LowBatteryNotificationTracker.cs` | Core |
| `DeviceProtocolFamilies.cs` | Core |
| `BladeFanCurveViewModel.cs` / `BladeFanCurveEditorViewModel.cs` | Core（纯计算，无 XAML 依赖） |
| `OpenRazerSoftwareLightingRuntime.cs` | Windows |
| `MacroRuntime.cs` | 留 App，但把可测部分（播放计划执行）抽成 Core 类型 |
| `ChromaRestHost.cs` | 留 App，单独补一个宿主级测试或明确记录为不可单测 |

验收：Compile Include 从 8 降到 ≤ 2，剩下两个在 csproj 里带注释说明原因；`TestProjectHygieneTests` 打开。

## 6. 阶段 3：拆 `MainViewModel`（3–5 天，按子系统）

**不是再开 partial，是普通的 Workspace 类。** 25 个 partial 已经证明 partial 不解决耦合，它只是把耦合摊平。

```csharp
public sealed class MacroWorkspace          // 状态 + 命令 + 调用 Core 校验
{
    public ObservableCollection<MacroRowViewModel> Macros { get; }
    public Task SaveAsync(CancellationToken ct) ...
}

public sealed partial class MainViewModel
{
    public MacroWorkspace Macros { get; }   // 只有这一行
}
```

XAML 侧：`{Binding Foo}` → `{Binding Macros.Foo}`。因为用的是经典绑定而不是 `x:Bind`，DataContext 不变，改路径是机械替换，可以按页面逐页验证。

顺序与理由：

1. **Macros（2 566 行）** — 阶段 3 刚把验证器和文档服务抽到 Core，边界最清楚，先做用来验证模式。
2. **OpenRazer（444 + `OpenRazerDeviceViewModel` 993）** — 每台设备已经是独立 VM，只差一个上层容器。
3. **Profiles / Telemetry / Fan / Display / Lighting（654 / 658 / 482 / 208 / 381）** — 体量小，可并行。
4. **Blade（1 924 行）最后做** — 它是 30 天内改动最多的文件（46 次）。在它天天被改的时候动手等于每周重新解冲突：先冻结（新逻辑写进新类），静一周再拆。

每一步的验收都一样：`build` 0 警告、`dotnet test` 全绿、该页面 UI 自动化通过、`MainViewModel` 的 public 成员数下降。

目标：10 504 → ≤ 1 500 行，468 → ≤ 60 个 public 成员。

## 7. 阶段 4：拆 `MainWindow` / XAML（2–3 天）

事实：1 733 行的 XAML 里 `DevicesPage` 占 1 225 行。这不是"6 个页面"的问题，是"1 个页面"的问题。

1. **Diagnostics（124 行）** 先做，演练完整路径：UserControl + DataContext 继承 + 事件处理器搬家 + 截图基线。
2. **Devices（1 225 行）** 再拆，这是最大的一笔，拆完文件立刻减 70%。
3. 然后 Profiles / Macros / Settings / Overview。

每页一个 `tests/ui/<page>.scenario.json` + 一张基线截图 + `scripts/run-ui-qa.ps1` 包装。

验收：`MainWindow.xaml` ≤ 500 行；`MainWindow*.cs` ≤ 1 200 行；导航 + 6 个页面截图全部通过；`x:Name` 引用清零。

## 8. 仓库行为规范（每一条都对应一个检查）

| 规矩 | 检查 |
|---|---|
| 单文件 ≤ 600 行，存量只降不升 | `StructureBudgetTests` |
| 单类型 public ≤ 100，存量只降不升 | `StructureBudgetTests` |
| 分层方向不许反向 | `LayeringTests` |
| 能力 id 只能来自 `CapabilityIds` | `ManifestCapabilityTests` |
| 新设备 = manifest + family contract + 结构测试 | 已有 `DeviceCapabilityStructureTests` |
| 测试项目不得 include App 源码 | `TestProjectHygieneTests` |
| UI 改动必须跑 UI 自动化并留截图 | `scripts/run-ui-qa.ps1` + `tests/ui/*.scenario.json` |
| 提交用 Conventional Commits、无 `Co-Authored-By`、不提交 `package.json` / `package-lock.json` | `.githooks/pre-commit` + `commit-msg` |
| 证据分四档：构建通过 / 协议对齐 / 实机验证 / 生产验证，不许混写 | PR 模板 + CHANGELOG 模板 |
| 每次交付必须写清"没做什么、没验证什么" | PR 模板固定一栏 |

启用：`git config core.hooksPath .githooks`（一行，写进 README 的开发环境小节）。

hook 拒绝三种提交：带 `Co-Authored-By:` 的、暂存了 `package.json`/`package-lock.json` 的、消息不以 `feat|fix|refactor|docs|test|chore|perf|build|ci` 开头的。

## 9. 不做清单

- 不重写协议层。1 797 个十六进制常量是硬件事实，不是债。
- 不引入 DI 容器或 Repository。装配点集中在启动那一处，够用。
- 不把 manifest 和 OpenRazer catalog 合成一张表——它们是两个后端，统一的是运行时解析结果。
- 不为"整齐"去拆 500 行以下的文件。
- 不在 Blade 还在高频改动时拆 Blade。

## 10. 风险与回滚

- 阶段 3、4 是纯搬移，不改行为，每一步一个 commit，出问题 `git revert` 单步。
- 阶段 1 若一次改太多导致编译错误堆积：保留字符串实现与常量并存，先加测试比对两边，再逐个切。
- UI 拆页的回归只能靠截图加人工点。某一页视觉不对就退回那一页，不影响其他页。
- 预算棘轮会让"临时加个功能"变慢。这是目的。真要放宽就改 `.structure-budget.json`，改动会出现在 review 里。

## 11. 怎么算完成

- 5 个检查全绿，且都被证明会红（阶段 0 用"故意加一行"证明过）。
- `MainViewModel` ≤ 1 500 行、≤ 60 public。
- `MainWindow.xaml` ≤ 500 行，`DevicesPage` 独立成 UserControl。
- 测试项目对 App 的 `<Compile Include>` ≤ 2 且带注释。
- `docs/ARCHITECTURE.md` 补一节"预算与检查"，指向真实脚本而不是复述规则。

## 12. 排期

| 阶段 | 工期 | 可独立发布 | 依赖 |
|---|---|---|---|
| 0 检查落地 | 1 天 | 是（不影响功能） | 无 |
| 1 能力常量 | 1 天 | 是 | 0 |
| 2 测试归位 | 1 天 | 是 | 0 |
| 3 拆 MainViewModel | 3–5 天 | 是（按子系统分 4 次） | 0 |
| 4 拆 XAML | 2–3 天 | 是（按页面分 6 次） | 3 |

合计 8–11 个工作日，可以分 6 次以上发布，每次都能出测试包。阶段 0 做完当天就能挡住新债，不必等全部完成。
