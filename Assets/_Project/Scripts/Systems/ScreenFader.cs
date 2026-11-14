using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// A full-screen black overlay for scene changes: fade to black, load the scene, fade back in.
    /// Creates itself on first use and survives scene loads, so nothing needs to be placed in a scene.
    /// </summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        private static ScreenFader instance;

        private Image overlay;

        /// <summary>True while a scene change is in progress.</summary>
        public static bool IsBusy { get; private set; }

        // Enter Play Mode Options skip the domain reload, so statics must be cleared by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            IsBusy = false;
        }

        /// <summary>Fades out, loads <paramref name="sceneName"/>, then fades back in.</summary>
        public static void LoadScene(string sceneName, float fadeTime)
        {
            if (IsBusy) return;
            Instance.StartCoroutine(Instance.FadeAndLoad(sceneName, fadeTime));
        }

        private static ScreenFader Instance
        {
            get
            {
                if (instance == null) instance = Create();
                return instance;
            }
        }

        private static ScreenFader Create()
        {
            var go = new GameObject("Screen Fader");
            DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000; // above every other UI

            var imageObject = new GameObject("Overlay", typeof(RectTransform));
            imageObject.transform.SetParent(go.transform, false);
            var rect = (RectTransform)imageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var fader = go.AddComponent<ScreenFader>();
            fader.overlay = imageObject.AddComponent<Image>();
            fader.overlay.color = new Color(0f, 0f, 0f, 0f);
            fader.overlay.raycastTarget = false;
            return fader;
        }

        private IEnumerator FadeAndLoad(string sceneName, float fadeTime)
        {
            IsBusy = true;
            yield return Fade(0f, 1f, fadeTime);

            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            if (load == null)
            {
                Debug.LogError($"{nameof(ScreenFader)}: couldn't load scene '{sceneName}'. Is it added to the Build Profile's scene list?");
                yield return Fade(1f, 0f, fadeTime);
                IsBusy = false;
                yield break;
            }

            while (!load.isDone) yield return null;
            yield return null; // let the new scene's Start methods run (the player is placed at its door)

            yield return Fade(1f, 0f, fadeTime);
            IsBusy = false;
        }

        /// <summary>Unscaled time, so a hit-stop or pause can't stall a transition.</summary>
        private IEnumerator Fade(float from, float to, float time)
        {
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(from, to, t / time));
                yield return null;
            }
            SetAlpha(to);
        }

        private void SetAlpha(float alpha) => overlay.color = new Color(0f, 0f, 0f, alpha);
    }
}
