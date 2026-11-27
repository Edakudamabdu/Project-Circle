using ProjectCircle.Combat;
using ProjectCircle.Systems;
using ProjectCircle.Systems.Story;
using UnityEngine;

namespace ProjectCircle.Player
{
    /// <summary>
    /// The circle's visual feedback, driven by events from its other components (nothing here changes gameplay):
    /// - Landing puff (bigger after a long fall), dash speed lines, shards when the cape platform breaks.
    /// - Hit sparks where the cape strikes (a bigger spray on a boss).
    /// - Parry: a sharp ring, a white flash and a small camera punch-in.
    /// - Taking damage: a red screen-edge pulse and pale shards knocked off the circle.
    /// - Last health point: the screen edges throb like a heartbeat.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFx : MonoBehaviour
    {
        [SerializeField] private Color sparkColor = new Color(1f, 0.85f, 0.5f, 1f);
        [SerializeField] private Color bodyColor = new Color(0.95f, 0.92f, 0.82f, 1f);
        [SerializeField] private Color parryColor = new Color(0.85f, 0.95f, 1f, 1f);
        [SerializeField] private Color hurtColor = new Color(0.85f, 0.1f, 0.15f, 1f);

        [Tooltip("Enemies with at least this much max health count as big (bosses): bigger hit sparks.")]
        [SerializeField, Min(1)] private int bigEnemyHealth = 20;

        private PlayerMovement movement;
        private PlayerAttack attack;
        private PlayerParry parry;
        private PlayerHealth health;
        private CameraFollow2D cameraFollow;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            attack = GetComponent<PlayerAttack>();
            parry = GetComponent<PlayerParry>();
            health = GetComponent<PlayerHealth>();

            // The sounds live in their own component; make sure every scene's circle has it.
            if (GetComponent<PlayerAudio>() == null) gameObject.AddComponent<PlayerAudio>();
        }

        private void Start() => cameraFollow = FindAnyObjectByType<CameraFollow2D>();

        private void OnEnable()
        {
            if (movement != null)
            {
                movement.Landed += HandleLanded;
                movement.Dashed += HandleDashed;
                movement.AirJumped += HandleAirJumped;
            }
            if (attack != null) attack.HitLanded += HandleHitLanded;
            if (parry != null) parry.Deflected += HandleDeflected;
            if (health != null)
            {
                health.Hit += HandleHurt;
                health.HealthChanged += HandleHealthChanged;
            }
        }

        private void OnDisable()
        {
            if (movement != null)
            {
                movement.Landed -= HandleLanded;
                movement.Dashed -= HandleDashed;
                movement.AirJumped -= HandleAirJumped;
            }
            if (attack != null) attack.HitLanded -= HandleHitLanded;
            if (parry != null) parry.Deflected -= HandleDeflected;
            if (health != null)
            {
                health.Hit -= HandleHurt;
                health.HealthChanged -= HandleHealthChanged;
            }
        }

        private Vector2 Feet => (Vector2)transform.position + Vector2.down * (movement != null ? movement.Radius : 0.5f);

        private void HandleLanded(float fallSpeed)
        {
            if (fallSpeed < 4f) return; // small hops don't kick up dust
            float size = Mathf.Lerp(0.6f, 1.6f, Mathf.InverseLerp(4f, 22f, fallSpeed));
            Fx.Puff(Feet, size);
        }

        private void HandleDashed() => Fx.SpeedLines(transform.position, movement.FacingDirection, new Color(1f, 1f, 1f, 0.6f));

        private void HandleAirJumped(Vector2 platformPoint)
        {
            // The cape platform breaks apart under the circle.
            Fx.Burst(platformPoint, new Fx.BurstSettings
            {
                count = 10, colorA = new Color(0.45f, 0.08f, 0.12f, 0.9f), colorB = new Color(0.8f, 0.2f, 0.24f, 0.9f), // the red cape
                speedMin = 1.5f, speedMax = 4f, sizeMin = 0.08f, sizeMax = 0.18f, lifeMin = 0.25f, lifeMax = 0.45f,
                direction = -90f, spread = 70f, gravity = 1f, drag = 2f, area = new Vector2(1f, 0.1f), triangles = true, sortingOrder = 11,
            });
        }

        private void HandleHitLanded(Collider2D target)
        {
            Vector2 point = target.ClosestPoint(transform.position);
            Vector2 away = point - (Vector2)transform.position;
            float direction = Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg;

            var enemy = target.GetComponentInParent<EnemyHealth>();
            bool big = enemy != null && enemy.MaxHealth >= bigEnemyHealth;
            Fx.Sparks(point, direction, sparkColor, big ? 16 : 9, big ? 12f : 9f);
            if (big) Fx.Ring(point, new Color(1f, 0.9f, 0.7f, 0.7f), 0.8f, 0.18f, 0.08f);
        }

        private void HandleDeflected(Vector2 point)
        {
            Fx.Ring(transform.position, parryColor, 2.2f, 0.3f, 0.18f);
            Fx.Sparks(point, 0f, parryColor, 14, 10f, 180f);
            CinematicOverlay.Instance.Flash(Color.white, 0.12f, 0.12f);
            if (cameraFollow != null) cameraFollow.Punch(0.06f, 0.25f);
        }

        private void HandleHurt(int damage, Vector2 source)
        {
            CinematicOverlay.Instance.EdgePulse(hurtColor, 0.9f, 0.45f);
            Fx.Shards(transform.position, bodyColor, 8, 5f, 0.14f);
        }

        private void HandleHealthChanged(int current, int max) =>
            CinematicOverlay.Instance.SetLowHealth(current == 1 && max > 1);
    }
}
