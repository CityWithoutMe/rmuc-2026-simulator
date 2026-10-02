# 机器人属性管理器（RobotAttributeManager）

面向后续接入 **区域 Buff / 裁判 / 移动** 的开发说明。本文档对应当前工程里的实际代码，不描述尚未实现的功能。

相关脚本：

| 文件 | 作用 |
|------|------|
| `Assets/Script/Robot/RobotStat.cs` | `RobotTeam`、`RobotType`、`RobotStat`、`StatModifier` |
| `Assets/Script/Robot/RobotAttributeManager.cs` | 挂到机器人上的属性组件 |
| `Assets/Script/PlayerControl/PlayerMovement.cs` | 现有移动脚本，**尚未接入**属性管理器。商店打开时暂停锁鼠标和转向 |
| `Assets/Script/PlayerControl/PlayerShooting.cs` | 英雄射击：没有 42mm 则不开火、不加热；打出后每发 +100 热量 |
| `Assets/Script/PlayerControl/AmmoShop.cs` | 按 B 打开补给商店，用金币买 17mm / 42mm |

不要改这两个属性脚本的行为来迁就文档；数值默认值可按赛季在 Inspector 改。

---

## 1. 作用与文件结构

`RobotAttributeManager` 是挂在机器人（或建筑物）GameObject 上的 `MonoBehaviour`。它做三件事：

1. 保存 **身份**（`robotId` / 队伍 / 兵种），这些不走修饰器。
2. 为每个 `RobotStat` 保存 **运行时基础值**，再用修饰器算出 **当前值**。
3. 对血量、弹药、热量等 **资源型** 属性另存一份存量，读写时 Clamp。

当前值公式（派生型）：

```text
当前值 = (base + Σadd) * (1 + Σpercent)
```

然后部分属性会再做一次后处理（布尔压成 0/1，速度/伤害等不能为负）。见第 3 节。

生命周期：

- `Awake`：初始化数组、把 Inspector 的 `base*` 拷进运行时、填资源存量。
- `Update`：倒计时修饰器；若 `autoCoolBarrel` 则按 `CoolingRate` 降温；若 `autoRecoverHp` 且存活则按 `RecoveryRate` 回血；刷新 Inspector 的 `view*`。

内部常量（死亡 / 罚下时给 `CanMove`、`CanShoot` 打的修饰器来源）：

| 常量 | 值 |
|------|-----|
| `RobotAttributeManager.DeadSourceId` | `"_internal_dead"` |
| `RobotAttributeManager.FoulOutSourceId` | `"_internal_foul"` |

---

## 2. 怎么挂到机器人上

1. 选中机器人根物体（有碰撞体 / Rigidbody 的那一层即可，和移动脚本同物体更方便）。
2. **Add Component** → `RobotAttributeManager`。
3. Inspector **身份**：
   - `team`：`Neutral` / `Red` / `Blue`
   - `robotType`：见第 5 节兵种清单
   - `robotId`：可手填；用默认值菜单时会按队伍覆盖
4. 组件标题栏 **右键** → **「按兵种填入默认基础值（可按赛季改）」**  
   会调用 `ApplyRobotTypeDefaults()`，按当前 `robotType` + `team` 写入一套常见量级的 `base*`。Play 模式下还会立刻 `ResetToBase()`。
5. 按本赛季规则改 `baseHp`、`baseMoveSpeed`、弹药上限等。这些才是开局基础值。
6. 需要看运行时血量/速度时，展开 **「运行时查看」** 的 `view*`（仅 Play 模式由脚本写入）。
7. 死亡/复活也可在 Inspector 绑 `onDeath` / `onRevive`（UnityEvent）。

重置一整局：组件右键 **「比赛重置 ResetToBase」**，或代码调用 `ResetToBase()`。会清空全部修饰器，再从当前 Inspector 的 `base*` 重建。

---

## 3. 当前值公式

对 **非资源型**（派生型）属性：

```text
v = (runtimeBase + Σ modifier.add) * (1 + Σ modifier.percent)
```

再经 `PostProcess`：

| 处理 | 属性 |
|------|------|
| `v > 0.5` → `1`，否则 `0` | `CanMove`、`CanShoot`、`IsPowered`、`IsIdle`、`IsDefeated`、`IsFoulOut`、`IsInvulnerable` |
| `max(0, v)` | `MaxHp`、`MoveSpeed`、`RotateSpeed`、`SpinSpeed`、云台速度、`ChassisPowerLimit`、`CoolingRate`、`FireRate`、`ProjectileSpeed`、`Damage`、弹药上限、`BarrelHeatLimit`、`RecoveryRate` |
| 原样 | 其余派生项（如三个 Buff 显示百分比） |

示例：基础移速 `5`，一个修饰器 `add = 1`、`percent = 0.5`：

