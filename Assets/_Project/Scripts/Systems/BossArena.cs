using System.Collections;
using ProjectCircle.Enemies.Boss;
using ProjectCircle.Player;
using ProjectCircle.Systems.Story;
using ProjectCircle.UI;
using ProjectCircle.Systems.Audio;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Runs a boss arena:
    /// 1. The circle walks in past <see cref="entryTrigger"/>: the camera frames the whole arena, the boss makes
    ///    its entrance (if it has one, e.g. the Twins dropping in), the gates slam shut and the boss's name appears.
    /// 2. The fight runs (the <see cref="IArenaBoss"/>: one <see cref="BossBrain"/>, or the Twins), with its health
    ///    bar(s) along the top.
    /// 3. Boss beaten: the story flags are set, the gates open and the boss leaves a cape string behind (its
    ///    reward ability). The camera keeps framing the arena until the circle walks out of it.
    ///    Circle dies: once it respawns (outside the gates), the boss resets and the gates open.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossArena : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The boss: a BossBrain (one boss) or a TwinIsosceles (two fought together). Anything that is an IArenaBoss.")]
        [SerializeField] private MonoBehaviour boss;

        private IArenaBoss Boss => boss as IArenaBoss;
        [SerializeField] private StoryTrigger entryTrigger;

        [Tooltip("Gates that close during the fight (solid objects on the Ground layer). They rise out of the floor.")]
        [SerializeField] private Transform[] gates;

        [Tooltip("Centre of the camera framing during the fight.")]
        [SerializeField] private Transform cameraCentre;

        [SerializeField, Min(1f)] private float cameraSize = 8.5f;

        [Header("Feel")]
        [SerializeField, Min(0.05f)] private float gateTime = 0.35f;
        [SerializeField, Min(0f)] private float introHold = 1.4f;

        [Tooltip("Shown when the boss is beaten.")]
        [SerializeField] private string defeatedText = "Base Warden defeated";

        [Header("Reward (kept in GameProgress)")]
        [Tooltip("Story flags set when the boss is beaten. The first one also marks the boss as beaten for good " +
                 "(coming back later, the arena is empty and open).")]
        [SerializeField] private string[] rewardFlags = { "Seal.Warden" };

        [Tooltip("Ability on the cape string the boss leaves behind (None = no string).")]
        [SerializeField] private PlayerAbility rewardAbilities = PlayerAbility.AirJump;

        [Tooltip("The cape string prefab (Prefabs/World/CapeString). It appears in the middle of the arena floor.")]
        [SerializeField] private CapeStringPickup rewardStringPrefab;

        [Tooltip("Name shown when the string is collected.")]
        [SerializeField] private string rewardStringName = "The Climber's String";

        [SerializeField] private string rewardHint = "DOUBLE JUMP: jump again in the air";

        [Tooltip("Half the arena's width around the camera centre. After the fight the camera keeps framing the arena " +
                 "until the circle walks further out than this.")]
        [SerializeField, Min(1f)] private float arenaHalfWidth = 17f;

        [Header("Music")]
        [Tooltip("Optional. Starts when the fight starts (as the boss makes its entrance), loops, and fades out " +
                 "when the fight is won or the circle dies (it starts again with the next attempt).")]
        [SerializeField] private AudioClip fightMusic;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.7f;
        [SerializeField, Min(0f)] private float musicFadeIn = 0.8f;
        [SerializeField, Min(0f)] private float musicFadeOut = 2.5f;

        private AudioSource music;
        private Coroutine musicFade;

        private PlayerInputReader input;
        private PlayerHealth playerHealth;
        private CameraFollow2D cameraFollow;
        private Vector3[] gateClosed;
        private Vector2 bossHome;
        private bool fightOn;
        private bool won;
        private bool holdArenaView;

        private void Start()
        {
            var player = FindAnyObjectByType<PlayerMovement>();
            input = player.GetComponent<PlayerInputReader>();
            playerHealth = player.GetComponent<PlayerHealth>();
            cameraFollow = FindAnyObjectByType<CameraFollow2D>();
            bossHome = boss.transform.position;

            // Gates are placed closed in the scene; sink them into the floor until the fight.
            gateClosed = new Vector3[gates.Length];
            for (int i = 0; i < gates.Length; i++)
            {
                gateClosed[i] = gates[i].position;
                gates[i].position = OpenPosition(i);
                gates[i].gameObject.SetActive(false);
            }

            entryTrigger.Entered += () => StartCoroutine(BeginFight());
            Boss.Defeated += () => StartCoroutine(EndFight());
            playerHealth.Respawned += HandlePlayerRespawned;
            playerHealth.Died += () =>
            {
                if (!fightOn) return;
                StopMusic(1.2f);
                Sfx.DuckAmbience(false, 1.2f);
            };

            if (fightMusic != null)
            {
                music = gameObject.AddComponent<AudioSource>();
                music.clip = fightMusic;
                music.loop = true;
                music.playOnAwake = false;
                music.spatialBlend = 0f; // plain stereo, not positioned in the world
                music.volume = 0f;
            }

            // Already beaten on this run: the arena is empty and stays open (and the string waits if it was left behind).
            if (rewardFlags != null && rewardFlags.Length > 0 && GameProgress.Has(rewardFlags[0]))
            {
                won = true;
                boss.gameObject.SetActive(false);
                SpawnRewardString(quiet: true);
            }
        }

        private void Update()
        {
            // After the fight, the arena view holds until the circle leaves the arena.
            if (!holdArenaView || cameraFollow == null || playerHealth == null) return;
            if (Mathf.Abs(playerHealth.transform.position.x - cameraCentre.position.x) > arenaHalfWidth)
            {
                holdArenaView = false;
                cameraFollow.ClearFocus();
            }
        }

        /// <summary>The boss's reward: a cape string on the arena floor (skipped if the circle already has it).</summary>
        private void SpawnRewardString(bool quiet)
        {
            if (rewardAbilities == PlayerAbility.None || rewardStringPrefab == null) return;
            var abilities = playerHealth.GetComponent<PlayerAbilities>();
            if (abilities != null && abilities.Has(rewardAbilities)) return;

            // Stand it on the arena floor, under the camera centre.
            Vector2 from = cameraCentre.position;
            RaycastHit2D hit = Physics2D.Raycast(from, Vector2.down, 40f, LayerMask.GetMask("Ground"));
            float floor = hit.collider != null ? hit.point.y : bossHome.y - 1f;
            Vector3 at = new Vector3(from.x, floor + 1.4f, 0f);

            CapeStringPickup pickup = Instantiate(rewardStringPrefab, at, Quaternion.identity, transform);
            pickup.name = "Reward String - " + rewardAbilities;
            pickup.Configure(rewardAbilities, rewardStringName, rewardHint);
            if (!quiet)
            {
                Fx.LightColumn(new Vector2(at.x, floor), 9f, new Color(0.86f, 0.25f, 0.22f, 1f), 1.1f, 1.6f);
                Fx.Ring(at, new Color(1f, 0.85f, 0.8f, 1f), 3f, 0.5f, 0.12f);
            }
        }

        private IEnumerator BeginFight()
        {
            if (won || fightOn) yield break;
            fightOn = true;
            input.AddControlLock(this);
            PlayMusic();
            Sfx.DuckAmbience(true);

            // Frame the arena, let the boss make its entrance (if any), then lock the arena.
            cameraFollow.FocusOn(cameraCentre.position, cameraSize, 0.6f);
            yield return Boss.Entrance();
            StartCoroutine(MoveGates(close: true));
            cameraFollow.Shake(0.15f, 0.25f);
            yield return new WaitForSeconds(0.5f);

            CinematicOverlay.Instance.ShowAreaName(Boss.BossName.ToUpperInvariant(), introHold);
            yield return new WaitForSeconds(introHold);

            Boss.ShowHealthBar();
            input.RemoveControlLock(this);
            Boss.BeginFight();
        }

        private IEnumerator EndFight()
        {
            won = true;
            fightOn = false;
            StopMusic(musicFadeOut);
            Sfx.DuckAmbience(false, musicFadeOut);
            Boss.HideHealthBar();
            yield return new WaitForSeconds(1.2f);
            CinematicOverlay.Instance.ShowAreaName(defeatedText, 2.5f);

            // The camera keeps the arena framed (see Update) until the circle walks out.
            holdArenaView = true;

            // The reward: the seal and story flags now; the new ability waits as a string on the floor.
            if (rewardFlags != null) foreach (string f in rewardFlags) GameProgress.Set(f);
            yield return MoveGates(close: false);
            yield return new WaitForSeconds(1.6f);
            SpawnRewardString(quiet: false);
        }

        /// <summary>Starts the fight music from the top, fading in.</summary>
        private void PlayMusic()
        {
            if (music == null) return;
            music.Stop();
            music.volume = 0f;
            music.Play();
            FadeMusic(musicVolume, musicFadeIn, stopAtEnd: false);
        }

        private void StopMusic(float fade)
        {
            if (music == null || !music.isPlaying) return;
            FadeMusic(0f, fade, stopAtEnd: true);
        }

        private void FadeMusic(float target, float time, bool stopAtEnd)
        {
            if (musicFade != null) StopCoroutine(musicFade);
            musicFade = StartCoroutine(FadeRoutine(target, time, stopAtEnd));
        }

        // Real time, so hit-stops and slow motion don't stretch the fade.
        private IEnumerator FadeRoutine(float target, float time, bool stopAtEnd)
        {
            float from = music.volume;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                music.volume = Mathf.Lerp(from, target, t / time);
                yield return null;
            }
            music.volume = target;
            if (stopAtEnd) music.Stop();
            musicFade = null;
        }

        private void HandlePlayerRespawned()
        {
            if (!fightOn || won) return;

            // Start over: the circle is back outside the gates.
            fightOn = false;
            input.RemoveControlLock(this);
            Boss.ResetFight(bossHome);
            Boss.HideHealthBar();
            cameraFollow.ClearFocus();
            StartCoroutine(MoveGates(close: false));
            entryTrigger.Rearm();
        }

        private IEnumerator MoveGates(bool close)
        {
            for (int i = 0; i < gates.Length; i++) gates[i].gameObject.SetActive(true);
            foreach (Transform g in gates) Sfx.Play(SoundId.Gate, g.position, 1f, close ? 1f : 0.85f);

            for (float t = 0f; t < gateTime; t += Time.deltaTime)
            {
                float p = t / gateTime;
                float e = close ? p * p : 1f - (1f - p) * (1f - p);
                for (int i = 0; i < gates.Length; i++)
                {
                    gates[i].position = close ? Vector3.Lerp(OpenPosition(i), gateClosed[i], e) : Vector3.Lerp(gateClosed[i], OpenPosition(i), e);
                }
                yield return null;
            }

            for (int i = 0; i < gates.Length; i++)
            {
                gates[i].position = close ? gateClosed[i] : OpenPosition(i);
                gates[i].gameObject.SetActive(close);
            }
        }

        /// <summary>Sunk into the floor by its own height.</summary>
        private Vector3 OpenPosition(int i) => gateClosed[i] + Vector3.down * gates[i].lossyScale.y;
    }
}
