using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 前哨站旋转装甲：红、蓝各自的 target 绕所属哨塔的本地 Y 轴匀速转。
// 规则手册常用转速 0.4 rad/s。不要套用能量机关的正弦变速。
// 运行时按名字查找并挂组件，不改场景，也不改场地模型。
//
// 常见层级都认（同一座哨塔同一侧只留一个 target）：
// - 名字含「哨塔」，其下有 red/blue（等于、以 red/blue 结尾，或含 _red/_blue），再往下任意深度有名为 target 的物体。
// - 物体本身叫 哨塔_red / 哨塔_blue，其下任意深度有 target，中间不必再有一层 red/blue。
// - 场景里任意名为 target 的物体，父链上有哨塔，红蓝由父链名字判断。
// 能量机关（挂了 RotationCenterSpin 的层级）整支跳过。
public class OutpostTargetSpin : MonoBehaviour
{
    const float SpeedRadPerSec = 0.4f;
    const int MaxChildNames = 12;

    enum Side
    {
        None,
        Red,
        Blue
    }

    class SpinSlot
    {
        public Transform target;
        public Transform tower;
        public Rigidbody body;
        public Side side;
    }

    class Candidate
    {
        public Transform target;
        public Transform tower;
        public Side side;
        public int depth;
    }

    readonly List<SpinSlot> slots = new List<SpinSlot>();
    bool ready;
    static bool warned;

    public static void BindForLoadedMatch()
    {
        if (UnityEngine.Object.FindAnyObjectByType<OutpostTargetSpin>() != null)
            return;

        warned = false;

        Transform host = FindFirstOutpost();
        if (host == null)
        {
            WarnOnce("OutpostTargetSpin：场景里没有名字含「哨塔」的物体，找不到可转的 target");
            return;
        }

        host.gameObject.AddComponent<OutpostTargetSpin>();
    }

    void Start()
    {
        CollectSlots();
        if (slots.Count == 0)
        {
            WarnOnce(BuildMissMessage());
            return;
        }

        ready = true;
        for (int i = 0; i < slots.Count; i++)
        {
            SpinSlot slot = slots[i];
            Debug.Log(
                "OutpostTargetSpin：" + SideLabel(slot.side) + " " + PathOf(slot.target)
                + " 绕 " + PathOf(slot.tower) + " 的 Y 轴 0.4 rad/s",
                slot.target);
        }
    }

    void Update()
    {
        if (!ready)
            return;

        float deg = SpeedRadPerSec * Mathf.Rad2Deg * Time.deltaTime;
        for (int i = 0; i < slots.Count; i++)
        {
            SpinSlot slot = slots[i];
            if (slot.target == null || slot.tower == null)
                continue;

            Vector3 pivot = slot.tower.position;
            Vector3 axis = slot.tower.up;
            if (slot.body == null)
            {
                slot.target.RotateAround(pivot, axis, deg);
                continue;
            }

            Quaternion spin = Quaternion.AngleAxis(deg, axis);
            Vector3 pos = pivot + spin * (slot.target.position - pivot);
            Quaternion rot = spin * slot.target.rotation;
            slot.body.MovePosition(pos);
            slot.body.MoveRotation(rot);
        }
    }

