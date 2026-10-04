using System;
using UnityEngine;

[Serializable]
public class LanMessage
{
    public const int Protocol = 3;
    public string kind;
    public int version = Protocol;
    public int peer;
    public int slot = -1;
    public bool ready;
    public string text;
    public string action;
    public LanMember[] members;
    public LanInput input;
    public LanSnapshot snapshot;
    public LanShot shot;
    public LanCombatFeedback feedback;
}

[Serializable] public class LanMember { public int peer; public int slot; public bool ready; }
[Serializable] public class LanInput
{
    public float x, z, yaw, pitch;
    public bool sprint, fire, use42, shop;
}
[Serializable] public class LanModifier
{
    public int stat;
    public StatModifier modifier;
    public float remaining;
}
[Serializable] public class LanRobotState
{
    public int slot;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public float[] stats;
    public LanModifier[] modifiers;
    public bool heatLocked, permanentlyLocked, weakUntilCard, naturalInvulnerable;
    public float revive, invulnerable, weak, remoteHeal, combatAge;
    public int immediateRevives;
    public string fieldProgress;
}
[Serializable] public class LanCombatFeedback
{
    public int attackerSlot = -1, victimSlot = -1;
    public string attacker, target, reason;
    public float damage;
    public bool killed;
}
[Serializable] public class LanShot
{
    public int id, slot;
    public bool use42;
    public Vector3 position, direction;
    public float speed;
}
[Serializable] public class LanPose { public Vector3 position; public Quaternion rotation; }
[Serializable] public class LanRuneState
{
    public int phase, kind, litA, litB, hits;
    public float activateLeft, shotLeft, buffLeft, extraLeft;
    public bool waitingSecond;
    public bool[] struck;
}
[Serializable] public class LanSnapshot
{
    public int sequence;
    public float remaining;
    public bool timeFinished, decided;
    public int winner;
    public string result;
    public float redDamage, blueDamage;
    public float redBase, blueBase, redShield, blueShield, redLowest, blueLowest;
    public bool redArmor, blueArmor;
    public float redOutpost, blueOutpost;
    public bool redOutpostDestroyed, blueOutpostDestroyed;
    public LanRobotState[] robots;
    public float runeAngle;
    public LanRuneState[] runes;
    public LanPose[] outpostPoses;
    public bool testComplete;
    public string testError;
}
