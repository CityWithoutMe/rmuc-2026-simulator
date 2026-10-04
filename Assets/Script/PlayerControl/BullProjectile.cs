using UnityEngine;

// 子弹飞行：补刚体与非 Trigger 碰撞、给初速；碰到物体做一次伤害结算后销毁自身，超时兜底。
// 同阵营、自己、场地不扣血，子弹照样销毁。敌对结算和日志在 CombatDamage。
public class BullProjectile : MonoBehaviour
{
    public int LanShotId { get; set; }
    public float lifeTime = 3f;

    private Rigidbody rb;
    private bool hitDestroyed;
    private GameObject attackerObject;
    private RobotAttributeManager attacker;

    public void SetAttacker(GameObject shooter, RobotAttributeManager shooterAttributes)
    {
        attackerObject = shooter;
        attacker = shooterAttributes;
    }

    private void Awake()
    {
        EnsurePhysics();
        Destroy(gameObject, lifeTime);
    }

    public void Launch(Vector3 direction, float speed)
    {
        EnsurePhysics();

        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        rb.useGravity = false; // 直飞，避免小子弹很快落地
        rb.linearVelocity = dir * speed;
    }

    private void EnsurePhysics()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.mass = 0.01f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.freezeRotation = true;

        EnsureSolidCollider();
    }

    // 必须有非 Trigger 碰撞体，OnCollisionEnter 才会触发
    private void EnsureSolidCollider()
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        bool hasSolid = false;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null)
                continue;

            MeshCollider mesh = c as MeshCollider;
            if (mesh != null)
            {
                // 动态刚体只能用凸网格碰撞；非凸保持原样，后面补球体
                if (mesh.convex && !mesh.isTrigger)
                    hasSolid = true;
                continue;
            }

            c.isTrigger = false;
            hasSolid = true;
        }

        if (hasSolid)
            return;

        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere == null)
            sphere = gameObject.AddComponent<SphereCollider>();
        sphere.isTrigger = false;
    }

    // 碰到实体碰撞体：先结算，再销毁子弹本身，不销毁被打中的物体
    private void OnCollisionEnter(Collision collision)
    {
        ResolveAndDestroy(collision != null ? collision.collider : null);
    }

    // 若弹丸是 Trigger：碰到非 Trigger 物体也结算并销毁；Buff 区（对方也是 Trigger）忽略
    private void OnTriggerEnter(Collider other)
    {
        if (other == null || other.isTrigger)
            return;
        ResolveAndDestroy(other);
    }

    // 预制体名 bull_42mm / bull_17mm。对不上时，英雄按 42mm，其余按 17mm。
    bool Is42mmRound()
    {
        string n = gameObject.name;
        if (!string.IsNullOrEmpty(n))
        {
            if (n.IndexOf("42", System.StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("17", System.StringComparison.Ordinal) >= 0)
                return false;
        }

        return attacker != null && attacker.robotType == RobotType.Hero;
    }

    private void ResolveAndDestroy(Collider hit)
    {
        if (hitDestroyed)
            return;
        hitDestroyed = true;
        CombatDamage.HandleBulletHit(hit, attackerObject, attacker, Is42mmRound());
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (LanSession.IsHost && LanShotId > 0) LanSession.Instance.NotifyImpact(LanShotId);
    }
}
