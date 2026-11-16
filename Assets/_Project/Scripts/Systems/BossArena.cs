using System.Collections;
using ProjectCircle.Enemies.Boss;
using ProjectCircle.Player;
using ProjectCircle.Systems.Story;
using ProjectCircle.UI;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// Runs a boss arena:
    /// 1. The circle walks in past <see cref="entryTrigger"/>: the gates slam shut behind it, the camera
    ///    frames the whole arena and the boss's name appears.
    /// 2. The fight runs (<see cref="BossBrain"/>), with the health bar along the bottom.
    /// 3. Boss beaten: the gates open, the camera follows the circle again.
    ///    Circle dies: once it respawns (outside the gates), the boss resets and the gates open.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossArena : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BossBrain boss;
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

        private PlayerInputReader input;
        private PlayerHealth playerHealth;
        private CameraFollow2D cameraFollow;
        private Vector3[] gateClosed;
        private Vector2 bossHome;
        private bool fightOn;
        private bool won;

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
            boss.Defeated += () => StartCoroutine(EndFight());
            playerHealth.Respawned += HandlePlayerRespawned;
        }

        private IEnumerator BeginFight()
        {
            if (won || fightOn) yield break;
            fightOn = true;
            input.AddControlLock(this);

            StartCoroutine(MoveGates(close: true));
            cameraFollow.Shake(0.15f, 0.25f);
            cameraFollow.FocusOn(cameraCentre.position, cameraSize, 0.6f);
            yield return new WaitForSeconds(0.5f);

            CinematicOverlay.Instance.ShowAreaName(boss.BossName.ToUpperInvariant(), introHold);
            yield return new WaitForSeconds(introHold);

            BossHealthBar.Instance.Show(boss.BossName, boss.Health);
            input.RemoveControlLock(this);
            boss.BeginFight();
        }

        private IEnumerator EndFight()
        {
            won = true;
            fightOn = false;
            BossHealthBar.Instance.Hide();
            yield return new WaitForSeconds(1.2f);
            CinematicOverlay.Instance.ShowAreaName(defeatedText, 2.5f);
            cameraFollow.ClearFocus();
            yield return MoveGates(close: false);
        }

        private void HandlePlayerRespawned()
        {
            if (!fightOn || won) return;

            // Start over: the circle is back outside the gates.
            fightOn = false;
            input.RemoveControlLock(this);
            boss.ResetFight(bossHome);
            BossHealthBar.Instance.Hide();
            cameraFollow.ClearFocus();
            StartCoroutine(MoveGates(close: false));
            entryTrigger.Rearm();
        }

        private IEnumerator MoveGates(bool close)
        {
            for (int i = 0; i < gates.Length; i++) gates[i].gameObject.SetActive(true);

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
