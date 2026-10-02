using UnityEngine;

// 选车结果。LoadScene 之后静态字段还在，SampleScene 里按这里决定操作哪一台车。
// 没有选过（直接打开 SampleScene 再 Play）时 HasSelection 为假，对局仍操作 hero_red。
public static class MatchLaunchSelection
{
    public static bool HasSelection { get; private set; }
    public static RobotTeam Team { get; private set; }
    public static RobotType VehicleType { get; private set; }
    public static int InfantryIndex { get; private set; }
    public static string SlotHeader { get; private set; }

    // 关掉 Domain Reload 再进 Play 时清掉上一局的选择，避免直接播放 SampleScene 还停在蓝方或步兵。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        HasSelection = false;
        Team = RobotTeam.Red;
        VehicleType = RobotType.Hero;
        InfantryIndex = 0;
        SlotHeader = TenRobotAttributeBoard.HeaderRedHero;
    }

    public static bool PlaysBlueHero =>
        HasSelection && Team == RobotTeam.Blue && VehicleType == RobotType.Hero;

    public static bool PlaysInfantry =>
        HasSelection && VehicleType == RobotType.Infantry && InfantryIndex >= 1 && InfantryIndex <= 3;

    public static void SelectRedHero()
    {
        Select(RobotTeam.Red, RobotType.Hero, 0);
    }

    public static void SelectBlueHero()
    {
        Select(RobotTeam.Blue, RobotType.Hero, 0);
    }

    public static void Select(RobotTeam team, RobotType type, int infantryIndex)
    {
        HasSelection = true;
        Team = team;
        VehicleType = type;
        InfantryIndex = type == RobotType.Infantry ? Mathf.Clamp(infantryIndex, 1, 3) : 0;
        SlotHeader = HeaderFor(Team, VehicleType, InfantryIndex);
    }

    public static string HeaderFor(RobotTeam team, RobotType type, int infantryIndex)
    {
        bool blue = team == RobotTeam.Blue;
        if (type != RobotType.Infantry)
            return blue ? TenRobotAttributeBoard.HeaderBlueHero : TenRobotAttributeBoard.HeaderRedHero;

        switch (infantryIndex)
        {
            case 1:
                return blue ? TenRobotAttributeBoard.HeaderBlueInfantry1 : TenRobotAttributeBoard.HeaderRedInfantry1;
            case 2:
                return blue ? TenRobotAttributeBoard.HeaderBlueInfantry2 : TenRobotAttributeBoard.HeaderRedInfantry2;
            default:
                return blue ? TenRobotAttributeBoard.HeaderBlueInfantry3 : TenRobotAttributeBoard.HeaderRedInfantry3;
        }
    }
}