```text
(5 + 1) * (1 + 0.5) = 9
```

多个修饰器的 `add` 相加、`percent` 相加（先加后乘，乘区之间是加算关系，不是叠乘）。

**资源型**不走这条公式读当前值：`GetCurrent` 直接返回存量 `stored`，再用上限 Clamp。上限本身仍用上面的公式（例如 `MaxHp`、`BarrelHeatLimit`）。

特例 **`ChassisPowerBuffer`**：Inspector / `GetBase` 表示 **容量**；`GetCurrent` 表示 **当前能量**；容量请用 `GetCap(RobotStat.ChassisPowerBuffer)`。给该属性加修饰器改的是容量，存量会被夹紧，不会把能量改成「算出来的容量」。

---

## 4. 资源型 vs 派生型

由内部 `IsResource` 决定。

**资源型**（有独立存量，`SetCurrent` 合法）：

`Hp`、`ShieldHp`、`Exp`、`Level`、`ReviveCount`、`ReviveRemaining`、`Ammo17mm`、`Ammo42mm`、`BarrelHeat`、`ChassisPowerBuffer`、`Coins`、`RemainingFoul`、`RfidZoneId`、`CurrentZoneFlags`

行为：

- `GetCurrent` = Clamp 后的存量。
- `SetCurrent` 写入存量并 Clamp。
- `SetBase` 一般会把存量改成新基础值（电容除外：只按新容量夹紧当前能量）。
- 扣血、打弹、加热请用第 9 节的专用方法，不要自己改 `MoveSpeed` 这种派生项。

**派生型**（由基础值 + 修饰器计算）：

`MaxHp`、移动/云台/功率上限、`CanMove` / `CanShoot`、射速伤害、三个 Buff **显示**百分比、`IsPowered` 等开关、`RecoveryRate` 等。

行为：

- `GetCurrent` = 公式结果。
- **`SetCurrent` 会打 Warning 并直接 return**。改这些请用 `SetBase` 或 `AddModifier`。

`GetCap` 给资源型找上限：

| 属性 | 上限来源 |
|------|----------|
| `Hp` | 当前 `MaxHp`（含修饰器） |
| `Ammo17mm` / `Ammo42mm` | 对应 `MaxAmmo*` |
| `BarrelHeat` | `GetCap` 仍返回 `BarrelHeatLimit`（给 `TryAddBarrelHeat`）。存量可以高于上限，Clamp 只保证 ≥ 0，见 9.6.1 |
| `ChassisPowerBuffer` | 该属性自己的修饰后基础值（容量） |
| `ShieldHp` | 修饰后的护盾基础值（注意：存量 Clamp 目前只保证 `>= 0`，没有按这个 cap 再夹一次） |
| 其它 | `ComputeModified(stat)` |

---

## 5. 身份字段

身份 **不在** `RobotStat` 里，不能 `GetCurrent`。

| 字段 | 类型 | 默认 | 说明 |
|------|------|------|------|
| `robotId` | `int` | `3` | 机器人编号。默认菜单会按兵种+队伍覆盖 |
| `team` | `RobotTeam` | `Red` | 队伍 |
| `robotType` | `RobotType` | `Infantry` | 兵种 / 建筑物 |

`RobotTeam`：

| 值 | 含义 |
|----|------|
| `Neutral = 0` | 中立，场地机关 / 未分配 |
| `Red = 1` | 红 |
| `Blue = 2` | 蓝 |

`RobotType` 与默认菜单写入的 `robotId`（红 / 蓝）：

| 兵种 | 枚举 | 红 | 蓝 | 备注 |
|------|------|----|----|------|
| 步兵 | `Infantry` | 3 | 103 | 默认组件数值就是这一档 |
| 英雄 | `Hero` | 1 | 101 | 血 200，42mm，伤害 100 |
| 工程 | `Engineer` | 2 | 102 | `CanShoot = false`，无弹药 |
| 空中支援 | `Aerial` | 6 | 106 | 无底盘功率/电容 |
| 哨兵 | `Sentry` | 7 | 107 | 血 600，复活次数 0 |
| 飞镖系统 | `Dart` | 8 | 108 | 不移动；保留射击相关基础伤害 |
| 雷达站 | `Radar` | 9 | 109 | 不能移动/射击 |
| 前哨站 | `Outpost` | 10 | 110 | 建筑物 |
| 基地 | `Base` | 11 | 111 | 血 5000 + 护盾 500 |

`ApplyRobotTypeDefaults()` 会先把一批公共字段复位（等级 1、护盾 0、电容 60、可移动/可射击、犯规余地 4、上电等），再按 `switch (robotType)` 覆盖。  
**未在该兵种分支里赋值的字段会保持「方法开头复位后的值」或进入方法前的 Inspector 值。** 例如 `Engineer`、`Aerial` 分支没有写 `baseRotateSpeed`，转向速度不会被菜单改掉。切兵种后请扫一眼 Inspector。

