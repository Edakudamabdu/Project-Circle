using System.Collections;
using ProjectCircle.Player;
using ProjectCircle.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// A Mirror: the game's checkpoint, and the way the mute circle's thoughts are shown.
    ///
    /// Walk up to it and press Interact. The circle steps in front of the glass and the camera moves in
    /// behind it: you see the circle's back (its cape drawn over it, no eye) and, in the glass, its
    /// reflection looking back. A menu offers:
    /// - Rest    — restores full health and makes this Mirror the respawn point.
    /// - Reflect — shows the circle's thoughts (the next of <see cref="thoughts"/>) in the dialogue box.
    /// - Leave   — steps away; the camera returns to normal.
    ///
    /// The reflection is live: <see cref="reflectionCamera"/> films the circle into a texture shown on
    /// <see cref="glass"/>, flipped like a real mirror. It only runs while the Mirror is in use.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MirrorCheckpoint : MonoBehaviour
    {
        private const int OptionRest = 0;
        private const int OptionReflect = 1;
        private const int OptionLeave = 2;

        [Header("Identity")]
        [Tooltip("Shown above the Mirror and as the menu title.")]
        [SerializeField] private string mirrorName = "Mirror";

        [Tooltip("The circle's thoughts here, shown by Reflect. Played in order; the last one repeats.")]
        [SerializeField] private Dialogue[] thoughts;

        [Header("References")]
        [Tooltip("Where the circle stands while using the Mirror (its centre, in front of the glass).")]
        [SerializeField] private Transform standPoint;

        [Tooltip("Where the camera looks while the Mirror is in use.")]
        [SerializeField] private Transform cameraFocus;

        [Tooltip("The glass. Its material shows the reflection texture.")]
        [SerializeField] private Renderer glass;

        [Tooltip("Films the circle for the reflection. Disabled except while in use.")]
        [SerializeField] private Camera reflectionCamera;

        [SerializeField] private MirrorMenu menu;

        [Tooltip("The Mirror's name, shown while the circle is near.")]
        [SerializeField] private TMP_Text nameLabel;

        [Tooltip("Prompt shown while the circle is near (e.g. 'Look').")]
        [SerializeField] private TMP_Text prompt;

        [Header("Feel")]
        [Tooltip("How close the circle must be to use the Mirror.")]
        [SerializeField, Min(0.1f)] private float useRadius = 1.6f;

        [Tooltip("Camera zoom (orthographic size) while in use. Smaller = closer.")]
        [SerializeField, Min(0.5f)] private float cameraZoom = 2.6f;

        [Tooltip("Seconds for the circle to step in front of the glass.")]
        [SerializeField, Min(0.01f)] private float stepTime = 0.3f;

        [SerializeField, Range(64, 1024)] private int reflectionResolution = 320;

        [Tooltip("Path from the Player to its eye, hidden when seen from behind.")]
        [SerializeField] private string playerEyePath = "Visual/Body/RollMarker";

        [Tooltip("Glass colour while not in use (no reflection).")]
        [SerializeField] private Color idleGlassColor = new Color(0.55f, 0.62f, 0.75f, 1f);

        private PlayerMovement player;
        private PlayerInputReader input;
        private PlayerHealth health;
        private PlayerCape cape;
        private Renderer capeRenderer;
        private Renderer playerBody;
        private Renderer playerEye;
        private CameraFollow2D cameraFollow;
        private DialogueBox dialogueBox;

        private RenderTexture reflectionTexture;
        private Material glassMaterial;
        private bool inUse;
        private float leftTime = float.NegativeInfinity;
        private int thoughtsShown;
        private float labelAlpha;
        private float restFlashTime = float.NegativeInfinity;

        #region Unity lifecycle

        private void Start()
        {
            player = FindAnyObjectByType<PlayerMovement>();
            if (player != null)
            {
                input = player.GetComponent<PlayerInputReader>();
                health = player.GetComponent<PlayerHealth>();
                cape = player.GetComponentInChildren<PlayerCape>();
                capeRenderer = cape != null ? cape.GetComponent<Renderer>() : null;
                Transform eye = player.transform.Find(playerEyePath);
                playerEye = eye != null ? eye.GetComponent<Renderer>() : null;
                playerBody = eye != null && eye.parent != null ? eye.parent.GetComponent<Renderer>() : null;
                input.InteractPressed += HandleInteract;
            }

            cameraFollow = FindAnyObjectByType<CameraFollow2D>();
            dialogueBox = FindAnyObjectByType<DialogueBox>();

            if (glass != null)
            {
                glassMaterial = glass.material; // an instance, so each Mirror has its own texture
                SetGlassIdle();
            }

            if (reflectionCamera != null) reflectionCamera.enabled = false;
            if (menu != null) menu.OptionChosen += HandleOption;
            if (nameLabel != null) nameLabel.text = mirrorName;
            SetLabelAlpha(0f);

            ApplyCheckpointOnSceneStart();
        }

        /// <summary>
        /// If this Mirror is the recorded checkpoint: dying in this scene respawns here, and if the circle is
        /// arriving because it died elsewhere, it appears in front of this Mirror now.
        /// </summary>
        private void ApplyCheckpointOnSceneStart()
        {
            if (player == null || !CheckpointRecord.Is(gameObject.scene.name, mirrorName)) return;

            if (health != null) health.SetRespawnPoint(standPoint.position);
            if (!CheckpointRecord.RespawnPending) return;

            CheckpointRecord.RespawnPending = false;
            var body = player.GetComponent<Rigidbody2D>();
            body.position = standPoint.position;
            player.transform.position = standPoint.position;
            player.ResetMotion();
            if (cape != null) cape.ResetCape();
            if (cameraFollow != null) cameraFollow.SnapToTarget();
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            if (inUse) EndUse();
        }

        private void OnDestroy()
        {
            if (input != null) input.InteractPressed -= HandleInteract;
            if (menu != null) menu.OptionChosen -= HandleOption;
            if (reflectionTexture != null) reflectionTexture.Release();
            if (glassMaterial != null) Destroy(glassMaterial);
        }

        private void Update()
        {
            // Name and prompt fade in while the circle is close enough to use the Mirror.
            float target = !inUse && InRange() ? 1f : 0f;
            labelAlpha = Mathf.MoveTowards(labelAlpha, target, Time.unscaledDeltaTime / 0.2f);
            SetLabelAlpha(labelAlpha);

            if (!inUse) return;

            // Safe while at the Mirror.
            if (health != null) health.GrantGrace(0.2f);

            // The menu waits while thoughts are being shown, and ignores the press that closed them.
            if (menu != null && dialogueBox != null)
            {
                menu.Interactable = !dialogueBox.IsOpen && Time.unscaledTime > dialogueBox.LastClosedTime + 0.15f;
            }

            UpdateRestFlash();
        }

        #endregion

        #region Using the Mirror

        private void HandleInteract()
        {
            if (inUse || player == null || !InRange() || (dialogueBox != null && dialogueBox.IsOpen)) return;
            if (Time.unscaledTime < leftTime + 0.3f) return; // the press that chose Leave mustn't step straight back in
            StartCoroutine(BeginUse());
        }

        private IEnumerator BeginUse()
        {
            inUse = true;
            input.AddControlLock(this);

            // Step in front of the glass.
            var body = player.GetComponent<Rigidbody2D>();
            Vector2 from = body.position;
            Vector2 to = new Vector2(standPoint.position.x, from.y);
            for (float t = 0f; t < stepTime; t += Time.deltaTime)
            {
                float e = t / stepTime;
                body.position = Vector2.Lerp(from, to, e * e * (3f - 2f * e));
                body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
                yield return null;
            }
            body.position = to;
            player.ResetMotion();

            // The glass ripples as the circle steps up to it.
            if (glass != null)
            {
                Fx.Ring(glass.bounds.center, new Color(0.85f, 0.92f, 1f, 0.7f), glass.bounds.extents.y * 1.1f, 0.6f, 0.08f);
                Fx.Ring(glass.bounds.center, new Color(0.85f, 0.92f, 1f, 0.5f), glass.bounds.extents.y * 0.7f, 0.45f, 0.06f);
            }

            // Camera behind the circle, live reflection on.
            if (cameraFollow != null) cameraFollow.FocusOn(cameraFocus.position, cameraZoom);
            StartReflection();

            if (menu != null) menu.Open(mirrorName);
        }

        private void HandleOption(int option)
        {
            if (!inUse) return;

            switch (option)
            {
                case OptionRest: Rest(); break;
                case OptionReflect: Reflect(); break;
                case OptionLeave: EndUse(); break;
            }
        }

        private void Rest()
        {
            if (health != null)
            {
                health.Heal(health.MaxHealth);
                health.SetRespawnPoint(standPoint.position);
            }

            // Remembered across scenes: dying anywhere brings the circle back here.
            CheckpointRecord.Set(gameObject.scene.name, mirrorName);

            restFlashTime = Time.unscaledTime;
            if (menu != null) menu.ShowStatus("Rested. Your reflection will wait for you here.", 2.5f);
        }

        private void Reflect()
        {
            if (dialogueBox == null || thoughts == null || thoughts.Length == 0) return;

            Dialogue next = thoughts[Mathf.Min(thoughtsShown, thoughts.Length - 1)];
            thoughtsShown++;
            dialogueBox.Play(next);
        }

        private void EndUse()
        {
            inUse = false;
            leftTime = Time.unscaledTime;
            if (menu != null) menu.Close();
            StopReflection();
            if (cameraFollow != null) cameraFollow.ClearFocus();
            if (isActiveAndEnabled) StartCoroutine(ReleaseControlsNextFrame());
            else if (input != null) input.RemoveControlLock(this);
        }

        /// <summary>Hands control back a frame later, so the press that chose Leave doesn't also jump or attack.</summary>
        private IEnumerator ReleaseControlsNextFrame()
        {
            yield return null;
            if (!inUse && input != null) input.RemoveControlLock(this);
        }

        private bool InRange() =>
            player != null && Vector2.Distance(player.transform.position, standPoint.position) <= useRadius;

        #endregion

        #region Reflection

        private void StartReflection()
        {
            if (reflectionCamera == null || glassMaterial == null) return;

            if (reflectionTexture == null)
            {
                // Same shape as the glass, so the reflection isn't stretched.
                Vector3 size = glass.bounds.size;
                int height = reflectionResolution;
                int width = Mathf.Max(16, Mathf.RoundToInt(height * size.x / Mathf.Max(0.01f, size.y)));
                reflectionTexture = new RenderTexture(width, height, 16) { name = mirrorName + " Reflection" };
            }

            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.enabled = true;

            glassMaterial.SetTexture("_BaseMap", reflectionTexture);
            glassMaterial.SetColor("_BaseColor", Color.white);
            // Flip horizontally, like a real mirror.
            glassMaterial.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
            glassMaterial.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
        }

        private void StopReflection()
        {
            if (reflectionCamera != null) reflectionCamera.enabled = false;
            SetGlassIdle();
        }

        private void SetGlassIdle()
        {
            if (glassMaterial == null) return;
            glassMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            glassMaterial.SetColor("_BaseColor", idleGlassColor);
        }

        /// <summary>A soft brightening of the glass after resting.</summary>
        private void UpdateRestFlash()
        {
            if (glassMaterial == null) return;
            float t = (Time.unscaledTime - restFlashTime) / 0.8f;
            float flash = t >= 0f && t < 1f ? (1f - t) * (1f - t) : 0f;
            glassMaterial.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(1.6f, 1.6f, 1.8f, 1f), flash));
        }

        /// <summary>
        /// Per camera, while in use: the main view sees the circle from behind (eye hidden, cape over the
        /// body); the reflection camera sees it from the front (eye visible, cape behind).
        /// </summary>
        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera rendering)
        {
            if (!inUse || player == null) return;

            bool isReflection = rendering == reflectionCamera;
            if (playerEye != null) playerEye.enabled = isReflection;

            if (capeRenderer != null && playerBody != null)
            {
                capeRenderer.sortingOrder = playerBody.sortingOrder + (isReflection ? -1 : 1);
            }

            // Restore the eye for whatever renders next outside this system (e.g. UI), once the main view is done.
            if (!isReflection && playerEye != null && rendering.cameraType != CameraType.Game) playerEye.enabled = true;
        }

        #endregion

        private void SetLabelAlpha(float alpha)
        {
            if (nameLabel != null) { nameLabel.alpha = alpha; nameLabel.gameObject.SetActive(alpha > 0.001f); }
            if (prompt != null) { prompt.alpha = alpha; prompt.gameObject.SetActive(alpha > 0.001f); }
        }

        private void LateUpdate()
        {
            // After use, make sure the eye is visible again (the per-camera trick leaves it as the last camera set it).
            if (!inUse && playerEye != null && !playerEye.enabled) playerEye.enabled = true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (standPoint == null) return;
            Gizmos.color = new Color(0.7f, 0.85f, 1f, 0.6f);
            Gizmos.DrawWireSphere(standPoint.position, useRadius);
        }
#endif
    }
}
