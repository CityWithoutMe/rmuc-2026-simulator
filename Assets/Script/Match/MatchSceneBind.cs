using UnityEngine;
using UnityEngine.SceneManagement;

// AfterSceneLoad 只在进 Play 的第一个场景跑一次。从主菜单开跑时第一个场景不是 SampleScene，
// 场地脚本若那时就查找，会找不到哨塔、能量机关、属性管理器，之后 LoadScene 也不会再跑。
// 这里在 SampleScene 每次加载完成后再绑定。直接打开 SampleScene 再 Play 同样会进到这里。
public static class MatchSceneBind
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != VehicleSelectUI.MatchSceneName)
            return;
        if (LanSession.Instance != null && LanSession.Instance.CancelledLoad) return;

        LanWorld lanWorld = null;
        if (LanSession.Active)
        {
            try
            {
                PlayerAttributeBinding.BindForLoadedMatch();
                lanWorld = LanWorld.Bind();
            }
            catch (System.Exception e)
            {
                LanSession.Instance.Fail("车辆绑定失败：" + e.Message);
                return;
            }
        }

        // 能量机关先挂上，哨塔收集 target 时才能整支跳过机关层级。
        RotationCenterSpin.BindForLoadedMatch();
        OutpostTargetSpin.BindForLoadedMatch();
        OutpostHealth.BindForLoadedMatch();
        BaseHealth.BindForLoadedMatch();
        PowerRuneActivator.BindForLoadedMatch();
        PlayerAttributeBinding.BindForLoadedMatch();
        if (!LanSession.Active)
        {
            CombatDamage.EnsureBlueHeroes();
            CombatDamage.EnsureInfantryBodies();
        }
        RobotCollisionDamage.BindForLoadedMatch();
        WheelRoll.BindForLoadedMatch();
        CenterCrosshair.BindForLoadedMatch();
        HeroAttributeHud.BindForLoadedMatch();
        OutpostHealthHud.BindForLoadedMatch();
        HighlandZoneContest.BindForLoadedMatch();
        TerrainCrossBuff.BindForLoadedMatch();
        SideZoneBuff.BindForLoadedMatch();
        FieldSupportZoneBuff.BindForLoadedMatch();
        MatchTeamEconomy.BindForLoadedMatch();
        Hero42mmShield.BindForLoadedMatch();
        AmmoExchange.BindForLoadedMatch();
        BuffGainHud.BindForLoadedMatch();
        CombatFeedbackHud.BindForLoadedMatch();
        MatchOutcome.BindForLoadedMatch();
        if (lanWorld != null) LanSession.Instance.AttachWorld(lanWorld);
    }
}
