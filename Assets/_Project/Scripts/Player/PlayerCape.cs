using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// A tattered cloak that hangs behind the circle.
    ///
    /// The cloak is one sheet of cloth stretched across a row of "strands". Each strand is a short chain
    /// of particles, and the cloth between them is shaded into soft pleats. Two things drive it:
    ///
    /// 1. A posed shape. At rest the strands fan out into a bell that shows on both sides of the circle.
    ///    When the player moves, the whole fan swings to trail behind the movement and closes up into a
    ///    streaming cape (back when running, up when falling, flat during the quickstep dash). A wave
    ///    travels down the strands so the cloth is never frozen.
    /// 2. Physics. Every particle has gravity, air drag and momentum, and is only pulled toward the pose:
    ///    firmly near the circle, loosely at the hem. That gives the cloth weight: the hem lags behind,
    ///    overshoots and settles, and reacts to jumps, landings and sudden stops.
    ///
    /// The strands collide with level geometry, so the hem rests on the floor and bends against walls.
    /// Purely cosmetic: it never affects the Rigidbody2D.
    ///
    /// The cape can also shape-shift. These are drawn on top of the simulated cloth, which keeps simulating
    /// underneath so it takes over again smoothly:
    /// - Blade: during an attack swing the sheet pulls together into a thin, pointed blade.
    /// - Platform: on an air jump it flies out and flattens under the circle, then zips back.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlayerCape : MonoBehaviour
    {
        private const float MaxSubStep = 1f / 120f;  // frames are split into simulation steps this size or smaller
        private const float ReferenceRate = 120f;    // stiffness values are authored "per step at 120 steps/second"
        private const int MaxStepsPerFrame = 8;
        private const float MaxFrameTime = 0.05f;    // a long hitch is simulated as at most this much time
        private const float TeleportDistance = 5f;   // anchor moved further than this in one frame → reset
        private const float TeleportSpeed = 80f;     // ...or faster than this (well above any real movement) → reset
        private const int SamplesPerSegment = 3;     // smoothness of the drawn curve between particles
        private const float FallbackGravity = 50f;   // used only if no PlayerMovement is assigned

        [Header("References")]
        [SerializeField] private PlayerMovement movement;

        [Tooltip("The cloak is attached relative to this transform. Use the squash pivot (Visual) so it follows squash, stretch and breathing but not the rolling.")]
        [SerializeField] private Transform attachSpace;

        [Tooltip("The circle's sprite. The cloak copies its sorting layer and is drawn relative to its order.")]
        [SerializeField] private SpriteRenderer bodyRenderer;

        [Tooltip("Order in Layer relative to the circle. -1 = directly behind the circle, in front of the level.")]
        [SerializeField] private int sortingOffset = -1;

        [Header("Shape")]
        [Tooltip("Number of strands the cloth is stretched across. Each strand is a pleat; odd numbers give a centre pleat.")]
        [SerializeField, Range(3, 15)] private int strands = 9;

        [Tooltip("Particles per strand. More = smoother bending.")]
        [SerializeField, Range(3, 12)] private int segments = 9;

        [Tooltip("Full length of the cloak when streaming.")]
        [SerializeField, Min(0.1f)] private float length = 1.6f;

        [Tooltip("Length at rest as a fraction of the full length. The cloak is gathered up when still and unfurls as the player moves, so it can be long in motion without piling up on the floor.")]
        [SerializeField, Range(0.3f, 1f)] private float idleLengthFactor = 0.6f;

        [Tooltip("Every other strand is shorter by this fraction, which cuts a zig-zag into the hem.")]
        [SerializeField, Range(0f, 0.5f)] private float hemZigZag = 0.12f;

        [Tooltip("Where the cloak is attached, relative to the circle's centre.")]
        [SerializeField] private Vector2 anchorOffset = new Vector2(0f, 0.2f);

        [Tooltip("Width of the edge the cloak is attached along. Keep it inside the circle so the attachment stays hidden.")]
        [SerializeField, Min(0f)] private float rootWidth = 0.56f;

        [Tooltip("How far the outer edges fan out at rest, in degrees. Bigger = a wider bell around the circle.")]
        [SerializeField, Range(0f, 120f)] private float idleSpread = 84f;

        [Tooltip("How far the outer edges fan out while streaming, in degrees.")]
        [SerializeField, Range(0f, 60f)] private float streamSpread = 13f;

        [Tooltip("How quickly a strand bends back in line with the rest along its length. Higher = the fan closes sooner.")]
        [SerializeField, Range(0.3f, 4f)] private float spreadFalloff = 1.5f;

        [Header("Colours")]
        [SerializeField] private Color capeColor = new Color(0.45f, 0.48f, 0.58f, 1f);

        [Tooltip("How much darker every other strand is. This shades the cloth into soft pleats.")]
        [SerializeField, Range(0f, 1f)] private float pleatShade = 0.16f;

        [Tooltip("How much darker the cloth gets towards the hem.")]
        [SerializeField, Range(0f, 1f)] private float hemShade = 0.22f;

        [Header("Following The Player")]
        [Tooltip("Speed at which the cloak trails at 45 degrees. Lower = streams out at lower speeds.")]
        [SerializeField, Min(0.1f)] private float streamSpeed = 3f;

        [Tooltip("Speed at which the fan is fully closed into a streaming cape.")]
        [SerializeField, Min(0.1f)] private float fullStreamSpeed = 7f;

        [Tooltip("How quickly the cloak swings round to a new direction.")]
        [SerializeField, Min(0.1f)] private float turnSpeed = 11f;

        [Tooltip("How quickly the attached edge turns to face the cloth. Fast, so the sheet keeps its width when the cloth is whipped in a new direction (take-off, landing).")]
        [SerializeField, Min(0.1f)] private float edgeTurnSpeed = 30f;

        [Tooltip("How firmly the cloth holds its pose next to the circle (0..1).")]
        [SerializeField, Range(0.01f, 1f)] private float rootStiffness = 0.5f;

        [Tooltip("How firmly the cloth holds its pose at the hem (0..1). Lower = the hem lags and swings more.")]
        [SerializeField, Range(0.001f, 1f)] private float tipStiffness = 0.1f;

        [Tooltip("Cloak gravity as a multiple of the player's gravity.")]
        [SerializeField, Min(0f)] private float gravityScale = 1f;

        [Tooltip("General energy loss per second. Higher = calmer cloth that settles sooner.")]
        [SerializeField, Min(0f)] private float damping = 1f;

        [Header("Wave")]
        [Tooltip("Size of the wave at rest, in degrees.")]
        [SerializeField, Range(0f, 30f)] private float idleWaveAngle = 3f;

        [Tooltip("Size of the wave while streaming, in degrees.")]
        [SerializeField, Range(0f, 45f)] private float moveWaveAngle = 16f;

        [Tooltip("Waves per second at rest.")]
        [SerializeField, Min(0f)] private float idleWaveSpeed = 0.35f;

        [Tooltip("Waves per second while streaming.")]
        [SerializeField, Min(0f)] private float moveWaveSpeed = 1.7f;

        [Tooltip("How many waves fit along one strand.")]
        [SerializeField, Min(0f)] private float wavesAlongStrand = 1.3f;

        [Tooltip("Timing offset between neighbouring strands, in waves. 0 = all strands move as one.")]
        [SerializeField, Range(0f, 0.5f)] private float strandWaveOffset = 0.07f;

        [Header("Reactions")]
        [Tooltip("Extra fan-out on a hard landing, in degrees (the cloak billows out).")]
        [SerializeField, Range(0f, 60f)] private float landFlare = 16f;

        [Tooltip("How much the fan closes on take-off, in degrees.")]
        [SerializeField, Range(0f, 40f)] private float jumpTuck = 4f;

        [Tooltip("How quickly the landing/take-off reaction fades.")]
        [SerializeField, Min(0.1f)] private float flareRecovery = 5f;

        [Tooltip("Downward speed added to the hem on take-off.")]
        [SerializeField, Min(0f)] private float jumpKick = 1.5f;

        [Tooltip("Fraction of landing impact speed pushed into the hem.")]
        [SerializeField, Min(0f)] private float landKick = 0.25f;

        [Tooltip("Fraction of the cloth's speed kept when a dash ends. Lower = softer forward swing on the stop.")]
        [SerializeField, Range(0f, 1f)] private float dashEndVelocityRetention = 0.35f;

        [Header("Attack Swing")]
        [Tooltip("How firmly the cloth follows the swing near the circle (0..1).")]
        [SerializeField, Range(0.01f, 1f)] private float swingRootStiffness = 0.9f;

        [Tooltip("How firmly the cloth follows the swing at the hem. Lower = more whip and lag.")]
        [SerializeField, Range(0.01f, 1f)] private float swingTipStiffness = 0.75f;

        [Tooltip("How far the strands fan out during a swing, in degrees. Small = a tight blade of cloth.")]
        [SerializeField, Range(0f, 60f)] private float swingSpread = 8f;

        [Tooltip("Cape length during a swing, as a multiple of its full length (a little extra reach).")]
        [SerializeField, Range(0.5f, 1.5f)] private float swingReach = 1.2f;

        [Tooltip("How long the cape stays drawn in front of the circle after a swing ends.")]
        [SerializeField, Min(0f)] private float swingFrontHold = 0.1f;

        [Header("Blade Form (during a swing)")]
        [Tooltip("Width of the blade at the circle, as a fraction of the cloak's normal width. The blade tapers from here to a point.")]
        [SerializeField, Range(0.05f, 1f)] private float bladeWidth = 0.45f;

        [Tooltip("Seconds for the cloth to pull together into a blade when a swing starts.")]
        [SerializeField, Min(0.001f)] private float bladeFormTime = 0.035f;

        [Tooltip("Seconds for the blade to loosen back into cloth after the swing.")]
        [SerializeField, Min(0.001f)] private float bladeReleaseTime = 0.14f;

        [Tooltip("How much lighter the blade's edges are drawn (a sheen along the cutting edge).")]
        [SerializeField, Range(0f, 1f)] private float bladeEdgeSheen = 0.3f;

        [Header("Platform Form (air jump)")]
        [Tooltip("Width of the platform the cape flattens into under the circle.")]
        [SerializeField, Min(0.1f)] private float platformWidth = 1.5f;

        [Tooltip("Thickness of the platform.")]
        [SerializeField, Min(0.01f)] private float platformThickness = 0.18f;

        [Tooltip("Seconds for the cape to fly out and flatten under the circle.")]
        [SerializeField, Min(0.001f)] private float platformFlyOutTime = 0.05f;

        [Tooltip("Seconds the platform stays in place after forming.")]
        [SerializeField, Min(0f)] private float platformHoldTime = 0.1f;

        [Tooltip("Seconds for the cape to zip back to the circle.")]
        [SerializeField, Min(0.001f)] private float platformReturnTime = 0.12f;

        [Tooltip("How far the platform's middle dips when the circle springs off it.")]
        [SerializeField, Min(0f)] private float platformDip = 0.12f;

        [Header("Collision")]
        [Tooltip("Layers the cloth collides with (floors, walls, slopes).")]
        [SerializeField] private LayerMask groundLayers;

        [Tooltip("Gap kept between the cloth and surfaces.")]
        [SerializeField, Min(0f)] private float surfaceOffset = 0.03f;

        [Tooltip("How much sliding speed the cloth loses while touching a surface.")]
        [SerializeField, Range(0f, 1f)] private float surfaceFriction = 0.25f;

        // --- Particles: strand i owns indices [i * PointsPerStrand, (i + 1) * PointsPerStrand). Index 0 of each is the pinned root.
        private Vector2[] positions;
        private Vector2[] velocities;
        private Vector2[] stepStart;    // positions at the start of the current simulation step
        private Vector2[] poseDirections; // target direction of every segment this frame
        private float[] segmentLengths; // per strand
        private float[] stiffness;      // per particle index along a strand
        private Vector2[] roots;
        private Vector2[] lastRoots;

        // --- Pose state
        private float pullAngle = -90f; // direction the cloak is posed to trail, degrees (-90 = straight down)
        private float edgeAngle = -90f; // direction the cloth is actually lying in; the attached edge sits across it
        private float streamFactor;     // 0 = resting bell, 1 = fully streaming
        private float lengthScale = 1f; // current length as a fraction of full length (gathered at rest)
        private float flare;            // temporary extra fan-out (degrees), from landings and take-offs
        private float wavePhase;        // in waves (1 = one full cycle)
        private Vector2 lastCentre;
        private bool initialised;
        private bool rebuildRequested;

        // --- Attack swing
        private bool swinging;
        private float swingStartTime;
        private float swingDuration;
        private float swingFromAngle;
        private float swingToAngle;
        private float swingFrontUntil = float.NegativeInfinity;
        private float bladeAmount;      // 0 = cloth, 1 = fully formed blade
        private float drawnBladeAmount = -1f; // blade amount the mesh colours were last written for

        // --- Air-jump platform
        private bool platformActive;
        private float platformStartTime;
        private Vector2 platformPoint;   // top-middle of the platform, world space

        // --- Flat floor under the player this frame (cheap, stable resting contact)
        private bool hasFloor;
        private float floorY;
        private float floorMinX;
        private float floorMaxX;

        // --- Mesh
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Vector3[] vertices;
        private Color[] baseColors;     // pleat + hem shading, before the blade sheen
        private Color[] colors;
        private Vector2[] sheet;        // world-space position of every mesh vertex this frame
        private Vector2[] curve;        // smoothed line of the strand being drawn

        private int PointsPerStrand => segments + 1;
        private int SamplesPerStrand => segments * SamplesPerSegment + 1;
        private Transform Space => attachSpace != null ? attachSpace : transform;

        #region Unity lifecycle

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            mesh = new Mesh { name = "Player Cape" };
            mesh.MarkDynamic(); // vertices are rewritten every frame
            GetComponent<MeshFilter>().sharedMesh = mesh;

            Build();
        }

        private void OnEnable()
        {
            if (movement == null) return;
            movement.Jumped += HandleJumped;
            movement.AirJumped += FormPlatform;
            movement.Landed += HandleLanded;
            movement.Dashed += HandleDashed;
            movement.DashEnded += HandleDashEnded;
        }

        private void OnDisable()
        {
            if (movement == null) return;
            movement.Jumped -= HandleJumped;
            movement.AirJumped -= FormPlatform;
            movement.Landed -= HandleLanded;
            movement.Dashed -= HandleDashed;
            movement.DashEnded -= HandleDashEnded;
        }

        private void Start() => ResetCape();

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        private void LateUpdate()
        {
            if (!initialised) return;

            if (rebuildRequested)
            {
                rebuildRequested = false;
                Build();
                ResetCape();
            }

            float dt = Mathf.Min(Time.deltaTime, MaxFrameTime);
            if (dt <= 0f) return;

            // A teleport or respawn shows up as an impossible jump or speed: start again from the resting shape.
            Vector2 centre = Space.TransformPoint(anchorOffset);
            float moved = (centre - lastCentre).magnitude;
            if (moved > TeleportDistance || moved / dt > TeleportSpeed)
            {
                ResetCape();
                return;
            }

            Vector2 playerVelocity = movement != null ? movement.Velocity : (centre - lastCentre) / dt;

            UpdatePose(playerVelocity, dt);
            UpdateSwing();
            UpdateForms(dt);
            UpdateEdgeAngle(centre, dt);
            if (swinging) edgeAngle = pullAngle; // keep the attached edge square to the swing
            UpdateSortingOrder();
            ComputeRoots(centre);
            ComputePoseDirections();
            SampleFloor(centre);

            // Split the frame into equal steps. The roots are interpolated from last frame to this frame
            // across the steps, so fast moves (dash) pull the cloth smoothly instead of in one jerk.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / MaxSubStep), 1, MaxStepsPerFrame);
            float h = dt / steps;
            ComputeStiffness(h);

            for (int s = 1; s <= steps; s++)
            {
                Step(h, (float)s / steps, playerVelocity);
            }

            ResolveCollisions();

            lastCentre = centre;
            System.Array.Copy(roots, lastRoots, roots.Length);

            UpdateMesh();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Whips the cape through an arc for an attack. Angles are in degrees (0 = right, 90 = up) and are not
        /// wrapped, so a sweep can pass the 180° mark (e.g. 150 → -40 swings over the top to the front-right).
        /// The cloth is driven firmly along the arc, then let go so it follows through on its own.
        /// </summary>
        public void PlaySwing(float fromAngle, float toAngle, float duration)
        {
            swinging = true;
            swingStartTime = Time.time;
            swingDuration = Mathf.Max(0.01f, duration);
            swingFromAngle = fromAngle;
            swingToAngle = toAngle;
            swingFrontUntil = Time.time + swingDuration + swingFrontHold;

            // Start the pose at the beginning of the arc right away so the first frame already reads.
            pullAngle = fromAngle;
        }

        /// <summary>True while the cape is being swung for an attack.</summary>
        public bool IsSwinging => swinging;

        /// <summary>
        /// True while the cape is away from the circle as an air-jump platform (flying out or holding).
        /// It can't be swung during this time. Its return trip doesn't count: a swing may start then.
        /// </summary>
        public bool IsFormingPlatform => platformActive && Time.time < platformStartTime + platformFlyOutTime + platformHoldTime;

        /// <summary>Seconds until the cape is back from being a platform and free to swing (0 if it is free).</summary>
        public float PlatformBusyTimeLeft =>
            IsFormingPlatform ? platformStartTime + platformFlyOutTime + platformHoldTime - Time.time : 0f;

        /// <summary>
        /// The cape flies out and flattens into a short platform with its top at <paramref name="point"/>,
        /// stays a moment while the circle springs off it, then zips back. Purely visual.
        /// </summary>
        public void FormPlatform(Vector2 point)
        {
            platformActive = true;
            platformStartTime = Time.time;
            platformPoint = point;
        }

        /// <summary>Stops a swing early (e.g. when an air jump takes the cape instead).</summary>
        public void CancelSwing()
        {
            swinging = false;
            swingFrontUntil = float.NegativeInfinity;
        }

        /// <summary>Snaps the cloak to its resting shape. Call after teleports/respawns.</summary>
        public void ResetCape()
        {
            if (positions == null) Build();

            pullAngle = -90f;
            edgeAngle = -90f;
            swinging = false;
            platformActive = false;
            bladeAmount = 0f;
            streamFactor = 0f;
            lengthScale = idleLengthFactor;
            flare = 0f;

            Vector2 centre = Space.TransformPoint(anchorOffset);
            ComputeRoots(centre);
            ComputePoseDirections();

            int points = PointsPerStrand;
            for (int i = 0; i < strands; i++)
            {
                int root = i * points;
                positions[root] = roots[i];
                velocities[root] = Vector2.zero;
                for (int j = 1; j < points; j++)
                {
                    positions[root + j] = positions[root + j - 1] + poseDirections[root + j] * (segmentLengths[i] * lengthScale);
                    velocities[root + j] = Vector2.zero;
                }
            }

            lastCentre = centre;
            System.Array.Copy(roots, lastRoots, roots.Length);
            initialised = true;
            UpdateMesh();
        }

        #endregion

        #region Pose

        /// <summary>Updates the smoothed values that describe the cloak's overall pose.</summary>
        private void UpdatePose(Vector2 playerVelocity, float dt)
        {
            // The cloak trails where gravity and the wind of the player's own movement pull it:
            // straight down at rest, back when running, up when falling fast.
            Vector2 pull = Vector2.down - playerVelocity / streamSpeed;
            if (pull.sqrMagnitude > 0.01f)
            {
                float target = Mathf.Atan2(pull.y, pull.x) * Mathf.Rad2Deg;
                pullAngle = Mathf.LerpAngle(pullAngle, target, 1f - Mathf.Exp(-turnSpeed * dt));
            }

            float targetStream = Mathf.Clamp01(playerVelocity.magnitude / fullStreamSpeed);
            streamFactor = Mathf.Lerp(streamFactor, targetStream, 1f - Mathf.Exp(-8f * dt));
            lengthScale = Mathf.Lerp(idleLengthFactor, 1f, streamFactor);

            flare = Mathf.Lerp(flare, 0f, 1f - Mathf.Exp(-flareRecovery * dt));
            wavePhase += Mathf.Lerp(idleWaveSpeed, moveWaveSpeed, streamFactor) * dt;
        }

        /// <summary>Drives the pose along the attack arc while a swing is playing.</summary>
        private void UpdateSwing()
        {
            if (!swinging) return;

            float p = (Time.time - swingStartTime) / swingDuration;
            if (p >= 1f)
            {
                // Let go: from here the normal pose takes over, and the cloth's momentum gives the follow-through.
                swinging = false;
                return;
            }

            // Fast out of the gate, easing into the end of the arc.
            float eased = 1f - (1f - p) * (1f - p);
            pullAngle = Mathf.Lerp(swingFromAngle, swingToAngle, eased);
            streamFactor = 1f;
            lengthScale = swingReach;
            flare = 0f;
        }

        /// <summary>Advances the blade and platform shape-shifts.</summary>
        private void UpdateForms(float dt)
        {
            // Snap together fast when a swing starts; loosen back into cloth more slowly afterwards.
            float target = swinging ? 1f : 0f;
            float time = swinging ? bladeFormTime : bladeReleaseTime;
            bladeAmount = Mathf.MoveTowards(bladeAmount, target, dt / time);

            if (platformActive && Time.time >= platformStartTime + platformFlyOutTime + platformHoldTime + platformReturnTime)
            {
                platformActive = false;
            }
        }

        /// <summary>How far the cape is shape-shifted into the platform (0..1), with smooth fly-out and return.</summary>
        private float PlatformAmount()
        {
            if (!platformActive) return 0f;

            float age = Time.time - platformStartTime;
            if (age < platformFlyOutTime) return Mathf.SmoothStep(0f, 1f, age / platformFlyOutTime);

            float returnAge = age - platformFlyOutTime - platformHoldTime;
            if (returnAge <= 0f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, returnAge / platformReturnTime);
        }

        /// <summary>Draws the cape in front of the circle while it swings, behind it otherwise.</summary>
        private void UpdateSortingOrder()
        {
            if (bodyRenderer == null) return;
            bool inFront = Time.time < swingFrontUntil;
            meshRenderer.sortingOrder = bodyRenderer.sortingOrder + (inFront ? 1 : sortingOffset);
        }

        /// <summary>
        /// Tracks the direction the cloth is actually lying in (from the anchor to the average particle).
        /// The attached edge is kept at right angles to this, so the sheet always shows its full width,
        /// even in the instant after a jump or landing when the cloth has been whipped away from its pose.
        /// </summary>
        private void UpdateEdgeAngle(Vector2 centre, float dt)
        {
            Vector2 sum = Vector2.zero;
            int points = PointsPerStrand;
            for (int i = 0; i < strands; i++)
            {
                for (int j = 1; j < points; j++)
                {
                    sum += positions[i * points + j];
                }
            }

            Vector2 toCloth = sum / (strands * segments) - centre;
            if (toCloth.sqrMagnitude < 1e-4f) return;

            float target = Mathf.Atan2(toCloth.y, toCloth.x) * Mathf.Rad2Deg;
            edgeAngle = Mathf.LerpAngle(edgeAngle, target, 1f - Mathf.Exp(-edgeTurnSpeed * dt));
        }

        /// <summary>Places the strand roots on a line behind the circle, at right angles to the direction the cloth lies in.</summary>
        private void ComputeRoots(Vector2 centre)
        {
            Vector3 scale = Space.lossyScale;
            float worldScale = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y)) * 0.5f;

            Vector2 across = Rotate(AngleToDirection(edgeAngle), 90f);
            for (int i = 0; i < strands; i++)
            {
                roots[i] = centre + across * (StrandSide(i) * rootWidth * 0.5f * worldScale);
            }
        }

        /// <summary>Works out which way every segment of every strand wants to point this frame.</summary>
        private void ComputePoseDirections()
        {
            float spread = swinging ? swingSpread : Mathf.Max(6f, Mathf.Lerp(idleSpread, streamSpread, streamFactor) + flare);
            float waveAngle = swinging ? 0f : Mathf.Lerp(idleWaveAngle, moveWaveAngle, streamFactor);
            int points = PointsPerStrand;

            for (int i = 0; i < strands; i++)
            {
                float side = StrandSide(i);
                for (int j = 1; j < points; j++)
                {
                    float along = (float)(j - 1) / (segments - 1); // 0 at the first segment, 1 at the last
                    float towardsHem = (float)j / segments;

                    // Outer strands start angled outward and bend back in line with the pull along their length.
                    float fan = side * spread * Mathf.Pow(1f - along, spreadFalloff);

                    // A wave travelling from the circle out to the hem, growing towards the hem.
                    float wave = Mathf.Sin((wavePhase - wavesAlongStrand * along + i * strandWaveOffset) * Mathf.PI * 2f)
                                 * waveAngle * towardsHem;

                    poseDirections[i * points + j] = AngleToDirection(pullAngle + fan + wave);
                }
            }
        }

        /// <summary>-1 for the first strand, +1 for the last, 0 for the centre.</summary>
        private float StrandSide(int i) => strands > 1 ? (float)i / (strands - 1) * 2f - 1f : 0f;

        private static Vector2 AngleToDirection(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        #endregion

        #region Simulation

        /// <summary>Converts the authored stiffness (per step at 120 steps/second) to this frame's step length.</summary>
        private void ComputeStiffness(float h)
        {
            for (int j = 1; j < PointsPerStrand; j++)
            {
                float along = (float)(j - 1) / (segments - 1);
                float perReferenceStep = swinging
                    ? Mathf.Lerp(swingRootStiffness, swingTipStiffness, along)
                    : Mathf.Lerp(rootStiffness, tipStiffness, along);
                stiffness[j] = 1f - Mathf.Pow(1f - perReferenceStep, h * ReferenceRate);
            }
        }

        private void Step(float h, float frameProgress, Vector2 playerVelocity)
        {
            // Gravity: match what the player feels while rising (including a cut jump) so the cloak trails
            // behind a jump instead of floating up past the circle. Otherwise use the base value.
            float baseGravity = (movement != null ? movement.BaseGravity : FallbackGravity) * gravityScale;
            float gravity = baseGravity;
            if (movement != null && playerVelocity.y > 0f)
            {
                gravity = movement.CurrentGravity * gravityScale;
            }

            // Air drag is tied to stream speed so loose cloth settles in the same direction as the pose.
            float drag = baseGravity / streamSpeed;
            float keep = Mathf.Exp(-damping * h);
            int points = PointsPerStrand;

            for (int i = 0; i < strands; i++)
            {
                int root = i * points;
                float segmentLength = segmentLengths[i] * lengthScale;

                // Pin the root, interpolated between last frame and this frame.
                positions[root] = Vector2.Lerp(lastRoots[i], roots[i], frameProgress);
                velocities[root] = Vector2.zero;

                for (int j = 1; j < points; j++)
                {
                    int index = root + j;

                    // 1) Momentum, gravity and air drag. Drag is applied implicitly (a divide) so it is stable.
                    Vector2 velocity = (velocities[index] + Vector2.down * (gravity * h)) / (1f + drag * h) * keep;
                    stepStart[index] = positions[index];
                    Vector2 position = positions[index] + velocity * h;

                    // 2) Pull toward the posed shape: firm near the circle, loose at the hem.
                    Vector2 parent = positions[index - 1];
                    Vector2 posed = parent + poseDirections[index] * segmentLength;
                    position = Vector2.Lerp(position, posed, stiffness[j]);

                    // 3) Keep the segment at its length.
                    Vector2 offset = position - parent;
                    float distance = offset.magnitude;
                    position = distance > 1e-5f
                        ? parent + offset * (segmentLength / distance)
                        : parent + poseDirections[index] * segmentLength;

                    // 4) Rest on the flat floor under the player.
                    if (hasFloor && position.x >= floorMinX && position.x <= floorMaxX)
                    {
                        float minY = floorY + surfaceOffset;
                        if (position.y < minY)
                        {
                            position.y = minY;
                            position.x = Mathf.Lerp(position.x, stepStart[index].x, surfaceFriction);
                        }
                    }

                    positions[index] = position;
                    velocities[index] = (position - stepStart[index]) / h;
                }
            }
        }

        /// <summary>
        /// Keeps the cloth out of level geometry (walls, slopes, ledges). Working from the circle outwards,
        /// each particle must be in line of sight of the one before it; if something is in the way, the
        /// particle is placed on that surface and loses the speed going into it, plus some sliding speed.
        ///
        /// Because every particle is checked against its neighbour each frame (never against where it was
        /// last frame), a particle can never be left stuck inside or behind a collider.
        /// </summary>
        private void ResolveCollisions()
        {
            if (groundLayers.value == 0) return;

            int points = PointsPerStrand;
            for (int i = 0; i < strands; i++)
            {
                for (int j = 1; j < points; j++)
                {
                    int index = i * points + j;
                    RaycastHit2D hit = Physics2D.Linecast(positions[index - 1], positions[index], groundLayers);
                    if (hit.collider == null) continue;

                    Vector2 normal = hit.normal;
                    Vector2 velocity = velocities[index];
                    float intoSurface = Vector2.Dot(velocity, normal);
                    Vector2 sliding = velocity - normal * intoSurface;

                    positions[index] = hit.point + normal * surfaceOffset;
                    velocities[index] = sliding * (1f - surfaceFriction) + normal * Mathf.Max(intoSurface, 0f);
                }
            }
        }

        /// <summary>Finds the flat floor directly under the player, if there is one within reach.</summary>
        private void SampleFloor(Vector2 centre)
        {
            hasFloor = false;
            if (groundLayers.value == 0) return;

            RaycastHit2D hit = Physics2D.Raycast(centre, Vector2.down, length + 1f, groundLayers);
            if (hit.collider == null || hit.normal.y < 0.99f) return; // slopes are left to ResolveCollisions

            Bounds bounds = hit.collider.bounds;
            hasFloor = true;
            floorY = hit.point.y;
            floorMinX = bounds.min.x;
            floorMaxX = bounds.max.x;
        }

        /// <summary>Adds a velocity to every free particle, weighted towards the hem.</summary>
        private void AddVelocity(Vector2 velocity)
        {
            if (!initialised) return;
            int points = PointsPerStrand;
            for (int i = 0; i < strands; i++)
            {
                for (int j = 1; j < points; j++)
                {
                    velocities[i * points + j] += velocity * ((float)j / segments);
                }
            }
        }

        #endregion

        #region Movement reactions

        private void HandleJumped()
        {
            flare -= jumpTuck;
            AddVelocity(Vector2.down * jumpKick);
        }

        private void HandleLanded(float impactSpeed)
        {
            flare += landFlare * Mathf.InverseLerp(3f, 20f, impactSpeed);
            AddVelocity(Vector2.down * (impactSpeed * landKick));
        }

        private void HandleDashed() => streamFactor = 1f; // close the fan immediately for the quickstep

        /// <summary>
        /// The circle slows abruptly when a quickstep ends. Bleeding off most of the cloth's speed here
        /// stops it whipping forward past the circle.
        /// </summary>
        private void HandleDashEnded()
        {
            if (!initialised) return;
            for (int i = 0; i < velocities.Length; i++)
            {
                velocities[i] *= dashEndVelocityRetention;
            }
        }

        #endregion

        #region Build & mesh

        /// <summary>Allocates the particles and builds the parts of the mesh that never change (triangles, colours).</summary>
        private void Build()
        {
            int points = PointsPerStrand;
            int samples = SamplesPerStrand;
            int particleCount = strands * points;

            positions = new Vector2[particleCount];
            velocities = new Vector2[particleCount];
            stepStart = new Vector2[particleCount];
            poseDirections = new Vector2[particleCount];
            stiffness = new float[points];
            segmentLengths = new float[strands];
            roots = new Vector2[strands];
            lastRoots = new Vector2[strands];
            curve = new Vector2[samples];

            for (int i = 0; i < strands; i++)
            {
                // Every other strand is shorter, which cuts a zig-zag into the hem.
                float strandLength = length * (i % 2 == 1 ? 1f - hemZigZag : 1f);
                segmentLengths[i] = strandLength / segments;
            }

            if (bodyRenderer != null)
            {
                meshRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
                meshRenderer.sortingOrder = bodyRenderer.sortingOrder + sortingOffset;
            }

            // The cloth is one sheet: a grid of vertices, one column per strand, one row per curve sample.
            vertices = new Vector3[strands * samples];
            sheet = new Vector2[vertices.Length];
            baseColors = new Color[vertices.Length];
            colors = new Color[vertices.Length];
            drawnBladeAmount = -1f;

            for (int i = 0; i < strands; i++)
            {
                // Alternate strands are darker. Colours blend smoothly between neighbours, so the sheet
                // reads as soft pleats: light ridges with shaded valleys between them.
                Color strandColor = i % 2 == 1 ? Shade(capeColor, pleatShade) : capeColor;
                for (int s = 0; s < samples; s++)
                {
                    float t = (float)s / (samples - 1);
                    baseColors[i * samples + s] = Shade(strandColor, hemShade * t);
                }
            }
            System.Array.Copy(baseColors, colors, colors.Length);

            // Two triangles per grid cell.
            var triangles = new int[(strands - 1) * (samples - 1) * 6];
            int n = 0;
            for (int i = 0; i < strands - 1; i++)
            {
                for (int s = 0; s < samples - 1; s++)
                {
                    int a = i * samples + s, b = a + 1, c = a + samples, d = c + 1;
                    triangles[n++] = a; triangles[n++] = c; triangles[n++] = b;
                    triangles[n++] = b; triangles[n++] = c; triangles[n++] = d;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = baseColors;
            mesh.triangles = triangles;
        }

        private static Color Shade(Color color, float amount)
        {
            Color shaded = Color.Lerp(color, Color.black, amount);
            shaded.a = color.a;
            return shaded;
        }

        private void UpdateMesh()
        {
            int points = PointsPerStrand;
            int samples = SamplesPerStrand;

            // 1) The simulated cloth, as smooth curves.
            for (int i = 0; i < strands; i++)
            {
                SampleCurve(i * points, points);
                System.Array.Copy(curve, 0, sheet, i * samples, samples);
            }

            // 2) Shape-shifts drawn on top of the cloth. They only change how it is drawn, never the simulation,
            //    so the cloth is always ready to take over again where it would naturally be.
            if (bladeAmount > 0f) ApplyBladeForm(samples);
            float platform = PlatformAmount();
            if (platform > 0f) ApplyPlatformForm(samples, platform);

            for (int v = 0; v < sheet.Length; v++)
            {
                vertices[v] = transform.InverseTransformPoint(sheet[v]);
            }

            mesh.vertices = vertices;
            UpdateBladeColours(samples);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Pulls the sheet together around its middle line into a thin blade: narrow at the circle and tapering
        /// to a point at the tip.
        /// </summary>
        private void ApplyBladeForm(int samples)
        {
            float blade = Mathf.SmoothStep(0f, 1f, bladeAmount);
            for (int s = 0; s < samples; s++)
            {
                Vector2 middle = Vector2.zero;
                for (int i = 0; i < strands; i++) middle += sheet[i * samples + s];
                middle /= strands;

                float t = (float)s / (samples - 1);
                float profile = bladeWidth * Mathf.Pow(1f - t, 0.8f); // straight-ish edges into a sharp point
                float scale = Mathf.Lerp(1f, profile, blade);

                for (int i = 0; i < strands; i++)
                {
                    int v = i * samples + s;
                    sheet[v] = middle + (sheet[v] - middle) * scale;
                }
            }
        }

        /// <summary>
        /// Moves the sheet towards a flat platform hanging in the air where the air jump happened. Each strand
        /// becomes a slice of the platform from top to bottom, so the pleats show across it. The middle dips
        /// and springs back as the circle pushes off.
        /// </summary>
        private void ApplyPlatformForm(int samples, float amount)
        {
            float age = Time.time - platformStartTime;
            float dip = platformDip * Mathf.Exp(-age * 9f) * Mathf.Cos(age * 30f);

            for (int i = 0; i < strands; i++)
            {
                float side = StrandSide(i);
                float sag = dip * (1f - side * side);
                float x = platformPoint.x + side * platformWidth * 0.5f;

                for (int s = 0; s < samples; s++)
                {
                    float t = (float)s / (samples - 1);
                    var target = new Vector2(x, platformPoint.y - sag - t * platformThickness);
                    int v = i * samples + s;
                    sheet[v] = Vector2.Lerp(sheet[v], target, amount);
                }
            }
        }

        /// <summary>Brightens the blade's outer edges while it is formed. Skipped when nothing changed.</summary>
        private void UpdateBladeColours(int samples)
        {
            if (Mathf.Approximately(bladeAmount, drawnBladeAmount)) return;
            drawnBladeAmount = bladeAmount;

            float sheen = bladeEdgeSheen * Mathf.SmoothStep(0f, 1f, bladeAmount);
            for (int i = 0; i < strands; i++)
            {
                float edge = Mathf.Abs(StrandSide(i));
                for (int s = 0; s < samples; s++)
                {
                    int v = i * samples + s;
                    Color c = Color.Lerp(baseColors[v], Color.white, sheen * edge);
                    c.a = baseColors[v].a;
                    colors[v] = c;
                }
            }

            mesh.colors = colors;
        }

        /// <summary>Fills <see cref="curve"/> with a smooth (Catmull-Rom) line through one strand's particles.</summary>
        private void SampleCurve(int root, int points)
        {
            int sample = 0;
            for (int j = 0; j < points - 1; j++)
            {
                Vector2 p0 = positions[root + Mathf.Max(j - 1, 0)];
                Vector2 p1 = positions[root + j];
                Vector2 p2 = positions[root + j + 1];
                Vector2 p3 = positions[root + Mathf.Min(j + 2, points - 1)];

                for (int k = 0; k < SamplesPerSegment; k++)
                {
                    float t = (float)k / SamplesPerSegment;
                    float t2 = t * t;
                    float t3 = t2 * t;
                    curve[sample++] = 0.5f * (2f * p1 + (p2 - p0) * t
                                              + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                                              + (3f * p1 - p0 - 3f * p2 + p3) * t3);
                }
            }

            curve[sample] = positions[root + points - 1];
        }

        #endregion

        private void OnValidate()
        {
            // Shape and colour values are baked into the mesh, so rebuild if they change during Play Mode.
            if (Application.isPlaying && mesh != null)
            {
                rebuildRequested = true;
            }
        }
    }
}
