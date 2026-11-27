using ProjectCircle.Combat;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// Wedge Charger: a long, thin triangle lying on its side, point forward. It paces slowly; when the circle is
    /// on its floor and in front of it, it digs in (trembling, point glowing) and then charges along the floor
    /// at high speed until it hits a wall or the edge of the floor, where it stops, dazed: the opening.
    ///
    /// Dash through it (the dash makes the circle untouchable) or jump over it.
    /// Setup: root at the body's centre with a Rigidbody2D and BoxCollider2D; the shape is a child "Visual".
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
    public sealed class WedgeCharger : MonoBehaviour
    {
        private enum Phase { Pace, Windup, Charge, Dazed }

        [SerializeField] private RoundedTriangleMesh shape;

        [Header("Pacing")]
        [SerializeField, Min(0f)] private float paceSpeed = 1.5f;
        [SerializeField, Min(0f)] private float paceRange = 3f;

        [Header("Charge")]
        [Tooltip("Charges when the circle is within this distance, on its floor (height difference below 1.5).")]
        [SerializeField, Min(1f)] private float sightRange = 11f;

        [SerializeField, Min(0.05f)] private float windupTime = 0.5f;
        [SerializeField, Min(1f)] private float chargeSpeed = 15f;
        [SerializeField, Min(1f)] private float maxChargeDistance = 14f;
        [SerializeField, Min(0f)] private float dazedTime = 1f;

        [Tooltip("After the daze it paces at least this long before it can charge again.")]
        [SerializeField, Min(0f)] private float chargeCooldown = 1.2f;

        [Tooltip("How far the circle is thrown ahead of the charge when it gets hit.")]
        [SerializeField, Min(0f)] private float ramKnockback = 1f;

        [SerializeField, Min(1)] private int ramDamage = 1;
        [SerializeField] private LayerMask playerLayers = 1 << 8;
        [SerializeField] private LayerMask groundLayers = 1 << 3;
        [Tooltip("Colour the point vertex glows as it winds up (white-hot during the charge itself).")]
        [SerializeField] private Color chargeColor = new Color(1f, 0.95f, 0.7f, 1f);

        private Rigidbody2D body;
        private BoxCollider2D box;
        private Transform visual;
        private Transform player;
        private Phase phase = Phase.Pace;
        private float phaseStart;
        private float homeX;
        private int facing = 1;
        private float chargeStartX;
        private float nextPuff;
        private float canChargeAt;
        private VertexGlow tipGlow;
        private ContactDamage contact;
        private Player.PlayerHealth playerHealth;
        private ContactFilter2D playerFilter;
        private readonly System.Collections.Generic.List<Collider2D> overlaps = new System.Collections.Generic.List<Collider2D>(4);

        private float PhaseTime => Time.time - phaseStart;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            if (shape == null) shape = GetComponentInChildren<RoundedTriangleMesh>();
            visual = shape.transform;
            body.freezeRotation = true;
            homeX = transform.position.x;
            contact = GetComponent<ContactDamage>();
            playerFilter = new ContactFilter2D { useLayerMask = true, layerMask = playerLayers, useTriggers = true };

            // A halo on the point vertex: the power that drives the charge (the body is orange, so the corner glow
            // alone doesn't stand out).
            var glowGo = new GameObject("Charge Vertex Glow");
            glowGo.transform.SetParent(transform, false);
            glowGo.AddComponent<MeshFilter>();
            glowGo.AddComponent<MeshRenderer>().sharedMaterial = Systems.Fx.Material;
            tipGlow = glowGo.AddComponent<VertexGlow>();
            glowGo.GetComponent<MeshRenderer>().sortingOrder = 8;
            tipGlow.Color = chargeColor;
            tipGlow.Radius = 0.45f;
            tipGlow.BurstRadius = 1.1f;
            tipGlow.Intensity = 0f;
        }

        private void LateUpdate()
        {
            if (tipGlow != null) tipGlow.transform.position = shape.transform.TransformPoint(shape.GetCornerPoint(0));
        }

        /// <summary>Lights the point vertex (corner glow + halo). 0 = off.</summary>
        private void SetPointGlow(float amount, Color color)
        {
            shape.SetCornerGlow(amount > 0.01f ? 0 : -1, amount, color);
            if (tipGlow == null) return;
            tipGlow.Color = color;
            tipGlow.Intensity = amount;
        }

        private void Start()
        {
            var movement = FindAnyObjectByType<Player.PlayerMovement>();
            if (movement != null)
            {
                player = movement.transform;
                playerHealth = movement.GetComponent<Player.PlayerHealth>();
            }
            Face(facing);
        }

        private void FixedUpdate()
        {
            float vy = body.linearVelocity.y;
            switch (phase)
            {
                case Phase.Pace:
                {
                    if (Time.time >= canChargeAt && SeesPlayer())
                    {
                        Face(player.position.x >= body.position.x ? 1 : -1);
                        body.linearVelocity = new Vector2(0f, vy);
                        SetPhase(Phase.Windup);
                        break;
                    }
                    // Back and forth around home, turning at walls and edges.
                    if (Blocked(facing) || Mathf.Abs(body.position.x + facing * 0.3f - homeX) > paceRange) Face(-facing);
                    body.linearVelocity = new Vector2(facing * paceSpeed, vy);
                    break;
                }

                case Phase.Windup:
                {
                    body.linearVelocity = new Vector2(0f, vy);
                    float p = PhaseTime / windupTime;
                    visual.localPosition = new Vector3(-facing * 0.15f * p, 0f, 0f) + (Vector3)(Random.insideUnitCircle * 0.05f * p);
                    // The point vertex gathers power, flickering faster as the charge nears.
                    bool flick = Mathf.Repeat(PhaseTime * Mathf.Lerp(6f, 18f, p), 1f) < 0.65f;
                    SetPointGlow(flick ? Mathf.Lerp(0.3f, 1f, p) : 0.25f * p, chargeColor);
                    if (p >= 1f)
                    {
                        visual.localPosition = Vector3.zero;
                        chargeStartX = body.position.x;
                        if (tipGlow != null) tipGlow.Burst();
                        SetPhase(Phase.Charge);
                    }
                    break;
                }

                case Phase.Charge:
                {
                    body.linearVelocity = new Vector2(facing * chargeSpeed, vy);
                    SetPointGlow(1.2f, Color.white); // white-hot while the power drives it
                    if (Time.time >= nextPuff)
                    {
                        nextPuff = Time.time + 0.06f;
                        Systems.Fx.Puff(new Vector2(body.position.x - facing * box.size.x * 0.4f, box.bounds.min.y), 0.6f);
                    }
                    // Rammed the circle: it's thrown ahead of the charge and the charger stops dead.
                    // (A dashing circle is untouchable, so it passes straight through.)
                    if (RamPlayer())
                    {
                        EndCharge(vy);
                        break;
                    }

                    bool tooFar = Mathf.Abs(body.position.x - chargeStartX) >= maxChargeDistance;
                    if (Blocked(facing) || tooFar) EndCharge(vy);
                    break;
                }

                case Phase.Dazed:
                {
                    body.linearVelocity = new Vector2(0f, vy);
                    // Wobbles, tipped back, while dazed.
                    visual.localRotation = Quaternion.Euler(0f, 0f, BaseAngle + 8f * Mathf.Sin(PhaseTime * 18f) * (1f - PhaseTime / dazedTime));
                    if (PhaseTime >= dazedTime)
                    {
                        Face(facing);
                        canChargeAt = Time.time + chargeCooldown;
                        SetPhase(Phase.Pace);
                    }
                    break;
                }
            }
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStart = Time.time;

            // Touching it hurts while it paces or winds up. During the charge it rams instead (see RamPlayer),
            // and while dazed it is harmless: the daze is the opening, not a trap.
            if (contact != null) contact.enabled = next == Phase.Pace || next == Phase.Windup;
        }

        /// <summary>Stops the charge with a thud and goes dazed.</summary>
        private void EndCharge(float vy)
        {
            body.linearVelocity = new Vector2(0f, vy);
            SetPointGlow(0f, chargeColor);
            Systems.Fx.Dust(new Vector2(body.position.x + facing * box.size.x * 0.5f, box.bounds.min.y), 1.2f, 0.7f);
            Sfx.Play(SoundId.Slam, body.position, 0.6f, 1.3f);
            var cam = FindAnyObjectByType<Systems.CameraFollow2D>();
            if (cam != null) cam.Shake(0.15f, 0.2f);
            homeX = body.position.x;
            SetPhase(Phase.Dazed);
        }

        /// <summary>
        /// If the charge touches the circle, hurts it once and knocks it forward, out of the charge's path.
        /// True if it hit (the charge then ends).
        /// </summary>
        private bool RamPlayer()
        {
            if (playerHealth == null) return false;
            int count = box.Overlap(playerFilter, overlaps);
            for (int i = 0; i < count; i++)
            {
                if (overlaps[i].GetComponentInParent<Player.PlayerHealth>() != playerHealth) continue;

                // Knockback is away from the source, so put the source behind the circle: it flies ahead.
                Vector2 source = (Vector2)player.position - new Vector2(facing * ramKnockback, 0.3f);
                return playerHealth.TakeDamage(ramDamage, source);
            }
            return false;
        }

        private float BaseAngle => facing > 0 ? -90f : 90f; // the point (corner 0) faces forward

        private void Face(int direction)
        {
            facing = direction;
            visual.localRotation = Quaternion.Euler(0f, 0f, BaseAngle);
        }

        private bool SeesPlayer()
        {
            if (player == null) return false;
            Vector2 to = (Vector2)player.position - body.position;
            if (Mathf.Abs(to.y) > 1.5f || Mathf.Abs(to.x) > sightRange) return false;
            return Physics2D.Linecast(body.position, player.position, groundLayers).collider == null;
        }

        /// <summary>A wall just ahead, or no floor just ahead.</summary>
        private bool Blocked(int direction)
        {
            Bounds b = box.bounds;
            float ahead = b.extents.x + 0.15f;
            if (Physics2D.Raycast(b.center, Vector2.right * direction, ahead, groundLayers).collider != null) return true;
            Vector2 edge = new Vector2(b.center.x + direction * ahead, b.min.y + 0.05f);
            return Physics2D.Raycast(edge, Vector2.down, 0.4f, groundLayers).collider == null;
        }
    }
}
