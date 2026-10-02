using UnityEngine;

// 挂在正在使用的那台子相机上。只改相机本地位置和本地旋转，不碰车根刚体、不挪车身。
// 每帧先回到鼠标写完的基准，再加这一帧的偏移，避免累加把画面甩飞。
public class VehicleCameraShake : MonoBehaviour
{
    const float MotionStartSpeed = 0.4f;
    const float MotionFullSpeed = 8f;
    const float MotionFullOffset = 0.012f;

    const float ImpactMinSpeed = 1.5f;
    const float ImpactFullSpeed = 10f;
    const float ImpactMinOffset = 0.025f;
    const float ImpactMaxOffset = 0.08f;
    const float ImpactMinDegrees = 1.2f;
    const float ImpactMaxDegrees = 3f;
    const float ImpactDecay = 8f;
    const float ImpactWobble = 24f;

    PlayerMovement movement;
    Rigidbody body;
    Vector3 restLocalPosition;
    Quaternion baseLocalRotation;
    bool bound;
    bool hasBase;

    float fallSpeed;
    bool impactQueued;
    float queuedSpeed;
    Vector3 queuedWorldDir;

    float impactPosAmp;
    float impactRotAmp;
    float impactAge;
    Vector3 impactPosAxis;
    float impactPitchSign;
    float impactRollSign;

    public static void Attach(PlayerMovement owner, Transform camera)
    {
        if (owner == null || camera == null)
            return;

        VehicleCameraShake onBody = owner.GetComponent<VehicleCameraShake>();
        if (onBody != null)
        {
            onBody.enabled = false;
            Destroy(onBody);
        }

        VehicleCameraShake shake = camera.GetComponent<VehicleCameraShake>();
        if (shake == null)
            shake = camera.gameObject.AddComponent<VehicleCameraShake>();
        shake.Bind(owner, camera);

        VehicleImpactRelay relay = owner.GetComponent<VehicleImpactRelay>();
        if (relay == null)
            relay = owner.gameObject.AddComponent<VehicleImpactRelay>();
        relay.Target = shake;
    }

    public static void SyncBaseRotation(Transform camera)
    {
        if (camera == null)
            return;
        VehicleCameraShake shake = camera.GetComponent<VehicleCameraShake>();
        if (shake != null)
            shake.CaptureBaseRotation();
    }

    public void Bind(PlayerMovement owner, Transform camera)
    {
        movement = owner;
        body = owner != null ? owner.GetComponent<Rigidbody>() : null;
        restLocalPosition = camera.localPosition;
        baseLocalRotation = camera.localRotation;
        hasBase = true;
        bound = true;
    }

    public void CaptureBaseRotation()
    {
        baseLocalRotation = transform.localRotation;
        hasBase = true;
    }

    void FixedUpdate()
    {
        if (body == null && movement != null)
            body = movement.GetComponent<Rigidbody>();
        if (body == null)
            return;

        float vy = body.linearVelocity.y;
        if (vy < -0.35f)
            fallSpeed = Mathf.Max(fallSpeed, -vy);
        else
            fallSpeed *= 0.5f;
    }

    public void OnVehicleCollision(Collision collision)
    {
        if (!bound || collision == null)
            return;

        float rel = collision.relativeVelocity.magnitude;
        float impulseSpeed = 0f;
        if (body != null && body.mass > 0.01f)
            impulseSpeed = collision.impulse.magnitude / body.mass;

        float drop = fallSpeed;
        float impact = Mathf.Max(rel, impulseSpeed);
        bool landing = drop >= ImpactMinSpeed || Mathf.Abs(collision.relativeVelocity.y) >= ImpactMinSpeed;
        if (landing)
            impact = Mathf.Max(impact, drop);
        if (impact < ImpactMinSpeed)
            return;

        Vector3 worldDir = collision.relativeVelocity;
        if (worldDir.sqrMagnitude < 0.0001f)
            worldDir = Vector3.down * Mathf.Max(drop, 1f);

        if (!impactQueued || impact > queuedSpeed)
        {
            impactQueued = true;
            queuedSpeed = impact;
            queuedWorldDir = worldDir;
        }
    }

