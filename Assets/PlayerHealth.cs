using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 5;
    [SerializeField, Min(0f)] private float invulnerabilityDuration = 0.85f;
    [SerializeField, Min(0.05f)] private float respawnDelay = 0.8f;
    [SerializeField, Min(0f)] private float postRespawnGrace = 0.65f;
    [SerializeField, Tooltip("Place this marker above a safe platform. If empty, the saved position below is used.")] private Transform initialSpawnPoint;
    [SerializeField] private Vector2 fallbackSpawnPosition = new Vector2(0f, -3f);

    [Header("Health display")]
    [SerializeField] private bool showHealthHud = true;
    [SerializeField] private Vector2 hudOffset = new Vector2(28f, 28f);
    [SerializeField] private Vector2 hudSize = new Vector2(280f, 30f);
    [SerializeField, Min(0f)] private float hudPadding = 4f;
    [SerializeField] private Color hudBackground = new Color(0.025f, 0.035f, 0.06f, 0.88f);
    [SerializeField] private Color healthColor = new Color(0.92f, 0.33f, 0.29f, 1f);
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.16f, 0.14f, 1f);
    [SerializeField] private Color hudTextColor = new Color(1f, 0.94f, 0.82f, 1f);

    private Rigidbody2D body;
    private BallController controller;
    private Renderer[] renderers;
    private bool[] rendererWasEnabled;
    private Vector2 respawnPosition;
    private float invulnerabilityTimer;
    private float killDebounceTimer;
    private Canvas hudCanvas;
    private Image healthFill;
    private Text healthLabel;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public bool IsDead { get; private set; }

    public event Action<int, int> HealthChanged;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        controller = GetComponent<BallController>();
        renderers = GetComponentsInChildren<Renderer>(true);
        rendererWasEnabled = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            rendererWasEnabled[i] = renderers[i].enabled;

        respawnPosition = initialSpawnPoint != null
            ? (Vector2)initialSpawnPoint.position
            : fallbackSpawnPosition;
        CurrentHealth = maxHealth;
        if (showHealthHud)
            CreateHealthHud();
    }

    private void Start()
    {
        NotifyHealthChanged();
    }

    private void Update()
    {
        if (killDebounceTimer > 0f)
            killDebounceTimer = Mathf.Max(0f, killDebounceTimer - Time.deltaTime);

        if (invulnerabilityTimer > 0f)
            invulnerabilityTimer = Mathf.Max(0f, invulnerabilityTimer - Time.deltaTime);
    }

    /// <summary>Apply damage from an enemy attack. Returns false if damage was blocked.</summary>
    public bool TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead || killDebounceTimer > 0f || invulnerabilityTimer > 0f ||
            (controller != null && controller.IsDashing))
            return false;

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        NotifyHealthChanged();

        if (CurrentHealth == 0)
            BeginDeath();
        else
            invulnerabilityTimer = invulnerabilityDuration;

        return true;
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        int newHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        if (newHealth == CurrentHealth)
            return;

        CurrentHealth = newHealth;
        NotifyHealthChanged();
    }

    /// <summary>Moves the respawn point for a future checkpoint system.</summary>
    public void SetRespawnPoint(Vector2 position)
    {
        respawnPosition = position;
    }

    /// <summary>Instantly kills the player, such as when entering an abyss or kill zone.</summary>
    public void Kill()
    {
        if (IsDead || killDebounceTimer > 0f)
            return;

        CurrentHealth = 0;
        NotifyHealthChanged();
        BeginDeath();
    }

    private void BeginDeath()
    {
        if (IsDead)
            return;

        IsDead = true;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.simulated = false;
        SetRenderersVisible(false);
        StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);

        transform.position = new Vector3(respawnPosition.x, respawnPosition.y, transform.position.z);
        body.position = respawnPosition;
        body.rotation = 0f;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        Physics2D.SyncTransforms();
        body.simulated = true;
        controller?.ResetAfterRespawn();
        SetRenderersVisible(true);

        CurrentHealth = maxHealth;
        IsDead = false;
        invulnerabilityTimer = invulnerabilityDuration;
        killDebounceTimer = postRespawnGrace;
        NotifyHealthChanged();
    }

    private void NotifyHealthChanged()
    {
        HealthChanged?.Invoke(CurrentHealth, maxHealth);
        UpdateHealthHud();
    }

    private void SetRenderersVisible(bool visible)
    {
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = visible && rendererWasEnabled[i];
    }

    private void CreateHealthHud()
    {
        GameObject canvasObject = new GameObject("Health HUD");
        hudCanvas = canvasObject.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 20;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform backgroundRect = CreateRect(
            "Health Bar Background", hudCanvas.transform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(hudOffset.x, -hudOffset.y), hudSize);
        Image backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
        backgroundImage.color = hudBackground;
        backgroundImage.raycastTarget = false;

        RectTransform fillRect = CreateRect(
            "Health Bar Fill", backgroundRect, Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        fillRect.offsetMin = new Vector2(hudPadding, hudPadding);
        fillRect.offsetMax = new Vector2(-hudPadding, -hudPadding);
        healthFill = fillRect.gameObject.AddComponent<Image>();
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;
        healthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        healthFill.color = healthColor;
        healthFill.raycastTarget = false;

        RectTransform labelRect = CreateRect(
            "Health Value", backgroundRect, Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        healthLabel = labelRect.gameObject.AddComponent<Text>();
        healthLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        healthLabel.fontSize = 18;
        healthLabel.fontStyle = FontStyle.Bold;
        healthLabel.alignment = TextAnchor.MiddleCenter;
        healthLabel.color = hudTextColor;
        healthLabel.raycastTarget = false;
    }

    private void UpdateHealthHud()
    {
        if (healthFill == null || healthLabel == null)
            return;

        float ratio = maxHealth > 0 ? Mathf.Clamp01((float)CurrentHealth / maxHealth) : 0f;
        healthFill.fillAmount = ratio;
        healthFill.color = ratio <= 0.3f ? lowHealthColor : healthColor;
        healthLabel.text = $"{CurrentHealth} / {maxHealth}";
    }

    private static RectTransform CreateRect(
        string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject item = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        return rect;
    }

    private void OnDestroy()
    {
        if (hudCanvas != null)
            Destroy(hudCanvas.gameObject);
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        invulnerabilityDuration = Mathf.Max(0f, invulnerabilityDuration);
        respawnDelay = Mathf.Max(0.05f, respawnDelay);
        postRespawnGrace = Mathf.Max(0f, postRespawnGrace);
        hudSize.x = Mathf.Max(80f, hudSize.x);
        hudSize.y = Mathf.Max(18f, hudSize.y);
        hudPadding = Mathf.Max(0f, hudPadding);
    }
}