`ReviveRemaining`：`< 0` 表示不限制；建筑物默认菜单写成 `0`。

---

## 6. RobotStat 全表

枚举定义在 `RobotStat.cs`。单位以脚本 Tooltip / Header 为准；赛季不同请改 `base*`，不要改枚举名。

「资源型」列与 `IsResource` 一致。

### 等级

| 枚举 | 含义 | 单位 | 资源型 |
|------|------|------|--------|
| `Level` | 等级 | 级 | 是 |
| `Exp` | 经验 | — | 是 |

### 生存

装甲板暂未拆分，用总血 + `IsAlive` / `IsInvulnerable`。

| 枚举 | 含义 | 单位 | 资源型 |
|------|------|------|--------|
| `Hp` | 当前血量 | HP | 是，Clamp 到 `[0, MaxHp]` |
| `MaxHp` | 最大血量 | HP | 否 |
| `ShieldHp` | 护盾（基地等） | HP | 是 |
| `ReviveCount` | 已复活次数 | 次 | 是 |
| `ReviveRemaining` | 剩余复活次数 | 次，`<0` 不限制 | 是 |

### 移动 / 底盘

| 枚举 | 含义 | 单位 | 资源型 |
|------|------|------|--------|
| `MoveSpeed` | 平移速度 | m/s | 否 |
| `RotateSpeed` | 底盘转向 | deg/s | 否 |
| `ChassisPowerLimit` | 底盘功率上限 | W | 否 |
| `ChassisPowerBuffer` | 超级电容：基础值=容量，当前值=能量 | 能量 | 是（存量） |
| `SpinSpeed` | 小陀螺转速 | deg/s | 否 |
| `CanMove` | 底盘允许移动（0/1） | 布尔 | 否 |

对外属性 `CanMove`（注意和枚举同名）还会叠加存活、上电、未罚下，见第 9 节。

### 云台

| 枚举 | 含义 | 单位 | 资源型 |
|------|------|------|--------|
| `GimbalYawSpeed` | 云台偏航 | deg/s | 否 |
| `GimbalPitchSpeed` | 云台俯仰 | deg/s | 否 |
| `CanShoot` | 允许射击（0/1） | 布尔 | 否 |

对外 `CanShoot` 还会要求未过热等。

### 射击 / 武器

| 枚举 | 含义 | 单位 | 资源型 |
|------|------|------|--------|
| `Ammo17mm` | 17mm 当前弹量 | 发 | 是 |
| `Ammo42mm` | 42mm 当前弹量 | 发 | 是 |
| `MaxAmmo17mm` | 17mm 上限 | 发 | 否 |
| `MaxAmmo42mm` | 42mm 上限 | 发 | 否 |
| `BarrelHeat` | 枪口热量 | 热量 | 是 |
| `BarrelHeatLimit` | 热量上限 | 热量 | 否 |
| `CoolingRate` | 冷却速度 | 热量/秒 | 否 |
| `FireRate` | 射速 | 发/秒 | 否 |
| `ProjectileSpeed` | 弹速 | m/s | 否 |
| `Damage` | 单发伤害（真正乘区打在这） | — | 否 |
| `AttackBuffPercent` | 攻击加成 **显示值**，`0.5` = +50% | 比例 | 否 |
| `DefenseBuffPercent` | 防御加成 **显示值**，`0.5` = 承伤 50%；`TakeDamage` 会读 | 比例 | 否 |
| `CooldownBuffPercent` | 冷却加成 **显示值**；真正冷却乘区请打在 `CoolingRate` | 比例 | 否 |

步兵默认：17mm 伤害约 10，热量上限约 240，射速 10（占位量级）。英雄一级的热量上限、冷却和过热扣血见 9.6.1。

### 经济 / 裁判

| 枚举 | 含义 | 单位 | 资源型 |
|------|------|------|--------|
| `Coins` | 金币 | — | 是，`>= 0` |
| `RemainingFoul` | 剩余犯规余地 | 次，**可以为负** | 是 |
| `IsFoulOut` | 是否罚下 | 布尔 | 否 |
| `RfidZoneId` | 当前 RFID / 功能区 id，`0` = 不在区 | id | 是 |
| `CurrentZoneFlags` | 功能区 bitmask，给区域管理器 | 位标志 | 是 |

**XZ 平面区域判定尚未实现。** 本组件只提供 `SetRfidZone` / `AddZoneFlag` / `AddZoneBuff`，不检测机器人在不在绿区里。

### 状态