    void CollectSlots()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<Candidate> found = new List<Candidate>();

        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null || candidate.name != "target")
                continue;
            if (IsUnderEnergyRune(candidate))
                continue;

            Transform tower = NearestOutpost(candidate);
            if (tower == null || tower == candidate)
                continue;

            Side side = SideAlongChain(candidate);
            if (side == Side.None)
                continue;

            found.Add(new Candidate
            {
                target = candidate,
                tower = tower,
                side = side,
                depth = DepthUnder(tower, candidate)
            });
        }

        Dictionary<string, List<Candidate>> groups = new Dictionary<string, List<Candidate>>();
        for (int i = 0; i < found.Count; i++)
        {
            Candidate candidate = found[i];
            string key = candidate.tower.GetInstanceID().ToString() + ":" + (int)candidate.side;
            List<Candidate> group;
            if (!groups.TryGetValue(key, out group))
            {
                group = new List<Candidate>();
                groups.Add(key, group);
            }

            group.Add(candidate);
        }

        foreach (KeyValuePair<string, List<Candidate>> pair in groups)
        {
            Candidate chosen = ChooseOne(pair.Value);
            if (chosen == null || chosen.target == null)
                continue;

            Rigidbody body = chosen.target.GetComponent<Rigidbody>();
            if (body != null)
                PrepareRigidbody(body);

            slots.Add(new SpinSlot
            {
                target = chosen.target,
                tower = chosen.tower,
                body = body,
                side = chosen.side
            });
        }
    }

    static Candidate ChooseOne(List<Candidate> group)
    {
        Candidate best = null;
        for (int i = 0; i < group.Count; i++)
        {
            Candidate candidate = group[i];
            if (candidate == null || candidate.target == null)
                continue;
            if (IsNestedTarget(candidate, group))
                continue;
            if (best == null || candidate.depth < best.depth)
                best = candidate;
        }

        return best;
    }

    static bool IsNestedTarget(Candidate candidate, List<Candidate> group)
    {
        for (int i = 0; i < group.Count; i++)
        {
            Candidate other = group[i];
            if (other == null || other.target == null || other.target == candidate.target)
                continue;
            if (candidate.target.IsChildOf(other.target))
                return true;
        }

        return false;
    }

    static int DepthUnder(Transform tower, Transform target)
    {
        int depth = 0;
        Transform cursor = target;
        while (cursor != null && cursor != tower)
        {
            depth++;
            cursor = cursor.parent;
        }

        return depth;
    }

    static bool IsUnderEnergyRune(Transform t)
    {
        while (t != null)
        {
            if (t.GetComponent<RotationCenterSpin>() != null)
                return true;
            t = t.parent;
        }

        return false;
    }

    static void PrepareRigidbody(Rigidbody body)
    {
        body.isKinematic = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    static Transform FindFirstOutpost()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && NameHasOutpost(all[i].name))
                return all[i];
        }

        return null;
    }

    // 自身或父级名字含「哨塔」时，取离 target 最近的那一座，当作旋转根。
    static Transform NearestOutpost(Transform t)
    {
        while (t != null)
        {
            if (NameHasOutpost(t.name))
                return t;
            t = t.parent;
        }

        return null;
    }

    // 从 target 往父链走，先碰到的红/蓝标记为准。哨塔_red 这种名字本身就能定侧。
    static Side SideAlongChain(Transform t)
    {
        while (t != null)
        {
            Side side = SideOfName(t.name);
            if (side != Side.None)
                return side;
            t = t.parent;
        }

        return Side.None;
    }

    static Side SideOfName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return Side.None;

        string lower = name.ToLowerInvariant();
        int redAt = SideMarkIndex(lower, "red");
        int blueAt = SideMarkIndex(lower, "blue");
        if (redAt < 0 && blueAt < 0)
            return Side.None;
        if (blueAt > redAt)
            return Side.Blue;
        return Side.Red;
    }

    // 等于 red/blue、以 red/blue 结尾，或名字里含 _red / _blue。大小写忽略。
    static int SideMarkIndex(string lower, string side)
    {
        int best = -1;
        int marked = lower.LastIndexOf("_" + side, StringComparison.Ordinal);
        if (marked >= 0)
            best = marked;

        if (lower.Equals(side, StringComparison.Ordinal) || lower.EndsWith(side, StringComparison.Ordinal))
        {
            int at = lower.Length - side.Length;
            if (at > best)
                best = at;
        }

        return best;
    }

    static bool NameHasOutpost(string name)
    {
        return !string.IsNullOrEmpty(name) && name.IndexOf("哨塔", StringComparison.Ordinal) >= 0;
    }

    static string SideLabel(Side side)
    {
        return side == Side.Blue ? "蓝" : "红";
    }

    static string PathOf(Transform t)
    {
        StringBuilder builder = new StringBuilder();
        List<string> names = new List<string>();
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }

        for (int i = names.Count - 1; i >= 0; i--)
        {
            if (builder.Length > 0)
                builder.Append('/');
            builder.Append(names[i]);
        }

        return builder.ToString();
    }

    string BuildMissMessage()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        StringBuilder builder = new StringBuilder();
        builder.Append("OutpostTargetSpin：没有找到可转的 target（父链上要有哨塔，且能判断红/蓝；能量机关已跳过）。扫到的哨塔：");
        int towers = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Transform tower = all[i];
            if (tower == null || !NameHasOutpost(tower.name))
                continue;

            towers++;
            builder.Append("\n- ");
            builder.Append(PathOf(tower));
            builder.Append(" 直接子物体：");
            AppendChildNames(builder, tower);
        }

        if (towers == 0)
            builder.Append("\n（场景里没有名字含「哨塔」的物体）");

        return builder.ToString();
    }

    static void AppendChildNames(StringBuilder builder, Transform tower)
    {
        int count = tower.childCount;
        if (count == 0)
        {
            builder.Append("（无）");
            return;
        }

        List<string> readable = new List<string>();
        List<string> hashed = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string childName = tower.GetChild(i).name;
            if (childName.IndexOf("|:|", StringComparison.Ordinal) >= 0)
                hashed.Add(childName);
            else
                readable.Add(childName);
        }

        int shown = 0;
        shown = AppendSome(builder, readable, shown);
        shown = AppendSome(builder, hashed, shown);
        if (shown == 0)
            builder.Append("（无）");
        if (count > shown)
        {
            builder.Append(" …共 ");
            builder.Append(count);
            builder.Append(" 个");
        }
    }

    static int AppendSome(StringBuilder builder, List<string> names, int shown)
    {
        for (int i = 0; i < names.Count && shown < MaxChildNames; i++)
        {
            if (shown > 0)
                builder.Append("、");
            builder.Append(names[i]);
            shown++;
        }

        return shown;
    }

    static void WarnOnce(string message)
    {
        if (warned)
            return;

        warned = true;
        Debug.LogWarning(message);
    }
}
