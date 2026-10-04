using UnityEngine;

// 射击跟着本物体。PlayerMovement 把控制迁到选中的英雄时会把本组件一起带上，这里不再另选车。
[RequireComponent(typeof(Rigidbody))]
public class PlayerShooting : MonoBehaviour
{
    const string ProjectileChildName = "弹丸";

    [Header("子弹")]
    [Tooltip("留空则从 Resources/BulletPrefabs 加载。英雄用 42mm，步兵用 17mm")]
    public GameObject bulletPrefab;
    [SerializeField] GameObject prefab17mm;
    [SerializeField] GameObject prefab42mm;
    public float bulletSpeed = 20f;
    public float muzzleOffset = 0.2f;

    [Header("瞄准")]
    [Tooltip("从摄像机穿过屏幕中心的射线最大距离")]
    public float aimRange = 200f;

    [Header("按键 / 间隔")]
    public KeyCode fireKey = KeyCode.Mouse0;
    public KeyCode altFireKey = KeyCode.J;
    [Tooltip("没有属性管理器时的后备间隔（秒）。有管理器时：英雄 42mm 用自己的 FireRate，英雄 17mm 用步兵默认射速")]
    public float fireInterval = 0.1f;

    // 英雄默认 42mm，Tab 在 42mm 与 17mm 之间切换。步兵固定 17mm。热量见手册 5.1.3：42mm +100，17mm +10。
    const string Prefab17 = "bull_17mm";
    const string Prefab42 = "bull_42mm";

    bool heroSelects42mm = true;

    public bool SelectedIs42mm => Uses42mm();
    public string SelectedCaliberLabel => SelectedIs42mm ? "42mm" : "17mm";

    private Transform muzzlePoint;
    private Camera aimCamera;
    private float nextFireTime;
    private RobotAttributeManager attributes;
    private AmmoShop shop;
    private bool warnedMissingPrefab;
    private bool warnedMissingMuzzle;

    private void Awake()
    {
        if (!enabled)
            return;
        BindProjectileTemplate();
        BindAimCamera();
    }

    private void Start()
    {
        BindProjectileTemplate();
        BindAimCamera();
        if (LanSession.Active)
        {
            var vehicle = GetComponent<LanVehicle>();
            if (vehicle == null || !vehicle.IsLocal) return;
        }
        // 商店挂在这台车上。Cube 上的射击若在 Awake 里被关掉，不会进 Start。
        if (GetComponent<AmmoShop>() == null)
            shop = gameObject.AddComponent<AmmoShop>();
        else
            shop = GetComponent<AmmoShop>();
    }

    private void Update()
    {
        if (MatchOutcome.Decided)
            return;

        if (LanSession.Active)
        {
            var vehicle = GetComponent<LanVehicle>();
            if (vehicle == null) return;
            if (vehicle.IsLocal && !IsShopOpen()) TrySwitchCaliber();
            if (!LanSession.CanSimulate) return;
            if (!vehicle.IsLocal)
            {
                var input = LanSession.Instance.InputFor(vehicle.Slot);
                ResolveAttributes();
                heroSelects42mm = attributes != null && attributes.robotType == RobotType.Hero && input.use42;
            }
        }

        // 商店开着时不切弹、不射击，避免 Tab 在商店里误切换。
        if (!IsShopOpen())
            if (!LanSession.Active) TrySwitchCaliber();

        if (IsShopOpen())
            return;

        if (!IsFireHeld())
            return;
        if (Time.time < nextFireTime)
            return;

        // 负数表示当前不能开火（CanShoot 为假、已过热，或射速小于等于 0）。这里返回时还没扣弹。
        float interval = GetFireInterval();
        if (interval < 0f)
            return;

        // 当前弹种扣对应弹药。42mm 热量 +100，17mm 热量 +10。没有弹药：不生成子弹，也不加热。切换弹种不扣弹、不加热。
        bool use42 = Uses42mm();
        bool consumed = false;
        if (attributes != null)
        {
            bool ok = use42 ? attributes.TryConsumeAmmo42mm(1) : attributes.TryConsumeAmmo17mm(1);
            if (!ok)
                return;
            consumed = true;
        }

        if (!Fire())
        {
            if (consumed)
            {
                if (use42)
                    attributes.AddAmmo42mm(1);
                else
                    attributes.AddAmmo17mm(1);
            }
            nextFireTime = Time.time + interval;
            return;
        }

        if (consumed)
        {
            attributes.NotifyRoundFired();
            attributes.AddBarrelHeat(use42 ? RobotAttributeManager.HeatPer42mmRound : RobotAttributeManager.HeatPer17mmRound);
            attributes.GrantFlatExperience(
                RobotLevelRules.ShotExperience(attributes.robotType),
                "发射弹丸 1发");
        }

        nextFireTime = Time.time + interval;
    }

