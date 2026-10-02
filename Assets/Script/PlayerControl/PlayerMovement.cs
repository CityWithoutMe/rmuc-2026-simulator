using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;

    // 按住 Shift 时，从常速平滑加到 1.6 倍，大约 1.2 秒。松开再平滑回到常速。
    const float SprintMultiplier = 1.6f;
    const float SprintRampSeconds = 1.2f;

    // 规则手册场地坡：梯形高地 23°/43°，公路 11°/15°，飞坡 17°，中央高地 10.5°。
    // 43° 再留一点余量，45° 以内把水平速度投到坡面上。更陡的面当墙，不顺着往上写速度。
    const float InfantryMaxClimbDegrees = 45f;
    // 从底盘底面略上方向下探一小段，只认脚底附近的地面。
    const float GroundProbeLift = 0.2f;
    const float GroundProbeDistance = 0.4f;
    const float GroundProbeForward = 0.08f;
    // 图 4-37 凸起高 70mm、间距 240mm。离地超过这一截就还在空中，继续用重力，不悬在坡上方。
    const float GroundStickGap = 0.09f;

    [Header("鼠标转向")]
    public float mouseSensitivity = 2f;
    public bool lockCursor = true;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    private Rigidbody rb;
    private Transform cam;
    private Vector3 moveInput;
    private bool sprintHeld;
    private float sprintBlend;
    private float currentHorizontalSpeed;
    private float yaw;
    private float pitch;
    private bool cursorLocked;
    private static bool skipCursorUnlock;

    // 商店打开则不锁鼠标、不转向。关掉后按打开前的锁定状态恢复，不改移动本身。
    private bool shopOpen;
    private bool cursorLockedBeforeShop;

    public bool ShopOpen => shopOpen;
    // 锁定光标后的前几帧丢掉鼠标位移。Game 窗口获得焦点时 Unity 会塞进一次很大的跳动。
    private int ignoreMouseFrames;
    Collider[] chassisColliders;

    public void SetShopOpen(bool open)
    {
        if (open == shopOpen)
        {
            if (open)
                SetCursorLocked(false);
            return;
        }

        if (open)
        {
            cursorLockedBeforeShop = cursorLocked;
            shopOpen = true;
            SetCursorLocked(false);
            return;
        }

        shopOpen = false;
        if (lockCursor)
            SetCursorLocked(cursorLockedBeforeShop);
    }

    private void Awake()
    {
        TryMoveControlToSelected();

        rb = GetComponent<Rigidbody>();
        // 防止人物走路时自己倒下
        rb.freezeRotation = true;

        yaw = transform.eulerAngles.y;
        BindCamera();
    }

    private void BindCamera()
    {
        Camera found = GetComponentInChildren<Camera>(true);
        if (found == null || !found.transform.IsChildOf(transform))
            return;

        cam = found.transform;
        pitch = cam.localEulerAngles.x;
        if (pitch > 180f)
            pitch -= 360f;
    }

    // 按选车结果只操作一台。没选过则操作 hero_red。目标车上已有镜头就用它，没有才把当前启用的玩家镜头挂过去。
    private void TryMoveControlToSelected()
    {
        if (!isActiveAndEnabled)
            return;

        if (IsSelectedPlayerVehicle(gameObject))
        {
            HideCubeMeshIfHeroChild();
            ReleaseOtherPlayers();
            ClaimPlayerCamera();
            return;
        }

        Transform vehicle = FindSelectedTransform();
        if (vehicle == null || vehicle == transform)
        {
            if (vehicle == null)
                WarnMissingVehicle();
            HideCubeMeshIfHeroChild();
            return;
        }

        if (vehicle.IsChildOf(transform))
        {
            HideCubeMeshIfHeroChild();
            ClaimPlayerCamera();
            return;
        }

        if (!IsSelectedPlayerVehicle(vehicle.gameObject))
            return;

        if (vehicle.GetComponent<PlayerMovement>() != null)
        {
            DisablePlayerScriptsOnly();
            return;
        }

        EnsureHeroPhysics(vehicle.gameObject);

        PlayerShooting srcShoot = GetComponent<PlayerShooting>();
        PlayerShooting dstShoot = vehicle.GetComponent<PlayerShooting>();
        if (dstShoot == null)
            dstShoot = vehicle.gameObject.AddComponent<PlayerShooting>();
        if (srcShoot != null)
        {
            dstShoot.bulletPrefab = srcShoot.bulletPrefab;
            dstShoot.bulletSpeed = srcShoot.bulletSpeed;
            dstShoot.muzzleOffset = srcShoot.muzzleOffset;
            dstShoot.aimRange = srcShoot.aimRange;
            dstShoot.fireKey = srcShoot.fireKey;
            dstShoot.altFireKey = srcShoot.altFireKey;
            dstShoot.fireInterval = srcShoot.fireInterval;
        }

        PlayerMovement dstMove = vehicle.gameObject.AddComponent<PlayerMovement>();
        dstMove.speed = speed;
        dstMove.mouseSensitivity = mouseSensitivity;
        dstMove.lockCursor = lockCursor;
        dstMove.minPitch = minPitch;
        dstMove.maxPitch = maxPitch;

        DisablePlayerScriptsOnly();

        if (name == "Cube")
            RetirePlaceholderCube();
    }

    private void DisablePlayerScriptsOnly()
    {
        PlayerShooting srcShoot = GetComponent<PlayerShooting>();
        skipCursorUnlock = true;
        if (srcShoot != null)
            srcShoot.enabled = false;
        enabled = false;
        skipCursorUnlock = false;
    }

    private void RetirePlaceholderCube()
    {
        Rigidbody selfRb = GetComponent<Rigidbody>();
        if (selfRb != null)
            selfRb.isKinematic = true;

        MeshRenderer cubeMesh = GetComponent<MeshRenderer>();
        if (cubeMesh != null)
            cubeMesh.enabled = false;

        Collider cubeCol = GetComponent<Collider>();
        if (cubeCol != null)
            cubeCol.enabled = false;
    }

    private static string warnedMissingName;

    private void WarnMissingVehicle()
    {
        string which = SelectedVehicleLabel();
        if (warnedMissingName == which)
            return;
        warnedMissingName = which;
        Debug.LogWarning("PlayerMovement：场景里没有可操作的 " + which + "，玩家控制留在「" + name + "」上。");
    }

    static string SelectedVehicleLabel()
    {
        if (MatchLaunchSelection.PlaysInfantry && !string.IsNullOrEmpty(MatchLaunchSelection.SlotHeader))
            return MatchLaunchSelection.SlotHeader;
        return MatchLaunchSelection.PlaysBlueHero ? "hero_blue" : "hero_red";
    }

    // 关掉其他物体上的移动和射击，只留当前这台。
    private void ReleaseOtherPlayers()
    {
        PlayerMovement[] all = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            PlayerMovement other = all[i];
            if (other == null || other == this || !other.enabled)
                continue;
            other.DisablePlayerScriptsOnly();
        }
    }

    // 英雄车镜头的本地偏移。步兵自己没有镜头时，把当前启用的玩家镜头挂到同样的位置。
    static readonly Vector3 BorrowedCameraLocalPosition = new Vector3(-0.15f, 0.37f, -0.97f);

    // 优先用这台车自己的子相机。步兵没有子相机就只警告，不再把英雄镜头搬过来。英雄仍可以借用当前启用的玩家镜头。
    private void ClaimPlayerCamera()
    {
        Camera own = null;
        Camera[] mine = GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < mine.Length; i++)
        {
            if (mine[i] == null || !mine[i].transform.IsChildOf(transform))
                continue;
            if (own == null)
                own = mine[i];
            mine[i].enabled = mine[i] == own;
        }

        if (own == null && !MatchLaunchSelection.PlaysInfantry)
            own = BorrowEnabledPlayerCamera();

        if (own == null)
        {
            WarnMissingOwnCamera();
            return;
        }

        own.enabled = true;
        AudioListener keep = own.GetComponent<AudioListener>();
        if (keep == null)
            keep = own.gameObject.AddComponent<AudioListener>();
        keep.enabled = true;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c == null || c == own || c.transform.IsChildOf(transform))
                continue;
            if (!IsUnderOtherRobot(c.transform))
                continue;
            c.enabled = false;
        }

        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null && listeners[i] != keep)
                listeners[i].enabled = false;
        }

        VehicleCameraShake.Attach(this, own.transform);
    }

    private static bool warnedMissingOwnCamera;

    private void WarnMissingOwnCamera()
    {
        if (warnedMissingOwnCamera)
            return;
        warnedMissingOwnCamera = true;
        Debug.LogWarning("PlayerMovement：「" + name + "」下没有找到 Camera。请把相机挂在这台车的子物体上。");
    }

    Camera BorrowEnabledPlayerCamera()
    {
        Camera donor = null;
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c == null || !c.enabled || c.transform.IsChildOf(transform))
                continue;
            if (IsUnderAttributeBoard(c.gameObject))
                continue;

            bool main = c.gameObject.tag == "MainCamera";
            bool onRobot = IsUnderOtherRobot(c.transform) || IsUnderNamedHero(c.transform);
            if (!main && !onRobot)
                continue;

            donor = c;
            if (main)
                break;
        }

        if (donor == null)
            return null;

        Vector3 scale = donor.transform.localScale;
        donor.transform.SetParent(transform, false);
        donor.transform.localPosition = BorrowedCameraLocalPosition;
        donor.transform.localRotation = Quaternion.identity;
        donor.transform.localScale = scale;
        donor.enabled = true;
        return donor;
    }

    static bool IsUnderNamedHero(Transform t)
    {
        while (t != null)
        {
            if (IsHeroName(t.name) || IsBlueHeroName(t.name))
                return true;
            t = t.parent;
        }

        return false;
    }

    static bool IsUnderOtherRobot(Transform t)
    {
        while (t != null)
        {
            if (!IsUnderAttributeBoard(t.gameObject) && IsRobotName(t.gameObject))
                return true;
            t = t.parent;
        }

        return false;
    }

    static bool IsRobotName(GameObject go)
    {
        if (go == null)
            return false;
        if (IsHeroName(go.name) || IsBlueHeroName(go.name))
            return true;
        return TenRobotAttributeBoard.TryMatchSlot(go, out int slot) && slot >= 0;
    }

    private void HideCubeMeshIfHeroChild()
    {
        if (name != "Cube")
            return;

        bool hasProjectile = FindNamedChild(transform, "弹丸") != null;
        bool hasHeroChild = false;
        for (int i = 0; i < transform.childCount; i++)
        {
            if (IsSelectedPlayerVehicle(transform.GetChild(i).gameObject))
            {
                hasHeroChild = true;
                break;
            }
        }

        if (!hasProjectile && !hasHeroChild)
            return;

        MeshRenderer cubeMesh = GetComponent<MeshRenderer>();
        if (cubeMesh != null)
            cubeMesh.enabled = false;
    }

    Transform FindSelectedTransform()
    {
        if (MatchLaunchSelection.PlaysInfantry)
            return FindInfantryTransform();
        return FindHeroTransform();
    }

    // 步兵：名字对上 TenRobotAttributeBoard 的车位（infantry + 编号，红蓝靠 tag 或名字）。
    // 属性管理器下面的车位物体不是车，对不上场景里的车就返回 null。
    Transform FindInfantryTransform()
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null || !IsSelectedPlayerVehicle(candidate.gameObject))
                continue;

            int score = 0;
            if (candidate.GetComponentInChildren<Camera>(true) != null)
                score += 1000;
            int depth = 0;
            for (Transform walk = candidate; walk != null; walk = walk.parent)
                depth++;
            score += 200 - depth;
            if (string.Equals(candidate.name, MatchLaunchSelection.SlotHeader, System.StringComparison.OrdinalIgnoreCase))
                score += 50;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    // 不按「弹丸」选车。选蓝方英雄时只找 hero_blue，否则只找 hero_red，避免两台车抢控制。
    private Transform FindHeroTransform()
    {
        bool blue = MatchLaunchSelection.PlaysBlueHero;
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform exactRoot = null;
        Transform exactChild = null;
        Transform taggedRoot = null;
        Transform taggedChild = null;

        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null)
                continue;
            if (blue)
            {
                if (!IsBlueHeroVehicle(candidate.gameObject))
                    continue;
            }
            else if (IsBlueSideVehicle(candidate.gameObject))
            {
                continue;
            }

            bool isRoot = candidate.parent == null;
            bool exact = blue ? IsBlueHeroName(candidate.name) : IsHeroName(candidate.name);
            if (exact)
            {
                if (isRoot)
                {
                    if (exactRoot == null)
                        exactRoot = candidate;
                }
                else if (exactChild == null)
                {
                    exactChild = candidate;
                }

                continue;
            }

            if (blue || !IsSecondaryRedHero(candidate.gameObject))
                continue;

            if (isRoot)
            {
                if (taggedRoot == null)
                    taggedRoot = candidate;
            }
            else if (taggedChild == null)
            {
                taggedChild = candidate;
            }
        }

        if (exactRoot != null)
            return exactRoot;
        if (exactChild != null)
            return exactChild;
        if (taggedRoot != null)
            return taggedRoot;
        return taggedChild;
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

    // 红方英雄名字。单独叫 Hero、或 Hero 开头（含蓝方）都不算红方英雄。
    private static bool IsHeroName(string n)
    {
        return !string.IsNullOrEmpty(n)
            && string.Equals(n, "hero_red", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlueHeroName(string n)
    {
        return !string.IsNullOrEmpty(n)
            && n.IndexOf("hero_blue", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // 当前这一局要开的那一台。没选过或选了红英雄是 hero_red，选了蓝英雄是 hero_blue，选了步兵是对应编号。
    public static bool IsSelectedPlayerVehicle(GameObject go)
    {
        if (go == null || IsUnderAttributeBoard(go))
            return false;
        if (MatchLaunchSelection.PlaysInfantry)
            return InfantryMatchesSelection(go);
        if (MatchLaunchSelection.PlaysBlueHero)
            return IsBlueHeroVehicle(go);
        return IsRedPlayerVehicle(go);
    }

    static bool InfantryMatchesSelection(GameObject go)
    {
        if (go == null || IsUnderAttributeBoard(go))
            return false;

        if (TenRobotAttributeBoard.TryMatchSlot(go, out int slot)
            && slot >= 0
            && slot < TenRobotAttributeBoard.SlotHeaders.Length
            && string.Equals(TenRobotAttributeBoard.SlotHeaders[slot], MatchLaunchSelection.SlotHeader, System.StringComparison.OrdinalIgnoreCase))
            return true;

        return ChineseInfantryMatches(go);
    }

    // 名字写成「步兵」而不是 infantry 时，仍按编号和红蓝对上同一个车位。
    static bool ChineseInfantryMatches(GameObject go)
    {
        string n = go.name;
        if (!TenRobotAttributeBoard.NameHasInfantry(n))
            return false;

        int index = InfantryIndexInName(n);
        if (index != MatchLaunchSelection.InfantryIndex)
            return false;

        if (!TrySideFromObject(go, out RobotTeam side))
            return false;
        return side == MatchLaunchSelection.Team;
    }

    static int InfantryIndexInName(string name)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        int role = name.IndexOf("步兵", System.StringComparison.Ordinal);
        if (role < 0)
            role = name.IndexOf("infantry", System.StringComparison.OrdinalIgnoreCase);
        bool anyDigit = false;
        int i = 0;
        while (i < name.Length)
        {
            if (!char.IsDigit(name[i]))
            {
                i++;
                continue;
            }

            anyDigit = true;
            int start = i;
            int value = 0;
            while (i < name.Length && char.IsDigit(name[i]))
            {
                value = value * 10 + (name[i] - '0');
                i++;
            }

            if (value < 1 || value > 3)
                continue;

            int dist = role >= 0 ? Mathf.Abs(start - role) : start;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = value;
            }
        }

        if (best >= 1)
            return best;
        if (!anyDigit && TenRobotAttributeBoard.NameHasInfantry(name))
            return 1;
        return 0;
    }

    static bool TrySideFromObject(GameObject go, out RobotTeam side)
    {
        if (CombatDamage.TryGetFactionTeam(go.transform, out RobotTeam tagged)
            && (tagged == RobotTeam.Red || tagged == RobotTeam.Blue))
        {
            side = tagged;
            return true;
        }

        Transform t = go.transform;
        while (t != null)
        {
            string n = t.name;
            bool red = !string.IsNullOrEmpty(n)
                && (n.IndexOf("red", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("红", System.StringComparison.Ordinal) >= 0);
            bool blue = !string.IsNullOrEmpty(n)
                && (n.IndexOf("blue", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("蓝", System.StringComparison.Ordinal) >= 0);
            if (red && !blue)
            {
                side = RobotTeam.Red;
                return true;
            }

            if (blue && !red)
            {
                side = RobotTeam.Blue;
                return true;
            }

            if (string.Equals(n, "red", System.StringComparison.OrdinalIgnoreCase))
            {
                side = RobotTeam.Red;
                return true;
            }

            if (string.Equals(n, "blue", System.StringComparison.OrdinalIgnoreCase))
            {
                side = RobotTeam.Blue;
                return true;
            }

            t = t.parent;
        }

        side = RobotTeam.Neutral;
        return false;
    }

    public static bool IsUnderAttributeBoard(GameObject go)
    {
        if (go == null)
            return false;

        Transform t = go.transform;
        while (t != null)
        {
            if (t.GetComponent<TenRobotAttributeBoard>() != null)
                return true;
            if (t.name == PlayerAttributeBinding.SceneObjectName)
                return true;
            t = t.parent;
        }

        return false;
    }

    private static bool IsHeroChassis(GameObject go)
    {
        return IsRedPlayerVehicle(go) || IsBlueHeroVehicle(go);
    }

    // 蓝方英雄：名字含 hero_blue，或 Tag 为 blue 且名字含 hero。红方英雄不算。
    public static bool IsBlueHeroVehicle(GameObject go)
    {
        if (go == null)
            return false;
        if (IsBlueHeroName(go.name))
            return true;
        if (go.tag != CombatDamage.TagBlue)
            return false;
        if (string.IsNullOrEmpty(go.name) || go.name.IndexOf("hero", System.StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return go.name.IndexOf("hero_red", System.StringComparison.OrdinalIgnoreCase) < 0;
    }

    // 名字含 hero_blue / blue，或自己及父级 Tag 为 blue：绝不能成为控制迁移目标。
    public static bool IsBlueSideVehicle(GameObject go)
    {
        if (go == null)
            return false;

        Transform t = go.transform;
        while (t != null)
        {
            if (NameCountsAsBlue(t.name) || t.gameObject.tag == CombatDamage.TagBlue)
                return true;
            t = t.parent;
        }

        return false;
    }

    // 红方玩家车：优先名字正好是 hero_red；其次 Tag 为 red、名字含 hero，且不是蓝方。
    public static bool IsRedPlayerVehicle(GameObject go)
    {
        if (go == null || IsBlueSideVehicle(go))
            return false;
        if (IsHeroName(go.name))
            return true;
        return IsSecondaryRedHero(go);
    }

    private static bool IsSecondaryRedHero(GameObject go)
    {
        if (go == null || IsBlueSideVehicle(go))
            return false;
        if (string.IsNullOrEmpty(go.name) || go.name.IndexOf("hero", System.StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (NameCountsAsBlue(go.name))
            return false;
        return go.tag == CombatDamage.TagRed;
    }

    private static bool NameCountsAsBlue(string n)
    {
        if (string.IsNullOrEmpty(n))
            return false;
        if (n.IndexOf("hero_blue", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return n.IndexOf("blue", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void EnsureHeroPhysics(GameObject hero)
    {
        Rigidbody heroRb = hero.GetComponent<Rigidbody>();
        if (heroRb == null)
            heroRb = hero.AddComponent<Rigidbody>();
        heroRb.useGravity = true;
        heroRb.freezeRotation = true;
        heroRb.isKinematic = false;
        heroRb.interpolation = RigidbodyInterpolation.Interpolate;
        if (hero.GetComponent<RobotCollisionDamage>() == null)
            hero.AddComponent<RobotCollisionDamage>();

        if (hero.GetComponent<Collider>() != null)
            return;

        Bounds b = new Bounds(hero.transform.position, Vector3.one * 0.5f);
        bool hasBound = false;
        Renderer[] rends = hero.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null || !rends[i].enabled)
                continue;
            if (!hasBound)
            {
                b = rends[i].bounds;
                hasBound = true;
            }
            else
            {
                b.Encapsulate(rends[i].bounds);
            }
        }

        CapsuleCollider cap = hero.AddComponent<CapsuleCollider>();
        Vector3 localCenter = hero.transform.InverseTransformPoint(b.center);
        cap.center = localCenter;
        cap.height = Mathf.Max(0.4f, b.size.y / Mathf.Max(hero.transform.lossyScale.y, 0.0001f));
        float radius = 0.5f * Mathf.Max(b.size.x, b.size.z) / Mathf.Max(hero.transform.lossyScale.x, 0.0001f);
        cap.radius = Mathf.Max(0.15f, radius * 0.5f);
    }

    private void Start()
    {
        if (lockCursor)
            SetCursorLocked(true);
    }

    private void OnDisable()
    {
        if (skipCursorUnlock)
            return;
        // 退出 Play 时把光标还回来，避免编辑器卡住
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        HandleCursor();
        Look();

        // 输入放 Update，更跟手
        float x = Input.GetAxisRaw("Horizontal"); // A/D
        float z = Input.GetAxisRaw("Vertical");   // W/S

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        Vector3 right = transform.right;
        right.y = 0f;
        if (fwd.sqrMagnitude > 0.0001f) fwd.Normalize();
        if (right.sqrMagnitude > 0.0001f) right.Normalize();

        if (MatchOutcome.Decided)
        {
            moveInput = Vector3.zero;
            sprintHeld = false;
            return;
        }

        moveInput = fwd * z + right * x;
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        sprintHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    // 本帧写进刚体的水平速度（米/秒）。死亡或没给移动输入时为 0。
    public float CurrentHorizontalSpeed => currentHorizontalSpeed;

    private void FixedUpdate()
    {
        Move();
    }

    private void HandleCursor()
    {
        // 商店开着时不要用左键把光标重新锁回去
        if (shopOpen)
            return;

        if (!lockCursor)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLocked(!cursorLocked);
        else if (!cursorLocked && Input.GetMouseButtonDown(0))
            SetCursorLocked(true);
    }

    private void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        if (locked)
            ignoreMouseFrames = 8;
    }

    private void Look()
    {
        if (shopOpen)
            return;

        // 光标解锁时不转向，方便点菜单 / 移出窗口
        if (lockCursor && !cursorLocked)
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        // 记录里开局第二帧 mouseY 为 -50.3，俯仰从 0 被打到约 50 度，画面贴地。
        // 正常快速甩鼠标到不了这么大，锁定光标时的那一次跳动直接丢掉。
        if (ignoreMouseFrames > 0)
        {
            ignoreMouseFrames--;
            mouseX = 0f;
            mouseY = 0f;
        }
        else if (Mathf.Abs(mouseX) > 30f || Mathf.Abs(mouseY) > 30f)
        {
            mouseX = 0f;
            mouseY = 0f;
        }

        yaw += mouseX;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f); // 只绕 Y，Cube 不仰倒

        if (cam == null)
            return;

        pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
        cam.localEulerAngles = new Vector3(pitch, 0f, 0f);
        VehicleCameraShake.SyncBaseRotation(cam);
    }

    private void Move()
    {
        float ramp = SprintRampSeconds > 0.0001f ? SprintRampSeconds : 1.2f;
        sprintBlend = Mathf.MoveTowards(sprintBlend, sprintHeld ? 1f : 0f, Time.fixedDeltaTime / ramp);

        RobotAttributeManager moveStats = PlayerAttributeBinding.Resolve(gameObject);
        if (moveStats != null && !moveStats.CanMove)
        {
            Vector3 stopped = rb.linearVelocity;
            stopped.x = 0f;
            stopped.z = 0f;
            rb.linearVelocity = stopped;
            currentHorizontalSpeed = 0f;
            return;
        }

        float cruise = CruiseSpeed(moveStats);
        float current = Mathf.Lerp(cruise, cruise * SprintMultiplier, sprintBlend);
        Vector3 velocity = moveInput * current;
        // 没踩在可爬的坡上（英雄、空中、死亡、本局已结束、坡比 45° 更陡）时，竖直速度留给重力。
        if (velocity.sqrMagnitude > 0.0001f
            && InfantryMayClimb(moveStats)
            && TryClimbVelocity(velocity, out Vector3 alongSlope))
            velocity = alongSlope;
        else
            velocity.y = rb.linearVelocity.y;

        currentHorizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
        rb.linearVelocity = velocity;
    }

    // 只有当前这台是步兵才沿坡走。英雄属性类型是 Hero，选车也不是 PlaysInfantry，进不了这里。
    // 工程、哨兵同理，不因为脚本在同一份移动里就跟着爬。
    bool InfantryMayClimb(RobotAttributeManager stats)
    {
        if (MatchOutcome.Decided)
            return false;
        if (stats != null && !stats.IsAlive)
            return false;
        if (stats != null)
            return stats.robotType == RobotType.Infantry;
        return MatchLaunchSelection.PlaysInfantry;
    }

    bool TryClimbVelocity(Vector3 horizontal, out Vector3 alongSlope)
    {
        alongSlope = horizontal;
        Vector3 origin = ClimbProbeOrigin();
        if (!Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                GroundProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return false;

        Collider col = hit.collider;
        if (col == null || col.isTrigger)
            return false;
        if (col.transform == transform || col.transform.IsChildOf(transform))
            return false;

        float bottomY = origin.y - GroundProbeLift;
        float clearance = hit.point.y - bottomY;
        if (clearance > GroundStickGap)
            return false;

        Vector3 normal = hit.normal;
        if (normal.y <= 0f)
            return false;
        if (Vector3.Angle(normal, Vector3.up) > InfantryMaxClimbDegrees)
            return false;

        alongSlope = Vector3.ProjectOnPlane(horizontal, normal);
        return true;
    }

    Vector3 ClimbProbeOrigin()
    {
        float bottom = LowestChassisY();
        Vector3 origin = new Vector3(rb.position.x, bottom + GroundProbeLift, rb.position.z);
        if (moveInput.sqrMagnitude > 0.0001f)
            origin += moveInput * GroundProbeForward;
        return origin;
    }

    float LowestChassisY()
    {
        if (chassisColliders == null)
            chassisColliders = GetComponentsInChildren<Collider>(true);

        float bottom = rb.position.y;
        bool any = false;
        for (int i = 0; i < chassisColliders.Length; i++)
        {
            Collider col = chassisColliders[i];
            if (col == null || col.isTrigger || !col.enabled)
                continue;
            float y = col.bounds.min.y;
            if (!any || y < bottom)
            {
                bottom = y;
                any = true;
            }
        }

        return bottom;
    }

    // 常速读属性移速（含功能区加成）。没有属性时用本组件的 speed，默认 5。
    float CruiseSpeed(RobotAttributeManager moveStats)
    {
        if (moveStats != null)
            return moveStats.MoveSpeed;
        return speed;
    }
}
