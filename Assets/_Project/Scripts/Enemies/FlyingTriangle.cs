using System.Collections.Generic;
using ProjectCircle.Combat;
using ProjectCircle.Player;
using ProjectCircle.Systems;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// A triangle that flies on vertex power. Two of its vertices burn as thrusters to keep it in the air;
    /// the third is its gun.
    ///
    /// It flies like a little ship: the gun vertex always turns to face its target, the thrusters glow behind.
    /// - Patrol: drifts around where it was placed.
    /// - Chase: spotting the circle, it takes up a position above and to one side of it, keeping its distance
    ///   and its altitude off the ground.
    /// - Shoot: a bolt grows on the gun vertex (the cue), the aim locks a moment before firing, then the bolt
    ///   flies straight and the triangle is pushed back by the recoil.
    /// - Hit: knocked away, thrusters sputter for a moment. It can be pogoed off (down attack).
    /// - Death: shatters.
    ///
    /// Duplicate it freely: the patrol is measured from wherever each copy is placed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(PolygonCollider2D), typeof(EnemyHealth))]
    public sealed class FlyingTriangle : MonoBehaviour
    {
        private const int GunCorner = 0;     // top corner at rest = the gun
        private const int ThrusterA = 1;     // the other two burn as thrusters
        private const int ThrusterB = 2;

        private enum State { Patrol, Chase, Return }

        [Header("References")]
        [SerializeField] private RoundedTriangleMesh shape;
        [SerializeField] private Transform eye;
        [SerializeField] private Transform pupil;

        [Tooltip("Vertex-colour material for the thruster/gun glows and bolts (e.g. ShapeUnlit). Uses the shape's own if empty.")]
        [SerializeField] private Material glowMaterial;

        [Header("Flight")]
        [SerializeField, Min(0.1f)] private float maxSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float acceleration = 10f;

        [Tooltip("How fast it turns its gun towards the target (degrees per second).")]
        [SerializeField, Min(1f)] private float turnSpeed = 300f;

        [Tooltip("Horizontal distance it keeps from the circle while chasing.")]
        [SerializeField, Min(0f)] private float keepDistance = 4.5f;

        [Tooltip("How high above the circle it likes to hover.")]
        [SerializeField] private float hoverHeight = 2.5f;

        [Tooltip("Never flies lower than this above the ground beneath it.")]
        [SerializeField, Min(0.5f)] private float minAltitude = 1.8f;

        [Tooltip("Side-to-side range of its patrol around its starting point.")]
        [SerializeField, Min(0f)] private float patrolRange = 3f;

        [SerializeField, Min(0f)] private float bobAmount = 0.25f;
        [SerializeField, Min(0f)] private float bobSpeed = 1.6f;

        [Tooltip("Layers it flies around (walls, floors, platforms).")]
        [SerializeField] private LayerMask terrainLayers = 1 << 3;

        [Header("Senses")]
        [SerializeField, Min(0f)] private float detectionRadius = 9f;
        [SerializeField, Min(0f)] private float loseRadius = 13f;

        [Tooltip("Seconds it keeps chasing after losing sight of the circle.")]
        [SerializeField, Min(0f)] private float memoryTime = 2.5f;

        [Header("Shooting")]
        [SerializeField, Min(0f)] private float firstShotDelay = 0.6f;
        [SerializeField, Min(0f)] private float fireCooldown = 1.8f;

        [Tooltip("The bolt grows on the gun vertex for this long before firing: the player's cue.")]
        [SerializeField, Min(0.05f)] private float chargeTime = 0.55f;

        [Tooltip("Fraction of the charge after which the aim stops following the circle (so it can be dodged).")]
        [SerializeField, Range(0f, 1f)] private float aimLockAt = 0.7f;

        [Tooltip("Push-back from firing.")]
        [SerializeField, Min(0f)] private float recoilSpeed = 3f;

        [Tooltip("The bolt. Turn Rate 0 = it flies straight.")]
        [SerializeField] private HomingOrb.Settings bolt = DefaultBolt;

        [Header("Getting Hit")]
        [SerializeField, Min(0f)] private float knockbackSpeed = 6f;
        [SerializeField, Min(0f)] private float stunTime = 0.35f;
        [SerializeField, Min(0f)] private float flashTime = 0.1f;

        [Header("Look")]
        [SerializeField] private Color calmTop = new Color(0.42f, 0.78f, 0.72f, 1f);
        [SerializeField] private Color calmBottom = new Color(0.25f, 0.55f, 0.52f, 1f);
        [SerializeField] private Color angryTop = new Color(0.35f, 0.92f, 0.78f, 1f);
        [SerializeField] private Color angryBottom = new Color(0.12f, 0.6f, 0.55f, 1f);
        [SerializeField] private Color thrusterColor = new Color(0.65f, 1f, 0.92f, 1f);
        [SerializeField] private Color gunColor = new Color(0.55f, 1f, 0.85f, 1f);

        private static HomingOrb.Settings DefaultBolt
        {
            get
            {
                HomingOrb.Settings s = HomingOrb.Settings.Default;
                s.color = new Color(0.45f, 1f, 0.85f, 1f);
                s.glowRadius = 0.3f;
                s.hitRadius = 0.16f;
                s.driftSpeed = 10f;
                s.driftTime = 0f;
                s.maxSpeed = 10f;
                s.acceleration = 40f;
                s.turnRate = 0f;
                s.homingTime = 0f;
                s.lifetime = 2.5f;
                return s;
            }
        }

        private Rigidbody2D body;
        private PolygonCollider2D polygon;
        private EnemyHealth health;
        private Transform target;
        private Transform visual;
        private VertexGlow gunGlow;
        private readonly VertexGlow[] thrusters = new VertexGlow[2];

        private State state = State.Patrol;
        private Vector2 home;
        private float lastSeenTime = float.NegativeInfinity;
        private Vector2 lastSeenPosition;
        private bool canSeeTarget;
        private int chaseSide = 1;

        private float nextShotTime;
        private bool wasChasing;
        private bool charging;
        private float chargeStartTime;
        private Vector2 aimDirection = Vector2.down;
        private HomingOrb chargingBolt;

        private float stunnedUntil = float.NegativeInfinity;
        private float flashUntil = float.NegativeInfinity;
        private bool flashing;
        private float appliedAnger = -1f;
        private float anger;

        private float Circumradius => shape != null ? shape.Circumradius : 0.7f;

        #region Unity lifecycle

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            polygon = GetComponent<PolygonCollider2D>();
            health = GetComponent<EnemyHealth>();
            body.gravityScale = 0f;
            visual = shape != null ? shape.transform : transform;
            home = body.position;
            FitCollider();
            CreateGlows();
        }

        private void OnEnable()
        {
            health.Hit += HandleHit;
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            health.Hit -= HandleHit;
            health.Died -= HandleDied;
            CancelCharge();
        }

        private void Start()
        {
            PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
            target = player != null ? player.transform : null;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            UpdateSenses();

            if (Time.time < stunnedUntil)
            {
                // Tumbling from a hit: no control, the knockback slowly bleeds off.
                body.linearVelocity = Vector2.MoveTowards(body.linearVelocity, Vector2.zero, acceleration * 0.5f * dt);
                body.angularVelocity = 0f;
                return;
            }

            Vector2 desired = DesiredPosition();
            Vector2 toDesired = desired - body.position;
            Vector2 desiredVelocity = Vector2.ClampMagnitude(toDesired * 2.2f, charging ? maxSpeed * 0.25f : maxSpeed);
            desiredVelocity += AvoidTerrain(desiredVelocity);
            body.linearVelocity = Vector2.MoveTowards(body.linearVelocity, desiredVelocity, acceleration * dt);

            // Turn the gun vertex towards what it is looking at.
            float targetAngle = Mathf.Atan2(LookDirection().y, LookDirection().x) * Mathf.Rad2Deg - 90f;
            body.angularVelocity = 0f;
            body.MoveRotation(Mathf.MoveTowardsAngle(body.rotation, targetAngle, turnSpeed * dt));
        }

        private void Update()
        {
            UpdateAttack();
            UpdateLook();
        }

        #endregion

        #region Senses & state

        private void UpdateSenses()
        {
            canSeeTarget = false;
            if (target == null) return;

            Vector2 to = (Vector2)target.position - body.position;
            float distance = to.magnitude;
            float range = state == State.Chase ? loseRadius : detectionRadius;
            if (distance <= range && Physics2D.Linecast(body.position, target.position, terrainLayers).collider == null)
            {
                canSeeTarget = true;
                lastSeenTime = Time.time;
                lastSeenPosition = target.position;
            }

            switch (state)
            {
                case State.Patrol:
                    if (canSeeTarget) StartChase();
                    break;
                case State.Chase:
                    if (!canSeeTarget && Time.time > lastSeenTime + memoryTime) state = State.Return;
                    break;
                case State.Return:
                    if (canSeeTarget) StartChase();
                    else if (Vector2.Distance(body.position, home) < 0.5f) state = State.Patrol;
                    break;
            }
        }

        private void StartChase()
        {
            state = State.Chase;
            chaseSide = body.position.x >= target.position.x ? 1 : -1;
        }

        /// <summary>Where it wants to be right now.</summary>
        private Vector2 DesiredPosition()
        {
            float bob = Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobAmount;
            Vector2 desired;

            switch (state)
            {
                case State.Chase:
                    Vector2 focus = canSeeTarget ? (Vector2)target.position : lastSeenPosition;
                    // Stay on the side it is on; switch only if the circle gets right underneath.
                    float dx = body.position.x - focus.x;
                    if (Mathf.Abs(dx) > 0.5f) chaseSide = dx > 0f ? 1 : -1;
                    desired = focus + new Vector2(chaseSide * keepDistance, hoverHeight + bob);
                    break;
                case State.Return:
                    desired = home;
                    break;
                default:
                    desired = home + new Vector2(Mathf.Sin(Time.time * 0.35f) * patrolRange, bob);
                    break;
            }

            // Keep its altitude above whatever is beneath that spot, and below any ceiling.
            RaycastHit2D ground = Physics2D.Raycast(desired + Vector2.up * 0.5f, Vector2.down, 30f, terrainLayers);
            if (ground.collider != null && desired.y < ground.point.y + minAltitude) desired.y = ground.point.y + minAltitude;
            RaycastHit2D ceiling = Physics2D.Raycast(desired, Vector2.up, Circumradius + 0.6f, terrainLayers);
            if (ceiling.collider != null) desired.y = ceiling.point.y - Circumradius - 0.6f;

            return desired;
        }

        /// <summary>Steers away from walls and floors it is about to fly into.</summary>
        private Vector2 AvoidTerrain(Vector2 velocity)
        {
            if (velocity.sqrMagnitude < 0.01f) return Vector2.zero;
            RaycastHit2D hit = Physics2D.CircleCast(body.position, Circumradius * 0.8f, velocity.normalized, 1.2f, terrainLayers);
            return hit.collider != null ? hit.normal * maxSpeed : Vector2.zero;
        }

        /// <summary>Where the gun points: at the target (or the locked aim while firing), else along its flight.</summary>
        private Vector2 LookDirection()
        {
            if (charging) return aimDirection;
            if (state == State.Chase && target != null)
            {
                Vector2 focus = canSeeTarget ? (Vector2)target.position : lastSeenPosition;
                Vector2 to = focus - body.position;
                if (to.sqrMagnitude > 0.01f) return to.normalized;
            }

            // Idle: nose slightly down, leaning into its drift.
            Vector2 drift = body.linearVelocity;
            return (Vector2.down * 2f + new Vector2(drift.x, 0f) * 0.3f).normalized;
        }

        #endregion

        #region Shooting

        private void UpdateAttack()
        {
            bool chasing = state == State.Chase && canSeeTarget && Time.time >= stunnedUntil;
            if (chasing && !wasChasing) nextShotTime = Mathf.Max(nextShotTime, Time.time + firstShotDelay);
            wasChasing = chasing;

            if (charging)
            {
                float p = (Time.time - chargeStartTime) / chargeTime;

                // Follow the circle until the aim locks, so a late move dodges it.
                if (p < aimLockAt && target != null)
                {
                    Vector2 to = (Vector2)target.position - GunTip();
                    if (to.sqrMagnitude > 0.01f) aimDirection = to.normalized;
                }

                if (chargingBolt != null) chargingBolt.SetForming(GunTip() + aimDirection * bolt.glowRadius * 0.6f, Mathf.Clamp01(p));
                if (p >= 1f) Fire();
                return;
            }

            if (!chasing || Time.time < nextShotTime || target == null) return;

            // Start charging a bolt on the gun vertex.
            charging = true;
            chargeStartTime = Time.time;
            Vector2 toTarget = (Vector2)target.position - GunTip();
            aimDirection = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector2.down;
            chargingBolt = HomingOrb.Spawn(GunTip(), bolt, GlowMaterial, target);
        }

        private void Fire()
        {
            charging = false;
            nextShotTime = Time.time + fireCooldown;

            if (chargingBolt != null) chargingBolt.Release(aimDirection);
            chargingBolt = null;
            if (gunGlow != null) gunGlow.Burst();

            // Kicked back by the shot.
            body.linearVelocity -= aimDirection * recoilSpeed;
        }

        private void CancelCharge()
        {
            charging = false;
            if (chargingBolt != null) chargingBolt.Cancel();
            chargingBolt = null;
        }

        private Vector2 GunTip() => visual.TransformPoint(shape != null ? shape.GetCornerPoint(GunCorner) : Vector2.up * 0.7f);

        #endregion

        #region Getting hit

        private void HandleHit(int damage, Vector2 source)
        {
            flashUntil = Time.time + flashTime;
            if (health.IsDead) return;

            CancelCharge();
            nextShotTime = Mathf.Max(nextShotTime, Time.time + stunTime + 0.4f);
            stunnedUntil = Time.time + stunTime;

            Vector2 away = body.position - source;
            if (away.sqrMagnitude < 0.01f) away = Vector2.up;
            body.linearVelocity = away.normalized * knockbackSpeed;

            // Being hit makes it hunt the attacker.
            if (target != null)
            {
                lastSeenTime = Time.time;
                lastSeenPosition = target.position;
                if (state != State.Chase) StartChase();
            }
        }

        private void HandleDied()
        {
            CancelCharge();
            Vector2 awayFrom = target != null ? (Vector2)target.position : body.position + Vector2.down;
            TriangleShatter.Spawn(visual.position, Circumradius, angryTop, angryBottom, new Color(0.08f, 0.16f, 0.15f, 1f),
                                  thrusterColor, GlowMaterial, awayFrom);
            Destroy(gameObject);
        }

        #endregion

        #region Look

        private Material GlowMaterial =>
            glowMaterial != null ? glowMaterial : (shape != null ? shape.GetComponent<MeshRenderer>().sharedMaterial : null);

        private void CreateGlows()
        {
            if (shape == null) return;
            int order = shape.GetComponent<MeshRenderer>().sortingOrder;

            VertexGlow MakeGlow(string glowName, int corner, Color color, float radius)
            {
                var go = new GameObject(glowName);
                go.layer = gameObject.layer;
                go.transform.SetParent(visual, false);
                go.transform.localPosition = shape.GetCornerPoint(corner, -0.05f);
                VertexGlow glow = go.AddComponent<VertexGlow>();
                go.GetComponent<MeshRenderer>().sharedMaterial = GlowMaterial;
                go.GetComponent<MeshRenderer>().sortingOrder = order - 1;
                glow.Color = color;
                glow.Radius = radius;
                return glow;
            }

            thrusters[0] = MakeGlow("Thruster A", ThrusterA, thrusterColor, Circumradius * 0.55f);
            thrusters[1] = MakeGlow("Thruster B", ThrusterB, thrusterColor, Circumradius * 0.55f);
            gunGlow = MakeGlow("Gun Glow", GunCorner, gunColor, Circumradius * 0.45f);

            // Exhaust: a short fading streak behind each thruster, so they read as engines.
            foreach (VertexGlow thruster in thrusters)
            {
                var trail = thruster.gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = GlowMaterial;
                trail.time = 0.22f;
                trail.minVertexDistance = 0.04f;
                trail.widthCurve = AnimationCurve.Linear(0f, Circumradius * 0.4f, 1f, 0f);
                trail.sortingOrder = order - 2;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(thrusterColor, 0f), new GradientColorKey(thrusterColor, 1f) },
                    new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;
            }
        }

        private void UpdateLook()
        {
            bool stunned = Time.time < stunnedUntil;

            // Thrusters: a steady burn with flicker, harder while accelerating; they sputter when stunned.
            float burn = 0.75f + 0.35f * Mathf.Clamp01(body.linearVelocity.magnitude / maxSpeed);
            for (int i = 0; i < 2; i++)
            {
                if (thrusters[i] == null) continue;
                float flicker = 0.85f + 0.15f * Mathf.Sin(Time.time * (37f + i * 11f));
                if (stunned) flicker *= Random.value < 0.5f ? 0.15f : 1f;
                thrusters[i].Intensity = burn * flicker;
            }

            // Gun vertex: dim at rest, building while a shot charges.
            if (gunGlow != null)
            {
                float charge = charging ? Mathf.Clamp01((Time.time - chargeStartTime) / chargeTime) : 0f;
                gunGlow.Intensity = 0.25f + 1.1f * charge;
            }

            UpdateColour();

            // Pupil looks at the circle.
            if (pupil != null && eye != null && target != null)
            {
                Vector2 toTarget = eye.InverseTransformDirection((Vector2)target.position - (Vector2)eye.position);
                Vector2 local = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized * 0.22f : Vector2.zero;
                pupil.localPosition = new Vector3(local.x, local.y, pupil.localPosition.z);
            }
        }

        private void UpdateColour()
        {
            if (shape == null) return;

            if (Time.time < flashUntil)
            {
                shape.SetFill(Color.white, new Color(0.85f, 1f, 0.95f, 1f));
                flashing = true;
                return;
            }

            anger = Mathf.MoveTowards(anger, state == State.Chase ? 1f : 0f, 6f * Time.deltaTime);
            if (!flashing && Mathf.Abs(anger - appliedAnger) < 0.01f) return;
            flashing = false;
            appliedAnger = anger;
            shape.SetFill(Color.Lerp(calmTop, angryTop, anger), Color.Lerp(calmBottom, angryBottom, anger));
        }

        /// <summary>Makes the collider match the drawn triangle.</summary>
        private void FitCollider()
        {
            if (shape == null) return;
            var points = new List<Vector2>();
            RoundedTriangleMesh.BuildOutline(points, shape.Circumradius, shape.CornerRadius, 2);
            for (int i = 0; i < points.Count; i++) points[i] = (Vector2)shape.transform.localPosition + points[i];
            polygon.pathCount = 1;
            polygon.SetPath(0, points);
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector2 centre = Application.isPlaying ? home : (Vector2)transform.position;
            Gizmos.color = new Color(0.4f, 1f, 0.8f, 0.6f);
            Gizmos.DrawLine(centre + Vector2.left * patrolRange, centre + Vector2.right * patrolRange);
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
        }
#endif
    }
}
