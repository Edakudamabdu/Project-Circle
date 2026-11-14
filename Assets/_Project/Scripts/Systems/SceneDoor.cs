using ProjectCircle.Player;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// A doorway to another scene. Touching it fades the screen to black, loads the target scene and puts
    /// the circle next to the matching door there.
    ///
    /// Setup: give every door a <see cref="doorId"/> that is unique within its scene, then point
    /// <see cref="targetScene"/> and <see cref="targetDoorId"/> at the door it should lead to. The target
    /// scene must be in the Build Profile's scene list (File > Build Profiles > Scene List).
    ///
    /// A door only works once the circle has stepped clear of it, so arriving next to a door can never
    /// bounce you straight back.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SceneDoor : MonoBehaviour
    {
        [Header("This Door")]
        [Tooltip("Name of this door. Doors in other scenes use it to send the circle here.")]
        [SerializeField] private string doorId = "A";

        [Tooltip("Where the circle appears when it arrives through this door, relative to the door. Keep it outside the door's box.")]
        [SerializeField] private Vector2 arrivalOffset = new Vector2(1.6f, 0f);

        [Header("Leads To")]
        [Tooltip("Scene to load (its file name without .unity, e.g. StoryTest).")]
        [SerializeField] private string targetScene;

        [Tooltip("Door Id of the door to arrive at in that scene.")]
        [SerializeField] private string targetDoorId = "A";

        [Header("Detection & Feel")]
        [SerializeField] private LayerMask playerLayers = 1 << 8;

        [Tooltip("Seconds to fade to black (and back in).")]
        [SerializeField, Min(0.01f)] private float fadeTime = 0.25f;

        [Tooltip("Optional sprite that gently pulses so the door reads as something to walk into.")]
        [SerializeField] private SpriteRenderer glow;

        [SerializeField, Min(0f)] private float glowPulseSpeed = 1.6f;

        /// <summary>Door to arrive at after the next scene load (null = arrive wherever the scene places the player).</summary>
        private static string pendingArrivalDoor;

        private BoxCollider2D box;
        private bool armed;
        private Color glowBaseColor;

        // Enter Play Mode Options skip the domain reload, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => pendingArrivalDoor = null;

        private void Awake()
        {
            box = GetComponent<BoxCollider2D>();
            box.isTrigger = true;
            if (glow != null) glowBaseColor = glow.color;
        }

        private void Start()
        {
            if (pendingArrivalDoor == null || pendingArrivalDoor != doorId) return;
            pendingArrivalDoor = null;
            PlaceArrivingPlayer();
        }

        private void FixedUpdate()
        {
            bool touching = Physics2D.OverlapBox(box.bounds.center, box.bounds.size, 0f, playerLayers) != null;

            // Only arm once the circle is clear of the door (e.g. after arriving next to it).
            if (!touching)
            {
                armed = true;
                return;
            }

            if (armed && !ScreenFader.IsBusy && !string.IsNullOrEmpty(targetScene))
            {
                armed = false;
                pendingArrivalDoor = targetDoorId;
                ScreenFader.LoadScene(targetScene, fadeTime);
            }
        }

        private void Update()
        {
            if (glow == null) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * glowPulseSpeed * Mathf.PI * 2f);
            Color c = glowBaseColor;
            c.a *= pulse;
            glow.color = c;
        }

        /// <summary>Moves the circle out in front of this door and settles the camera and cape on it.</summary>
        private void PlaceArrivingPlayer()
        {
            PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
            if (player == null) return;

            Vector2 point = (Vector2)transform.position + arrivalOffset;
            var body = player.GetComponent<Rigidbody2D>();
            body.position = point;
            player.transform.position = point;
            player.ResetMotion();

            PlayerCape cape = player.GetComponentInChildren<PlayerCape>();
            if (cape != null) cape.ResetCape();

            CameraFollow2D cameraFollow = FindAnyObjectByType<CameraFollow2D>();
            if (cameraFollow != null) cameraFollow.SnapToTarget();

            armed = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Where the circle arrives.
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + (Vector3)arrivalOffset, 0.5f);
        }
#endif
    }
}