    void LateUpdate()
    {
        if (!bound)
            return;

        if (impactQueued)
        {
            Kick(queuedSpeed, queuedWorldDir);
            impactQueued = false;
            queuedSpeed = 0f;
            fallSpeed = 0f;
        }

        float dt = Time.deltaTime;
        Vector3 offset = MotionOffset();
        Vector3 euler = Vector3.zero;
        if (impactPosAmp > 0f || impactRotAmp > 0f)
        {
            impactAge += dt;
            float env = Mathf.Exp(-ImpactDecay * impactAge);
            if (env < 0.02f)
            {
                impactPosAmp = 0f;
                impactRotAmp = 0f;
            }
            else
            {
                float wave = Mathf.Cos(impactAge * ImpactWobble);
                offset += impactPosAxis * (impactPosAmp * env * wave);
                float degrees = impactRotAmp * env * wave;
                euler = new Vector3(impactPitchSign * degrees, 0f, impactRollSign * degrees);
            }
        }

        offset = Vector3.ClampMagnitude(offset, ImpactMaxOffset);
        if (offset.sqrMagnitude < 1e-10f)
            transform.localPosition = restLocalPosition;
        else
            transform.localPosition = restLocalPosition + offset;

        Quaternion baseRot = hasBase ? baseLocalRotation : transform.localRotation;
        if (euler.sqrMagnitude < 1e-8f)
            transform.localRotation = baseRot;
        else
            transform.localRotation = baseRot * Quaternion.Euler(euler);
    }

    void OnDisable()
    {
        transform.localPosition = restLocalPosition;
        if (hasBase)
            transform.localRotation = baseLocalRotation;
        impactPosAmp = 0f;
        impactRotAmp = 0f;
        impactQueued = false;
    }

    Vector3 MotionOffset()
    {
        if (!MotionAllowed() || body == null)
            return Vector3.zero;

        Vector3 velocity = body.linearVelocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;
        if (speed <= MotionStartSpeed)
            return Vector3.zero;

        float t = Mathf.Clamp01(Mathf.InverseLerp(MotionStartSpeed, MotionFullSpeed, speed));
        float amp = t * MotionFullOffset;
        float time = Time.time;
        return new Vector3(
            Mathf.Sin(time * 11.3f) * amp,
            Mathf.Sin(time * 17.1f + 1.7f) * amp * 0.55f,
            Mathf.Sin(time * 8.2f + 0.4f) * amp * 0.25f);
    }

    bool MotionAllowed()
    {
        if (movement != null && !movement.enabled)
            return false;
        if (movement != null && movement.ShopOpen)
            return false;

        RobotAttributeManager stats = PlayerAttributeBinding.Resolve(movement != null ? movement.gameObject : gameObject);
        if (stats != null && !stats.IsAlive)
            return false;
        return true;
    }

    void Kick(float speed, Vector3 worldDir)
    {
        float t = Mathf.Clamp01(Mathf.InverseLerp(ImpactMinSpeed, ImpactFullSpeed, speed));
        float pos = Mathf.Lerp(ImpactMinOffset, ImpactMaxOffset, t);
        float rot = Mathf.Lerp(ImpactMinDegrees, ImpactMaxDegrees, t);
        float current = impactPosAmp * Mathf.Exp(-ImpactDecay * impactAge);
        if (pos < current)
            return;

        Vector3 local = transform.InverseTransformDirection(worldDir);
        Vector3 planar = new Vector3(local.x, local.y, 0f);
        if (planar.sqrMagnitude < 0.04f)
            planar = new Vector3(0.35f, 1f, 0f);
        impactPosAxis = planar.normalized;

        Vector2 swing = new Vector2(Mathf.Clamp(-local.y, -1f, 1f), Mathf.Clamp(-local.x, -1f, 1f));
        if (swing.magnitude < 0.25f)
            swing = new Vector2(0.45f, 0.85f);
        swing.Normalize();
        impactPitchSign = swing.x;
        impactRollSign = swing.y;

        impactPosAmp = pos;
        impactRotAmp = rot;
        impactAge = 0f;
    }
}

// 碰撞消息只发给刚体所在物体。这里只转发，不改车身位置或旋转。
public class VehicleImpactRelay : MonoBehaviour
{
    public VehicleCameraShake Target;

    void OnCollisionEnter(Collision collision)
    {
        if (Target != null)
            Target.OnVehicleCollision(collision);
    }
}
