using System;
using System.Collections.Generic;
using UnityEngine;

// 英雄、步兵的轮子按车身水平速度绕车根本地 X 轴滚。运行时挂上，不改场景。
// 左右轮共用车根的 right，不再按每个轮子自己的轴翻转。
// 英雄只转名字含「辊子」的模型。步兵转轮毂、轮胎、轮子、辆子、胶轮。
// 角速度 ω = v / r。v 是刚体水平速度在车头朝向上的分量，倒车为负。
public class WheelRoll : MonoBehaviour
{
    const float FallbackRadius = 0.05f;
    const float TinyRadius = 0.001f;

    struct Wheel
    {
        public Transform transform;
        public float radius;
    }

    readonly List<Wheel> wheels = new List<Wheel>();
    Rigidbody body;
    bool hero;
    bool warnedMissing;
    bool warnedRadius;

    public static void BindForLoadedMatch()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || !IsVehicleRoot(t))
                continue;
            if (t.GetComponent<WheelRoll>() != null)
                continue;
            t.gameObject.AddComponent<WheelRoll>();
        }
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        hero = IsHeroName(name);
        CollectWheels();
        if (wheels.Count == 0 && !warnedMissing)
        {
            warnedMissing = true;
            Debug.LogWarning("WheelRoll：" + name + " 上没有找到轮子", this);
            return;
        }

        Debug.Log("WheelRoll：" + name + " 转动 " + DescribeWheels(), this);
    }

    string DescribeWheels()
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < wheels.Count; i++)
        {
            if (i > 0)
                builder.Append("、");
            Transform wheel = wheels[i].transform;
            builder.Append(wheel != null ? wheel.name : "?");
            builder.Append(" r=");
            builder.Append(wheels[i].radius.ToString("0.###"));
        }

        return builder.ToString();
    }

    void LateUpdate()
    {
        if (wheels.Count == 0)
            return;

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null)
            return;

        Vector3 velocity = body.linearVelocity;
        velocity.y = 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-8f)
            return;
        forward.Normalize();

        float signedSpeed = Vector3.Dot(velocity, forward);
        if (Mathf.Abs(signedSpeed) < 1e-5f)
            return;

        Vector3 axle = transform.right;
        if (axle.sqrMagnitude < 1e-8f)
            return;
        axle.Normalize();

        float dt = Time.deltaTime;
        for (int i = 0; i < wheels.Count; i++)
        {
            Wheel wheel = wheels[i];
            if (wheel.transform == null)
                continue;

            // 绕车身 +X 正转时，右手法则会让轮子往后滚，前进取反。左右轮同一根轴、同一个角度。
            float degrees = -(signedSpeed / wheel.radius) * Mathf.Rad2Deg * dt;
            wheel.transform.Rotate(axle, degrees, Space.World);
        }
    }

    void CollectWheels()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t == transform)
                continue;
            if (IsBlockedBranch(t))
                continue;
            if (!IsWheelName(t.name, hero))
                continue;

            float radius = MeasureRadius(t, transform.right);
            wheels.Add(new Wheel { transform = t, radius = radius });
        }
    }

    float MeasureRadius(Transform wheel, Vector3 axleWorld)
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds(wheel.position, Vector3.zero);

        Renderer renderer = wheel.GetComponent<Renderer>();
        if (renderer != null)
        {
            bounds = renderer.bounds;
            hasBounds = true;
        }
        else
        {
            Collider collider = wheel.GetComponent<Collider>();
            if (collider != null)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
        }

        float radius = 0f;
        if (hasBounds)
            radius = LateralRadius(bounds.extents, axleWorld);

        if (radius >= TinyRadius)
            return radius;

        if (!warnedRadius)
        {
            warnedRadius = true;
            Debug.LogWarning(
                "WheelRoll：" + name + " / " + wheel.name + " 半径为 " + radius.ToString("0.####")
                + "，改用 " + FallbackRadius.ToString("0.##") + " m",
                wheel);
        }

        return FallbackRadius;
    }

    // 世界包围盒半尺里，丢掉最贴近车身 X 轴的那一维，剩下两维取较小的作为半径。
    static float LateralRadius(Vector3 extents, Vector3 axleWorld)
    {
        float ax = Mathf.Abs(axleWorld.x);
        float ay = Mathf.Abs(axleWorld.y);
        float az = Mathf.Abs(axleWorld.z);
        float a;
        float b;
        if (ax >= ay && ax >= az)
        {
            a = extents.y;
            b = extents.z;
        }
        else if (ay >= az)
        {
            a = extents.x;
            b = extents.z;
        }
        else
        {
            a = extents.x;
            b = extents.y;
        }

        return Mathf.Min(Mathf.Abs(a), Mathf.Abs(b));
    }

    static bool IsVehicleRoot(Transform t)
    {
        if (!IsHeroName(t.name) && !IsInfantryName(t.name))
            return false;
        if (PlayerMovement.IsUnderAttributeBoard(t.gameObject))
            return false;
        if (IsBlockedBranch(t))
            return false;

        Transform parent = t.parent;
        while (parent != null)
        {
            if ((IsHeroName(parent.name) || IsInfantryName(parent.name))
                && !PlayerMovement.IsUnderAttributeBoard(parent.gameObject))
                return false;
            parent = parent.parent;
        }

        return true;
    }

    static bool IsBlockedBranch(Transform t)
    {
        while (t != null)
        {
            if (t.GetComponent<RotationCenterSpin>() != null)
                return true;
            string n = t.name;
            if (!string.IsNullOrEmpty(n))
            {
                if (n.IndexOf("哨塔", StringComparison.Ordinal) >= 0)
                    return true;
                if (n.IndexOf("能量机关", StringComparison.Ordinal) >= 0)
                    return true;
                if (IsNoImportantName(n))
                    return true;
            }

            t = t.parent;
        }

        return false;
    }

    static bool IsNoImportantName(string name)
    {
        string n = name.Trim();
        int suffix = n.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffix > 0 && n.EndsWith(")", StringComparison.Ordinal))
            n = n.Substring(0, suffix);
        n = n.Replace(" ", "").Replace("_", "").Replace("-", "");
        return n.Equals("noimportant", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsHeroName(string n)
    {
        if (string.IsNullOrEmpty(n))
            return false;
        if (n.IndexOf("英雄", StringComparison.Ordinal) >= 0)
            return true;
        return n.IndexOf("hero", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsInfantryName(string n)
    {
        if (string.IsNullOrEmpty(n))
            return false;
        if (n.IndexOf("infantry", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("平衡步兵", StringComparison.Ordinal) >= 0)
            return true;
        return n.IndexOf("步兵", StringComparison.Ordinal) >= 0;
    }

    // 英雄：辊子。步兵：轮毂 / 轮胎 / 轮子 / 辆子 / 胶轮。轮廓、云台、摩擦轮、导轮、腿轮不算。
    static bool IsWheelName(string n, bool heroVehicle)
    {
        if (string.IsNullOrEmpty(n))
            return false;
        if (n.IndexOf("轮廓", StringComparison.Ordinal) >= 0)
            return false;
        if (n.IndexOf("云台", StringComparison.Ordinal) >= 0)
            return false;

        if (heroVehicle)
            return n.IndexOf("辊子", StringComparison.Ordinal) >= 0;

        if (n.IndexOf("轮毂", StringComparison.Ordinal) >= 0)
            return true;
        if (n.IndexOf("轮胎", StringComparison.Ordinal) >= 0)
            return true;
        if (n.IndexOf("轮子", StringComparison.Ordinal) >= 0)
            return true;
        if (n.IndexOf("辆子", StringComparison.Ordinal) >= 0)
            return true;
        return n.IndexOf("胶轮", StringComparison.Ordinal) >= 0;
    }
}