| 枚举 | 含义 | 单位 | 资源型 |
|------|------|------|--------|
| `IsPowered` | 是否上电 | 布尔 | 否 |
| `IsIdle` | 是否空闲/待机 | 布尔 | 否 |
| `IsDefeated` | 是否阵亡标记 | 布尔 | 否 |
| `IsInvulnerable` | 无敌（`TakeDamage` 直接 0） | 布尔 | 否 |
| `RecoveryRate` | 回血速度；回血区把修饰器叠这里 | HP/秒 | 否 |

布尔在内部是 `float`：`> 0.5` 视为真。`SetBase(RobotStat.CanMove, 0f)` 可以关掉底盘开关。

---

## 7. StatModifier 字段

```csharp
[Serializable]
public class StatModifier
{
    public string sourceId;   // 来源 id，例如 zone_buff_a
    public float add;         // 加算，先加到 base
    public float percent;     // 乘区。0.5 表示 +50%
    public bool stackable;    // 同源是否叠多份
    public float duration;    // 秒；< 0 永久（默认 -1）
    public int refCount;      // 引用计数，默认 1
    // remaining：运行时剩余时间，[NonSerialized]，永久为 -1
}
```

| 字段 | 说明 |
|------|------|
| `sourceId` | 同源查找、引用计数、`RemoveModifiersFromSource` 都靠它。空字符串时 `AddModifier` 会改成 `"_anon_" + Guid` |
| `add` | 加到 `runtimeBase` 上 |
| `percent` | 加到乘区。公式 `(base + Σadd) * (1 + Σpercent)` |
| `stackable` | `true`：每次 `AddModifier` 都新加一份。`false`：同源合并，只加 `refCount` |
| `duration` | `< 0` 永久。`>= 0` 时 `Update` 里扣 `remaining`，到 0 删除 |
| `refCount` | 重叠次数。`<= 0` 时从列表移除 |
| `remaining` | 运行时倒计时，克隆时设为 `duration`。`IsPermanent => duration < 0f` |

工厂方法（二者都把 **`stackable = false`**）：

```csharp
StatModifier.PercentBonus(string sourceId, float percent, float duration = -1f);
StatModifier.Additive(string sourceId, float add, float duration = -1f);
```

---

## 8. 引用计数规则（区域 Buff 必读）

区域 Buff **必须** `stackable = false`（工厂方法已是如此）。`AddZoneBuff` 内部也走这两套工厂。

同源且不可叠加时，`AddModifier`：

1. 若该 `RobotStat` 上已有相同 `sourceId`：`refCount++`，并用新修饰器的 `add` / `percent` / `duration` / `remaining` **刷新**（不会再插一条）。
2. 否则克隆一份加入列表。

移除：

- `RemoveModifier(stat, sourceId)` 或 `RemoveModifiersFromSource(sourceId)`：对该来源 **减一次** `refCount`，减到 `0` 才从列表删掉。
- `force: true`：不管计数，直接摘掉该来源（`Die`/`Revive` 清 `_internal_dead` 时用 force）。

因此两个绿区重叠时：

```text
进区 A → AddZoneBuff("zone_green")     refCount = 1
进区 B（同一 sourceId）→ 再 Add         refCount = 2，数值刷新但不双倍
出区 A → RemoveZoneBuff("zone_green")  refCount = 1，Buff 仍在
出区 B → 再 Remove                     refCount = 0，移除
```

不要给同一功能区用 `stackable = true`，否则每进一次就多乘一份。

`RemoveModifier(stat, sourceId)` 非 force 时，同一列表里同 `sourceId` **一次调用只减一条**（内部 `ReleaseList` 减完立刻 return）。

---

## 9. RobotAttributeManager 公开 API

### 9.1 便捷属性（只读）

这些是 `GetCurrent` 的包装，布尔组合逻辑如下。

| 属性 | 含义 |
|------|------|
| `Hp` / `MaxHp` | 当前血 / 最大血 |
| `MoveSpeed` / `RotateSpeed` / `Damage` | 当前移速、转向、伤害 |
| `IsAlive` | `Hp > 0` **且** `IsDefeated < 0.5` |
| `IsInvulnerable` / `IsPowered` / `IsIdle` / `IsDefeated` / `IsFoulOut` | 对应枚举当前值 `> 0.5` |
| `IsOverheated` | `BarrelHeat > BarrelHeatLimit`（略过一点浮点误差）。等于上限不算过热 |
| `IsOverheatDamaging` | 已过热且仍存活，正在按 10 Hz 扣血 |
| `CanMove` | `IsAlive && IsPowered && !IsFoulOut && GetCurrent(CanMove) > 0.5` |
| `CanShoot` | 同上，再加 `GetCurrent(CanShoot) > 0.5 && !IsOverheated` |

