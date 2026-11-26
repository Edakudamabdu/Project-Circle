using System.Collections.Generic;
using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Enemies
{
    /// <summary>
    /// A triangle's orb attack: it leaps into the air and fires a <see cref="HomingOrb"/> from each of its
    /// three vertices. The orbs hunt the player for a while, then lose interest and fly straight.
    /// Meant to be dodged with a double jump or by dashing through an orb.
    ///
    /// Sequence (timings in the Inspector):
    /// 1. Crouch  — it squats; an orb starts to grow on every corner.
    /// 2. Leap    — it jumps straight up, spinning half a turn so its corners fan out.
    /// 3. Release — it hangs at the top for a moment and lets the orbs go, each flung out from its corner.
    /// 4. Fall    — it finishes the spin and drops back down.
    /// 5. Recover — a short pause on landing before it moves again.
    ///
    /// Use this instead of <see cref="TriangleLaser"/> (not as well as) on a <see cref="TriangleEnemy"/>.
    /// It attacks whenever <see cref="TriangleEnemy.IsInAttackPosition"/> is true and the cooldown has passed.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // after TriangleEnemy, so the corner positions are final for the frame
    [RequireComponent(typeof(TriangleEnemy), typeof(Rigidbody2D))]
    public sealed class TriangleVolley : MonoBehaviour
    {
        private enum Phase { Ready, Crouch, Leap, Release, Fall, Recover }

        [Tooltip("Vertex-colour material for the orbs (e.g. ShapeUnlit).")]
        [SerializeField] private Material orbMaterial;

        [Header("Timing")]
        [Tooltip("Wait after first getting into attack position before the first volley.")]
        [SerializeField, Min(0f)] private float firstShotDelay = 0.3f;

        [Tooltip("Time between the end of one volley and the start of the next.")]
        [SerializeField, Min(0f)] private float cooldown = 2.4f;

        [Tooltip("Squat before the leap. The orbs start growing here: the player's first cue.")]
        [SerializeField, Min(0.05f)] private float crouchTime = 0.3f;

        [Tooltip("How long it hangs at the top of the leap. The orbs are released halfway through.")]
        [SerializeField, Min(0.05f)] private float hangTime = 0.3f;

        [SerializeField, Min(0.05f)] private float recoverTime = 0.3f;

        [Tooltip("After being hit while not attacking, it fires back within this many seconds (as soon as it is in position).")]
        [SerializeField, Min(0f)] private float retaliationDelay = 0.6f;

        [Header("Leap")]
        [Tooltip("How high it jumps before releasing the orbs. Keep it low: orbs released near the player's height come in from the side, so a double jump can clear them (from high up they come down onto a jumping player).")]
        [SerializeField, Min(0.2f)] private float leapHeight = 1.3f;

        [Tooltip("How far it squats down before leaping.")]
        [SerializeField, Min(0f)] private float crouchDepth = 0.12f;

        [Header("Orbs")]
        [SerializeField] private HomingOrb.Settings orbs = HomingOrb.Settings.Default;

        /// <summary>True from the crouch until it has recovered from landing.</summary>
        public bool IsAttacking => phase != Phase.Ready;

        private TriangleEnemy enemy;
        private Rigidbody2D body;
        private EnemyHealth health;
        private Phase phase = Phase.Ready;
        private float phaseStartTime;
        private float nextAttackTime;
        private bool wasInPosition;
        private bool released;
        private float savedGravityScale;
        private float riseTime;
        private readonly List<HomingOrb> forming = new List<HomingOrb>(3);

        private float PhaseTime => Time.time - phaseStartTime;

        #region Unity lifecycle

        private void Awake()
        {
            enemy = GetComponent<TriangleEnemy>();
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<EnemyHealth>();
            orbs.color = TriangleKinds.Tint(orbs.color, TriangleKinds.HueShiftOf(this)); // orbs in its owner's colour
        }

        private void OnEnable()
        {
            if (health != null) health.Hit += HandleHit;
        }

        private void OnDisable()
        {
            if (health != null) health.Hit -= HandleHit;
            if (phase != Phase.Ready) Abort();
        }

        private void LateUpdate()
        {
            if (phase == Phase.Ready)
            {
                TryStartAttack();
                return;
            }

            // Something else ended the attack (e.g. an unarmored triangle was hit and stunned).
            if (!enemy.IsAttackLocked)
            {
                Abort();
                return;
            }

            switch (phase)
            {
                case Phase.Crouch: UpdateCrouch(); break;
                case Phase.Leap: UpdateLeap(); break;
                case Phase.Release: UpdateRelease(); break;
                case Phase.Fall: UpdateFall(); break;
                case Phase.Recover: UpdateRecover(); break;
            }
        }

        #endregion

        /// <summary>Hitting it while it isn't attacking provokes a quicker volley.</summary>
        private void HandleHit(int damage, Vector2 source)
        {
            if (phase == Phase.Ready) nextAttackTime = Mathf.Min(nextAttackTime, Time.time + retaliationDelay);
        }

        #region Phases

        private void TryStartAttack()
        {
            bool inPosition = enemy.IsInAttackPosition;
            if (inPosition && !wasInPosition) nextAttackTime = Mathf.Max(nextAttackTime, Time.time + firstShotDelay);
            wasInPosition = inPosition;

            if (!inPosition || Time.time < nextAttackTime || enemy.Target == null) return;
            if (!enemy.BeginAttack()) return;

            // An orb starts growing on every corner.
            forming.Clear();
            for (int c = 0; c < 3; c++)
            {
                forming.Add(HomingOrb.Spawn(CornerPosition(c), orbs, orbMaterial, enemy.Target));
            }

            released = false;
            SetPhase(Phase.Crouch);
        }

        private void UpdateCrouch()
        {
            float p = Mathf.Clamp01(PhaseTime / crouchTime);
            enemy.SetAttackPose(new Vector2(0f, -crouchDepth * Ease(p)), 0f);
            UpdateFormingOrbs(0.35f * p);

            if (p < 1f) return;

            // Jump straight up to the leap height.
            float gravity = Mathf.Abs(Physics2D.gravity.y) * body.gravityScale;
            float launchSpeed = Mathf.Sqrt(2f * gravity * leapHeight);
            riseTime = gravity > 0f ? launchSpeed / gravity : 0.3f;
            body.linearVelocity = new Vector2(0f, launchSpeed);
            SetPhase(Phase.Leap);
        }

        private void UpdateLeap()
        {
            // Spin half a turn on the way up, so the corners point down, up-left and up-right at the top.
            float p = Mathf.Clamp01(PhaseTime / riseTime);
            enemy.SetAttackPose(Vector2.zero, 180f * Ease(p));
            UpdateFormingOrbs(0.35f + 0.65f * p);

            if (body.linearVelocity.y > 0.05f && p < 1f) return;

            // At the top: hang in the air.
            savedGravityScale = body.gravityScale;
            body.gravityScale = 0f;
            body.linearVelocity = Vector2.zero;
            SetPhase(Phase.Release);
        }

        private void UpdateRelease()
        {
            enemy.SetAttackPose(Vector2.zero, 180f);
            if (!released) UpdateFormingOrbs(1f);

            if (!released && PhaseTime >= hangTime * 0.5f)
            {
                released = true;
                Vector2 centre = enemy.Visual.position;
                foreach (HomingOrb orb in forming)
                {
                    if (orb == null) continue;
                    orb.Release((Vector2)orb.transform.position - centre);
                }
                forming.Clear();
                ClearCornerGlow();
            }

            if (PhaseTime < hangTime) return;

            body.gravityScale = savedGravityScale;
            SetPhase(Phase.Fall);
        }

        private void UpdateFall()
        {
            // Finish the turn while dropping (a full turn looks the same as no turn).
            float p = Mathf.Clamp01(PhaseTime / Mathf.Max(0.1f, riseTime));
            enemy.SetAttackPose(Vector2.zero, 180f + 180f * Ease(p));

            if (PhaseTime > 0.05f && enemy.IsGrounded && body.linearVelocity.y <= 0.01f) SetPhase(Phase.Recover);
        }

        private void UpdateRecover()
        {
            enemy.SetAttackPose(Vector2.zero, 0f);
            if (PhaseTime >= recoverTime) Finish();
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStartTime = Time.time;
        }

        private void Finish()
        {
            phase = Phase.Ready;
            nextAttackTime = Time.time + cooldown / Mathf.Max(0.1f, enemy.EffectiveHaste); // an Overseer makes it attack more often
            if (enemy != null) enemy.EndAttack();
        }

        /// <summary>Stops mid-volley: unreleased orbs fizzle, gravity is restored.</summary>
        private void Abort()
        {
            foreach (HomingOrb orb in forming)
            {
                if (orb != null) orb.Cancel();
            }
            forming.Clear();
            ClearCornerGlow();

            if (phase == Phase.Release) body.gravityScale = savedGravityScale;

            bool wasLocked = enemy != null && enemy.IsAttackLocked;
            phase = Phase.Ready;
            nextAttackTime = Time.time + cooldown * 0.5f / Mathf.Max(0.1f, enemy.EffectiveHaste);
            if (wasLocked) enemy.EndAttack();
        }

        #endregion

        #region Helpers

        private void UpdateFormingOrbs(float progress)
        {
            for (int c = 0; c < forming.Count; c++)
            {
                if (forming[c] != null) forming[c].SetForming(CornerPosition(c), progress);
            }
            // The vertices the orbs grow on glow with the power being drawn out of them.
            if (enemy.Shape != null) enemy.Shape.SetCornerGlow(RoundedTriangleMesh.AllCorners, progress, orbs.color);
        }

        private void ClearCornerGlow()
        {
            if (enemy != null && enemy.Shape != null) enemy.Shape.SetCornerGlow(-1, 0f, orbs.color);
        }

        /// <summary>World position just outside the tip of a corner.</summary>
        private Vector2 CornerPosition(int corner)
        {
            if (enemy.Shape == null || enemy.Visual == null) return transform.position;
            Vector2 local = enemy.Shape.GetCornerPoint(corner, -orbs.glowRadius * 0.4f); // a negative inset pushes it past the tip
            return enemy.Visual.TransformPoint(local);
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        #endregion
    }
}
