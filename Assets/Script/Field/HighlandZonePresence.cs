using System.Collections.Generic;
using UnityEngine;

// 挂在梯形高地已有的 Trigger 上，只记录谁进了这块碰撞体。
// 争夺脚本读 Inside，不另做一套范围。
public class HighlandZonePresence : MonoBehaviour
{
    readonly Dictionary<RobotAttributeManager, int> counts = new Dictionary<RobotAttributeManager, int>();
    readonly List<RobotAttributeManager> inside = new List<RobotAttributeManager>();

    public IReadOnlyList<RobotAttributeManager> Inside => inside;

    void OnTriggerEnter(Collider other)
    {
        Touch(other, 1);
    }

    void OnTriggerExit(Collider other)
    {
        Touch(other, -1);
    }

    void Touch(Collider other, int delta)
    {
        RobotAttributeManager attr = Resolve(other);
        if (attr == null)
            return;

        counts.TryGetValue(attr, out int count);
        count += delta;
        if (count <= 0)
        {
            counts.Remove(attr);
            inside.Remove(attr);
            return;
        }

        counts[attr] = count;
        if (!inside.Contains(attr))
            inside.Add(attr);
    }

    static RobotAttributeManager Resolve(Collider other)
    {
        if (other == null)
            return null;

        GameObject go = other.attachedRigidbody != null
            ? other.attachedRigidbody.gameObject
            : other.gameObject;

        if (PlayerAttributeBinding.TryResolveRoster(go, out RobotAttributeManager roster))
            return roster;

        RobotAttributeManager onBody = go.GetComponent<RobotAttributeManager>();
        if (onBody == null)
            onBody = go.GetComponentInParent<RobotAttributeManager>();
        return onBody;
    }
}