移动 / 开火请用这两个 **属性**，不要只看枚举 `RobotStat.CanMove`。死亡和罚下会再往枚举上叠 `add = -1` 的内部修饰器。

### 9.2 基础值 / 当前值

```csharp
float GetBase(RobotStat stat);
void  SetBase(RobotStat stat, float value);
float GetCurrent(RobotStat stat);
void  SetCurrent(RobotStat stat, float value); // 仅资源型
float GetCap(RobotStat stat);
```

- `SetBase`：改运行时基础值。资源型会同步存量（电容只夹紧）。派生型会夹紧受影响的资源（例如改 `MaxHp` 会夹当前 `Hp`）。
- `SetCurrent`：非资源型会 `Debug.LogWarning`：「SetCurrent 只用于资源型属性（血量/弹药/热量等），派生属性请用 SetBase 或 AddModifier」。

### 9.3 修饰器

```csharp
StatModifier AddModifier(RobotStat stat, StatModifier modifier);
bool RemoveModifier(StatModifier modifier);                          // 按引用从所有属性列表摘掉
bool RemoveModifier(RobotStat stat, string sourceId, bool force = false);
void RemoveModifiersFromSource(string sourceId, bool force = false); // 所有属性上该来源
bool HasModifier(string sourceId);                                   // 任一属性
bool HasModifier(RobotStat stat, string sourceId);
int  GetModifierRefCount(RobotStat stat, string sourceId);           // 没有则 0
IReadOnlyList<StatModifier> GetModifiers(RobotStat stat);
void ClearAllModifiers();
```

`AddModifier` 返回列表里那份（合并时返回已有对象，新建时返回克隆）。`modifier == null` 返回 `null`。

### 9.4 功能区便捷接口

```csharp
void AddZoneBuff(string sourceId,
    float attackPercent = 0f,
    float defensePercent = 0f,
    float cooldownPercent = 0f,
    float speedPercent = 0f);

void RemoveZoneBuff(string sourceId); // 即 RemoveModifiersFromSource(sourceId)
```

非 0 的参数才会加修饰器，全部 `stackable = false`、永久（`duration = -1`）：

| 参数 | 实际打点 |
|------|----------|
| `attackPercent` | `Damage` ← `PercentBonus`；`AttackBuffPercent` ← `Additive`（给 UI/裁判看） |
| `defensePercent` | 只打 `DefenseBuffPercent` ← `Additive`。`TakeDamage` 用 `承伤 *= (1 - Clamp01(防御百分比))` |
| `cooldownPercent` | `CoolingRate` ← `PercentBonus`；`CooldownBuffPercent` ← `Additive` |
| `speedPercent` | 只打 `MoveSpeed` ← `PercentBonus`（没有单独的移速显示百分比字段） |

回血区不要走 `AddZoneBuff`，对 `RecoveryRate` 自己 `AddModifier`。

### 9.5 伤害 / 治疗 / 死亡 / 复活

```csharp
float TakeDamage(float amount, bool ignoreDefense = false);
float Heal(float amount);
void  Die();
bool  Revive(float hpPercent = 1f);
void  ResetToBase();
```

`TakeDamage`：

- `amount <= 0`、已死、无敌 → 返回 `0`。
- 默认读 `DefenseBuffPercent`，`Clamp01` 后 `amount *= 1 - def`。`ignoreDefense: true` 跳过。
- 先扣 `ShieldHp`，再扣 `Hp`。返回实际扣掉的总量（含护盾）。
- `Hp` 扣到 `<= 0` 会调 `Die()`。

`Heal`：已死返回 `0`；不超过当前 `MaxHp`；返回实际加了多少。

`Die()`：

- 血量置 0，`IsDefeated` 基础值设 1，`IsIdle` 设 0。
- 若还没有 `DeadSourceId`，给 `CanMove` / `CanShoot` 各加 `Additive(-1)`。
- 第一次死亡触发 `Died` 和 `onDeath`。已死且事件已发过则直接 return。

`Revive(hpPercent)`：

- 已活着 → `false`。
- `ReviveRemaining == 0` → `false`（建筑物默认如此）。
- `> 0` 则减 1；`< 0` 不减（无限复活）。
- `ReviveCount + 1`，清 `IsDefeated`，**force** 移除 `DeadSourceId`。
- 血量 = `clamp(MaxHp * clamp01(hpPercent), 1, MaxHp)`（至少 1 点）。
- 触发 `Revived`、`onRevive`。

`ResetToBase()`：清空修饰器，按 **此刻 Inspector 的 `base*`** 重建，并清死亡事件标记。

### 9.6 弹药 / 热量 / 电容

