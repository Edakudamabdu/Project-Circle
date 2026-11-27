using ProjectCircle.Player;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// A cracked wall that breaks when the circle dashes into it (the "D" gates on the map). Once broken it
    /// stays broken (saved as the flag "Wall." + <see cref="wallId"/>).
    ///
    /// Setup: a solid block on the Ground layer with a BoxCollider2D (not part of the Level's composite, so it
    /// can disappear on its own). Children are looks.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class CrumblingWall : MonoBehaviour
    {
        [Tooltip("Unique name in the game, so it stays broken (e.g. SlumsToFissure).")]
        [SerializeField] private string wallId = "Wall";

        [Tooltip("How close (beyond the wall's edge) a dashing circle must be to break it.")]
        [SerializeField, Min(0f)] private float reach = 0.35f;

        [SerializeField] private Color debrisColor = new Color(0.55f, 0.5f, 0.45f, 1f);

        private Collider2D col;
        private PlayerMovement player;
        private string Flag => "Wall." + wallId;

        private void Awake() => col = GetComponent<Collider2D>();

        private void Start()
        {
            player = FindAnyObjectByType<PlayerMovement>();
            if (GameProgress.Has(Flag)) gameObject.SetActive(false);
        }

        private void FixedUpdate()
        {
            if (player == null || !player.IsDashing) return;
            Bounds b = col.bounds;
            b.Expand(new Vector3(reach * 2f + player.Radius * 2f, player.Radius * 2f, 0f));
            if (!b.Contains(player.transform.position)) return;
            Break();
        }

        private void Break()
        {
            GameProgress.Set(Flag);
            Bounds b = col.bounds;
            for (int i = 0; i < 4; i++)
                Fx.Shards(new Vector2(b.center.x, b.min.y + b.size.y * (i + 0.5f) / 4f), debrisColor, 8, 7f, 0.22f);
            Fx.Dust(new Vector2(b.center.x, b.min.y), b.size.x + 2f, 1.4f);
            Sfx.Play(SoundId.WallCrumble, b.center);
            var cam = FindAnyObjectByType<CameraFollow2D>();
            if (cam != null) cam.Shake(0.25f, 0.3f);
            gameObject.SetActive(false);
        }
    }
}
