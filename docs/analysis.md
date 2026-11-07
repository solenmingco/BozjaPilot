# 三个博兹雅/新月岛自动插件的对比分析

> 本文是 BozjaPilot 的设计依据。分析对象：
> 1. **BOCCHI** — 新月岛（Occult Crescent）自动插件，`OhKannaDuh/BOCCHI`
> 2. **你给的插件库** — `YouShux/DalamudPlugins` 的 `Pluginmaster.json`
> 3. 国内常见的「特殊场景探索 / 自动临危受命」类模块（DailyRoutines 系）

---

## 0. 先说一个事实核对

你给的 `https://raw.githubusercontent.com/YouShux/DalamudPlugins/main/Pluginmaster.json`
目前只有 4 个插件，没有「特殊场景探索」：

| Name | InternalName | 说明 |
| --- | --- | --- |
| Omni Toolbox | OmniToolbox | 物价/库存/商店/悬浮窗预览 |
| Macro Redirection | MacroRedirection | 宏目标重定向 |
| FF Logs Viewer (汉化) | FFLogsViewer | 战斗日志 |
| Distance (汉化) | Distance | 距离显示 |

树树的仓库是「中文插件维护/汉化」集合，自动化不在里面。
「特殊场景探索」「自动临危受命」这两个名字对应的是 **Daily Routines（DR）** 一类模块，
它是另一个项目（原作者仓库已转为只发布、不公开源码，社区镜像多为旧版）。

所以下面的分析里，第 2 部分我按「这类模块的通用实现方式」来讲——这些模块的代码结构高度相似，
问题也高度一致，可以逐条对上你说的"不是特别好用"。

---

## 1. BOCCHI 架构拆解（值得抄的部分）

383 个 `.cs`，按层拆开大致是：

```
BOCCHI.Common        数据/配置/区域图/以太之光/持久化
BOCCHI.World         DynamicEvent 扫描（FateRepository / CriticalEncounterRepository）
BOCCHI.Automator     状态机（20 个 Handler）+ 目标规划 + 寻路 + 战斗接管
BOCCHI.Treasure      宝箱路线
BOCCHI.MobFarmer     刷怪
BOCCHI.Buff          知识水晶 buff
Ocelot               自研基础库（生命周期、状态机、IPC、日志、翻译）
```

### 1.1 它做对的关键四件事

**(a) 用 `DynamicEventContainer` 而不是猜。**
新月岛的 CE 和 FATE 都是 `DynamicEvent`，BOCCHI 直接读
`PublicContentOccultCrescent->DynamicEventContainer.Events`，拿到
`DynamicEventId / State / SecondsLeft / MapMarker.Position / Participants / Progress`。
不是靠聊天框正则，也不是靠地图图标。**这是所有同类插件里最正确的一步。**

**(b) 「我到底报上名没有」用 `EventId.EntryId` 判断。**
`BOCCHI.World/Services/CriticalEncounterContext.cs`：

```csharp
ushort entryId = player.BattleChara()->EventId.EntryId;
if (entryId == 0) return false;
CriticalEncounter ce = repository.SnapshotWithoutForkedTower()
    .FirstOrDefault(c => c.Id == new CriticalEncounterId(entryId));
```

玩家参与某个 DynamicEvent 时，自身的 `EventId.EntryId` 会被写成该事件的 id。
这是**唯一可靠的报名状态信号**。绝大多数简单插件在这一步靠"我站在圈里了所以应该报上了"，
于是出现了"报了名却进不去"的经典问题。

**(c) 状态机 + 显式「承诺」记忆。**
`CommittedFateMemory` / `CommittedCriticalEncounterMemory` / `SuspendTravelForActivityMemory`
这类「记忆」对象是防抖动核心：目标一旦承诺，就不允许每帧重算把它换掉。

**(d) 战斗接管刻意做了去抖。**
`AutoRotationController.EnableActivity` 里有一段注释很说明问题：

> Re-issuing Enable every tick made RSR flip Targeting Henched ↔ Off in FATEs

