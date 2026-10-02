using System;
using System.Collections.Generic;
using UnityEngine;

// 能量机关：整组绕「红方 logo 中心 - 蓝方 logo 中心」转动。
// 大能量机关转速（规则手册）：spd = 0.785 * sin(1.884 * t) + 1.305 ，单位 rad/s。
//
// 转动目标在运行时决定，不改场景：
// - 只转名为 rotation 的最外层根，红/蓝扇叶跟着走。
// - noimportant 不进旋转列表。它若挂在 rotation 下面，每帧把世界位姿锁回初始值，避免跟着父节点转。
// - 子级上的独立刚体不会跟父物体走，按同一角度改它们的位姿；noimportant 下面的刚体除外。
public class RotationCenterSpin : MonoBehaviour
{
    public enum SpeedMode
    {
        LargeRune, // 大能量机关，正弦变速
        SmallRune  // 小能量机关，恒定 π/3 rad/s
    }

    // 一个被直接改位姿的物体。根物体用父空间；独立刚体记住初始位姿，每帧按同一角度摆回去。
    class SpinSlot
    {
        public Transform target;
        public Transform space;
        public Rigidbody body;
        public Vector3 originLocal;
        public Vector3 axisLocal;
        public Vector3 startLocalPos;
        public Quaternion startLocalRot;
    }

    [Header("规则转速")]
    public SpeedMode speedMode = SpeedMode.LargeRune;
    [Tooltip("正方向沿红 logo 指向减蓝 logo 指向（右手定则）。-1 反向")]
    public float directionSign = 1f;

    [Header("大能量机关 spd = a*sin(omega*t)+b")]
    public float largeA = 0.785f;
    public float largeOmega = 1.884f;
    public float largeB = 1.305f;

    [Header("小能量机关")]
    public float smallSpeed = 1.047f; // π/3 rad/s

    // 挂在 rotation 下面、不该跟着转的 noimportant。记下开局世界位姿，父节点转完再摆回去。
    class StillSlot
    {
        public Transform target;
        public Vector3 worldPos;
        public Quaternion worldRot;
    }

    readonly List<SpinSlot> slots = new List<SpinSlot>();
    readonly List<StillSlot> stillSlots = new List<StillSlot>();
    float spunAngleRad;
    bool ready;

    public static void BindForLoadedMatch()
    {
        if (UnityEngine.Object.FindAnyObjectByType<RotationCenterSpin>() != null)
            return;

        Transform host = FindFirstNamed(IsRotationName);
        if (host == null)
            return;

        host.gameObject.AddComponent<RotationCenterSpin>();
    }

    void Start()
    {
        List<Transform> rotations = FindAllNamed(IsRotationName);
        List<Transform> noImportants = FindAllNamed(IsNoImportantName);
        List<Transform> roots = PickSpinRoots(rotations);

        if (roots.Count == 0)
        {
            Debug.LogWarning("RotationCenterSpin：场景里没有 rotation", this);
            return;
        }

        if (!TryBuildAxis(rotations, noImportants, out Vector3 originWorld, out Vector3 axisWorld))
        {
            Debug.LogWarning("RotationCenterSpin：找不到红/蓝 logo，或两点重合，无法形成中心轴", this);
            return;
        }

        for (int i = 0; i < roots.Count; i++)
            AddRootAndRigidbodies(roots[i], originWorld, axisWorld);

        HoldNoImportantsStill(roots, noImportants);

        ready = slots.Count > 0;
        if (ready)
            Debug.Log("RotationCenterSpin：整组绕中心轴转动 " + DescribeRoots(roots), this);
    }

    void Update()
    {
        if (!ready)
            return;

        // 角度累加，切换小符/大符转速时不会跳变。大符变速的 t 由 PowerRuneActivator 管。
        spunAngleRad += CurrentSpeed() * Time.deltaTime * directionSign;
        float angleDeg = Mathf.Rad2Deg * spunAngleRad;

        for (int i = 0; i < slots.Count; i++)
        {
            SpinSlot slot = slots[i];
            if (slot.target == null)
                continue;

            Quaternion spin = Quaternion.AngleAxis(angleDeg, slot.axisLocal);
            Vector3 localPos = slot.originLocal + spin * (slot.startLocalPos - slot.originLocal);
            Quaternion localRot = spin * slot.startLocalRot;
            ApplySlot(slot, localPos, localRot);
        }

        for (int i = 0; i < stillSlots.Count; i++)
        {
            StillSlot still = stillSlots[i];
            if (still.target == null)
                continue;
            still.target.SetPositionAndRotation(still.worldPos, still.worldRot);
        }
    }