```csharp
bool TryConsumeAmmo17mm(int count = 1);
bool TryConsumeAmmo42mm(int count = 1);
int  AddAmmo17mm(int count);   // 返回实际加上的数量（受上限影响）
int  AddAmmo42mm(int count);

bool AddBarrelHeat(float heat);      // 正加热、负降温，允许高于上限；返回是否过热
bool TryAddBarrelHeat(float heat);   // 这一加热会超过上限则 false 且不加；降温仍走 AddBarrelHeat

float AddChassisPowerBuffer(float energy); // 可正可负；返回实际变化量
```

`count <= 0` 的消耗视为成功。不够弹返回 `false` 且不扣。`AddAmmo17mm` / `AddAmmo42mm` 加完会 Clamp 到对应 `MaxAmmo`，返回值是实际加上的发数。

玩家英雄车 `PlayerShooting` 在生成弹丸前调用 `TryConsumeAmmo42mm(1)`。`CanShoot` 为假（含过热）或弹药不够时，本发不生成，也不扣弹、不加热。打出后 `AddBarrelHeat(HeatPer42mmRound)`，即 +100，允许超过上限。这次射击不消耗 17mm。

`autoCoolBarrel == true` 时每帧：`AddBarrelHeat(-CoolingRate * dt)`。过热期间同样走这条降温。扣血规则见下一小节。

### 9.6.1 枪口过热

出处：RoboMaster 机甲大师赛规则「枪口热量超过上限」。2024、2025 手册条文一致：17mm 每发 +10 热量，42mm 每发 +100；热量大于上限时该发射机构不能发射，并以 10 Hz 扣血，每次伤害为 `(当前热量 - 热量上限) / 250 × 最大血量`；热量回到上限及以下后停止扣血并恢复发射。本次未能打开 2026 手册正文，代码未另写公式。

| 项 | 行为 |
|----|------|
| 每发热量 | `HeatPer17mmRound = 10`，`HeatPer42mmRound = 100`。英雄射击用 42mm |
| 过热判定 | `BarrelHeat > BarrelHeatLimit` 时 `IsOverheated`，`CanShoot` 为 false |
| 射击 | `PlayerShooting` 在扣 42mm 之前就返回，不发射、不白扣弹。把热量打过上限的那一发仍然打出 |
| 扣血 | 仅超限且存活时，累计满 0.1 秒结算一次（10 Hz），不按帧扣。`TakeDamage((热量 - 上限) / 250 × 最大血量, ignoreDefense: true)`。伤害 ≤ 0 不扣，不吃防御 Buff |
| 解除 | 热量 ≤ 上限后停止扣血，`IsOverheatDamaging` 为 false，射击恢复（仍须存活、有弹、上电等） |
| 冷却 | 只有 `autoCoolBarrel`，过热期间继续降温 |

英雄一级（`ApplyRobotTypeDefaults`）：血量 200，42mm 热量上限 200，冷却 20/秒，射速 2 发/秒。血量和热量上限与常见一级初值一致，未改。冷却和射速本次打不开 2025/2026 等级表，不能确认和手册不符，因此保持原值。

### 9.7 金币 / 犯规 / RFID

```csharp
int  AddCoins(int delta);          // 不低于 0；返回实际变化
bool TrySpendCoins(int cost);      // cost<=0 为 true；不够则 false

bool AddFoul(int count = 1);       // 扣剩余犯规。罚下返回 true，尚未罚下返回 false

void SetRfidZone(int zoneId);      // SetCurrent(RfidZoneId)
void AddZoneFlag(int flag);        // 按位或写入 CurrentZoneFlags
void RemoveZoneFlag(int flag);     // 按位清除
```

`AddCoins` 不低于 0，返回实际变化。`TrySpendCoins`：`cost <= 0` 为 true；存量不够则 `false` 且不扣。

补给商店（`AmmoShop`，按 **B**）里金币暂时无限：界面固定写「金币：无限」，购买前把 `Coins` 设得很大再调用 `TrySpendCoins`，不会因为余额不够而买失败。文档原先没有单价，商店常量按常见 RM 兑换（可按赛季改）：**17mm 为 1 金币/发，按钮一次买 10 发（10 金币）；42mm 为 15 金币/发，按钮一次买 1 发（15 金币）**。已到 `MaxAmmo` 时按钮仍可点，但不会超过上限。

`AddFoul`：`RemainingFoul -= count`。若结果 `> 0` 返回 `false`。否则：

- `SetBase(IsFoulOut, 1)`
- 若还没有 `FoulOutSourceId`，给 `CanMove` / `CanShoot` 加 `Additive(-1)`
- 返回 `true`（已罚下）

没有对应的「撤销罚下」公开方法；需要的话应 `SetBase(IsFoulOut, 0)` 并 `RemoveModifiersFromSource(FoulOutSourceId, true)`。

### 9.8 兵种默认值与自动结算字段

