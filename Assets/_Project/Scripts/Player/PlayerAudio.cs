using ProjectCircle.Combat;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// The circle's sounds, driven by events from its other components (like <see cref="PlayerFx"/> for visuals):
    /// swings and hits, jumps, the cape air jump, dash, landings, parries, getting hurt, dying and mending.
    /// Add it to the Player object; the sounds themselves are set in the SoundLibrary asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAudio : MonoBehaviour
    {
        [Tooltip("Landings faster than this (units/s) make a sound; small hops stay quiet.")]
        [SerializeField, Min(0f)] private float landSoundSpeed = 9f;

        [Tooltip("Enemies with at least this much max health count as big (bosses): a deeper hit.")]
        [SerializeField, Min(1)] private int bigEnemyHealth = 20;

        private PlayerMovement movement;
        private PlayerAttack attack;
        private PlayerParry parry;
        private PlayerHealth health;
        private PlayerMend mend;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            attack = GetComponent<PlayerAttack>();
            parry = GetComponent<PlayerParry>();
            health = GetComponent<PlayerHealth>();
            mend = GetComponent<PlayerMend>();
        }

        private void OnEnable()
        {
            if (movement != null)
            {
                movement.Jumped += HandleJumped;
                movement.AirJumped += HandleAirJumped;
                movement.Dashed += HandleDashed;
                movement.Landed += HandleLanded;
            }
            if (attack != null)
            {
                attack.Attacked += HandleAttacked;
                attack.HitLanded += HandleHitLanded;
                attack.Parried += HandleGlanced;
            }
            if (parry != null) parry.Deflected += HandleDeflected;
            if (health != null)
            {
                health.Hit += HandleHurt;
                health.Died += HandleDied;
            }
            if (mend != null) mend.Mended += HandleMended;
        }

        private void OnDisable()
        {
            if (movement != null)
            {
                movement.Jumped -= HandleJumped;
                movement.AirJumped -= HandleAirJumped;
                movement.Dashed -= HandleDashed;
                movement.Landed -= HandleLanded;
            }
            if (attack != null)
            {
                attack.Attacked -= HandleAttacked;
                attack.HitLanded -= HandleHitLanded;
                attack.Parried -= HandleGlanced;
            }
            if (parry != null) parry.Deflected -= HandleDeflected;
            if (health != null)
            {
                health.Hit -= HandleHurt;
                health.Died -= HandleDied;
            }
            if (mend != null) mend.Mended -= HandleMended;
        }

        private void HandleJumped() => Sfx.Play(SoundId.Jump);
        private void HandleAirJumped(Vector2 platformPoint) => Sfx.Play(SoundId.AirJump);
        private void HandleDashed() => Sfx.Play(SoundId.Dash);

        private void HandleLanded(float fallSpeed)
        {
            if (fallSpeed < landSoundSpeed) return;
            // Harder landings are a little louder.
            Sfx.Play(SoundId.Land, Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(landSoundSpeed, 25f, fallSpeed)));
        }

        private void HandleAttacked(AttackDirection direction) => Sfx.Play(SoundId.Swing);

        private void HandleHitLanded(Collider2D target)
        {
            var enemy = target.GetComponentInParent<EnemyHealth>();
            bool big = enemy != null && enemy.MaxHealth >= bigEnemyHealth;
            Sfx.Play(SoundId.EnemyHit, target.ClosestPoint(transform.position), big ? 1f : 0.85f, big ? 0.8f : 1f);
        }

        // The blade glancing off a shield: a sharp metallic snap.
        private void HandleGlanced(Vector2 point) => Sfx.Play(SoundId.ParrySnap, point, 0.8f, 0.85f);

        private void HandleDeflected(Vector2 point)
        {
            Sfx.Play(SoundId.ParrySnap);
            Sfx.Play(SoundId.ParryRing);
        }

        private void HandleHurt(int damage, Vector2 source) => Sfx.Play(SoundId.PlayerHurt);
        private void HandleDied() => Sfx.Play(SoundId.PlayerDeath);
        private void HandleMended() => Sfx.Play(SoundId.Mend);
    }
}
