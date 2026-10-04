using UnityEngine;

public sealed class LanVehicle : MonoBehaviour
{
    public int Slot { get; private set; }
    public RobotAttributeManager Stats { get; private set; }
    public PlayerShooting Shooting { get; set; }
    public bool IsLocal => LanSession.Active && Slot == LanSession.Instance.LocalSlot;
    public Rigidbody Body { get; private set; }
    public Vector3 NetworkVelocity { get; private set; }
    public string FieldProgress { get; set; }

    public static LanVehicle ForStats(RobotAttributeManager stats)
    {
        if (stats == null) return null;
        foreach (var vehicle in FindObjectsByType<LanVehicle>(FindObjectsSortMode.None))
            if (vehicle.Stats == stats) return vehicle;
        return null;
    }
    Vector3 targetPosition;
    Quaternion targetRotation;
    bool hasPose;

    public void Configure(int slot, RobotAttributeManager stats)
    {
        Slot = slot;
        Stats = stats;
        Body = GetComponent<Rigidbody>();
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    public void ApplyPose(LanRobotState state)
    {
        NetworkVelocity = state.velocity;
        targetPosition = state.position;
        targetRotation = state.rotation;
        if (!hasPose || Vector3.Distance(transform.position, targetPosition) > 2f)
            transform.position = targetPosition;
        hasPose = true;
    }

    void LateUpdate()
    {
        if (!LanSession.IsClient || !hasPose) return;
        float alpha = 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, alpha);
        if (!IsLocal) transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, alpha);
    }
}

public sealed class LanProjectileVisual : MonoBehaviour
{
    public Vector3 Velocity;
    void Update() { transform.position += Velocity * Time.deltaTime; }
}