```csharp
void ApplyRobotTypeDefaults(); // ContextMenu「按兵种填入默认基础值（可按赛季改）」
```

公开字段（可在 Inspector 改）：

| 字段 | 默认 | 说明 |
|------|------|------|
| `autoCoolBarrel` | `true` | 每帧按当前 `CoolingRate` 降温 |
| `autoRecoverHp` | `true` | 存活时每帧 `Heal(RecoveryRate * dt)` |
| 全部 `base*` | 见脚本 Header | 开局基础值，`ResetToBase` / `Awake` 读取 |

---

## 10. 事件

| 名称 | 类型 | 触发 |
|------|------|------|
| `StatChanged` | `event Action<RobotStat, float, float>` | 某属性新旧值变化（近似相等不发）。参数：`stat, oldValue, newValue` |
| `Died` | `event Action<RobotAttributeManager>` | 第一次进入死亡流程 |
| `Revived` | `event Action<RobotAttributeManager>` | 复活成功 |
| `onDeath` | `UnityEvent` | 与 `Died` 同时，可在 Inspector 绑 |
| `onRevive` | `UnityEvent` | 与 `Revived` 同时 |

代码订阅示例：

```csharp
void OnEnable()
{
    attr.StatChanged += OnStatChanged;
    attr.Died += OnDied;
    attr.Revived += OnRevived;
}

void OnDisable()
{
    attr.StatChanged -= OnStatChanged;
    attr.Died -= OnDied;
    attr.Revived -= OnRevived;
}

void OnStatChanged(RobotStat stat, float oldValue, float newValue)
{
    if (stat == RobotStat.Hp)
        Debug.Log($"HP {oldValue} -> {newValue}");
}

void OnDied(RobotAttributeManager who) { }
void OnRevived(RobotAttributeManager who) { }
```

---

## 11. 接入示例

属性管理器 **还没有** 接到 `PlayerMovement`。下面是建议写法，不要在属性脚本里改移动。

### 11.1 绿区域 Buff

区域检测（Trigger / XZ 范围）由区域脚本负责。属性侧只在「进入 / 离开」时记账。同一 `sourceId` 可重叠多个碰撞体。

```csharp
using UnityEngine;

public class GreenZoneBuff : MonoBehaviour
{
    public string sourceId = "zone_green";
    [Tooltip("0.5 = 攻击 +50%，冷却 +50%")]
    public float attackPercent = 0.5f;
    public float cooldownPercent = 0.5f;
    public float defensePercent = 0f;
    public float speedPercent = 0f;

    void OnTriggerEnter(Collider other)
    {
        var attr = other.GetComponentInParent<RobotAttributeManager>();
        if (attr == null) return;

        attr.AddZoneBuff(
            sourceId,
            attackPercent: attackPercent,
            defensePercent: defensePercent,
            cooldownPercent: cooldownPercent,
            speedPercent: speedPercent);

        attr.SetRfidZone(1);          // 具体 id 由裁判/场地表定义
        attr.AddZoneFlag(1 << 0);     // 具体 bit 由区域管理器定义
    }

    void OnTriggerExit(Collider other)
    {
        var attr = other.GetComponentInParent<RobotAttributeManager>();
        if (attr == null) return;

        attr.RemoveZoneBuff(sourceId);
        // 只有完全离开该来源时才清 RFID；多区重叠时请先查 refCount
        if (attr.GetModifierRefCount(RobotStat.Damage, sourceId) <= 0)
            attr.SetRfidZone(0);
        attr.RemoveZoneFlag(1 << 0);
    }
}
```

回血区示例（不走 `AddZoneBuff`）：

```csharp
attr.AddModifier(
    RobotStat.RecoveryRate,
    StatModifier.Additive("zone_heal", add: 10f)); // 10 HP/s，stackable=false

// 离开：
attr.RemoveModifier(RobotStat.RecoveryRate, "zone_heal");
```

### 11.2 接移动脚本

当前 `PlayerMovement` 使用自己的 `speed` 字段，与属性无关。接入时用 `attr.MoveSpeed`，并用 `attr.CanMove` 卡住底盘。

```csharp
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(RobotAttributeManager))]
public class PlayerMovement : MonoBehaviour
{
    RobotAttributeManager attr;
    Rigidbody rb;
    Vector3 moveInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        attr = GetComponent<RobotAttributeManager>();
        rb.freezeRotation = true;
    }

    void Update()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        moveInput = new Vector3(x, 0f, z).normalized;
    }

    void FixedUpdate()
    {
        if (!attr.CanMove)
        {
            Vector3 stop = rb.linearVelocity;
            stop.x = 0f;
            stop.z = 0f;
            rb.linearVelocity = stop;
            return;
        }

        Vector3 velocity = moveInput * attr.MoveSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;
    }
}
```

