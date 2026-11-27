using System.Collections.Generic;
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

        [Header("Room Edge (camera)")]
        [Tooltip("For an opening at the left or right end of a room: the camera stops at this door's line (yellow in the " +
                 "Scene view) so the circle walks off-screen into the gap and the next room is never shown. " +
                 "Moving the door moves the stop with it. None for doorways and hatches in the middle of a room.")]
        [SerializeField] private CameraStopSide cameraStop = CameraStopSide.None;

        [Tooltip("How far the camera stop sits from this door's position, towards the room (usually the wall's outer face).")]
        [SerializeField] private float cameraStopOffset = 1f;

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

        /// <summary>Which end of the room an opening is at (for the camera stop).</summary>
        public enum CameraStopSide { None, Left, Right }

        private static readonly List<SceneDoor> active = new List<SceneDoor>();

        /// <summary>Every enabled door in the loaded scenes.</summary>
        public static IReadOnlyList<SceneDoor> Active => active;

        public string DoorId => doorId;
        public string TargetScene => targetScene;
        public string TargetDoorId => targetDoorId;

        /// <summary>Which side of the room this opening is on (None = not a room edge).</summary>
        public CameraStopSide CameraStop => cameraStop;

        /// <summary>World x the camera's view stops at for this opening.</summary>
        public float CameraStopX => transform.position.x + (cameraStop == CameraStopSide.Right ? cameraStopOffset : -cameraStopOffset);

        // Enter Play Mode Options skip the domain reload, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pendingArrivalDoor = null;
            active.Clear();
        }

        private void OnEnable() => active.Add(this);
        private void OnDisable() => active.Remove(this);

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

            // Safety net for edited levels: if the arrival point ended up inside a wall or platform, lift the circle
            // out (up to 6 units) rather than trapping it in the geometry.
            var playerCollider = player.GetComponent<Collider2D>();
            float radius = playerCollider != null ? Mathf.Max(playerCollider.bounds.extents.x, playerCollider.bounds.extents.y) : 0.5f;
            int solid = LayerMask.GetMask("Ground");
            for (int i = 0; i < 24 && Physics2D.OverlapCircle(point, radius * 0.95f, solid) != null; i++) point += Vector2.up * 0.25f;
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

            // The camera stop for a room-edge opening.
            if (cameraStop != CameraStopSide.None)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                Gizmos.DrawLine(new Vector3(CameraStopX, transform.position.y - 6f), new Vector3(CameraStopX, transform.position.y + 14f));
            }

            // Where it leads, so the links can be read straight off the Scene view.
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f,
                $"{doorId}  →  {(string.IsNullOrEmpty(targetScene) ? "(nowhere)" : targetScene)} / {targetDoorId}");
        }
#endif
    }
}
