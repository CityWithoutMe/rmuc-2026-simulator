using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 仅由显式 LAN 开发测试调用。验证真实奖励入口、命中结算和场景绑定，之后恢复状态。
public static class LanRuneShieldSmoke
{
    const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static void Set(object target, string field, object value)
    {
        var info = target.GetType().GetField(field, Fields);
        info.SetValue(target, info.FieldType.IsEnum ? Enum.ToObject(info.FieldType, value) : value);
    }
    static void Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Fields).Invoke(target, args);
    static object Side(PowerRuneActivator rune, RobotTeam team)
        => ((IEnumerable)typeof(PowerRuneActivator).GetField("sides", Fields).GetValue(rune)).Cast<object>()
            .First(s => (RobotTeam)s.GetType().GetField("team").GetValue(s) == team);
    static void Hp(string field, float value)
        => typeof(OutpostHealth).GetField("<" + field + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);

    public static void Exercise(LanVehicle[] vehicles)
    {
        var rune = UnityEngine.Object.FindAnyObjectByType<PowerRuneActivator>();
        Check(rune != null, "能量机关未绑定");
        var red = Side(rune, RobotTeam.Red); var blue = Side(rune, RobotTeam.Blue);
        var sideSaved = new Dictionary<object, Dictionary<FieldInfo, object>>();
        foreach (var side in new[] { red, blue })
            sideSaved[side] = side.GetType().GetFields(Fields).Where(f => !f.IsInitOnly).ToDictionary(f => f, f => f.GetValue(side));
        var inspector = typeof(RobotAttributeManager).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly).ToArray();
        var robotSaved = vehicles.ToDictionary(v => v.Stats, v => inspector.ToDictionary(f => f, f => f.GetValue(v.Stats)));
        var hero = vehicles[0].Stats; var infantry = vehicles[1].Stats;
        try
        {
            foreach (var v in vehicles) { v.Stats.SetBase(RobotStat.Exp, 0); v.Stats.SetBase(RobotStat.Level, 1); }
            Set(red, "kind", 1); Set(red, "totalHits", 5);
            Call(rune, "SucceedLarge", red);
            Check(Mathf.Abs(hero.Experience - 375) < .01f && Mathf.Abs(infantry.Experience - 375) < .01f,
                "大符 750 经验未平均分给存活英雄和步兵");
            Check(vehicles[2].Stats.Experience == 0 && vehicles[3].Stats.Experience == 0, "大符奖励给了敌方");
            Check(hero.GetCurrent(RobotStat.AttackBuffPercent) >= .5f && PowerRuneActivator.StructureDefense(RobotTeam.Red) == .25f,
                "大符机器人或建筑增益缺失");
            Call(rune, "TickSide", red, 1f);
            Check(hero.Experience == 375, "持续增益期间重复发放大符经验");
            infantry.Die(); Call(rune, "TickSide", red, 1f);
            Check(!infantry.HasModifier(RobotStat.DefenseBuffPercent, "rune_large_red"), "战亡没有撤销大符增益");
            infantry.Revive(); Call(rune, "TickSide", red, 1f);
            Check(infantry.HasModifier(RobotStat.DefenseBuffPercent, "rune_large_red") && infantry.Experience == 375,
                "复活后剩余增益未恢复或经验重复发放");
            Call(rune, "TickSide", red, 100f);
            Check(PowerRuneActivator.StructureDefense(RobotTeam.Red) == 0 && !hero.HasModifier("rune_large_red"), "大符结束未撤销增益");

            infantry.Die(); hero.SetBase(RobotStat.Exp, 0);
            Set(red, "kind", 1); Set(red, "totalHits", 5); Call(rune, "SucceedLarge", red);
            Check(hero.Experience == 750 && infantry.Experience == 375, "战亡者错误参与经验分配");
            Call(rune, "TickSide", red, 100f); infantry.Revive();
            infantry.robotType = RobotType.Engineer; hero.SetBase(RobotStat.Exp, 0);
            Set(red, "kind", 1); Call(rune, "SucceedLarge", red);
            Check(hero.Experience == 750, "不能获得经验的兵种错误参与分母");
            infantry.robotType = RobotType.Infantry; Call(rune, "TickSide", red, 100f);
            hero.SetBase(RobotStat.Exp, 0); infantry.SetBase(RobotStat.Exp, 0);
            Set(red, "kind", 0); Call(rune, "SucceedSmall", red);
            hero.GrantFlatExperience(800, "小符共享额外经验测试");
            infantry.GrantFlatExperience(800, "小符共享额外经验测试");
            Check(hero.Experience == 1600 && infantry.Experience == 1200, "小符共享 1200 额外经验上限错误");
            Call(rune, "TickSide", red, 100f);

            // 小符和大符的 25% 防御都必须进入基地、前哨真实命中结算。
            var plateRecords = (IEnumerable)typeof(OutpostHealth).GetField("plates", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Transform plate = plateRecords.Cast<object>().Where(p => (RobotTeam)p.GetType().GetField("team").GetValue(p) == RobotTeam.Blue)
                .Select(p => (Transform)p.GetType().GetField("target").GetValue(p)).First();
            Collider outpost = plate.GetComponentInChildren<Collider>();
            var roots = (IEnumerable)typeof(BaseHealth).GetField("roots", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Collider baseCollider = roots.Cast<object>().Where(p => (RobotTeam)p.GetType().GetField("team").GetValue(p) == RobotTeam.Blue)
                .Select(p => (Transform)p.GetType().GetField("root").GetValue(p)).First().GetComponentInChildren<Collider>();
            for (int kind = 0; kind <= 1; kind++)
            {
                BaseHealth.BindForLoadedMatch(); OutpostHealth.BindForLoadedMatch();
                Set(blue, "kind", kind); Set(blue, "totalHits", 5);
                Call(rune, kind == 0 ? "SucceedSmall" : "SucceedLarge", blue);
                float before = OutpostHealth.BlueHp;
                OutpostHealth.TryAbsorbBullet(outpost, RobotTeam.Red, false);
                Check(before - OutpostHealth.BlueHp == 15, "能量机关前哨防御未生效");
                Hp("BlueHp", 0);
                before = BaseHealth.BlueShield;
                BaseHealth.TryAbsorbBullet(baseCollider, RobotTeam.Red, false);
                Check(before - BaseHealth.BlueShield == 15, "能量机关基地防御未生效");
                Call(rune, "TickSide", blue, 100f);
            }

            BaseHealth.BindForLoadedMatch(); OutpostHealth.BindForLoadedMatch();
            Set(red, "kind", 1); Set(red, "totalHits", 5); Call(rune, "SucceedLarge", red);
            Set(blue, "kind", 0); Call(rune, "SucceedSmall", blue);
            float boostedBefore = OutpostHealth.BlueHp;
            OutpostHealth.TryAbsorbBullet(outpost, RobotTeam.Red, false, hero);
            Check(boostedBefore - OutpostHealth.BlueHp == 23, "攻击与建筑防御增益未共同结算或取整错误");
            Hp("BlueHp", 0);
            boostedBefore = BaseHealth.BlueShield;
            BaseHealth.TryAbsorbBullet(baseCollider, RobotTeam.Red, true, hero);
            Check(boostedBefore - BaseHealth.BlueShield + 5000 - BaseHealth.BlueHp == 225, "大符攻击没有作用于基地护盾与血量");
            Call(rune, "TickSide", red, 100f); Call(rune, "TickSide", blue, 100f);

            BaseHealth.BindForLoadedMatch(); OutpostHealth.BindForLoadedMatch();
            hero.SetCurrent(RobotStat.Ammo42mm, 0);
            Hero42mmShield.Notify42mmFired(hero, 0);
            Check(Hero42mmShield.IsBlocked(RobotTeam.Red) && !Hero42mmShield.IsBlocked(RobotTeam.Blue), "超发屏蔽没有按攻击方独立生效");
            float outpostBefore = OutpostHealth.BlueHp;
            OutpostHealth.TryAbsorbBullet(outpost, RobotTeam.Red, true);
            Check(OutpostHealth.BlueHp == outpostBefore, "前哨没有屏蔽 42mm");
            Hp("BlueHp", 0);
            float shieldBefore = BaseHealth.BlueShield;
            BaseHealth.TryAbsorbBullet(baseCollider, RobotTeam.Red, true);
            Check(BaseHealth.BlueShield == shieldBefore, "基地没有屏蔽 42mm");
            var enemy = vehicles[3]; enemy.Stats.ResetToBase();
            Collider enemyCollider = enemy.GetComponent<Collider>() ?? enemy.GetComponentInChildren<Collider>();
            float robotBefore = enemy.Stats.Hp;
            CombatDamage.HandleBulletHit(enemyCollider, vehicles[0].gameObject, hero, true);
            Check(enemy.Stats.Hp == robotBefore, "机器人没有屏蔽 42mm");
            CombatDamage.HandleBulletHit(enemyCollider, vehicles[0].gameObject, hero, false);
            Check(enemy.Stats.Hp < robotBefore, "42mm 屏蔽误伤 17mm 结算");
            var snapshot = UnityEngine.Object.FindAnyObjectByType<LanWorld>().Capture(0);
            Check(snapshot.red42mmBlocked && !snapshot.blue42mmBlocked, "屏蔽标记没有写入网络快照");
            hero.SetCurrent(RobotStat.Ammo42mm, 1);
            Check(!Hero42mmShield.IsBlocked(RobotTeam.Red), "存活且有弹没有解除屏蔽");
            hero.Die();
            Hero42mmShield.Notify42mmFired(hero, 1); Hero42mmShield.Notify42mmFired(hero, 1);
            Check(!Hero42mmShield.IsBlocked(RobotTeam.Red), "战亡后前两发提前屏蔽");
            Hero42mmShield.Notify42mmFired(hero, 1);
            Check(Hero42mmShield.IsBlocked(RobotTeam.Red), "战亡后第三发未屏蔽");
            hero.Revive(); hero.SetCurrent(RobotStat.Ammo42mm, 0);
            Check(Hero42mmShield.IsBlocked(RobotTeam.Red), "无弹复活错误解除屏蔽");
            hero.SetCurrent(RobotStat.Ammo42mm, 1);
            Check(!Hero42mmShield.IsBlocked(RobotTeam.Red), "复活购弹后未解除屏蔽");
            Debug.Log("[LAN_TEST] PASS 大符750经验/存活兵种分配/小符共享上限/复活剩余增益/建筑防御/42mm屏蔽所有装甲/17mm不受影响/恢复条件/快照标记");
        }
        finally
        {
            foreach (var side in new[] { red, blue }) Call(rune, "TickSide", side, 100f);
            foreach (var entry in robotSaved)
            {
                foreach (var value in entry.Value) value.Key.SetValue(entry.Key, value.Value);
                entry.Key.ResetToBase();
            }
            foreach (var side in sideSaved) foreach (var field in side.Value) field.Key.SetValue(side.Key, field.Value);
            BaseHealth.BindForLoadedMatch(); OutpostHealth.BindForLoadedMatch(); Hero42mmShield.BindForLoadedMatch();
        }
    }
}