转向可用 `attr.RotateSpeed`（deg/s）。射击前判断 `attr.CanShoot`。当前玩家英雄车在 `PlayerShooting` 里扣 42mm（不够则不开火、不加热），不扣 17mm。步兵接入时再改回 `TryConsumeAmmo17mm`：

```csharp
if (!attr.CanShoot) return;
if (!attr.TryConsumeAmmo42mm(1)) return;
attr.AddBarrelHeat(100f); // 英雄 42mm 每发，与 PlayerShooting 一致
float dmg = attr.Damage;
```

---

## 12. 注意事项

1. **数值按赛季改。** `ApplyRobotTypeDefaults` 只是常见量级占位（步兵 100HP / 80W / 500 发等）。以当年规则为准改 Inspector，不要把默认值写死进玩法逻辑。
2. **XZ 区域判定尚未实现。** 本组件不包含「机器人在不在绿区」的几何判断。区域脚本自己做 Trigger 或 XZ 距离，再调 `AddZoneBuff` / `SetRfidZone`。
3. **不要用 `SetCurrent` 改派生属性。** `MoveSpeed`、`Damage`、`MaxHp`、`CanMove` 等会 Warning。Buff 用修饰器；改开局数值用 `SetBase` 或改 Inspector 再 `ResetToBase`。
4. **攻击显示值和真实伤害是两套。** `AddZoneBuff` 的攻击百分比会同时改 `Damage`（乘区）和 `AttackBuffPercent`（显示）。自己加修饰器时，UI 要显示加成请记得同步 `AttackBuffPercent`；只改显示字段 **不会** 改变实际 `Damage`。
5. **防御只影响 `TakeDamage`。** `defensePercent` 不改 `Damage`。`Clamp01` 后承伤最低可到 0（100% 防御）。
6. **死亡 / 罚下不要重复叠内部修饰器。** 已用 `HasModifier` 防重入。复活用 force 清 `_internal_dead`；罚下清除需自行处理 `_internal_foul`。
7. **无限复活 vs 不能复活。** `ReviveRemaining < 0` 不扣次数；`== 0` 的 `Revive` 直接失败。
8. **电容。** `GetCurrent(ChassisPowerBuffer)` 是能量，`GetCap` 是容量。
9. **Play 前改 `base*` 会在 `Awake` 生效；Play 中改 Inspector 的 `base*` 不会自动写进 `runtimeBase`**，除非再调 `ResetToBase()` 或 `SetBase`。右键填默认值在 Play 中会 `ResetToBase`。
10. 工程 / 雷达 / 前哨 / 基地默认不能射击或不能移动；空中支援电容为 0。接通用移动前先看 `CanMove` 和 `baseMoveSpeed`。

---

## 13. Inspector 运行时 `view*` 字段

位于 Header **「运行时查看（Play 模式由脚本写入）」**，均为 `[SerializeField]` 私有字段，**不能**当 API 用。

| 字段 | 写入来源 |
|------|----------|
| `viewHp` | `GetCurrent(Hp)` |
| `viewMaxHp` | `GetCurrent(MaxHp)` |
| `viewMoveSpeed` | `GetCurrent(MoveSpeed)` |
| `viewDamage` | `GetCurrent(Damage)` |
| `viewBarrelHeat` | `GetCurrent(BarrelHeat)` |
| `viewAmmo17mm` | `GetCurrent(Ammo17mm)` |
| `viewAmmo42mm` | `GetCurrent(Ammo42mm)` |
| `viewAlive` | `IsAlive` |
| `viewCanMove` | 对外属性 `CanMove`（含死亡/断电/罚下） |
| `viewCanShoot` | 对外属性 `CanShoot`（含过热） |

`Awake`、`Update`、`OnValidate`（仅已初始化的 Play 模式）、`ResetToBase` 会 `RefreshView()`。用来在 Play 时核对 Buff 是否生效，不要在其它脚本里读这些字段（它们不是 public）。逻辑一律走 `GetCurrent` / `Hp` / `MoveSpeed` 等。

---

## 附录：步兵默认基础值（组件初始值）

未点「按兵种填入默认基础值」时，脚本字段初始值按步兵档：

| 项 | 值 |
|----|-----|
| `robotId` / `team` / `robotType` | 3 / Red / Infantry |
| 血 / 最大血 | 100 |
| 移速 / 转向 / 功率 / 电容 | 5 m/s，180 deg/s，80 W，60 |
| 17mm / 42mm | 500 / 0 |
| 热量上限 / 冷却 / 射速 / 弹速 / 伤害 | 240，12/s，10 发/s，30 m/s，10 |
| 犯规余地 | 4 |
| `ReviveRemaining` | -1（不限制） |