    // 当前瞬时角速度 rad/s，方便以后对接裁判
    public float CurrentSpeed()
    {
        // 2026：小符，以及没在激活的大符，都是 π/3。只有大符正在激活才走正弦，参数在进入激活时重抽。
        if (PowerRuneActivator.LargeActivatingSpin)
        {
            return PowerRuneActivator.LargeA * Mathf.Sin(PowerRuneActivator.LargeOmega * PowerRuneActivator.LargeTime)
                + PowerRuneActivator.LargeB;
        }

        return smallSpeed;
    }

    float IntegratedAngle(float t)
    {
        if (speedMode == SpeedMode.SmallRune)
            return smallSpeed * t;

        // ∫ (a*sin(ωt)+b) dt，t=0 时角度为 0
        float w = Mathf.Abs(largeOmega) < 0.0001f ? 0.0001f : largeOmega;
        return -(largeA / w) * Mathf.Cos(w * t) + largeB * t + (largeA / w);
    }

    // 只转 rotation。嵌套的只留最外层。noimportant 不进这个列表。
    static List<Transform> PickSpinRoots(List<Transform> rotations)
    {
        List<Transform> roots = new List<Transform>();
        for (int i = 0; i < rotations.Count; i++)
        {
            Transform candidate = rotations[i];
            if (candidate == null)
                continue;

            bool nested = false;
            for (int j = 0; j < rotations.Count; j++)
            {
                Transform other = rotations[j];
                if (other == null || other == candidate)
                    continue;
                if (candidate.IsChildOf(other))
                {
                    nested = true;
                    break;
                }
            }

            if (!nested && !roots.Contains(candidate))
                roots.Add(candidate);
        }

        return roots;
    }

    // noimportant 若是某个旋转根的子孙，父节点一转它的世界旋转就会跟着变。锁回开局位姿。
    void HoldNoImportantsStill(List<Transform> roots, List<Transform> noImportants)
    {
        for (int i = 0; i < noImportants.Count; i++)
        {
            Transform target = noImportants[i];
            if (target == null || !IsUnderSpinRoot(target, roots) || NestedUnderNoImportant(target))
                continue;

            stillSlots.Add(new StillSlot
            {
                target = target,
                worldPos = target.position,
                worldRot = target.rotation
            });
        }
    }

    static bool IsUnderSpinRoot(Transform target, List<Transform> roots)
    {
        for (int i = 0; i < roots.Count; i++)
        {
            Transform root = roots[i];
            if (root != null && target != root && target.IsChildOf(root))
                return true;
        }

        return false;
    }

    static bool NestedUnderNoImportant(Transform target)
    {
        Transform parent = target.parent;
        while (parent != null)
        {
            if (IsNoImportantName(parent.name))
                return true;
            parent = parent.parent;
        }

        return false;
    }

    static bool IsUnderNoImportant(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (IsNoImportantName(current.name))
                return true;
            current = current.parent;
        }