即：**每帧发一次「开启自动循环」IPC 会把 RSR 打崩**。这是血气教训，BozjaPilot 直接照抄了这个结论
（`CombatService` 只在 Engaged 状态翻转时发一次命令）。

### 1.2 BOCCHI 里对博兹雅**不能直接复用**的部分

| BOCCHI 的做法 | 为什么不适用于博兹雅 |
| --- | --- |
| 区域数据靠手写 JSON（`Data/Zones/*.json`：CE 坐标、战斗半径、传送点图） | 新月岛只有两张图且是作者自己测绘的；博兹雅/天佑女王的地图与 CE 数量大得多，没有现成数据文件 |
| 以太之光网络图寻路（`Aethernet` + `ZoneGraph`） | 博兹雅场内只有一个以太之光（营地），跨区靠坐骑跑，不存在城内传送网络 |
| `SupportJob`（幻境职业）、知识水晶 buff、魔法瓮 | 博兹雅是「失传技能 (Lost Action) + 战果 (Mettle) + 义军等级」体系，完全不同 |
| CE 判定依赖 zone JSON 里的 `ForkedTowerEventId` 等 | 博兹雅没有对应字段，必须换一种 CE/Skirmish 判定方式（见下） |
| 反作弊向的 `IllegalMode` 命名与开关 | 保留思路，但博兹雅需要独立的「报名可行性」判定 |

### 1.3 BOCCHI 在博兹雅场景下的真正缺口

1. **没有「报名窗口可行性判定」**。它把 CE 当成"目的地在哪"，而博兹雅的 CE 是"报名截止时间 + 圈内判定"。
   走不到就等于没参与，BOCCHI 的评分模型（距离/奖励）不会算这个。
2. **CE/Skirmish 分类依赖外部数据**。博兹雅没有这份数据，必须有运行时判定手段。
3. **没有失传技能（Lost Action）联动**。博兹雅里「失传圣灵药」「失传冲刺」等对刷效率影响很大。

---

## 2. 「特殊场景探索 / 自动临危受命」这类模块的通用问题

这类模块通常长这样（我按常见实现描述，代码风格各家不同）：

```csharp
// 每帧
var fate = 最近的 Fate;                       // 只看距离
if (fate != null && 距离 > 阈值) 寻路过去;
if (在 Fate 范围内) 自动循环开启;
```

它的问题清单，基本就是你感觉"不太好用"的来源：

| # | 问题 | 后果 |
| --- | --- | --- |
| 1 | **只处理 FATE，不处理 CE 报名**。博兹雅的 CE 不是 FATE，`FateManager` 里根本没有它 | CE 永远参与不上，只能刷遭遇战，战果效率腰斩 |
| 2 | **目标每帧重算，没有承诺机制** | 两个活动距离接近时，角色在两个点之间来回走，实际一个都没到 |
| 3 | **只按距离选目标，不算剩余时间** | 跑到一半活动结束；或者到场只剩 20 秒，贡献度极低拿不到战果 |
| 4 | **卡住无自救** | 博兹雅地形破碎（台阶、岩石、斜坡），卡一次就永久停摆，需要人盯着 |
| 5 | **阵亡后停在原地** | 等不到复活就白白挂机，还会因为「返回」逻辑缺失损失战果 |
| 6 | **每帧重复开关自动循环** | RSR 目标在开/关之间抖动，输出反而下降（BOCCHI 的注释已经证实） |
| 7 | **不区分「报名阶段 / 准备阶段 / 战斗阶段」** | 报名阶段没赶上，准备阶段还在往圈里跑，开战被传走后逻辑全乱 |

这七条里，1、2、3、4 是最致命的，也是 BozjaPilot 主要针对的点。

---

## 3. BozjaPilot 的设计取舍

### 3.1 判定：CE 还是遭遇战？

博兹雅没有新月岛那样的 CE 数据文件，也不适合维护一份 CE id 表（版本一变就废）。
但有一个免费的、永远正确的信号：

> **CE 一定有「报名 (Register)」阶段，遭遇战没有。**

