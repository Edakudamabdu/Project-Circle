using System;
using System.Collections.Generic;
using ProjectCircle.Combat;
using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>What the triangle enemy is currently doing.</summary>
    public enum TriangleState
    {
        /// <summary>Tumbling back and forth inside its patrol range.</summary>
        Patrol,

        /// <summary>Just spotted the player: hops in surprise before giving chase.</summary>
        Alert,

        /// <summary>Keeping a set distance from the player: closes in, backs off, holds position, or jumps to a better platform.</summary>
        Chase,

        /// <summary>Lost the player: heading back to its patrol range, jumping between platforms if needed.</summary>
        Return
    }

    /// <summary>
    /// Triangle enemy. On the ground it moves by tumbling: each step it tips over its front corner and
    /// lands on its next side. To reach other platforms it uses <b>vertex power</b>: it rocks back onto a
    /// corner, the corner glows as it charges, and it launches itself in a spinning arc.
    ///
    /// Behaviour:
    /// - Patrols left and right inside a range around where it was placed (green line when selected).
    /// - Spots the player inside its detection radius when no wall blocks the view, hops in surprise and
    ///   turns red.
    /// - While chasing it closes in to its preferred distance and stands its ground there (it only backs
    ///   away if Keep Distance is on). Up close it pounces onto the player; standing still it fires its laser.
    ///   If a different spot would be better (the player is on another platform, a gap or wall is in the
    ///   way, or it has no line of sight) it jumps there. <see cref="IsInAttackPosition"/> reports when it
    ///   is set up to shoot.
    /// - Once committed to an attack, hits don't stop it (Armored While Attacking), and after being hit it
    ///   can pounce straight back (Retaliates).
    /// - If it loses sight of the player it heads to where it last saw them, then gives up after a moment
    ///   and returns home, jumping back to its own platform if it left it.
    /// - It never tumbles off ledges, onto slopes or into walls, and never strays beyond its leash.
    ///
    /// Duplicate it freely: the patrol range is measured from wherever each copy is placed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(PolygonCollider2D))]
    public sealed class TriangleEnemy : MonoBehaviour, ITriangleLook
    {
        private const float TumbleAngle = 120f;   // an equilateral triangle turns 120° to land on its next side
        private const float Sqrt3 = 1.7320508f;
        private const float Cos30 = 0.8660254f;
        private const int CornerBottomLeft = 1;  // corner indices of RoundedTriangleMesh at rest
        private const int CornerBottomRight = 2;
        private const float SameFloorTolerance = 0.3f;
        private const float PlanInterval = 0.3f; // at most this often it searches for a jump

        /// <summary>All active triangles, so they can keep from bunching up on the same spot.</summary>
        private static readonly List<TriangleEnemy> Active = new List<TriangleEnemy>();
        private static int spawnCounter;

        /// <summary>
        /// The project skips domain reloads when entering Play Mode (faster iteration), so static data
        /// would otherwise survive from the previous session. Clear it at the start of each run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active.Clear();
            spawnCounter = 0;
        }

        private enum Motion { Idle, Tumbling, Charging, Airborne, Stunned }

        [Header("References")]
        [Tooltip("The triangle's shape. Lives on the Visual child.")]
        [SerializeField] private RoundedTriangleMesh shape;

        [Tooltip("Child that rotates while tumbling. Its origin is the centre of the triangle.")]
        [SerializeField] private Transform visual;

        [Tooltip("The eye (white). Child of this object; follows the centre of the triangle without rotating.")]
        [SerializeField] private Transform eye;

        [Tooltip("The pupil. Child of the eye; looks towards the player or the way it is moving.")]
        [SerializeField] private Transform pupil;

        [Tooltip("Glow shown on a corner when it uses vertex power. Child of Visual.")]
        [SerializeField] private VertexGlow vertexGlow;

        [Header("Patrol")]
        [Tooltip("How far it patrols to the left of where it was placed.")]
        [SerializeField, Min(0f)] private float patrolLeft = 3f;

        [Tooltip("How far it patrols to the right of where it was placed.")]
        [SerializeField, Min(0f)] private float patrolRight = 3f;

        [Tooltip("Seconds per tumble while patrolling.")]
        [SerializeField, Min(0.05f)] private float patrolStepTime = 0.42f;

        [Tooltip("Rest between tumbles while patrolling.")]
        [SerializeField, Min(0f)] private float patrolStepPause = 0.28f;

        [Tooltip("How long it waits (looking around) at each end of its patrol before turning back.")]
        [SerializeField, Min(0f)] private float turnPause = 1.1f;

        [Header("Detection")]
        [Tooltip("Spots the player inside this radius.")]
        [SerializeField, Min(0f)] private float detectionRadius = 7f;

        [Tooltip("Once chasing, keeps track of the player inside this (larger) radius.")]
        [SerializeField, Min(0f)] private float loseRadius = 10f;

        [Tooltip("If on, walls and floors between it and the player block its view.")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("Layers that block its view.")]
        [SerializeField] private LayerMask sightBlockers = 1 << 3;

        [Tooltip("How long it keeps hunting (heading to where it last saw the player) after losing sight.")]
        [SerializeField, Min(0f)] private float memoryTime = 3f;

        [Tooltip("How long the surprised hop lasts before it starts chasing.")]
        [SerializeField, Min(0f)] private float alertTime = 0.35f;

        [Header("Kind (rank)")]
        [Tooltip("Scalene (uneven, least aggressive), Isosceles, Equilateral (perfect, most aggressive). Standard = plain, no rank changes.")]
        [SerializeField] private TriangleKind kind = TriangleKind.Standard;

        [Tooltip("Scalene and isosceles triangles each get their own proportions and colour (abilities included), " +
                 "picked from this seed. 0 = pick from where it's placed. Type any other number to choose a look.")]
        [SerializeField] private int lookSeed;

        [Header("Chase")]
        [Tooltip("Horizontal distance it closes to before standing its ground and attacking.")]
        [SerializeField, Min(0f)] private float preferredDistance = 1.6f;

        [Tooltip("How far from the preferred distance it is happy to stay put. Keep it larger than half a tumble (about 0.5).")]
        [SerializeField, Min(0.1f)] private float distanceTolerance = 0.5f;

        [Tooltip("If on, it backs away when the player gets closer than the preferred distance (a cautious, ranged style). Off = it stands its ground and fights up close.")]
        [SerializeField] private bool keepDistance = false;

        [Tooltip("Seconds per tumble while chasing.")]
        [SerializeField, Min(0.05f)] private float chaseStepTime = 0.2f;

        [Tooltip("Rest between tumbles while chasing.")]
        [SerializeField, Min(0f)] private float chaseStepPause = 0.03f;

        [Tooltip("Never moves (or jumps) further than this from where it was placed.")]
        [SerializeField, Min(0f)] private float leashDistance = 12f;

        [Tooltip("Keeps at least this far from other triangles on the same floor, so a group spreads out.")]
        [SerializeField, Min(0f)] private float separationDistance = 1.4f;

        [Header("Vertex Jump")]
        [Tooltip("If off, it never leaves the platform it is on.")]
        [SerializeField] private bool canJump = true;

        [Tooltip("Highest it can rise above the floor it jumps from.")]
        [SerializeField, Min(0.5f)] private float maxJumpHeight = 5.5f;

        [Tooltip("Furthest it can jump sideways.")]
        [SerializeField, Min(0.5f)] private float maxJumpDistance = 7f;

        [Tooltip("Deepest drop it will jump down.")]
        [SerializeField, Min(0f)] private float maxDropHeight = 7f;

        [Tooltip("Fastest sideways speed a jump may use. Keeps jumps looking like leaps rather than shots.")]
        [SerializeField, Min(0.5f)] private float maxJumpSpeedX = 9f;

        [Tooltip("How far the arc clears the higher of the two floors.")]
        [SerializeField, Min(0.1f)] private float jumpClearance = 0.8f;

        [Tooltip("Jumps that rise higher than this need vertex power (a boosted jump: charge, glow, burst). Lower jumps are plain hops.")]
        [SerializeField, Min(0f)] private float boostHeight = 2.2f;

        [Tooltip("Jumps that travel further sideways than this also need vertex power.")]
        [SerializeField, Min(0f)] private float boostDistance = 4.5f;

        [Tooltip("How long it rocks back on its glowing corner, charging, before a boosted jump.")]
        [SerializeField, Min(0.05f)] private float jumpChargeTime = 0.42f;

        [Tooltip("Minimum sideways speed when hopping off a ledge to get down (it goes faster if needed to clear the edge).")]
        [SerializeField, Min(0.5f)] private float hopOffSpeed = 3.5f;

        [Tooltip("Upward speed when hopping off a ledge to get down.")]
        [SerializeField, Min(0.5f)] private float hopOffLift = 5f;

        [Tooltip("How long it crouches before a plain hop.")]
        [SerializeField, Min(0.02f)] private float hopCrouchTime = 0.14f;

        [Tooltip("How much it squashes while crouching for a plain hop.")]
        [SerializeField, Range(0f, 0.4f)] private float hopCrouchSquash = 0.16f;

        [Tooltip("How far it rocks back onto its corner while charging, in degrees.")]
        [SerializeField, Range(0f, 50f)] private float chargeTiltAngle = 28f;

        [Tooltip("Minimum time between jumps.")]
        [SerializeField, Min(0f)] private float jumpCooldown = 0.8f;

        [Tooltip("A landing spot must be at least this much better than where it stands before it bothers jumping.")]
        [SerializeField, Min(0f)] private float requiredImprovement = 1f;

        [Tooltip("How much it cares about being level with the player when picking a spot (per unit of height).")]
        [SerializeField, Min(0f)] private float heightWeight = 1.2f;

        [Tooltip("How much worse being too close to the player is than being too far (it is a ranged attacker).")]
        [SerializeField, Min(1f)] private float tooCloseWeight = 1.5f;

        [Tooltip("Penalty for a spot with no line of sight to the player.")]
        [SerializeField, Min(0f)] private float noSightPenalty = 6f;

        [Tooltip("Cost per unit of jump distance, so it prefers short hops when spots are equally good.")]
        [SerializeField, Min(0f)] private float jumpDistanceCost = 0.08f;

        [Header("Ground")]
        [Tooltip("Layers it can stand on. Walls on these layers also stop it.")]
        [SerializeField] private LayerMask groundLayers = 1 << 3;

        [Tooltip("It only tumbles onto floor within this height of the floor it is on, so it stays on its own platform.")]
        [SerializeField, Min(0.01f)] private float stepHeightTolerance = 0.15f;

        [Tooltip("It only tumbles or lands onto floor at least this flat (1 = perfectly flat). A triangle can't stand on slopes.")]
        [SerializeField, Range(0f, 1f)] private float minFloorFlatness = 0.97f;

        [Header("Pounce")]
        [Tooltip("If the player gets this close (horizontally), it leaps onto them.")]
        [SerializeField, Min(0f)] private float pounceRange = 3.5f;

        [Tooltip("Minimum time between pounces.")]
        [SerializeField, Min(0f)] private float pounceCooldown = 1.3f;

        [Tooltip("Wind-up before a pounce (its corner glows). The player's cue to move.")]
        [SerializeField, Min(0.05f)] private float pounceChargeTime = 0.24f;

        [Header("Getting Hit")]
        [Tooltip("If on, hits don't interrupt it once it has committed to an attack (laser wind-up or pounce charge): it only flashes. The player has to dodge instead of trading hits.")]
        [SerializeField] private bool armoredWhileAttacking = true;

        [Tooltip("If on, it can pounce again as soon as it recovers from a hit, instead of waiting for the pounce cooldown.")]
        [SerializeField] private bool retaliates = true;

        [Tooltip("Sideways speed when knocked back by the player's attack.")]
        [SerializeField, Min(0f)] private float hitKnockbackSpeed = 2.2f;

        [Tooltip("Upward speed when knocked back by the player's attack.")]
        [SerializeField, Min(0f)] private float hitKnockbackLift = 2f;

        [Tooltip("Minimum time it is stunned after a hit.")]
        [SerializeField, Min(0f)] private float hitStunTime = 0.18f;

        [Tooltip("How long it flashes white when hit.")]
        [SerializeField, Min(0f)] private float hitFlashTime = 0.1f;

        [Header("Feel")]
        [Tooltip("Short crouch before each tumble.")]
        [SerializeField, Min(0f)] private float anticipationTime = 0.07f;

        [SerializeField, Range(0f, 0.4f)] private float anticipationSquash = 0.1f;

        [Tooltip("Squash on landing each tumble.")]
        [SerializeField, Range(0f, 0.4f)] private float landSquash = 0.16f;

        [Tooltip("Squash on landing a jump.")]
        [SerializeField, Range(0f, 0.5f)] private float jumpLandSquash = 0.28f;

        [SerializeField, Min(0.1f)] private float squashRecovery = 16f;

        [Tooltip("Height of the surprised hop when it spots the player.")]
        [SerializeField, Min(0f)] private float alertHopHeight = 0.35f;

        [Tooltip("How far the pupil moves towards what it is looking at.")]
        [SerializeField, Min(0f)] private float lookDistance = 0.055f;

        [SerializeField, Min(0.5f)] private float minBlinkInterval = 2.5f;
        [SerializeField, Min(0.5f)] private float maxBlinkInterval = 5.5f;
        [SerializeField, Min(0.02f)] private float blinkTime = 0.12f;

        [Header("Vertex Glow")]
        [SerializeField] private Color glowColor = new Color(1f, 0.86f, 0.45f, 1f);

        [Tooltip("How strongly the glowing corner of the body itself lights up (the halo is separate).")]
        [SerializeField, Range(0f, 1f)] private float cornerGlowStrength = 0.85f;

        [Tooltip("How long the glow takes to fade after launching.")]
        [SerializeField, Min(0.05f)] private float glowFadeTime = 0.6f;

        [Header("Colours")]
        [SerializeField] private Color calmTop = new Color(0.8f, 0.5f, 0.46f, 1f);
        [SerializeField] private Color calmBottom = new Color(0.62f, 0.36f, 0.36f, 1f);
        [SerializeField] private Color angryTop = new Color(1f, 0.3f, 0.16f, 1f);
        [SerializeField] private Color angryBottom = new Color(0.78f, 0.12f, 0.1f, 1f);

        [Tooltip("How quickly the colour changes when it spots or loses the player.")]
        [SerializeField, Min(0.1f)] private float colorChangeSpeed = 8f;

        /// <summary>Raised when <see cref="State"/> changes. Arguments: previous state, new state.</summary>
        public event Action<TriangleState, TriangleState> StateChanged;

        /// <summary>Raised when it launches a vertex jump. Arguments: where it jumps from, where it plans to land (centres).</summary>
        public event Action<Vector2, Vector2> JumpLaunched;

        /// <summary>Raised when it lands a vertex jump.</summary>
        public event Action JumpLanded;

        /// <summary>Current behaviour.</summary>
        public TriangleState State { get; private set; } = TriangleState.Patrol;

        /// <summary>The player, once found.</summary>
        public Transform Target { get; private set; }

        /// <summary>True while standing on the ground.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>
        /// True while chasing, standing still at the preferred distance, with a clear view of the player.
        /// This is where the ranged attack will fire from.
        /// </summary>
        public bool IsInAttackPosition =>
            State == TriangleState.Chase && motion == Motion.Idle && IsGrounded && holding && canSeeTarget;

        /// <summary>-1 = left, +1 = right: the way it last moved or is looking.</summary>
        public int FacingDirection { get; private set; } = 1;

        /// <summary>True while an attack holds it in place (see <see cref="BeginAttack"/>).</summary>
        public bool IsAttackLocked { get; private set; }

        /// <summary>True if it can see the player right now.</summary>
        public bool CanSeeTarget => canSeeTarget;

        /// <summary>
        /// If true it always knows where the circle is (no detection range) and never patrols: it goes straight
        /// for it. It still needs a clear line of sight to attack. Used for minions a boss summons.
        /// </summary>
        public bool AlwaysHunt { get; set; }

        /// <summary>
        /// Speed of its decisions and movement (1 = normal). An <see cref="Overseer"/> drives its triangles
        /// above 1; below 1 it moves sluggishly (faltering).
        /// </summary>
        public float Haste { get; set; } = 1f;

        /// <summary>Its kind (rank): scalene, isosceles or equilateral. Set before Awake (in the Inspector).</summary>
        public TriangleKind Kind => kind;

        /// <summary>Overseer haste times its rank's pace: the more perfect the triangle, the faster it acts.</summary>
        public float EffectiveHaste => Haste * TriangleKinds.Haste(kind);

        /// <summary>The seed its individual look comes from (see <see cref="TriangleKinds.LookFor"/>).</summary>
        public int LookSeed { get { EnsureLook(); return resolvedSeed; } }

        /// <summary>How far its palette (body and abilities) is turned round the colour wheel.</summary>
        public float HueShift { get { EnsureLook(); return look.HueShift; } }

        private TriangleKinds.Look look;
        private bool lookReady;
        private int resolvedSeed;

        /// <summary>Works out its look once (from the seed, or from where it was placed).</summary>
        private void EnsureLook()
        {
            if (lookReady) return;
            lookReady = true;
            resolvedSeed = lookSeed != 0 ? lookSeed : TriangleKinds.SeedFrom(transform.position);
            look = TriangleKinds.LookFor(kind, resolvedSeed);
        }

        /// <summary>
        /// Horizontal distance it closes to before standing its ground. Settable at runtime, e.g. so the pieces
        /// of a <see cref="Splitter"/> stand at different distances instead of stacking on one spot.
        /// </summary>
        public float PreferredDistance
        {
            get => preferredDistance;
            set => preferredDistance = Mathf.Max(0f, value);
        }

        /// <summary>True if hits shouldn't interrupt an attack it has committed to.</summary>
        public bool ArmoredWhileAttacking => armoredWhileAttacking;

        /// <summary>The shape, for attacks that fire from (or light up) a corner.</summary>
        public RoundedTriangleMesh Shape => shape;

        /// <summary>The rotating visual. Corner positions are in its local space.</summary>
        public Transform Visual => visual;

        /// <summary>
        /// Called by an attack component when it starts: the triangle stops moving and making decisions until
        /// <see cref="EndAttack"/>. Only allowed while it is standing still.
        /// </summary>
        public bool BeginAttack()
        {
            if (IsAttackLocked || motion != Motion.Idle || !IsGrounded) return false;
            IsAttackLocked = true;
            holding = false;
            return true;
        }

        /// <summary>
        /// While true, an attack in progress drives the Rigidbody itself (e.g. a leaping slam), so the
        /// triangle doesn't hold itself in place. Cleared by <see cref="EndAttack"/>.
        /// </summary>
        public bool AttackOwnsVelocity { get; set; }

        /// <summary>Releases the triangle after an attack and returns its pose to normal.</summary>
        public void EndAttack()
        {
            IsAttackLocked = false;
            AttackOwnsVelocity = false;
            poseOverrideActive = false;
            restTimer = 0.2f;
        }

        /// <summary>Lets an attack drive the triangle's pose: offset of its centre and rotation in degrees.</summary>
        public void SetAttackPose(Vector2 offset, float angle)
        {
            poseOverrideActive = true;
            poseOverrideOffset = offset;
            poseOverrideAngle = angle;
        }

        private Rigidbody2D body;
        private PolygonCollider2D polygon;
        private int spawnOrder;
        private ContactFilter2D groundFilter;
        private readonly List<RaycastHit2D> castHits = new List<RaycastHit2D>(8);
        private readonly List<RaycastHit2D> floorHits = new List<RaycastHit2D>(8);
        private readonly List<Vector2> outlinePoints = new List<Vector2>();

        // Geometry (from the shape)
        private float circumradius;
        private float inradius;          // height of the centre above the floor at rest
        private float pivotDistance;     // centre → centre of a rounded corner
        private float stepDistance;      // how far one tumble moves it
        private float halfBase;          // centre → each bottom rounded corner, horizontally
        private float colliderHalfWidth; // half the width of the resting triangle

        // Home & patrol
        private float homeX;
        private float homeFloorY;
        private bool homeFloorKnown;
        private int patrolDirection = 1;

        // Current motion
        private Motion motion = Motion.Idle;
        private int moveDirection = 1;
        private float motionStartTime;
        private float clock; // its own time, running at Haste speed (see FixedUpdate)
        private float stepStartX;
        private float stepDuration;
        private float restTimer;

        // Vertex jump
        private Vector2 jumpVelocity;
        private Vector2 jumpTarget;
        private float flightTime;
        private float spinTarget;
        private float launchAngle;
        private Vector2 launchOffset;
        private float lastJumpTime = float.NegativeInfinity;
        private float nextPlanTime;
        private int glowCorner = CornerBottomLeft;
        private float lastLaunchTime = float.NegativeInfinity; // last boosted launch (drives the glow fade)
        private bool jumpIsBoosted;

        // Senses
        private bool canSeeTarget;
        private float lastSeenTime = float.NegativeInfinity;
        private Vector2 lastKnownTargetPosition;
        private float targetFloorY;
        private bool targetFloorKnown;
        private bool holding;
        private float stateStartTime;

        // Combat
        private EnemyHealth health;
        private float hitFlashUntil = float.NegativeInfinity;
        private float lastPounceTime = float.NegativeInfinity;
        private float chargeTimeOverride = -1f;
        private int stunDirection = 1;

        // Visual state
        private float squash;
        private float angerBlend;
        private float appliedAngerBlend = -1f;
        private bool cornerGlowActive;
        private Vector2 lookDirection = Vector2.right;
        private float nextBlinkTime;
        private float blinkStartTime = float.NegativeInfinity;
        private Vector3 eyeBaseScale = Vector3.one;

        // Attacks
        private bool poseOverrideActive;
        private Vector2 poseOverrideOffset;
        private float poseOverrideAngle;

        private float FloorY => body.position.y - inradius;
        private float Gravity => Mathf.Abs(Physics2D.gravity.y) * body.gravityScale;

        #region Unity lifecycle

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            polygon = GetComponent<PolygonCollider2D>();
            health = GetComponent<EnemyHealth>();
            spawnOrder = ++spawnCounter;

            groundFilter = new ContactFilter2D { useLayerMask = true, layerMask = groundLayers, useTriggers = false };

            // A piece of a split triangle takes its parent's kind and look.
            if (TriangleKinds.TakeInheritance(out TriangleKind inheritedKind, out int inheritedSeed))
            {
                kind = inheritedKind;
                lookSeed = inheritedSeed;
            }
            EnsureLook();

            CacheGeometry();
            TriangleKinds.ApplyFrame(visual, look.Width, look.Lean); // its own proportions (none for equilateral)
            FitColliderToShape();

            // Its own colour: the whole palette turned round the colour wheel (abilities follow in their Start).
            if (!Mathf.Approximately(look.HueShift, 0f))
            {
                calmTop = TriangleKinds.Tint(calmTop, look.HueShift);
                calmBottom = TriangleKinds.Tint(calmBottom, look.HueShift);
                angryTop = TriangleKinds.Tint(angryTop, look.HueShift);
                angryBottom = TriangleKinds.Tint(angryBottom, look.HueShift);
                glowColor = TriangleKinds.Tint(glowColor, look.HueShift);
                if (shape != null) shape.SetFill(calmTop, calmBottom);
            }

            homeX = transform.position.x;
            if (eye != null) eyeBaseScale = eye.localScale;
            nextBlinkTime = Time.time + UnityEngine.Random.Range(minBlinkInterval, maxBlinkInterval);
        }

        private void OnEnable()
        {
            Active.Add(this);
            if (health != null)
            {
                health.Hit += HandleHit;
                health.Died += HandleDied;
            }
        }

        private void OnDisable()
        {
            Active.Remove(this);
            if (health != null)
            {
                health.Hit -= HandleHit;
                health.Died -= HandleDied;
            }
        }

        private void Start()
        {
            PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
            Target = player != null ? player.transform : null;
        }

        private void FixedUpdate()
        {
            // Its own clock runs at Haste speed (an Overseer speeds it up, faltering slows it): every tumble,
            // charge, rest and cooldown below is timed on it. Physics still steps in real time.
            float dt = Time.fixedDeltaTime * EffectiveHaste;
            clock += dt;

            IsGrounded = ProbeGround();
            if (IsGrounded && !homeFloorKnown)
            {
                homeFloorY = FloorY;
                homeFloorKnown = true;
            }

            UpdateSenses();

            switch (motion)
            {
                case Motion.Tumbling:
                    FollowTumble(dt);
                    return;

                case Motion.Charging:
                    body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
                    if (clock - motionStartTime >= ChargeDuration) Launch();
                    return;

                case Motion.Airborne:
                    // Landed once it is back on the ground and no longer rising.
                    if (clock - motionStartTime > 0.1f && IsGrounded && body.linearVelocity.y <= 0.01f) Land();
                    return;

                case Motion.Stunned:
                    // Knocked back by a hit: recover once the stun has passed and it is back on the ground.
                    if (clock - motionStartTime >= hitStunTime && IsGrounded && body.linearVelocity.y <= 0.01f)
                    {
                        motion = Motion.Idle;
                        body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
                        squash = landSquash;
                        restTimer = 0.1f;
                    }
                    return;
            }

            // Falling (pushed off something, or placed in the air): wait until it lands.
            if (!IsGrounded) return;

            // An attack is in progress: stand still and let it finish (unless the attack is moving it itself).
            if (IsAttackLocked)
            {
                if (!AttackOwnsVelocity) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
                return;
            }

            // Decisions are only made while standing still, so every tumble and jump completes cleanly.
            UpdateState();
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);

            restTimer -= dt;
            if (restTimer <= 0f)
            {
                ChooseNextAction();
            }
        }

        private void LateUpdate()
        {
            UpdateVisuals(Time.deltaTime);
        }

        #endregion

        #region Senses & state

        private void UpdateSenses()
        {
            canSeeTarget = false;
            if (Target == null) return;

            bool hunting = State == TriangleState.Chase || State == TriangleState.Alert;
            float radius = (hunting ? loseRadius : detectionRadius) * TriangleKinds.Sight(kind);

            Vector2 from = EyePosition();
            Vector2 to = Target.position;

            // Summoned hunters always know where the circle is (so they keep chasing, any distance)...
            if (AlwaysHunt)
            {
                lastSeenTime = Time.time;
                lastKnownTargetPosition = to;
            }
            else if ((to - from).sqrMagnitude > radius * radius)
            {
                return;
            }

            // ...but only "see" it (and so attack) with a clear line of sight: no shooting through platforms.
            if (requireLineOfSight && Physics2D.Linecast(from, to, sightBlockers).collider != null) return;

            canSeeTarget = true;
            lastSeenTime = Time.time;
            lastKnownTargetPosition = to;

            // Remember which floor the player is standing on (not updated mid-jump, so a hop doesn't confuse it).
            RaycastHit2D floor = Physics2D.Raycast(to, Vector2.down, 2.5f, groundLayers);
            if (floor.collider != null && floor.normal.y > 0.7f)
            {
                targetFloorY = floor.point.y;
                targetFloorKnown = true;
            }
        }

        private void UpdateState()
        {
            // Summoned to fight: skip patrolling and the alert pause, go straight for the circle.
            if (AlwaysHunt && State != TriangleState.Chase)
            {
                SetState(TriangleState.Chase);
                return;
            }

            switch (State)
            {
                case TriangleState.Patrol:
                    if (canSeeTarget) SetState(TriangleState.Alert);
                    break;

                case TriangleState.Return:
                    if (canSeeTarget)
                    {
                        SetState(TriangleState.Alert);
                    }
                    else if (OnFloor(homeFloorY) && Mathf.Abs(homeX - body.position.x) <= stepDistance * 0.6f)
                    {
                        SetState(TriangleState.Patrol);
                    }
                    break;

                case TriangleState.Alert:
                    if (clock - stateStartTime >= alertTime)
                    {
                        SetState(canSeeTarget ? TriangleState.Chase : TriangleState.Return);
                    }
                    break;

                case TriangleState.Chase:
                    if (Time.time - lastSeenTime > memoryTime) SetState(TriangleState.Return);
                    break;
            }
        }

        private void SetState(TriangleState next)
        {
            if (next == State) return;

            TriangleState previous = State;
            State = next;
            stateStartTime = clock;
            holding = false;
            restTimer = 0f;

            if (next == TriangleState.Alert && Target != null)
            {
                FacingDirection = Target.position.x >= body.position.x ? 1 : -1;
            }

            StateChanged?.Invoke(previous, next);
        }

        /// <summary>Decides what to do next: tumble, jump, or stay put.</summary>
        private void ChooseNextAction()
        {
            holding = false;

            switch (State)
            {
                case TriangleState.Patrol: ChoosePatrolAction(); break;
                case TriangleState.Alert: restTimer = 0.05f; break; // stand still while surprised
                case TriangleState.Chase: ChooseChaseAction(); break;
                case TriangleState.Return: ChooseReturnAction(); break;
            }
        }

        private void ChoosePatrolAction()
        {
            float x = body.position.x;
            float next = x + patrolDirection * stepDistance;
            bool pastEnd = patrolDirection > 0 ? next > homeX + patrolRight + 0.01f : next < homeX - patrolLeft - 0.01f;

            if (pastEnd || !CanStep(patrolDirection))
            {
                // End of the patrol (or something in the way): look around, then turn back.
                patrolDirection = -patrolDirection;
                FacingDirection = patrolDirection;
                restTimer = turnPause;
                return;
            }

            StartStep(patrolDirection, patrolStepTime);
            restTimer = patrolStepPause;
        }

        private void ChooseChaseAction()
        {
            float x = body.position.x;
            Vector2 target = TargetPosition();
            float dx = target.x - x;
            int towards = dx >= 0f ? 1 : -1;
            FacingDirection = towards; // always watching the player, even when backing away
            float distance = Mathf.Abs(dx);

            // Too close: leap onto the player.
            if (TryPounce(target, distance)) return;

            int direction = 0;
            if (distance > preferredDistance + distanceTolerance) direction = towards;
            else if (keepDistance && distance < preferredDistance - distanceTolerance) direction = -towards;
            else direction = SeparationDirection();

            bool canWalk = direction != 0 && WithinLeash(x + direction * stepDistance) && CanStep(direction);

            // Would a jump get it somewhere clearly better? Worth checking when it is on a different
            // floor from the player, can't see them, or can't walk where it wants to go.
            bool onTargetFloor = targetFloorKnown && OnFloor(targetFloorY);
            bool stuck = direction != 0 && !canWalk;
            if ((!onTargetFloor || !canSeeTarget || stuck) && TryPlanJump(ChaseScore)) return;

            if (canWalk)
            {
                StartStep(direction, chaseStepTime);
                restTimer = chaseStepPause;
                return;
            }

            // In range (or can't move): hold position. The ranged attack will fire from here.
            holding = true;
            restTimer = 0.05f;
        }

        private void ChooseReturnAction()
        {
            float x = body.position.x;
            int direction = homeX >= x ? 1 : -1;
            FacingDirection = direction;
            bool onHomeFloor = OnFloor(homeFloorY);

            // Off its own platform: jump back as soon as a good jump exists.
            if (!onHomeFloor && TryPlanJump(ReturnScore)) return;

            if (CanStep(direction))
            {
                StartStep(direction, patrolStepTime);
                restTimer = patrolStepPause;
                return;
            }

            if (!onHomeFloor && (clock < lastJumpTime + jumpCooldown || clock < nextPlanTime))
            {
                restTimer = 0.1f; // wait for the next chance to jump
                return;
            }

            // No way home from here: make this its new patrol spot.
            homeX = x;
            homeFloorY = FloorY;
            SetState(TriangleState.Patrol);
        }

        /// <summary>Where it believes the player is: their real position if visible, else where it last saw them.</summary>
        private Vector2 TargetPosition() => canSeeTarget ? (Vector2)Target.position : lastKnownTargetPosition;

        private bool WithinLeash(float x) => Mathf.Abs(x - homeX) <= leashDistance;

        private bool OnFloor(float floorY) => Mathf.Abs(FloorY - floorY) < SameFloorTolerance;

        /// <summary>Direction to step to get clear of another triangle standing on the same spot, or 0.</summary>
        private int SeparationDirection()
        {
            float x = body.position.x;
            for (int i = 0; i < Active.Count; i++)
            {
                TriangleEnemy other = Active[i];
                if (other == this || !other.IsGrounded || Mathf.Abs(other.FloorY - FloorY) > SameFloorTolerance) continue;

                float gap = x - other.body.position.x;
                if (Mathf.Abs(gap) >= separationDistance) continue;
                if (Mathf.Abs(gap) > 0.01f) return gap > 0f ? 1 : -1;
                return spawnOrder > other.spawnOrder ? 1 : -1; // exactly on top of each other: split by spawn order
            }

            return 0;
        }

        #endregion

        #region Vertex jump planning

        /// <summary>How good a spot is for attacking the player from. Lower is better.</summary>
        private float ChaseScore(float x, float floorY)
        {
            if (!WithinLeash(x)) return float.PositiveInfinity;

            Vector2 target = TargetPosition();

            // Too close is worse than too far for a cautious (keep-distance) triangle; an aggressive one doesn't mind.
            float gap = Mathf.Abs(target.x - x) - preferredDistance;
            float score = gap < 0f ? -gap * (keepDistance ? tooCloseWeight : 0.5f) : gap;
            if (targetFloorKnown) score += heightWeight * Mathf.Abs(floorY - targetFloorY);

            if (requireLineOfSight)
            {
                Vector2 eyeThere = new Vector2(x, floorY + inradius * 1.12f);
                if (Physics2D.Linecast(eyeThere, target, sightBlockers).collider != null) score += noSightPenalty;
            }

            return score;
        }

        /// <summary>How close a spot is to home. Lower is better; being on the home floor matters most.</summary>
        private float ReturnScore(float x, float floorY) =>
            Mathf.Abs(x - homeX) + 3f * heightWeight * Mathf.Abs(floorY - homeFloorY);

        /// <summary>
        /// Looks for a reachable landing spot that scores clearly better than where it stands, and if it finds
        /// one, starts charging a vertex jump to it. Candidate spots are flat floors on every platform within
        /// reach; each needs a clear arc that stays under the maximum jump height.
        /// </summary>
        private bool TryPlanJump(Func<float, float, float> score)
        {
            if (!canJump || clock < lastJumpTime + jumpCooldown || clock < nextPlanTime) return false;
            nextPlanTime = clock + PlanInterval;

            Vector2 start = body.position;
            float startFloor = FloorY;

            // A jump has to beat the best spot it could simply walk to.
            float best = BestWalkableScore(score) - requiredImprovement;
            bool found = false;
            Vector2 bestVelocity = Vector2.zero;
            Vector2 bestLanding = Vector2.zero;
            float bestTime = 0f;

            var floorFilter = new ContactFilter2D { useLayerMask = true, layerMask = groundLayers, useTriggers = false };

            for (int side = -1; side <= 1; side += 2)
            {
                for (float offset = 0.9f; offset <= maxJumpDistance + 0.001f; offset += 0.3f)
                {
                    float x = start.x + side * offset;

                    // Every floor surface below the top of its reach at this x.
                    Vector2 origin = new Vector2(x, startFloor + maxJumpHeight);
                    int hits = Physics2D.Raycast(origin, Vector2.down, floorFilter, floorHits, maxJumpHeight + maxDropHeight);

                    for (int i = 0; i < hits; i++)
                    {
                        RaycastHit2D hit = floorHits[i];
                        if (hit.distance <= 0f || hit.normal.y < minFloorFlatness) continue;

                        float floorY = hit.point.y;
                        float s = score(x, floorY) + jumpDistanceCost * offset;
                        if (s >= best) continue;
                        if (!LandingFits(x, floorY)) continue;

                        Vector2 landing = new Vector2(x, floorY + inradius);
                        if (!SolveArc(start, landing, out Vector2 velocity, out float time)) continue;

                        best = s;
                        found = true;
                        bestVelocity = velocity;
                        bestLanding = landing;
                        bestTime = time;
                    }
                }
            }

            if (!found) return false;

            // Dropping down to a lower floor should never need vertex power. If the arc is big (usually
            // because it would have to sail over its own platform's edge), tumble towards the edge first and
            // plan again from there, so it ends up hopping off the edge instead.
            int towardsLanding = bestLanding.x >= start.x ? 1 : -1;
            float rise = bestVelocity.y * bestVelocity.y / (2f * Gravity);
            if (bestLanding.y < start.y - 0.5f && rise > boostHeight)
            {
                if (CanStep(towardsLanding))
                {
                    StartStep(towardsLanding, State == TriangleState.Chase ? chaseStepTime : patrolStepTime);
                    restTimer = State == TriangleState.Chase ? chaseStepPause : patrolStepPause;
                    nextPlanTime = 0f; // plan again as soon as this step lands
                    return true;
                }

                // Already at the edge: a small hop off it and let gravity do the rest.
                if (TryHopOffEdge(towardsLanding)) return true;
            }

            BeginCharge(bestVelocity, bestLanding, bestTime);
            return true;
        }

        /// <summary>
        /// A small hop off the edge of the current platform, landing on whatever flat floor is below.
        /// Used for getting down, where a full vertex-power arc would look silly.
        /// </summary>
        private bool TryHopOffEdge(int direction)
        {
            float g = Gravity;
            Vector2 start = body.position;

            // How far it is to the edge, so the hop carries the whole triangle past it before it comes
            // back down to this height (otherwise it lands on the corner of its own platform).
            float toEdge = 0f;
            while (toEdge < 2.5f && IsFlatFloorAt(start.x + direction * (toEdge + 0.05f), FloorY)) toEdge += 0.05f;
            float clearDistance = toEdge + colliderHalfWidth + 0.25f;
            float airTimeAtStartHeight = 2f * hopOffLift / g;
            float speed = Mathf.Clamp(clearDistance / Mathf.Max(airTimeAtStartHeight, 0.05f), hopOffSpeed, maxJumpSpeedX);
            Vector2 velocity = new Vector2(direction * speed, hopOffLift);

            // Find where the floor below the hop is, and when it gets there.
            RaycastHit2D below = Physics2D.Raycast(start + new Vector2(direction * (colliderHalfWidth * 2f + 0.3f), 0f),
                                                   Vector2.down, maxDropHeight + inradius, groundLayers);
            if (below.collider == null || below.normal.y < minFloorFlatness) return false;

            float drop = start.y - (below.point.y + inradius);
            float time = (velocity.y + Mathf.Sqrt(velocity.y * velocity.y + 2f * g * Mathf.Max(drop, 0f))) / g;
            float landingX = start.x + velocity.x * time;
            if (!LandingFits(landingX, below.point.y)) return false;

            // Nothing in the way beside the edge (it may graze its own platform's corner as it leaves, which is fine).
            Vector2 besideEdge = start + new Vector2(direction * (colliderHalfWidth * 2f + 0.3f), circumradius * 0.3f);
            if (Physics2D.OverlapBox(besideEdge, new Vector2(colliderHalfWidth * 2f, circumradius), 0f, groundLayers) != null) return false;

            BeginCharge(velocity, new Vector2(landingX, below.point.y + inradius), time, plainHop: true);
            return true;
        }

        /// <summary>
        /// Best score among spots it can reach by tumbling along its current floor (including where it stands),
        /// looking as far either way as it could jump.
        /// </summary>
        private float BestWalkableScore(Func<float, float, float> score)
        {
            float floorY = FloorY;
            float x = body.position.x;
            float best = score(x, floorY);
            int maxSteps = Mathf.CeilToInt(maxJumpDistance / stepDistance);

            for (int side = -1; side <= 1; side += 2)
            {
                float at = x;
                for (int step = 0; step < maxSteps; step++)
                {
                    at += side * stepDistance;
                    if (!LandingFits(at, floorY)) break; // ledge or wall: can't walk further this way
                    best = Mathf.Min(best, score(at, floorY));
                }
            }

            return best;
        }

        /// <summary>True if the triangle fits standing at this spot: flat floor under its whole base and room above.</summary>
        private bool LandingFits(float x, float floorY)
        {
            if (!IsFlatFloorAt(x - halfBase, floorY) || !IsFlatFloorAt(x, floorY) || !IsFlatFloorAt(x + halfBase, floorY)) return false;

            float height = circumradius * 1.5f;
            Vector2 centre = new Vector2(x, floorY + 0.05f + height * 0.5f);
            Vector2 size = new Vector2(halfBase * 2f, height * 0.9f);
            return Physics2D.OverlapBox(centre, size, 0f, groundLayers) == null;
        }

        /// <summary>
        /// Works out the launch velocity for an arc from <paramref name="start"/> to <paramref name="landing"/>
        /// that peaks a little above the higher of the two, and checks the triangle fits along the whole arc.
        /// Tries progressively higher arcs (up to its maximum jump height) if something is in the way,
        /// so it can leap over walls and columns.
        /// </summary>
        private bool SolveArc(Vector2 start, Vector2 landing, out Vector2 velocity, out float time)
        {
            velocity = Vector2.zero;
            time = 0f;
            float g = Gravity;
            float top = Mathf.Max(start.y, landing.y);

            for (float apex = top + jumpClearance; apex - start.y <= maxJumpHeight; apex += 0.5f)
            {
                float rise = apex - start.y;
                float vy = Mathf.Sqrt(2f * g * rise);
                float t = vy / g + Mathf.Sqrt(2f * (apex - landing.y) / g);
                float vx = (landing.x - start.x) / t;
                if (Mathf.Abs(vx) > maxJumpSpeedX) continue;

                if (ArcIsClear(start, vx, vy, t, g))
                {
                    velocity = new Vector2(vx, vy);
                    time = t;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks the triangle's collider fits along the arc. The collider keeps its resting pose in the air
        /// (only the visual spins), so a box around the resting triangle is tested, starting a little above
        /// its base so the start and landing floors don't count as hits.
        /// </summary>
        private bool ArcIsClear(Vector2 start, float vx, float vy, float t, float g)
        {
            const int samples = 40;
            const float floorGap = 0.12f;
            const float margin = 0.12f; // keeps it from clipping corners between samples

            Vector2 size = new Vector2((colliderHalfWidth + margin) * 2f, circumradius + inradius - floorGap);
            Vector2 centreOffset = new Vector2(0f, (circumradius - inradius + floorGap) * 0.5f);

            for (int i = 1; i < samples; i++)
            {
                float s = t * i / samples;
                Vector2 point = start + new Vector2(vx * s, vy * s - 0.5f * g * s * s);
                if (Physics2D.OverlapBox(point + centreOffset, size, 0f, groundLayers) != null) return false;
            }

            return true;
        }

        /// <summary>
        /// Pounce: when the player is close, charge a glowing corner and leap to land right on them
        /// (its contact damage does the hurting). Only onto the floor the player is standing on.
        /// </summary>
        private bool TryPounce(Vector2 target, float distance)
        {
            if (!canJump || distance > pounceRange || !canSeeTarget || !targetFloorKnown) return false;
            if (clock < lastPounceTime + pounceCooldown || clock < lastJumpTime + jumpCooldown * 0.5f) return false;

            // The player must actually be standing on (or just above) that floor, not mid-jump.
            if (target.y - targetFloorY > circumradius * 1.5f) return false;

            Vector2 start = body.position;
            foreach (float nudge in new[] { 0f, -0.3f, 0.3f })
            {
                float x = target.x + nudge;
                if (!WithinLeash(x) || !LandingFits(x, targetFloorY)) continue;

                Vector2 landing = new Vector2(x, targetFloorY + inradius);
                if (!SolveArc(start, landing, out Vector2 velocity, out float time)) continue;

                lastPounceTime = clock;
                BeginCharge(velocity, landing, time, forceBoost: true, chargeTime: pounceChargeTime);
                return true;
            }

            return false;
        }

        /// <param name="plainHop">True to always use a plain hop (no vertex power), e.g. hopping off a ledge.</param>
        /// <param name="forceBoost">True to always use vertex power (glowing charge), e.g. a pounce.</param>
        /// <param name="chargeTime">Wind-up length override; negative = the normal one for the jump type.</param>
        private void BeginCharge(Vector2 velocity, Vector2 landing, float time, bool plainHop = false,
                                 bool forceBoost = false, float chargeTime = -1f)
        {
            motion = Motion.Charging;
            chargeTimeOverride = chargeTime;
            motionStartTime = clock;
            jumpVelocity = velocity;
            jumpTarget = landing;
            flightTime = time;
            moveDirection = Mathf.Abs(velocity.x) > 0.05f ? (velocity.x > 0f ? 1 : -1) : FacingDirection;
            if (State != TriangleState.Chase) FacingDirection = moveDirection;

            // Only big jumps need vertex power; small ones are a plain crouch-and-spring.
            float rise = velocity.y * velocity.y / (2f * Gravity);
            jumpIsBoosted = forceBoost || (!plainHop && (rise > boostHeight || Mathf.Abs(landing.x - body.position.x) > boostDistance));

            // For a boosted jump it rocks back onto its rear corner and draws power from it.
            glowCorner = moveDirection > 0 ? CornerBottomLeft : CornerBottomRight;
            if (vertexGlow != null && shape != null)
            {
                vertexGlow.transform.localPosition = shape.GetCornerPoint(glowCorner, shape.CornerRadius * 0.8f);
                vertexGlow.Color = glowColor;
            }

            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        }

        private void Launch()
        {
            // Remember the charge pose so the visual can blend smoothly out of it into the spin.
            var pose = jumpIsBoosted ? GetChargePose(1f) : (offset: Vector2.zero, angle: 0f);
            launchAngle = pose.angle;
            launchOffset = pose.offset;

            motion = Motion.Airborne;
            motionStartTime = clock;
            lastJumpTime = clock;
            if (jumpIsBoosted) lastLaunchTime = clock;

            // Spin a whole number of thirds so it lands flat on a side.
            int turns = Mathf.Max(1, Mathf.RoundToInt(flightTime / 0.45f));
            spinTarget = -moveDirection * TumbleAngle * turns;

            // Unity's physics applies gravity in fixed steps, which makes a real jump fall a little lower than the
            // ideal arc it was planned on (about g·dt·t/2). Half a step of extra upward speed cancels that exactly.
            body.linearVelocity = jumpVelocity + new Vector2(0f, Gravity * Time.fixedDeltaTime * 0.5f);
            if (jumpIsBoosted && vertexGlow != null) vertexGlow.Burst();

            JumpLaunched?.Invoke(body.position, jumpTarget);
        }

        /// <summary>Wind-up before launching: a long glowing charge for boosted jumps, a quick crouch for hops.</summary>
        private float ChargeDuration =>
            chargeTimeOverride > 0f ? chargeTimeOverride : (jumpIsBoosted ? jumpChargeTime : hopCrouchTime);

        private void Land()
        {
            motion = Motion.Idle;
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            squash = jumpLandSquash;
            restTimer = 0.25f;
            JumpLanded?.Invoke();
        }

        #endregion

        #region Tumbling

        /// <summary>True if there is floor to land on and no wall in the way one tumble in this direction.</summary>
        private bool CanStep(int direction)
        {
            // Wall check: sweep the collider sideways by one tumble.
            int hits = polygon.Cast(new Vector2(direction, 0f), groundFilter, castHits, stepDistance + 0.02f);
            for (int i = 0; i < hits; i++)
            {
                if (Mathf.Abs(castHits[i].normal.x) > 0.7f) return false;
            }

            // Floor check: the whole base must land on flat floor at the same height as now
            // (no tumbling off ledges, onto slopes, or down/up onto other platforms).
            float landingX = body.position.x + direction * stepDistance;
            float floorY = FloorY;
            return IsFlatFloorAt(landingX - halfBase, floorY) && IsFlatFloorAt(landingX, floorY) && IsFlatFloorAt(landingX + halfBase, floorY);
        }

        /// <summary>True if there is flat floor at this x, level with <paramref name="floorY"/>.</summary>
        private bool IsFlatFloorAt(float x, float floorY)
        {
            Vector2 origin = new Vector2(x, floorY + stepHeightTolerance + 0.05f);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 2f * stepHeightTolerance + 0.05f, groundLayers);
            return hit.collider != null && hit.distance > 0f && hit.normal.y >= minFloorFlatness;
        }

        private bool ProbeGround()
        {
            int hits = polygon.Cast(Vector2.down, groundFilter, castHits, 0.06f);
            for (int i = 0; i < hits; i++)
            {
                if (castHits[i].normal.y > 0.7f) return true;
            }

            return false;
        }

        private void StartStep(int direction, float duration)
        {
            motion = Motion.Tumbling;
            moveDirection = direction;
            stepDuration = duration;
            motionStartTime = clock;
            stepStartX = body.position.x;
            if (State != TriangleState.Chase) FacingDirection = direction;
        }

        private void FollowTumble(float dt)
        {
            // Move the body so its centre follows the tumbling triangle exactly.
            float targetX = stepStartX + GetTumble(clock).offset.x;
            // Physics moves the body over one real step, so divide by the real step (not the hasted dt).
            body.linearVelocity = new Vector2((targetX - body.position.x) / Time.fixedDeltaTime, body.linearVelocity.y);

            if (clock - motionStartTime >= anticipationTime + stepDuration)
            {
                motion = Motion.Idle;
                body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
                squash = landSquash;
            }
        }

        /// <summary>
        /// Where the triangle's centre is (relative to where the tumble started) and how far it has turned.
        /// It pivots around the centre of its front-bottom rounded corner, which keeps that corner on the floor.
        /// </summary>
        private (Vector2 offset, float angle, float anticipation) GetTumble(float time)
        {
            float t = time - motionStartTime;
            if (t < anticipationTime)
            {
                return (Vector2.zero, 0f, Mathf.Sin(Mathf.PI * t / anticipationTime)); // crouch before tipping
            }

            float p = Mathf.Clamp01((t - anticipationTime) / stepDuration);
            float angle = -moveDirection * TumbleAngle * TumbleCurve(p);
            Vector2 pivot = new Vector2(moveDirection * Cos30, -0.5f) * pivotDistance;
            return (pivot + Rotate(-pivot, angle), angle, 0f);
        }

        /// <summary>
        /// Charging pose: rocked back onto its rear corner. Pivots around that corner so it stays on the floor.
        /// </summary>
        private (Vector2 offset, float angle) GetChargePose(float progress)
        {
            float eased = 1f - (1f - progress) * (1f - progress);
            float angle = moveDirection * chargeTiltAngle * eased;
            Vector2 pivot = new Vector2(-moveDirection * Cos30, -0.5f) * pivotDistance;
            return (pivot + Rotate(-pivot, angle), angle);
        }

        /// <summary>Slows as it tips up onto its corner, then falls faster onto its next side, so landings have weight.</summary>
        private static float TumbleCurve(float p)
        {
            if (p < 0.5f)
            {
                float u = 1f - 2f * p;
                return 0.5f * (1f - u * u);
            }

            float v = 2f * p - 1f;
            return 0.5f + 0.5f * v * v;
        }

        #endregion

        #region Visuals

        private void UpdateVisuals(float dt)
        {
            if (visual == null) return;

            Vector2 offset = Vector2.zero;
            float angle = 0f;
            float crouch = 0f;
            float glow = 0f;
            float now = clock + (Time.time - Time.fixedTime) * EffectiveHaste; // the body clock, between physics steps

            switch (motion)
            {
                case Motion.Tumbling:
                {
                    var tumble = GetTumble(now);
                    offset = tumble.offset;
                    offset.x = stepStartX + offset.x - transform.position.x; // exact, independent of interpolation
                    angle = tumble.angle;
                    crouch = tumble.anticipation;
                    break;
                }

                case Motion.Charging:
                {
                    float p = Mathf.Clamp01((now - motionStartTime) / ChargeDuration);
                    if (jumpIsBoosted)
                    {
                        var pose = GetChargePose(p);
                        offset = pose.offset;
                        angle = pose.angle;
                        // Power builds up, flickering as it nears release.
                        glow = p * (0.85f + 0.15f * Mathf.Sin(now * 45f)) + p * p * 0.25f;
                    }
                    else
                    {
                        crouch = p; // plain hop: just squash down, then spring
                    }
                    break;
                }

                case Motion.Airborne:
                {
                    float p = Mathf.Clamp01((now - motionStartTime) / Mathf.Max(flightTime, 0.01f));
                    angle = Mathf.Lerp(launchAngle, spinTarget, p * p * (3f - 2f * p));
                    offset = Vector2.Lerp(launchOffset, Vector2.zero, Mathf.Clamp01((now - motionStartTime) / 0.12f));
                    break;
                }

                case Motion.Stunned:
                {
                    // Reels back from the hit, tilting away from it, then straightens up.
                    float p = Mathf.Clamp01((now - motionStartTime) / Mathf.Max(hitStunTime, 0.01f));
                    angle = -stunDirection * 25f * (1f - p) * (1f - p);
                    break;
                }
            }

            // After launching, the glow flares and then fades.
            float sinceLaunch = now - lastLaunchTime;
            if (motion != Motion.Charging && sinceLaunch < glowFadeTime)
            {
                float fade = 1f - sinceLaunch / glowFadeTime;
                glow = 1.4f * fade * fade;
            }

            // Surprised hop when it spots the player.
            float hop = 0f;
            float hopProgress = Mathf.Clamp01((now - stateStartTime) / (alertTime * 0.7f));
            if (State == TriangleState.Alert && motion == Motion.Idle && alertTime > 0f)
            {
                hop = alertHopHeight * 4f * hopProgress * (1f - hopProgress);
            }

            // Squash & stretch (only while upright, so the shape stays rigid while turning).
            squash = Mathf.Lerp(squash, 0f, 1f - Mathf.Exp(-squashRecovery * dt));
            float s = Mathf.Max(squash, crouch * anticipationSquash);
            if (hop > 0f) s = -0.08f * Mathf.Sin(Mathf.PI * hopProgress);
            if (motion == Motion.Airborne || (motion == Motion.Charging && jumpIsBoosted)) s = 0f;
            if (motion == Motion.Charging && !jumpIsBoosted) s = crouch * hopCrouchSquash;

            // An attack can take over the pose (e.g. lifting up and turning a corner towards the player).
            if (poseOverrideActive)
            {
                offset = poseOverrideOffset;
                angle = poseOverrideAngle;
                hop = 0f;
                s = 0f;
            }

            // Scale about the bottom edge so the triangle stays on the floor while squashing.
            visual.localScale = new Vector3(1f + s, 1f - s, 1f);
            visual.localPosition = new Vector3(offset.x, offset.y + hop - s * inradius, 0f);
            visual.localRotation = Quaternion.Euler(0f, 0f, angle);

            UpdateGlow(glow);
            UpdateEye(visual.localPosition, s);
            UpdateColour(dt);
        }

        private void UpdateGlow(float glow)
        {
            if (vertexGlow != null) vertexGlow.Intensity = glow;
            if (shape == null) return;

            if (glow > 0.01f)
            {
                shape.SetCornerGlow(glowCorner, Mathf.Min(glow, 1f) * cornerGlowStrength, glowColor);
                cornerGlowActive = true;
            }
            else if (cornerGlowActive)
            {
                shape.SetCornerGlow(-1, 0f, glowColor);
                cornerGlowActive = false;
            }
        }

        private void UpdateEye(Vector3 centre, float s)
        {
            if (eye == null) return;

            // The eye rides the centre of the triangle (which every rotation turns around), so it never spins.
            // (Through the kind frame, if any, so it stays on the reshaped body.)
            Vector3 c = visual.parent != transform ? transform.InverseTransformPoint(visual.parent.TransformPoint(centre)) : centre;
            eye.localPosition = new Vector3(c.x, c.y, centre.z) + new Vector3(0f, inradius * 0.12f, 0f);

            // Blink now and then.
            float blink = 1f;
            float sinceBlink = Time.time - blinkStartTime;
            if (sinceBlink < blinkTime)
            {
                blink = Mathf.Lerp(0.08f, 1f, Mathf.Abs(sinceBlink / blinkTime * 2f - 1f));
            }
            else if (Time.time >= nextBlinkTime)
            {
                blinkStartTime = Time.time;
                nextBlinkTime = Time.time + UnityEngine.Random.Range(minBlinkInterval, Mathf.Max(minBlinkInterval, maxBlinkInterval));
            }

            // Narrow the eye while hunting; widen it while charging a jump.
            float narrow = Mathf.Lerp(1f, 0.78f, angerBlend);
            if (motion == Motion.Charging && jumpIsBoosted) narrow = 1.1f;
            eye.localScale = new Vector3(eyeBaseScale.x * (1f + s * 0.5f), eyeBaseScale.y * (1f - s) * blink * narrow, eyeBaseScale.z);

            if (pupil == null) return;

            Vector2 wanted = LookTarget();
            lookDirection = Vector2.Lerp(lookDirection, wanted, 1f - Mathf.Exp(-12f * Time.deltaTime));
            Vector2 local = lookDirection * lookDistance;
            pupil.localPosition = new Vector3(local.x / Mathf.Max(eye.localScale.x, 1e-3f) * eyeBaseScale.x,
                                              local.y / Mathf.Max(eye.localScale.y, 1e-3f) * eyeBaseScale.y,
                                              pupil.localPosition.z);
        }

        /// <summary>Where the eye looks: the player while hunting, the landing spot while jumping, else ahead or around.</summary>
        private Vector2 LookTarget()
        {
            if (motion == Motion.Charging || motion == Motion.Airborne)
            {
                Vector2 toLanding = jumpTarget - (Vector2)eye.position;
                if (toLanding.sqrMagnitude > 1e-4f) return toLanding.normalized;
            }

            if (Target != null && (State == TriangleState.Chase || State == TriangleState.Alert))
            {
                Vector2 toTarget = TargetPosition() - (Vector2)eye.position;
                if (toTarget.sqrMagnitude > 1e-4f) return toTarget.normalized;
            }

            // Looking around at the end of a patrol.
            if (State == TriangleState.Patrol && motion == Motion.Idle && restTimer > 0.3f)
            {
                return new Vector2(Mathf.Sin(Time.time * 2.4f) > 0f ? 1f : -1f, -0.2f).normalized;
            }

            return new Vector2(FacingDirection, -0.35f).normalized;
        }

        /// <summary>
        /// Replaces its colours from now on (e.g. a different palette when enraged). Calm colours show while
        /// patrolling, angry ones while chasing; pass the same pair twice to show one look all the time.
        /// </summary>
        public void SetColours(Color newCalmTop, Color newCalmBottom, Color newAngryTop, Color newAngryBottom)
        {
            calmTop = newCalmTop;
            calmBottom = newCalmBottom;
            angryTop = newAngryTop;
            angryBottom = newAngryBottom;
            appliedAngerBlend = -1f; // reapply on the next frame
        }

        private void UpdateColour(float dt)
        {
            if (shape == null) return;

            bool angry = State == TriangleState.Alert || State == TriangleState.Chase;
            angerBlend = Mathf.MoveTowards(angerBlend, angry ? 1f : 0f, colorChangeSpeed * dt);

            // Flash white when hit.
            if (Time.time < hitFlashUntil)
            {
                shape.SetFill(Color.white, new Color(1f, 0.85f, 0.8f, 1f));
                appliedAngerBlend = -1f; // force the normal colours back afterwards
                return;
            }

            if (Mathf.Abs(angerBlend - appliedAngerBlend) < 0.01f) return;
            appliedAngerBlend = angerBlend;
            shape.SetFill(Color.Lerp(calmTop, angryTop, angerBlend), Color.Lerp(calmBottom, angryBottom, angerBlend));
        }

        #endregion

        #region Getting hit

        private void HandleHit(int damage, Vector2 source)
        {
            hitFlashUntil = Time.time + hitFlashTime;
            if (health != null && health.IsDead) return;

            // Being hit makes it hunt the attacker, even if it hadn't noticed them yet.
            if (Target != null)
            {
                lastSeenTime = Time.time;
                lastKnownTargetPosition = Target.position;
            }
            if (State != TriangleState.Chase) SetState(TriangleState.Chase);

            // Committed to an attack (laser, or the wind-up of a pounce): it shrugs the hit off.
            bool chargingPounce = motion == Motion.Charging && chargeTimeOverride > 0f;
            if (armoredWhileAttacking && (IsAttackLocked || chargingPounce)) return;

            // Hit back: free to pounce again the moment it recovers.
            if (retaliates) lastPounceTime = clock - pounceCooldown;

            // Whatever it was doing is interrupted.
            if (IsAttackLocked) EndAttack();
            poseOverrideActive = false;
            holding = false;

            // Knocked back away from the hit.
            stunDirection = body.position.x >= source.x ? 1 : -1;
            motion = Motion.Stunned;
            motionStartTime = clock;
            body.linearVelocity = new Vector2(stunDirection * hitKnockbackSpeed, hitKnockbackLift);
        }

        private void HandleDied()
        {
            Color top = Color.Lerp(calmTop, angryTop, angerBlend);
            Color bottom = Color.Lerp(calmBottom, angryBottom, angerBlend);
            Material material = shape != null ? shape.GetComponent<MeshRenderer>().sharedMaterial : null;
            Vector2 centre = visual != null ? (Vector2)visual.position : body.position;
            Vector2 source = Target != null ? (Vector2)Target.position : centre + Vector2.down;

            TriangleShatter.Spawn(centre, circumradius, top, bottom, new Color(0.16f, 0.08f, 0.1f, 1f), glowColor, material, source);
            Destroy(gameObject);
        }

        #endregion

        #region Geometry helpers

        private void CacheGeometry()
        {
            circumradius = shape != null ? shape.Circumradius : 0.8f;
            float cornerRadius = shape != null ? shape.CornerRadius : 0.13f;

            inradius = circumradius * 0.5f;
            pivotDistance = circumradius - 2f * cornerRadius;
            stepDistance = pivotDistance * Sqrt3;
            halfBase = pivotDistance * Cos30;
            colliderHalfWidth = halfBase + cornerRadius;
        }

        private Vector2 EyePosition() => body.position + new Vector2(0f, inradius * 0.12f);

        /// <summary>Shapes the collider to match the triangle at rest.</summary>
        [ContextMenu("Fit Collider To Shape")]
        public void FitColliderToShape()
        {
            if (shape == null) return;
            if (polygon == null) polygon = GetComponent<PolygonCollider2D>();

            RoundedTriangleMesh.BuildOutline(outlinePoints, shape.Circumradius, shape.CornerRadius, Mathf.Min(shape.SegmentsPerCorner, 3));
            // Its own proportions reshape the visual; the collider takes the same shape so hits match.
            EnsureLook();
            if (!Mathf.Approximately(look.Width, 1f) || !Mathf.Approximately(look.Lean, 0f))
            {
                for (int i = 0; i < outlinePoints.Count; i++) outlinePoints[i] = TriangleKinds.Map(outlinePoints[i], look.Width, look.Lean);
            }
            polygon.SetPath(0, outlinePoints);
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        #endregion

#if UNITY_EDITOR
        /// <summary>Shows the patrol range, jump reach, detection radius and preferred distance when selected.</summary>
        private void OnDrawGizmosSelected()
        {
            float centreX = Application.isPlaying ? homeX : transform.position.x;
            float r = shape != null ? shape.Circumradius : 0.8f;
            float floorY = transform.position.y - r * 0.5f - 0.05f;

            // Patrol range (green) with end markers.
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Vector3 left = new Vector3(centreX - patrolLeft, floorY);
            Vector3 right = new Vector3(centreX + patrolRight, floorY);
            Gizmos.DrawLine(left, right);
            Gizmos.DrawLine(left + Vector3.down * 0.15f, left + Vector3.up * 0.15f);
            Gizmos.DrawLine(right + Vector3.down * 0.15f, right + Vector3.up * 0.15f);

            // Leash (grey).
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            Gizmos.DrawLine(new Vector3(centreX - leashDistance, floorY - 0.12f), new Vector3(centreX + leashDistance, floorY - 0.12f));

            // Jump reach (gold box: how far sideways, how high up and how far down it will jump).
            if (canJump)
            {
                Gizmos.color = new Color(1f, 0.86f, 0.45f, 0.35f);
                Vector3 p = transform.position;
                Vector3 size = new Vector3(maxJumpDistance * 2f, maxJumpHeight + maxDropHeight, 0f);
                Gizmos.DrawWireCube(new Vector3(p.x, floorY + (maxJumpHeight - maxDropHeight) * 0.5f, 0f), size);
            }

            // Detection (yellow), lose (faint yellow) and preferred attack distance (red).
            UnityEditor.Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, detectionRadius);
            UnityEditor.Handles.color = new Color(1f, 0.85f, 0.2f, 0.3f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, loseRadius);
            UnityEditor.Handles.color = new Color(1f, 0.3f, 0.25f, 0.8f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, preferredDistance);
        }
#endif
    }
}
