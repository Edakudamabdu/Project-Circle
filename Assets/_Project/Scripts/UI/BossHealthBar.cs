using System.Collections;
using ProjectCircle.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectCircle.UI
{
    /// <summary>
    /// The boss's name and health bar along the top of the screen. Built in code (nothing to place in the
    /// scene): call <see cref="Show"/> when the fight starts and <see cref="Hide"/> when it ends.
    ///
    /// Damage drains the bar at once; a pale "chip" behind it catches up a moment later, so each hit reads.
    /// </summary>
    public sealed class BossHealthBar : MonoBehaviour
    {
        private const float FullWidth = 1100f;
        private const float HalfWidth = 520f;
        private float width = FullWidth;
        private const float Height = 22f;

        private static BossHealthBar instance, left, right;

        private CanvasGroup group;
        private TMP_Text nameText;
        private RectTransform fill, chip;
        private EnemyHealth health;
        private float shown = 1f;    // fraction drawn by the fill
        private float chipShown = 1f;
        private float chipHoldUntil;
        private Coroutine fade;

        /// <summary>The bar for the current scene (created on first use).</summary>
        public static BossHealthBar Instance
        {
            get
            {
                if (instance == null) instance = Create();
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = left = right = null;

        /// <summary>Half-width bar on the left of the screen, for two bosses fought together.</summary>
        public static BossHealthBar Left => left != null ? left : (left = Create("Boss Health Bar (Left)", HalfWidth, -HalfWidth * 0.5f - 30f));

        /// <summary>Half-width bar on the right of the screen, for two bosses fought together.</summary>
        public static BossHealthBar Right => right != null ? right : (right = Create("Boss Health Bar (Right)", HalfWidth, HalfWidth * 0.5f + 30f));

        /// <summary>Fades the bar in, tracking <paramref name="bossHealth"/>.</summary>
        public void Show(string bossName, EnemyHealth bossHealth)
        {
            if (health != null) health.Hit -= HandleHit;
            health = bossHealth;
            health.Hit += HandleHit;
            nameText.text = bossName.ToUpperInvariant();
            shown = chipShown = Fraction();
            Redraw();
            FadeTo(1f);
        }

        /// <summary>Fades the bar out.</summary>
        public void Hide()
        {
            if (health != null) health.Hit -= HandleHit;
            health = null;
            FadeTo(0f);
        }

        private void HandleHit(int damage, Vector2 source) => chipHoldUntil = Time.unscaledTime + 0.45f;

        private void Update()
        {
            if (health == null) return;
            shown = Fraction();
            if (Time.unscaledTime > chipHoldUntil) chipShown = Mathf.MoveTowards(chipShown, shown, Time.unscaledDeltaTime * 0.6f);
            if (chipShown < shown) chipShown = shown;
            Redraw();
        }

        private float Fraction() => health != null ? health.CurrentHealth / (float)health.MaxHealth : 0f;

        private void Redraw()
        {
            fill.sizeDelta = new Vector2(width * shown, Height);
            chip.sizeDelta = new Vector2(width * chipShown, Height);
        }

        private void FadeTo(float alpha)
        {
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(FadeRoutine(alpha));
        }

        private IEnumerator FadeRoutine(float target)
        {
            float from = group.alpha;
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, target, t / 0.5f);
                yield return null;
            }
            group.alpha = target;
        }

        #region Building

        private static BossHealthBar Create(string objectName = "Boss Health Bar", float barWidth = FullWidth, float x = 0f)
        {
            var root = new GameObject(objectName);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30; // under the dialogue box and cutscene overlays
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var bar = root.AddComponent<BossHealthBar>();
            bar.width = barWidth;
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f); // top of the screen
            panelRect.anchoredPosition = new Vector2(x, -70f);
            bar.group = panel.AddComponent<CanvasGroup>();
            bar.group.alpha = 0f;
            bar.group.blocksRaycasts = false;

            Image(panel.transform, "Frame", new Color(0.05f, 0.05f, 0.07f, 0.9f), new Vector2(barWidth + 8f, Height + 8f), barWidth, centred: true);
            bar.chip = Image(panel.transform, "Chip", new Color(0.95f, 0.9f, 0.8f, 1f), new Vector2(barWidth, Height), barWidth, centred: false);
            bar.fill = Image(panel.transform, "Fill", new Color(0.72f, 0.24f, 0.3f, 1f), new Vector2(barWidth, Height), barWidth, centred: false);

            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(panel.transform, false);
            var nameRect = (RectTransform)nameGo.transform;
            nameRect.anchoredPosition = new Vector2(0f, -34f); // under the bar
            nameRect.sizeDelta = new Vector2(barWidth, 40f);
            bar.nameText = nameGo.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) bar.nameText.font = TMP_Settings.defaultFontAsset;
            bar.nameText.fontSize = 30f;
            bar.nameText.characterSpacing = 10f;
            bar.nameText.alignment = TextAlignmentOptions.Center;
            bar.nameText.color = new Color(0.92f, 0.9f, 0.86f, 1f);
            bar.nameText.raycastTarget = false;
            return bar;
        }

        private static RectTransform Image(Transform parent, string name, Color color, Vector2 size, float barWidth, bool centred)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            // Bars grow from the left edge; the frame sits centred behind them.
            rect.pivot = centred ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
            rect.anchoredPosition = centred ? Vector2.zero : new Vector2(-barWidth * 0.5f, 0f);
            rect.sizeDelta = size;
            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        #endregion
    }
}