    // 只有英雄能切。死亡不能切。切换不消耗弹药、不加热量。默认 42mm。
    void TrySwitchCaliber()
    {
        if (!Input.GetKeyDown(KeyCode.Tab))
            return;

        ResolveAttributes();
        bool hero = attributes != null
            ? attributes.robotType == RobotType.Hero
            : !MatchLaunchSelection.PlaysInfantry;
        if (!hero)
            return;
        if (attributes != null && !attributes.IsAlive)
            return;

        heroSelects42mm = !heroSelects42mm;
    }

    private bool IsShopOpen()
    {
        if (shop == null)
            shop = GetComponent<AmmoShop>();
        return shop != null && shop.IsOpen;
    }

    // 有十车总控时按这台车读车位（红英雄 red_hero，蓝英雄 blue_hero）。没有总控时，当前玩家读场景「属性管理器」，再没有才用车上的组件。
    private void ResolveAttributes()
    {
        if (attributes != null)
            return;

        attributes = PlayerAttributeBinding.Resolve(gameObject);
    }

    // 有管理器：间隔 = 1 / 射速（发/秒）。英雄 42mm 用自己的 FireRate（约 2）。
    // 英雄 17mm 用步兵默认 baseFireRate，不写回英雄的 FireRate。热量、伤害不在这里改。
    // 热量已超过上限，或射速 <= 0，不能开火（本方法在扣弹之前调用）。
    // 没有管理器：用后备间隔，不加假热量。
    private float GetFireInterval()
    {
        ResolveAttributes();
        if (attributes == null)
            return fireInterval;

        if (!attributes.CanShoot || attributes.IsWeak || attributes.IsOverheated)
            return -1f;

        float rate = attributes.GetCurrent(RobotStat.FireRate);
        if (attributes.robotType == RobotType.Hero && !Uses42mm())
            rate = RobotAttributeManager.InfantryBaseFireRate;
        if (rate <= 0f)
            return -1f;

        return 1f / rate;
    }

    private bool IsFireHeld()
    {
        if (LanSession.Active)
        {
            var vehicle = GetComponent<LanVehicle>();
            var input = vehicle != null ? LanSession.Instance.InputFor(vehicle.Slot) : null;
            return input != null && input.fire && !input.shop;
        }
        return Input.GetKey(fireKey) || Input.GetKey(altFireKey);
    }

    private bool Fire()
    {
        EnsureCaliberPrefabs();
        if (muzzlePoint == null)
            muzzlePoint = FindNamedChild(transform, ProjectileChildName);

        bool use42 = Uses42mm();
        GameObject source = use42 ? prefab42mm : prefab17mm;
        if (source == null && bulletPrefab != null)
            source = bulletPrefab;

        if (source == null)
        {
            if (!warnedMissingPrefab)
            {
                warnedMissingPrefab = true;
                Debug.LogWarning("PlayerShooting：加载不到 " + (use42 ? Prefab42 : Prefab17) + "。请确认 Resources/BulletPrefabs 上的引用。");
            }
            return false;
        }

        if (muzzlePoint == null && !warnedMissingMuzzle)
        {
            warnedMissingMuzzle = true;
            Debug.LogWarning("PlayerShooting：找不到子物体「弹丸」，改从车身前方射出 " + source.name);
        }

        // 从炮口（弹丸世界坐标）出发；没有弹丸时从车身前方出膛
        Vector3 spawnPos = muzzlePoint != null
            ? muzzlePoint.position
            : transform.position + transform.forward * muzzleOffset;

        Vector3 dir = GetFireDirection(spawnPos);
        Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        Quaternion spawnRot = Quaternion.LookRotation(dir, up);

        GameObject bullet = Instantiate(source, spawnPos, spawnRot);

        bullet.name = source.name;
        bullet.SetActive(true);
        bullet.transform.SetParent(null, true);
        SetVisualAndCollision(bullet, true);

        BullProjectile projectile = bullet.GetComponent<BullProjectile>();
        if (projectile == null)
            projectile = bullet.AddComponent<BullProjectile>();
        projectile.SetAttacker(gameObject, attributes);
        projectile.Launch(dir, CurrentBulletSpeed());
        if (LanSession.IsHost)
        {
            var vehicle = GetComponent<LanVehicle>();
            if (vehicle != null) projectile.LanShotId = LanSession.Instance.NotifyShot(vehicle, use42, spawnPos, dir, CurrentBulletSpeed());
        }
        // Launch/Awake 可能补了碰撞体，必须在其后忽略发射者，避免子弹一出膛就撞自己销毁
        IgnoreCollisionWithShooter(bullet);
        return true;
    }

