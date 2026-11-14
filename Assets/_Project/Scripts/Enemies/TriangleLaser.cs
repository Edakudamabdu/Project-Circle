using System;
using ProjectCircle.Combat;
using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// The triangle's ranged attack: a laser beam fired from one of its vertices.
    ///
    /// Sequence (each phase has its own timing in the Inspector):
    /// The beam's path is decided the moment the attack starts (aimed at where the player is then) and never
    /// moves after that, so a player who sees the warning ray can always step out of it.
    ///
    /// 1. Rise   — it lifts off the floor on vertex power and turns its nearest corner along the path.
    ///             That corner starts to glow.
    /// 2. Aim    — a thin, flickering warning ray shows exactly where the beam will go.
    /// 3. Lock   — the ray blinks fast: the beam is about to fire.
    /// 4. Fire   — a thick beam blasts along the locked line until it hits a wall or floor. Touching it hurts.
    /// 5. Recover — the beam fades and the triangle settles back down.
    ///
    /// It attacks whenever <see cref="TriangleEnemy.IsInAttackPosition"/> is true and the cooldown has passed.
    /// The triangle stands still for the whole attack.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // after TriangleEnemy, so the vertex position is final for the frame
    [RequireComponent(typeof(TriangleEnemy))]
    public sealed class TriangleLaser : MonoBehaviour
    {
        private enum Phase { Ready, Rise, Aim, Lock, Fire, Recover }

        [Header("References")]
        [Tooltip("Thin line shown while aiming.")]
        [SerializeField] private LineRenderer warningLine;

        [Tooltip("Wide, soft outer part of the beam.")]
        [SerializeField] private LineRenderer beamGlow;

        [Tooltip("Bright inner core of the beam.")]
        [SerializeField] private LineRenderer beamCore;

        [Tooltip("Glow on the firing vertex.")]
        [SerializeField] private VertexGlow muzzleGlow;

        [Tooltip("Glow where the beam hits a surface.")]
        [SerializeField] private VertexGlow impactGlow;

        [Header("Damage")]
        [SerializeField, Min(0)] private int damage = 1;

        [Tooltip("How far the beam reaches if nothing blocks it.")]
        [SerializeField, Min(1f)] private float range = 18f;

        [Tooltip("Width of the beam. Touching any of it hurts.")]
        [SerializeField, Min(0.05f)] private float beamWidth = 0.42f;

        [Tooltip("Layers that take damage from the beam.")]
        [SerializeField] private LayerMask targetLayers = 1 << 8;

        [Tooltip("Layers that stop the beam (and the warning ray).")]
        [SerializeField] private LayerMask blockingLayers = 1 << 3;

        [Header("Timing")]
        [Tooltip("Wait after first getting into attack position before the first shot.")]
        [SerializeField, Min(0f)] private float firstShotDelay = 0.1f;

        [Tooltip("Time between the end of one attack and the start of the next.")]
        [SerializeField, Min(0f)] private float cooldown = 0.9f;

        [SerializeField, Min(0.01f)] private float riseTime = 0.12f;

        [Tooltip("How long the warning ray shows the path before it starts blinking.")]
        [SerializeField, Min(0.05f)] private float aimTime = 0.42f;

        [Tooltip("How long the ray blinks fast right before firing.")]
        [SerializeField, Min(0.05f)] private float lockTime = 0.22f;

        [SerializeField, Min(0.05f)] private float fireTime = 0.35f;

        [Tooltip("Wait before trying again after being hit out of a wind-up (only when the triangle isn't armored).")]
        [SerializeField, Min(0f)] private float interruptedCooldown = 0.8f;
        [SerializeField, Min(0.05f)] private float recoverTime = 0.25f;

        [Tooltip("After being hit while not attacking, it fires back within this many seconds (as soon as it is in position).")]
        [SerializeField, Min(0f)] private float retaliationDelay = 0.3f;

        [Header("Pose")]
        [Tooltip("How high it lifts off the floor while attacking.")]
        [SerializeField, Min(0f)] private float liftHeight = 0.35f;

        [Header("Look")]
        [SerializeField] private Color warningColor = new Color(1f, 0.32f, 0.22f, 0.85f);
        [SerializeField] private Color beamColor = new Color(1f, 0.5f, 0.25f, 0.55f);
        [SerializeField] private Color coreColor = new Color(1f, 0.95f, 0.82f, 1f);
        [SerializeField] private Color vertexGlowColor = new Color(1f, 0.6f, 0.35f, 1f);

        [SerializeField, Min(0.005f)] private float warningWidth = 0.05f;

        /// <summary>Raised when the beam fires.</summary>
        public event Action Fired;

        /// <summary>True from the start of the rise until the triangle has settled again.</summary>
        public bool IsAttacking => phase != Phase.Ready;

        private TriangleEnemy enemy;
        private Phase phase = Phase.Ready;
        private float phaseStartTime;
        private float nextAttackTime;
        private bool wasInPosition;

        private EnemyHealth health;
        private int corner;            // which corner fires
        private float cornerRestAngle; // that corner's direction at rest, degrees
        private float aimAngle;        // beam direction, degrees (fixed for the whole attack)
        private Vector2 beamStart;
        private Vector2 beamEnd;
        private bool damagedThisShot;

        private float PhaseTime => Time.time - phaseStartTime;

        #region Unity lifecycle

        private void Awake()
        {
            enemy = GetComponent<TriangleEnemy>();
            health = GetComponent<EnemyHealth>();
            HideAll();
        }

        private void OnEnable()
        {
            if (health != null) health.Hit += HandleHit;
        }

        private void OnDisable()
        {
            if (health != null) health.Hit -= HandleHit;
            if (phase != Phase.Ready) Finish();
        }

        /// <summary>
        /// - Armored triangle (the default): hits never stop a shot it has started.
        /// - Otherwise, a hit during the wind-up cancels the shot; once the beam is firing it no longer stops it.
        /// - Hitting it while it isn't attacking provokes a quick counter-shot.
        /// </summary>
        private void HandleHit(int damage, Vector2 source)
        {
            if (phase == Phase.Ready)
            {
                nextAttackTime = Mathf.Min(nextAttackTime, Time.time + retaliationDelay);
                return;
            }

            if (enemy.ArmoredWhileAttacking) return;

            if (phase == Phase.Rise || phase == Phase.Aim || phase == Phase.Lock)
            {
                Finish();
                nextAttackTime = Time.time + interruptedCooldown;
            }
        }

        private void LateUpdate()
        {
            if (phase == Phase.Ready)
            {
                TryStartAttack();
                return;
            }

            if (enemy.Target == null)
            {
                Finish();
                return;
            }

            switch (phase)
            {
                case Phase.Rise: UpdateRise(); break;
                case Phase.Aim: UpdateAim(); break;
                case Phase.Lock: UpdateLock(); break;
                case Phase.Fire: UpdateFire(); break;
                case Phase.Recover: UpdateRecover(); break;
            }
        }

        #endregion

        #region Phases

        private void TryStartAttack()
        {
            bool inPosition = enemy.IsInAttackPosition;

            // Short pause the first time it gets into position, so it doesn't fire the instant it arrives.
            if (inPosition && !wasInPosition) nextAttackTime = Mathf.Max(nextAttackTime, Time.time + firstShotDelay);
            wasInPosition = inPosition;

            if (!inPosition || Time.time < nextAttackTime) return;
            if (!enemy.BeginAttack()) return;

            // The path is set now, towards where the player is at this moment, and never changes.
            float toTarget = AngleTo(enemy.Target.position);
            aimAngle = toTarget;

            // Fire from whichever corner needs the least turning to line up with the path.
            float bestTurn = float.MaxValue;
            for (int c = 0; c < 3; c++)
            {
                float rest = 90f + c * 120f;
                float turn = Mathf.Abs(Mathf.DeltaAngle(rest, toTarget));
                if (turn < bestTurn)
                {
                    bestTurn = turn;
                    corner = c;
                    cornerRestAngle = rest;
                }
            }

            damagedThisShot = false;
            SetPhase(Phase.Rise);
        }

        private void UpdateRise()
        {
            // Lift and turn the firing corner onto the path.
            float p = Ease(PhaseTime / riseTime);
            ApplyPose(p, Mathf.DeltaAngle(cornerRestAngle, aimAngle) * p);
            SetVertexGlow(0.35f * p);

            if (PhaseTime >= riseTime) SetPhase(Phase.Aim);
        }

        private void UpdateAim()
        {
            ApplyPose(1f, Mathf.DeltaAngle(cornerRestAngle, aimAngle));

            // Charging up: the glow grows and the warning ray flickers.
            float p = Mathf.Clamp01(PhaseTime / aimTime);
            SetVertexGlow(0.35f + 0.5f * p);
            float flicker = 0.55f + 0.45f * Mathf.Sin(Time.time * 30f);
            DrawWarning(flicker, 1f);

            if (PhaseTime >= aimTime) SetPhase(Phase.Lock);
        }

        private void UpdateLock()
        {
            // About to fire: the ray blinks fast and gets brighter - last chance to move.
            ApplyPose(1f, Mathf.DeltaAngle(cornerRestAngle, aimAngle));
            float p = Mathf.Clamp01(PhaseTime / lockTime);
            SetVertexGlow(0.85f + 0.4f * p);
            bool on = Mathf.Repeat(Time.time * 16f, 1f) < 0.6f;
            DrawWarning(on ? 1f : 0.15f, 1f + p);

            if (PhaseTime >= lockTime)
            {
                SetPhase(Phase.Fire);
                if (muzzleGlow != null) muzzleGlow.Burst();
                Fired?.Invoke();
            }
        }

        private void UpdateFire()
        {
            ApplyPose(1f, Mathf.DeltaAngle(cornerRestAngle, aimAngle));
            SetWarningVisible(false);
            SetVertexGlow(1.4f);

            // Beam snaps open quickly, holds, then the next phase fades it out.
            float open = Mathf.Clamp01(PhaseTime / 0.06f);
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 60f);
            DrawBeam(open * pulse, 1f);
            DealDamage();

            if (PhaseTime >= fireTime) SetPhase(Phase.Recover);
        }

        private void UpdateRecover()
        {
            float p = Mathf.Clamp01(PhaseTime / recoverTime);
            float eased = Ease(p);

            // Beam thins out and fades; the triangle turns back and settles onto the floor.
            DrawBeam(1f - eased, 1f - p);
            ApplyPose(1f - eased, Mathf.DeltaAngle(cornerRestAngle, aimAngle) * (1f - eased));
            SetVertexGlow(1.2f * (1f - p));

            if (p >= 1f) Finish();
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStartTime = Time.time;
        }

        private void Finish()
        {
            HideAll();
            SetVertexGlow(0f);
            if (enemy != null) enemy.EndAttack();
            phase = Phase.Ready;
            nextAttackTime = Time.time + cooldown;
        }

        #endregion

        #region Aiming & damage

        /// <summary>Angle (degrees) from the triangle's centre to a point.</summary>
        private float AngleTo(Vector2 point)
        {
            Vector2 centre = enemy.Visual != null ? (Vector2)enemy.Visual.position : (Vector2)transform.position;
            Vector2 to = point - centre;
            return Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
        }

        /// <summary>Lifts the triangle by a fraction of the lift height and rotates it.</summary>
        private void ApplyPose(float lift, float rotation)
        {
            enemy.SetAttackPose(new Vector2(0f, liftHeight * lift), rotation);
        }

        /// <summary>Works out where the beam starts (the firing vertex) and where it stops (first surface).</summary>
        private void UpdateBeamLine()
        {
            Vector2 direction = Direction(aimAngle);
            beamStart = VertexPosition();
            RaycastHit2D hit = Physics2D.Raycast(beamStart, direction, range, blockingLayers);
            beamEnd = hit.collider != null ? hit.point : beamStart + direction * range;
        }

        private void DealDamage()
        {
            if (damagedThisShot || damage <= 0) return;

            Vector2 direction = beamEnd - beamStart;
            float length = direction.magnitude;
            if (length < 0.01f) return;

            RaycastHit2D hit = Physics2D.CircleCast(beamStart, beamWidth * 0.5f, direction / length, length, targetLayers);
            if (hit.collider == null) return;

            var health = hit.collider.GetComponentInParent<PlayerHealth>();
            if (health != null && health.TakeDamage(damage, beamStart)) damagedThisShot = true;
        }

        /// <summary>World position of the firing vertex, on the tip of its rounded corner.</summary>
        private Vector2 VertexPosition()
        {
            if (enemy.Shape == null || enemy.Visual == null) return transform.position;
            Vector2 local = enemy.Shape.GetCornerPoint(corner, enemy.Shape.CornerRadius * 0.6f);
            return enemy.Visual.TransformPoint(local);
        }

        #endregion

        #region Drawing

        private void DrawWarning(float alpha, float widthScale)
        {
            UpdateBeamLine();
            if (warningLine == null) return;

            warningLine.enabled = true;
            Color c = warningColor;
            c.a *= alpha;
            warningLine.startColor = c;
            warningLine.endColor = c;
            warningLine.startWidth = warningLine.endWidth = warningWidth * widthScale;
            warningLine.SetPosition(0, beamStart);
            warningLine.SetPosition(1, beamEnd);

            ShowImpact(0.35f * alpha);
        }

        private void DrawBeam(float widthScale, float alpha)
        {
            UpdateBeamLine();
            widthScale = Mathf.Max(0f, widthScale);

            if (beamGlow != null)
            {
                beamGlow.enabled = widthScale > 0.01f;
                Color c = beamColor;
                c.a *= alpha;
                beamGlow.startColor = beamGlow.endColor = c;
                beamGlow.startWidth = beamGlow.endWidth = beamWidth * 1.8f * widthScale;
                beamGlow.SetPosition(0, beamStart);
                beamGlow.SetPosition(1, beamEnd);
            }

            if (beamCore != null)
            {
                beamCore.enabled = widthScale > 0.01f;
                Color c = coreColor;
                c.a *= alpha;
                beamCore.startColor = beamCore.endColor = c;
                beamCore.startWidth = beamCore.endWidth = beamWidth * 0.55f * widthScale;
                beamCore.SetPosition(0, beamStart);
                beamCore.SetPosition(1, beamEnd);
            }

            ShowImpact(1.3f * widthScale * alpha);
        }

        private void ShowImpact(float intensity)
        {
            if (impactGlow == null) return;
            impactGlow.transform.position = new Vector3(beamEnd.x, beamEnd.y, impactGlow.transform.position.z);
            impactGlow.Intensity = intensity;
        }

        private void SetVertexGlow(float intensity)
        {
            if (muzzleGlow != null)
            {
                muzzleGlow.Color = vertexGlowColor;
                muzzleGlow.transform.position = VertexPosition();
                muzzleGlow.Intensity = intensity;
            }

            if (enemy.Shape != null)
            {
                enemy.Shape.SetCornerGlow(intensity > 0.01f ? corner : -1, Mathf.Min(intensity, 1f), vertexGlowColor);
            }
        }

        private void SetWarningVisible(bool visible)
        {
            if (warningLine != null) warningLine.enabled = visible;
        }

        private void HideAll()
        {
            if (warningLine != null) warningLine.enabled = false;
            if (beamGlow != null) beamGlow.enabled = false;
            if (beamCore != null) beamCore.enabled = false;
            if (impactGlow != null) impactGlow.Intensity = 0f;
            if (muzzleGlow != null) muzzleGlow.Intensity = 0f;
        }

        private static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        #endregion
    }
}