所以：

```csharp
if (ev.State == DynamicEventState.Register && !activity.IsCriticalEngagement)
{
    activity.IsCriticalEngagement = true;            // 本次会话立即生效
    config.KnownCriticalEngagementIds.Add(id);       // 落盘，跨会话生效
}
```

首次遇到某 CE 的报名的窗口（通常 1~2 分钟）就完成学习，之后哪怕是在战斗中才进本也能认出来。
代价是「首次进入、且 CE 已经在打」的那一次可能误判为遭遇战——可接受，且自愈。

### 3.2 报名可行性判定

```text
eta   = distance / travelSpeed(mounted ? 18 : 7.5) * 1.18   // 地形惩罚
可报名 = eta + RegisterSafetyMargin <= SecondsLeft
```

不满足就直接不选这个 CE，而不是"先跑过去看看"。这是"不白跑"的核心。

### 3.3 评分模型

```text
score = clamp(SecondsLeft - eta, 0, 300)
      - eta * 0.6
      - Progress * 0.15          // 进度越高越接近结束，反而降权
      + Participants * 0.5
      + (IsCE ? 900 : 0)
```

- 到场后剩余时间是主项，路程是软惩罚，进度是反向修正。
- CE 的固定加成保证「只要 CE 能报名，永远优先于遭遇战」，但仍受可行性判定约束。

### 3.4 承诺机制

```text
if (有承诺目标 && 仍然有效 && 没有更好的 CE 报名) → 继续当前目标
else → 采用新目标
```

切换门槛是 `新目标分数 > 当前 + 120`，防止分数接近时反复横跳。

### 3.5 兜底恢复

| 场景 | 处理 |
| --- | --- |
| 位移停滞 > `StuckTimeout` | ① 跳跃 + 重新寻路 ② 下坐骑再上 + 重新寻路 ③ 放弃该目标并拉黑 90 秒 |
| 阵亡 | 等 `WaitForRaiseSeconds`，超时 `/return` |
| 过场/切区 | 暂停一切操作，不误触 |
| 不在博兹雅 | 停止一切操作并清空状态 |
| 活动消失 | 承诺失效，回到选目标 |

### 3.6 战斗为什么不做进来

- 博兹雅是 80 级同步、失传技能体系，循环写死会跟版本脱节。
- 用户基本都已经装了 RSR / WrathCombo / BossMod，重复实现只会互相打架（参考 BOCCHI 的 RSR 抖动问题）。
- 插件做**选目标**（`AutoTarget`，优先选 `EventId.EntryId` 匹配当前事件的敌人），循环插件就会自己开打。
- 需要强制开关时，用可配置的「开战/脱战命令」（如 `/rotation auto`），只发一次。

---

## 4. 结论

| 维度 | BOCCHI | DR 类模块 | BozjaPilot |
| --- | --- | --- | --- |
| 数据来源 | DynamicEventContainer | 多为 FateManager / 聊天框 | DynamicEventContainer |
| CE 支持 | 有（靠手写地图数据） | 基本没有 | 有（运行时学习 CE id） |
| 报名状态判定 | `EventId.EntryId` ✅ | 通常没有 | `EventId.EntryId` ✅ |
| 报名可行性判定 | ❌ | ❌ | ✅ |
| 防抖动 | ✅ 记忆机制 | 常见缺失 | ✅ 承诺 + 切换阈值 |
| 卡住自救 | ✅ | 常见缺失 | ✅ 三级降级 + 拉黑 |
| 战斗接管 | 内置多方案 | 依赖外部 | 只选目标 + 可选命令 |
| 适用区域 | 新月岛 | 通用 FATE | 南方战线 / 天佑女王 |

BozjaPilot 的目标不是"功能比 BOCCHI 多"，而是**把 BOCCHI 已经被验证正确的四个机制（DynamicEvent 读取、
EventId 报名判定、承诺记忆、战斗去抖）移植到博兹雅，并补上博兹雅独有的报名可行性判定与地形自救**。
