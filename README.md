# BozjaPilot

博兹雅（南方战线 Bozjan Southern Front / 天佑女王 Zadnor）自动刷 **遭遇战 (Skirmish)** 与
**紧急任务 (Critical Engagement, CE)** 的 Dalamud 插件。

> 本插件只做「判断 + 跑图 + 报名 + 选目标 + 兜底恢复」，战斗循环交给你自己装好的
> RotationSolver Reborn / WrathCombo / BossMod。这样既不会和你现有配置打架，也不会因为
> 战斗逻辑写死而跟不上版本。

---

## 为什么不是又一个「走到红点然后站着」的脚本

大多数博兹雅自动插件不好用，根因是同一个：它们把 CE 当成普通 FATE，**只处理"去哪"，不处理"能不能去成"**。
具体表现为：

| 常见问题 | BozjaPilot 的做法 |
| --- | --- |
| CE 报名窗口结束前才动身，走到时已经开打、进不去 | 报名阶段单独成为最高优先级，先用 `距离 ÷ 速度 + 安全余量 < 剩余报名时间` 做可行性判定，不满足直接放弃，绝不白跑 |
| 报名成功与否全靠猜，站在圈里以为报上了 | 直接读角色 `BattleChara.EventId.EntryId`，它等于 DynamicEvent 的 id 才算真的报上名 |
| 遭遇战和 CE 分不清，CE 用遭遇战逻辑处理，永远进不去 | CE 一定有「报名」阶段，遭遇战没有。首次观察到 `Register` 状态即永久标记该事件 id 为 CE，并按 id 记忆到配置里，跨会话有效 |
| 目标每帧重算，两个活动之间来回横跳 | 目标有「承诺」机制：选定后只有出现更好的 CE 报名，或目标失效/被拉黑才会切换 |
| 选目标只看距离，跑到一半活动结束了 | 评分 = 到场后剩余时间 − 路程惩罚 − 进度惩罚 + 参与人数，到场剩余时间不足 `MinTimeOnArrival` 的直接不选 |
| 卡地形、卡台阶、卡坐骑，卡住后原地不动 | 位移停滞 N 秒 → 跳跃 → 重新上坐骑 → 重新寻路，连续 3 次失败把该目标拉黑 90 秒，换下一个 |
| 死了就停在原地等 | 先等队友复活 `WaitForRaiseSeconds`，超时自动返回营地 |
| 每个 tick 都去喊一次自动循环，导致 RSR 目标在开/关之间反复横跳 | 开战/脱战命令只在状态真正变化时发一次 |

---

## 依赖

| 插件 | 必需 | 用途 |
| --- | --- | --- |
| **vnavmesh** | 是 | 寻路。没有它本插件不会乱走，只会提示你去装 |
| RotationSolver Reborn / WrathCombo / BossMod | 建议 | 战斗循环。本插件负责选目标 |
| Lifestream | 否 | 用不到，营地返回走游戏自带 `/return` |

## 安装

1. 安装并启用 [vnavmesh](https://puni.sh/api/repository/veyn)。
2. Dalamud 设置 → 实验性 → 自定义插件仓库，添加：

   ```text
   https://raw.githubusercontent.com/solenmingco/BozjaPilot/main/repo.json
   ```

3. 在插件列表里搜索 **BozjaPilot** 安装。

> 也可以直接把 Release 里的 `BozjaPilot.zip` 解压到 `%APPDATA%\XIVLauncherCN\devPlugins\BozjaPilot\` 做本地开发测试。

### 安装失败怎么办

Dalamud 的「插件 XXX 安装失败，请重启游戏后再试一次」是个通用错误框，真实原因只有一个地方能看到：

- 本插件自己的启动日志：`%APPDATA%\XIVLauncherCN\pluginConfigs\BozjaPilot\startup-error.log`
- Dalamud 主日志：`%APPDATA%\XIVLauncherCN\dalamud.log`，搜索 `BozjaPilot`

两个文件里搜到的第一段异常堆栈，就是失败原因。

如果只是 GitHub 下载不通（国内常见），可以打开
[Releases 页面](https://github.com/solenmingco/BozjaPilot/releases/latest) 用浏览器直接下 `BozjaPilot.zip`，
解压后把 `BozjaPilot.dll` 和 `BozjaPilot.json` 一起放进
`%APPDATA%\XIVLauncherCN\installedPlugins\BozjaPilot\1.0.1.0\`，重启游戏即可。

## 使用

```text
/bzp            打开主窗口
/bzp on         开始
/bzp off        停止
/bzp toggle     切换
/bzp config     设置
/bzp scan       把当前扫描到的活动打到聊天框（排查用）
```

进本后点「开始」即可。窗口里会实时显示：当前状态、决策理由、活动列表（类型 / 名称 / 状态 / 距离 / 剩余 / 前往）、
本次战果与每小时战果。

## 工作流程

```text
每 200ms 扫描 DynamicEventContainer
        │
        ├─ 已报名某个 CE ──→ 报名/准备阶段：站在圈里等   战斗阶段：打
        │
        ├─ 有 CE 正在报名 且 能赶到 ──→ 最高优先级，赶过去（必要时放弃当前遭遇战）
        │
        ├─ 有进行中的活动 ──→ 评分选最优 ──→ 赶路 ──→ 到场开打
        │
        └─ 都没有 ──→ 待机（可配置空闲后返回营地）
```

## 配置要点

| 选项 | 默认 | 说明 |
| --- | --- | --- |
| 参与遭遇战 / 参与 CE | 开 | 想专心刷 CE 可以关掉遭遇战 |
| CE 报名时放弃当前遭遇战 | 开 | 关掉的话遭遇战打到一半不会因为 CE 离开 |
| 报名安全余量 | 8 秒 | 路程估算的容错。网络卡就调大 |
| 到场后至少剩余 | 30 秒 | 到场只剩 10 秒的活动不值得跑 |
| 最远参与距离 | 700 米 | 超过就不考虑 |
| 停手距离 | 12 米 | 有敌人这么近就停脚，别拖着怪跑 |
| 卡住判定 | 3 秒 | 位移停滞多久算卡住 |
| 开战/脱战命令 | 空 | 例如 `/rotation auto`，用于联动你的循环插件 |

## 已知限制

- **必须先有 vnavmesh 网格**。博兹雅的网格需要你在 vnavmesh 里先跑一次建网格。
- CE / 遭遇战判定依赖「报名阶段」这一特征：如果你在 CE 已经开打后才启用插件，首次遇到它可能被当成遭遇战。
  打过一次后 id 会被记住（存在配置里），之后就不会再误判。
- 部分 CE（如攻城战 Castrum Lacus Litore、达尔里亚达 The Dalriada）是独立副本流程，本插件不会处理。
- 决斗（Duel）不处理。
- 自动化行为本身违反 SQUARE ENIX 的服务条款，使用风险自负。

## 开发

```bash
git clone https://github.com/solenmingco/BozjaPilot
cd BozjaPilot
dotnet build BozjaPilot/BozjaPilot.csproj -c Release
```

需要 .NET 10 SDK 与本地 Dalamud（`%APPDATA%\XIVLauncher\addon\Hooks\dev`，或用 `DALAMUD_HOME` 指定）。

## 许可证

MIT