        return false;
    }

    void AddRootAndRigidbodies(Transform root, Vector3 originWorld, Vector3 axisWorld)
    {
        Transform space = root.parent;
        Rigidbody rootBody = root.GetComponent<Rigidbody>();
        slots.Add(Capture(root, space, rootBody, originWorld, axisWorld, true));
        if (rootBody != null)
            PrepareRigidbody(rootBody);

        // 父物体转动带不动独立刚体，记下它们相对同一转轴的初始位姿。
        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];
            if (body == null || body.transform == root)
                continue;
            if (IsUnderNoImportant(body.transform))
                continue;

            PrepareRigidbody(body);
            slots.Add(Capture(body.transform, space, body, originWorld, axisWorld, false));
        }
    }

    static SpinSlot Capture(Transform target, Transform space, Rigidbody body, Vector3 originWorld, Vector3 axisWorld, bool useLocalOfRoot)
    {
        SpinSlot slot = new SpinSlot
        {
            target = target,
            space = space,
            body = body
        };

        if (space != null)
        {
            slot.originLocal = space.InverseTransformPoint(originWorld);
            slot.axisLocal = space.InverseTransformDirection(axisWorld).normalized;
            if (useLocalOfRoot && body == null)
            {
                slot.startLocalPos = target.localPosition;
                slot.startLocalRot = target.localRotation;
            }
            else
            {
                slot.startLocalPos = space.InverseTransformPoint(target.position);
                slot.startLocalRot = Quaternion.Inverse(space.rotation) * target.rotation;
            }
        }
        else
        {
            slot.originLocal = originWorld;
            slot.axisLocal = axisWorld.normalized;
            slot.startLocalPos = target.position;
            slot.startLocalRot = target.rotation;
        }

        return slot;
    }

    static void ApplySlot(SpinSlot slot, Vector3 localPos, Quaternion localRot)
    {
        if (slot.body == null)
        {
            if (slot.space != null)
            {
                slot.target.localPosition = localPos;
                slot.target.localRotation = localRot;
            }
            else
            {
                slot.target.SetPositionAndRotation(localPos, localRot);
            }

            return;
        }

        Vector3 worldPos;
        Quaternion worldRot;
        if (slot.space != null)
        {
            worldPos = slot.space.TransformPoint(localPos);
            worldRot = slot.space.rotation * localRot;
        }
        else
        {
            worldPos = localPos;
            worldRot = localRot;
        }

        slot.body.position = worldPos;
        slot.body.rotation = worldRot;
        slot.target.SetPositionAndRotation(worldPos, worldRot);
    }

    static void PrepareRigidbody(Rigidbody body)
    {
        body.isKinematic = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    bool TryBuildAxis(List<Transform> rotations, List<Transform> noImportants, out Vector3 originWorld, out Vector3 axisWorld)
    {
        originWorld = transform.position;
        axisWorld = Vector3.zero;

        if (TryLogosUnder(rotations, out Transform redLogo, out Transform blueLogo)
            || TryLogosUnder(noImportants, out redLogo, out blueLogo)
            || TryLogosInScene(out redLogo, out blueLogo))
        {
            Vector3 redCenter = MeshCenter(redLogo);
            Vector3 blueCenter = MeshCenter(blueLogo);
            axisWorld = redCenter - blueCenter;
            if (axisWorld.sqrMagnitude < 1e-8f)
                return false;

            originWorld = (redCenter + blueCenter) * 0.5f;
            return true;
        }

        return false;
    }

    static bool TryLogosUnder(List<Transform> scopes, out Transform redLogo, out Transform blueLogo)
    {
        redLogo = null;
        blueLogo = null;
        for (int i = 0; i < scopes.Count; i++)
        {
            if (scopes[i] == null)
                continue;
            if (TryLogos(scopes[i], out redLogo, out blueLogo))
                return true;
        }

        return false;
    }

    static bool TryLogos(Transform scope, out Transform redLogo, out Transform blueLogo)
    {
        redLogo = null;
        blueLogo = null;
        Transform red = FindChild(scope, "red");
        Transform blue = FindChild(scope, "blue");
        if (red == null || blue == null)
            return false;

        redLogo = FindChild(red, "logo");
        blueLogo = FindChild(blue, "logo");
        return redLogo != null && blueLogo != null;
    }

    static bool TryLogosInScene(out Transform redLogo, out Transform blueLogo)
    {
        redLogo = null;
        blueLogo = null;
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform red = null;
        Transform blue = null;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
                continue;
            if (red == null && all[i].name == "red")
                red = all[i];
            else if (blue == null && all[i].name == "blue")
                blue = all[i];
        }

        if (red == null || blue == null)
            return false;

        redLogo = FindChild(red, "logo");
        blueLogo = FindChild(blue, "logo");
        return redLogo != null && blueLogo != null;
    }

    static Transform FindChild(Transform root, string childName)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].name == childName)
                return all[i];
        }

        return null;
    }

    static Vector3 MeshCenter(Transform t)
    {
        MeshFilter filter = t.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
            return t.TransformPoint(filter.sharedMesh.bounds.center);

        Renderer renderer = t.GetComponent<Renderer>();
        if (renderer != null)
            return renderer.bounds.center;

        return t.position;
    }

    static Transform FindFirstNamed(Func<string, bool> match)
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && match(all[i].name))
                return all[i];
        }

        return null;
    }

    static List<Transform> FindAllNamed(Func<string, bool> match)
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        List<Transform> found = new List<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && match(all[i].name))
                found.Add(all[i]);
        }

        return found;
    }

    static bool IsRotationName(string name)
    {
        return name == "rotation";
    }

    // 大小写不敏感，空格、下划线、连字符忽略；也认 Unity 复制出来的 "noimportant (1)"。
    static bool IsNoImportantName(string name)
    {
        return NormalizeName(name) == "noimportant";
    }

    static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        name = name.Trim();
        int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffix > 0 && name.EndsWith(")", StringComparison.Ordinal))
        {
            bool digits = true;
            for (int i = suffix + 2; i < name.Length - 1; i++)
            {
                if (!char.IsDigit(name[i]))
                {
                    digits = false;
                    break;
                }
            }

            if (digits && name.Length - suffix > 3)
                name = name.Substring(0, suffix);
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == ' ' || c == '_' || c == '-' || c == '\t')
                continue;
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    static string DescribeRoots(List<Transform> roots)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < roots.Count; i++)
        {
            if (i > 0)
                builder.Append("、");
            Transform root = roots[i];
            string parentName = root.parent != null ? root.parent.name : "（场景根）";
            builder.Append(root.name);
            builder.Append("（父级 ");
            builder.Append(parentName);
            builder.Append("）");
        }

        return builder.ToString();
    }
}