    public GameObject CreateLanVisual(LanShot shot)
    {
        EnsureCaliberPrefabs();
        GameObject source = shot.use42 ? prefab42mm : prefab17mm;
        if (source == null) source = bulletPrefab;
        if (source == null) return null;
        GameObject visual = Instantiate(source, shot.position, Quaternion.LookRotation(shot.direction));
        visual.name = "LanVisual_" + source.name;
        visual.SetActive(true);
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (BullProjectile projectile in visual.GetComponentsInChildren<BullProjectile>(true)) projectile.enabled = false;
        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        visual.AddComponent<LanProjectileVisual>().Velocity = shot.direction * shot.speed;
        Destroy(visual, 3f);
        return visual;
    }

    // 准星瞄准：摄像机穿过屏幕正中心打射线；有碰撞朝击中点，否则朝射线远点。允许打天上/地下，不把 dir.y 清零。
    private Vector3 GetFireDirection(Vector3 spawnPos)
    {
        Camera cam = aimCamera;
        if (cam == null)
        {
            BindAimCamera();
            cam = aimCamera;
        }

        if (cam == null)
            return transform.forward.sqrMagnitude > 0.0001f ? transform.forward.normalized : Vector3.forward;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float range = aimRange > 0.01f ? aimRange : 200f;

        if (TryGetAimHitPoint(ray, range, out Vector3 hitPoint))
        {
            Vector3 toHit = hitPoint - spawnPos;
            if (toHit.sqrMagnitude > 0.0001f)
                return toHit.normalized;
        }

        // 没命中：沿射线远点（与屏幕中心同向），退化时用射线方向
        Vector3 toFar = ray.GetPoint(range) - spawnPos;
        if (toFar.sqrMagnitude > 0.0001f)
            return toFar.normalized;
        return ray.direction.sqrMagnitude > 0.0001f ? ray.direction.normalized : Vector3.forward;
    }

    private void BindAimCamera()
    {
        // 只瞄这台车自己的镜头，避免蓝车开火时用到红车上的 Main Camera。
        Camera[] cameras = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null || !cameras[i].transform.IsChildOf(transform))
                continue;
            aimCamera = cameras[i];
            if (cameras[i].enabled)
                return;
        }
    }

    // 忽略 Trigger、跳过发射者自身碰撞，避免射线打到自己
    private bool TryGetAimHitPoint(Ray ray, float range, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        RaycastHit[] hits = Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsShooterCollider(hits[i].collider))
                continue;
            if (hits[i].distance >= bestDist)
                continue;
            bestDist = hits[i].distance;
            hitPoint = hits[i].point;
            found = true;
        }

        return found;
    }

    private bool IsShooterCollider(Collider col)
    {
        if (col == null)
            return true;

        Transform t = col.transform;
        if (t == transform || t.IsChildOf(transform))
            return true;
        if (muzzlePoint != null && (t == muzzlePoint || t.IsChildOf(muzzlePoint)))
            return true;
        return false;
    }

    bool Uses42mm()
    {
        ResolveAttributes();
        bool hero = attributes != null
            ? attributes.robotType == RobotType.Hero
            : !MatchLaunchSelection.PlaysInfantry;
        return hero && heroSelects42mm;
    }

    float CurrentBulletSpeed()
    {
        ResolveAttributes();
        if (attributes != null)
        {
            float speed = attributes.GetCurrent(RobotStat.ProjectileSpeed);
            if (speed > 0.01f)
                return speed;
        }

        return bulletSpeed;
    }

    private void BindProjectileTemplate()
    {
        EnsureCaliberPrefabs();
        muzzlePoint = FindNamedChild(transform, ProjectileChildName);
    }

    void EnsureCaliberPrefabs()
    {
        if (prefab17mm != null && prefab42mm != null)
            return;

        BulletPrefabCatalog catalog = Resources.Load<BulletPrefabCatalog>("BulletPrefabs");
        if (catalog != null)
        {
            if (prefab17mm == null)
                prefab17mm = catalog.prefab17mm;
            if (prefab42mm == null)
                prefab42mm = catalog.prefab42mm;
        }

        if (prefab42mm == null && bulletPrefab != null)
            prefab42mm = bulletPrefab;
    }

    private static Transform FindNamedChild(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name)
                return all[i];
        }

        return null;
    }

    private static void SetVisualAndCollision(GameObject go, bool enabled)
    {
        Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            rends[i].enabled = enabled;

        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = enabled;
    }

    private void IgnoreCollisionWithShooter(GameObject bullet)
    {
        Collider[] bulletCols = bullet.GetComponentsInChildren<Collider>(true);
        Collider[] shooterCols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < bulletCols.Length; i++)
        {
            for (int j = 0; j < shooterCols.Length; j++)
            {
                if (bulletCols[i] != null && shooterCols[j] != null)
                    Physics.IgnoreCollision(bulletCols[i], shooterCols[j], true);
            }
        }
    }

}
