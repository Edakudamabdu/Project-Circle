using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
[RequireComponent(typeof(PlayerHealth))]
public sealed class BallController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float maxSpeed = 8f;
    [SerializeField, Min(0f)] private float acceleration = 40f;
    [SerializeField, Min(0f)] private float deceleration = 30f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.5f;

    [Header("Dash")]
    [SerializeField, Min(0.01f)] private float dashSpeed = 18f;
    [SerializeField, Min(0.01f)] private float dashDuration = 0.16f;
    [SerializeField, Min(0f)] private float dashCooldown = 0.55f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpSpeed = 12f;
    [SerializeField, Min(0f)] private float coyoteTime = 0.1f;
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.1f;
    [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.5f;
    [SerializeField, Min(0f)] private float fallGravityScale = 2.5f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField, Min(0f)] private float groundCheckDistance = 0.08f;
    [SerializeField, Range(0f, 1f)] private float minimumGroundNormalY = 0.5f;

    [Header("Visuals")]
    [SerializeField] private Transform sprite;
    [SerializeField, Range(0f, 0.25f)] private float impactSquash = 0.12f;
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 1.5f;
    [SerializeField, Min(0.01f)] private float squashRecoverySpeed = 12f;

    [Header("Dash Afterimages")]
    [SerializeField, Range(0f, 1f)] private float afterimageOpacity = 0.55f;
    [SerializeField, Min(0.01f)] private float afterimageInterval = 0.035f;
    [SerializeField, Min(0.01f)] private float afterimageLifetime = 0.22f;

    [Header("Movement Audio")]
    [SerializeField] private AudioClip jumpSound;
    [SerializeField, Range(0f, 1f)] private float jumpSoundVolume = 1f;
    [SerializeField] private AudioClip dashSound;
    [SerializeField, Range(0f, 1f)] private float dashSoundVolume = 1f;
    [SerializeField, Range(0.1f, 0.9f)]
    private float loudSectionThreshold = 0.55f;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private PlayerHealth playerHealth;
    private AudioSource jumpAudioSource;
    private AudioSource dashAudioSource;
    private int jumpSoundStartSample;
    private int dashSoundStartSample;
    private BallCapeCloth cape;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction wrapAction;
    private float baseGravityScale;
    private float inputX;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private Vector3 spriteBaseScale;
    private Vector3 squashScale = Vector3.one;
    private bool jumpReleased;
    private bool grounded;
    private bool dashRequested;
    private bool isDashing;
    private float dashTimeRemaining;
    private float dashCooldownRemaining;
    private float afterimageTimer;
    private float lastFacingDirection = 1f;
    private float dashDirection = 1f;
    private float dashVerticalVelocity;
    public bool IsDashing => isDashing;
    private readonly System.Collections.Generic.List<Afterimage> afterimages =
        new System.Collections.Generic.List<Afterimage>();

    private sealed class Afterimage
    {
        public SpriteRenderer renderer;
        public GameObject capeGhost;
        public Color spriteColor;
        public Material capeMaterial;
        public Color capeColor;
        public Mesh capeMesh;
        public float age;
    }

    private void Awake()
    {
        CleanupOrphanedCapeAfterimages();
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        playerHealth = GetComponent<PlayerHealth>();
        AudioSource[] existingAudioSources = GetComponents<AudioSource>();
        jumpAudioSource = existingAudioSources.Length > 0
            ? existingAudioSources[0]
            : gameObject.AddComponent<AudioSource>();
        dashAudioSource = existingAudioSources.Length > 1
            ? existingAudioSources[1]
            : gameObject.AddComponent<AudioSource>();
        ConfigureAudioSource(jumpAudioSource);
        ConfigureAudioSource(dashAudioSource);
        jumpSoundStartSample = FindLoudSectionStartSample(jumpSound);
        dashSoundStartSample = FindLoudSectionStartSample(dashSound);
        cape = GetComponent<BallCapeCloth>();
        baseGravityScale = rb.gravityScale;
        if (sprite != null)
            spriteBaseScale = sprite.localScale;

        groundFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = groundMask,
            useTriggers = false
        };

        CreateInputActions();
    }

    private static void CleanupOrphanedCapeAfterimages()
    {
        MeshRenderer[] renderers = FindObjectsByType<MeshRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null &&
                renderers[i].gameObject.name.StartsWith("Cape Dash Afterimage"))
            {
                Destroy(renderers[i].gameObject);
            }
        }
    }

    private void OnEnable()
    {
        if (moveAction == null)
            CreateInputActions();

        moveAction.Enable();
        jumpAction.Enable();
        dashAction.Enable();
        wrapAction.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        jumpAction?.Disable();
        dashAction?.Disable();
        wrapAction?.Disable();
        ClearAfterimages();
        cape?.SetAttackWrap(false);
    }

    private void OnDestroy()
    {
        ClearAfterimages();
        moveAction?.Dispose();
        jumpAction?.Dispose();
        dashAction?.Dispose();
        wrapAction?.Dispose();
    }

    private void Update()
    {
        bool canWrapCape = (playerHealth == null || !playerHealth.IsDead) && !isDashing;
        cape?.SetAttackWrap(canWrapCape && wrapAction != null && wrapAction.IsPressed());

        if (playerHealth != null && playerHealth.IsDead)
            return;

        inputX = moveAction.ReadValue<Vector2>().x;
        if (Mathf.Abs(inputX) > 0.01f)
            lastFacingDirection = Mathf.Sign(inputX);

        if (dashAction.WasPressedThisFrame())
            dashRequested = true;

        if (jumpAction.WasPressedThisFrame())
            jumpBufferCounter = jumpBufferTime;
        else if (jumpBufferCounter > 0f)
            jumpBufferCounter = Mathf.Max(0f, jumpBufferCounter - Time.deltaTime);

        if (jumpAction.WasReleasedThisFrame())
            jumpReleased = true;
    }

    private void FixedUpdate()
    {
        if (playerHealth != null && playerHealth.IsDead)
            return;

        dashCooldownRemaining = Mathf.Max(0f, dashCooldownRemaining - Time.fixedDeltaTime);

        if (dashRequested)
        {
            dashRequested = false;
            if (!isDashing && dashCooldownRemaining <= 0f)
                BeginDash();
        }

        if (isDashing)
        {
            dashTimeRemaining -= Time.fixedDeltaTime;
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
            rb.gravityScale = 0f;
            if (dashTimeRemaining <= 0f)
                EndDash();
            return;
        }

        grounded = CheckGrounded();
        coyoteCounter = grounded ? coyoteTime : Mathf.Max(0f, coyoteCounter - Time.fixedDeltaTime);

        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
            PerformJump();

        ApplyHorizontalMovement();
        ApplyFallGravity();
        ApplyJumpCut();
    }

    private void LateUpdate()
    {
        UpdateAfterimages();

        if (sprite == null || bodyCollider == null)
            return;

        squashScale = Vector3.Lerp(squashScale, Vector3.one,
            1f - Mathf.Exp(-squashRecoverySpeed * Time.deltaTime));
        sprite.localScale = Vector3.Scale(spriteBaseScale, squashScale);

        float radius = Mathf.Max(0.001f, bodyCollider.bounds.extents.x);
        float degreesPerSecond = -(rb.linearVelocity.x / radius) * Mathf.Rad2Deg;
        sprite.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }

    private void BeginDash()
    {
        isDashing = true;
        dashTimeRemaining = dashDuration;
        dashCooldownRemaining = dashCooldown;
        dashDirection = Mathf.Abs(inputX) > 0.01f ? Mathf.Sign(inputX) : lastFacingDirection;
        dashVerticalVelocity = rb.linearVelocity.y;
        afterimageTimer = 0f;
        PlayMovementSound(dashAudioSource, dashSound, dashSoundVolume, dashSoundStartSample);
    }

    private void EndDash()
    {
        isDashing = false;
        rb.gravityScale = baseGravityScale;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, dashVerticalVelocity);
    }

    public void ResetAfterRespawn()
    {
        isDashing = false;
        dashRequested = false;
        dashTimeRemaining = 0f;
        dashCooldownRemaining = 0f;
        dashVerticalVelocity = 0f;
        rb.gravityScale = baseGravityScale;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
        jumpReleased = false;
        grounded = false;
        squashScale = Vector3.one;
        ClearAfterimages();
    }

    private void UpdateAfterimages()
    {
        if (isDashing && sprite != null)
        {
            afterimageTimer -= Time.deltaTime;
            if (afterimageTimer <= 0f)
            {
                CreateAfterimage();
                afterimageTimer = afterimageInterval;
            }
        }

        for (int i = afterimages.Count - 1; i >= 0; i--)
        {
            Afterimage image = afterimages[i];
            image.age += Time.deltaTime;
            if (image.renderer == null || image.age >= afterimageLifetime)
            {
                if (image.renderer != null)
                    Destroy(image.renderer.gameObject);
                if (image.capeGhost != null)
                    Destroy(image.capeGhost);
                if (image.capeMaterial != null)
                    Destroy(image.capeMaterial);
                if (image.capeMesh != null)
                    Destroy(image.capeMesh);
                afterimages.RemoveAt(i);
                continue;
            }

            float fade = 1f - image.age / afterimageLifetime;
            Color color = image.spriteColor;
            color.a *= fade;
            image.renderer.color = color;
            if (image.capeMaterial != null)
            {
                color = image.capeColor;
                color.a *= fade;
                image.capeMaterial.color = color;
            }
        }
    }

    private void CreateAfterimage()
    {
        SpriteRenderer source = sprite.GetComponent<SpriteRenderer>();
        if (source == null || source.sprite == null)
            return;

        GameObject ghost = new GameObject("Dash Afterimage");
        ghost.transform.SetPositionAndRotation(sprite.position, sprite.rotation);
        ghost.transform.localScale = sprite.lossyScale;

        SpriteRenderer ghostRenderer = ghost.AddComponent<SpriteRenderer>();
        ghostRenderer.sprite = source.sprite;
        ghostRenderer.sharedMaterial = source.sharedMaterial;
        ghostRenderer.sortingLayerID = source.sortingLayerID;
        ghostRenderer.sortingOrder = source.sortingOrder - 2;
        ghostRenderer.flipX = source.flipX;
        ghostRenderer.flipY = source.flipY;
        Color spriteColor = source.color;
        spriteColor.a *= afterimageOpacity;
        ghostRenderer.color = spriteColor;

        Afterimage afterimage = new Afterimage
        {
            renderer = ghostRenderer,
            spriteColor = spriteColor
        };
        if (cape != null)
        {
            MeshRenderer capeRenderer = cape.CreateAfterimage(
                afterimageOpacity, out afterimage.capeMesh, out afterimage.capeMaterial);
            if (capeRenderer != null)
                afterimage.capeGhost = capeRenderer.gameObject;
            if (afterimage.capeMaterial != null)
                afterimage.capeColor = afterimage.capeMaterial.color;
        }

        afterimages.Add(afterimage);
    }

    private void ClearAfterimages()
    {
        for (int i = 0; i < afterimages.Count; i++)
        {
            Afterimage image = afterimages[i];
            if (image.renderer != null)
                Destroy(image.renderer.gameObject);
            if (image.capeGhost != null)
                Destroy(image.capeGhost);
            if (image.capeMaterial != null)
                Destroy(image.capeMaterial);
            if (image.capeMesh != null)
                Destroy(image.capeMesh);
        }

        afterimages.Clear();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (sprite == null || collision.contactCount == 0 ||
            collision.relativeVelocity.magnitude < minimumImpactSpeed)
            return;

        ContactPoint2D strongestContact = collision.GetContact(0);
        float strongestAlignment = 0f;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);
            float alignment = Mathf.Abs(Vector2.Dot(
                collision.relativeVelocity.normalized, contact.normal));
            if (alignment > strongestAlignment)
            {
                strongestAlignment = alignment;
                strongestContact = contact;
            }
        }

        Vector3 localNormal = sprite.InverseTransformDirection(strongestContact.normal);
        bool squashX = Mathf.Abs(localNormal.x) > Mathf.Abs(localNormal.y);
        float amount = impactSquash * Mathf.Clamp01(
            (collision.relativeVelocity.magnitude - minimumImpactSpeed) / 8f + 0.35f);

        squashScale = squashX
            ? new Vector3(1f - amount, 1f + amount, 1f)
            : new Vector3(1f + amount, 1f - amount, 1f);
    }

    private void CreateInputActions()
    {
        if (moveAction != null)
            return;

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/e");

        wrapAction = new InputAction("Cape Wrap", InputActionType.Button);
        wrapAction.AddBinding("<Mouse>/leftButton");
        wrapAction.AddBinding("<Mouse>/rightButton");
    }

    private void PerformJump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpSpeed);
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
        PlayMovementSound(jumpAudioSource, jumpSound, jumpSoundVolume, jumpSoundStartSample);
    }

    private static void ConfigureAudioSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.spatialBlend = 0f;
    }

    private int FindLoudSectionStartSample(AudioClip clip)
    {
        if (clip == null || !clip.LoadAudioData())
            return 0;

        int sampleCount = clip.samples * clip.channels;
        float[] samples = new float[sampleCount];
        if (!clip.GetData(samples, 0))
            return 0;

        int windowSize = Mathf.Max(1, clip.frequency / 100) * clip.channels;
        int windowCount = Mathf.CeilToInt((float)sampleCount / windowSize);
        float[] levels = new float[windowCount];
        float peakLevel = 0f;

        for (int window = 0; window < windowCount; window++)
        {
            int start = window * windowSize;
            int end = Mathf.Min(sampleCount, start + windowSize);
            double sum = 0d;
            for (int i = start; i < end; i++)
                sum += samples[i] * samples[i];

            float level = Mathf.Sqrt((float)(sum / Mathf.Max(1, end - start)));
            levels[window] = level;
            peakLevel = Mathf.Max(peakLevel, level);
        }

        if (peakLevel <= 0.0001f)
            return 0;

        float threshold = peakLevel * loudSectionThreshold;
        for (int window = 0; window < levels.Length; window++)
        {
            if (levels[window] >= threshold)
                return Mathf.Min(window * windowSize / clip.channels, clip.samples - 1);
        }

        return 0;
    }

    private static void PlayMovementSound(
        AudioSource source, AudioClip clip, float volume, int startSample)
    {
        if (clip == null || source == null || volume <= 0f)
            return;

        source.Stop();
        source.clip = clip;
        source.volume = volume;
        source.timeSamples = Mathf.Clamp(startSample, 0, clip.samples - 1);
        source.Play();
    }

    private void ApplyHorizontalMovement()
    {
        float targetSpeed = inputX * maxSpeed;
        float rate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
        if (!grounded)
            rate *= airControl;

        float velocityX = Mathf.MoveTowards(
            rb.linearVelocity.x,
            targetSpeed,
            rate * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector2(velocityX, rb.linearVelocity.y);
    }

    private void ApplyFallGravity()
    {
        rb.gravityScale = rb.linearVelocity.y < 0f ? fallGravityScale : baseGravityScale;
    }

    private void ApplyJumpCut()
    {
        if (jumpReleased && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                rb.linearVelocity.y * jumpCutMultiplier);
        }

        jumpReleased = false;
    }

    private bool CheckGrounded()
    {
        if (groundMask.value == 0 || bodyCollider == null)
            return false;

        int hitCount = bodyCollider.Cast(Vector2.down, groundFilter, groundHits, groundCheckDistance);
        for (int i = 0; i < hitCount; i++)
        {
            if (groundHits[i].collider != null && groundHits[i].normal.y >= minimumGroundNormalY)
                return true;
        }

        return false;
    }

    private void OnValidate()
    {
        maxSpeed = Mathf.Max(0f, maxSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        deceleration = Mathf.Max(0f, deceleration);
        jumpSpeed = Mathf.Max(0f, jumpSpeed);
        coyoteTime = Mathf.Max(0f, coyoteTime);
        jumpBufferTime = Mathf.Max(0f, jumpBufferTime);
        fallGravityScale = Mathf.Max(0f, fallGravityScale);
        groundCheckDistance = Mathf.Max(0f, groundCheckDistance);
        dashSpeed = Mathf.Max(0.01f, dashSpeed);
        dashDuration = Mathf.Max(0.01f, dashDuration);
        dashCooldown = Mathf.Max(0f, dashCooldown);
        afterimageInterval = Mathf.Max(0.01f, afterimageInterval);
        afterimageLifetime = Mathf.Max(0.01f, afterimageLifetime);
        afterimageOpacity = Mathf.Clamp01(afterimageOpacity);
        loudSectionThreshold = Mathf.Clamp(loudSectionThreshold, 0.1f, 0.9f);
    }
}
