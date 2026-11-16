using System.Collections;
using ProjectCircle.Player;
using ProjectCircle.UI;
using UnityEngine;

namespace ProjectCircle.Systems.Story
{
    /// <summary>
    /// Runs the game's opening (see the "Project Circle — Opening Script" doc), one scene after another:
    ///
    /// S01 The Outskirts   – the circle walks and jumps; no cape. Reaching the border starts S02.
    /// S02 Looked Down On  – three triangles mock and shove it; the low camera shot from the circle's view; it passes out.
    /// S03 The Discarded Pit – it wakes up hurt in the pit and finds the Old Triangle (talk to him).
    /// S04 The Last Door   – the world shakes; he warns of the entity, opens the gateway and collapses.
    /// S05 The Hall of Strings – reaching the middle, the strings form the cape. Title card. The game begins.
    ///
    /// The circle never speaks and has no thought text here: thoughts only appear at Mirrors.
    ///
    /// If the circle arrives some other way (through a door, or respawning at a Mirror), or the opening has
    /// already been played this session, everything is set to its "after" state instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OpeningSequence : MonoBehaviour
    {
        [Header("S01 The Outskirts")]
        [Tooltip("Where the circle starts (its home). If it starts far from here, the opening is skipped.")]
        [SerializeField] private Transform homeSpawn;
        [SerializeField, Min(0.5f)] private float skipDistance = 4f;
        [SerializeField] private StoryTrigger borderTrigger;
        [SerializeField] private string outskirtsName = "The Outskirts";
        [SerializeField] private string movementHint = "Move:  A / D        Jump:  Space";

        [Header("S02 Looked Down On")]
        [Tooltip("Bully 1 (the leader) first.")]
        [SerializeField] private StoryActor[] bullies;

        [Tooltip("How far in front of the circle each bully stops, in the same order.")]
        [SerializeField] private float[] bullyStopOffsets = { 1.75f, 3.4f, 5f };
        [SerializeField, Min(0.1f)] private float bullyWalkSpeed = 3.2f;
        [SerializeField] private Dialogue bully1Mock;
        [SerializeField] private Dialogue bully2Mock;
        [SerializeField] private Dialogue bully3Mock;
        [SerializeField] private Dialogue bully1Pov;
        [SerializeField] private PovShot povShot;

        [Tooltip("The grate over the pit (intact before, broken after).")]
        [SerializeField] private GameObject grateIntact;
        [SerializeField] private GameObject grateBroken;

        [Header("S03 The Discarded Pit")]
        [SerializeField] private Transform pitWakePoint;

        [Tooltip("What the circle looks up at after waking (the light at the top of the pit).")]
        [SerializeField] private Transform pitLightFocus;
        [SerializeField, Min(1)] private int wakeHealth = 2;
        [SerializeField] private string pitName = "The Discarded Pit";
        [SerializeField] private NpcConversation oldTriangleTalk;
        [SerializeField] private OldTriangleIdle oldTriangle;

        [Header("S04 The Last Door")]
        [SerializeField] private Dialogue whispers;
        [SerializeField] private Dialogue stringsSpeech;
        [SerializeField] private Dialogue lastWords;
        [SerializeField] private SealedGateway gateway;

        [Header("S05 The Hall of Strings")]
        [SerializeField] private StoryTrigger hallEntryTrigger;
        [SerializeField] private StoryTrigger hallCentreTrigger;
        [SerializeField] private StringHall stringHall;
        [SerializeField] private string hallName = "The Hall of Strings";
        [SerializeField] private PlayerAbility capeAbilities = PlayerAbility.All;
        [SerializeField] private string gameTitle = "PROJECT CIRCLE";
        [SerializeField] private string capeHint = "Attack:  Left Mouse      Dash:  Shift      Air jump:  Space (in the air)      Parry:  F";

        /// <summary>True once the opening has been played this session (it won't play again).</summary>
        public static bool Completed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Completed = false;

        private PlayerMovement movement;
        private PlayerInputReader input;
        private PlayerHealth health;
        private PlayerAbilities abilities;
        private PlayerCape cape;
        private Rigidbody2D playerBody;
        private CameraFollow2D cameraFollow;
        private DialogueBox dialogueBox;
        private CinematicOverlay overlay;

        #region Setup

        private IEnumerator Start()
        {
            movement = FindAnyObjectByType<PlayerMovement>();
            if (movement == null)
            {
                Debug.LogError($"{name}: no player in the scene.", this);
                yield break;
            }

            input = movement.GetComponent<PlayerInputReader>();
            health = movement.GetComponent<PlayerHealth>();
            abilities = movement.GetComponent<PlayerAbilities>();
            playerBody = movement.GetComponent<Rigidbody2D>();
            cape = movement.GetComponentInChildren<PlayerCape>();
            cameraFollow = FindAnyObjectByType<CameraFollow2D>();
            dialogueBox = FindAnyObjectByType<DialogueBox>();
            overlay = CinematicOverlay.Instance;

            if (povShot != null) povShot.End();
            if (grateBroken != null) grateBroken.SetActive(false);

            // Doors and Mirrors place the circle in their Start, so wait a frame before deciding.
            yield return null;

            bool arrivedElsewhere = homeSpawn != null && Vector2.Distance(playerBody.position, homeSpawn.position) > skipDistance;
            if (Completed || arrivedElsewhere)
            {
                ApplyFinishedState();
                yield break;
            }

            yield return PlayOutskirts();
        }

        /// <summary>Everything as it is after the opening: cape on, gateway open, Old Triangle collapsed.</summary>
        private void ApplyFinishedState()
        {
            foreach (StoryActor bully in bullies) if (bully != null) bully.SetVisible(false);
            if (grateIntact != null) grateIntact.SetActive(false);
            if (grateBroken != null) grateBroken.SetActive(true);
            if (gateway != null) gateway.SetOpen();
            if (oldTriangle != null) oldTriangle.Collapse(instant: true);
            if (oldTriangleTalk != null) oldTriangleTalk.enabled = false;
            if (stringHall != null) stringHall.HideStrings();
            borderTrigger?.Disarm();
            hallCentreTrigger?.Disarm();
            if (hallEntryTrigger != null) hallEntryTrigger.Entered += () => overlay.ShowAreaName(hallName);
        }

        #endregion

        #region S01 The Outskirts

        private IEnumerator PlayOutskirts()
        {
            if (abilities != null) abilities.RemoveCape();
            foreach (StoryActor bully in bullies) if (bully != null) bully.SetVisible(false);

            borderTrigger.Entered += () => StartCoroutine(PlayLookedDownOn());
            hallEntryTrigger.Entered += () => overlay.ShowAreaName(hallName);
            hallCentreTrigger.Entered += () => StartCoroutine(PlayHallOfStrings());
            if (oldTriangleTalk != null) oldTriangleTalk.ConversationFinished += HandleOldTriangleTalked;

            overlay.SetBlack(1f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return overlay.FadeBlack(0f, 1.6f);
            overlay.ShowAreaName(outskirtsName);
            yield return new WaitForSecondsRealtime(1.2f);
            overlay.ShowHint(movementHint, 7f);
        }

        #endregion

        #region S02 Looked Down On

        private IEnumerator PlayLookedDownOn()
        {
            Lock();
            overlay.HideHint();
            StartCoroutine(overlay.Letterbox(true));

            Transform player = movement.transform;
            StoryActor leader = bullies[0];

            // 1. They walk in and block the way (they come from the border side, ahead of the circle).
            yield return new WaitForSeconds(0.4f); // let the circle come to a stop
            float side = Mathf.Sign(bullies[0].transform.position.x - player.position.x);
            float lastStop = player.position.x;
            float longestWalk = 0f;
            for (int i = 0; i < bullies.Length; i++)
            {
                float stop = player.position.x + side * bullyStopOffsets[Mathf.Min(i, bullyStopOffsets.Length - 1)];
                longestWalk = Mathf.Max(longestWalk, Mathf.Abs(bullies[i].transform.position.x - stop));
                lastStop = stop;
                bullies[i].SetVisible(true);
                bullies[i].LookAt(player);
                StartCoroutine(bullies[i].WalkTo(stop, bullyWalkSpeed));
            }
            Vector2 frame = new Vector2((player.position.x + lastStop) * 0.5f, player.position.y + 1.2f);
            cameraFollow.FocusOn(frame, 5f, 0.8f);
            yield return new WaitForSeconds(longestWalk / bullyWalkSpeed + 0.4f);

            // 2. The mocking.
            yield return Say(bully1Mock);
            StartCoroutine(bullies[1].Laugh(0.8f));
            StartCoroutine(bullies[2].Laugh(0.8f));
            yield return Say(bully2Mock);
            yield return Say(bully3Mock);
            foreach (StoryActor b in bullies) StartCoroutine(b.Laugh(1f));
            yield return new WaitForSeconds(1f);

            // 3. The leader shoves the circle back.
            float towardPlayer = Mathf.Sign(player.position.x - leader.transform.position.x);
            yield return leader.Shove(towardPlayer, 0.8f, () =>
            {
                movement.ApplyKnockback(new Vector2(towardPlayer, 0.35f));
                cameraFollow.Shake(0.12f, 0.2f);
            });
            yield return new WaitForSeconds(0.7f);

            // 4. The circle's point of view: they tower over it.
            yield return PlayPov();

            // 5. In the dark: the circle is thrown into the pit.
            povShot.End();
            foreach (StoryActor b in bullies) b.SetVisible(false);
            if (grateIntact != null) grateIntact.SetActive(false);
            if (grateBroken != null) grateBroken.SetActive(true);
            Teleport(pitWakePoint.position);
            health.SetHealth(wakeHealth);
            health.SetRespawnPoint(pitWakePoint.position); // no way back up: never respawn in the outskirts
            cameraFollow.ClearFocus();
            cameraFollow.SnapToTarget();
            yield return overlay.Letterbox(false, 0.01f);
            yield return new WaitForSecondsRealtime(2f);

            yield return PlayPitAwakening();
        }

        private IEnumerator PlayPov()
        {
            // A hard cut, after a split second of black.
            overlay.SetBlack(1f);
            yield return new WaitForSecondsRealtime(0.15f);
            povShot.Begin();
            cameraFollow.CutTo(povShot.CameraPoint, povShot.CameraSize);
            overlay.SetBlack(0f);

            Coroutine creep = StartCoroutine(povShot.CreepIn(5f));
            yield return new WaitForSeconds(1.2f);
            yield return Say(bully1Pov);
            StopCoroutine(creep);

            yield return povShot.RearUp(0.9f);
            yield return new WaitForSeconds(0.2f);

            // Cut away before the hit: the eyes close as the point comes down.
            StartCoroutine(povShot.Lunge(1.6f));
            cameraFollow.Shake(0.08f, 1.6f);
            yield return overlay.PassOut();
        }

        #endregion

        #region S03 The Discarded Pit

        private IEnumerator PlayPitAwakening()
        {
            yield return overlay.WakeUp();
            yield return new WaitForSeconds(0.9f);

            // Slow to get up: a small, tired hop.
            playerBody.linearVelocity = new Vector2(0f, 4.5f);
            yield return new WaitForSeconds(0.9f);

            // It looks up at the faint light where it fell from.
            if (pitLightFocus != null)
            {
                cameraFollow.FocusOn(pitLightFocus.position, 6f, 1.4f);
                yield return new WaitForSeconds(2.8f);
                cameraFollow.ClearFocus();
                yield return new WaitForSeconds(0.8f);
            }

            overlay.ShowAreaName(pitName);
            Unlock();
        }

        #endregion

        #region S04 The Last Door

        private void HandleOldTriangleTalked(int timesTalked)
        {
            if (timesTalked != 1) return;
            oldTriangleTalk.ConversationFinished -= HandleOldTriangleTalked;
            StartCoroutine(PlayLastDoor());
        }

        private IEnumerator PlayLastDoor()
        {
            Lock();
            Transform player = movement.transform;
            Vector2 old = oldTriangle.transform.position;

            // The world shakes; the Shapeshifter pulls at him.
            cameraFollow.Shake(0.3f, 1.8f);
            oldTriangle.Agitated = true;
            yield return new WaitForSeconds(1.6f);

            StartCoroutine(overlay.Letterbox(true));
            cameraFollow.FocusOn(new Vector2((player.position.x + old.x) * 0.5f, old.y + 1.4f), 4.2f, 0.8f);
            yield return new WaitForSeconds(0.6f);
            yield return Say(whispers);

            // The circle shrinks back a little.
            float away = Mathf.Sign(player.position.x - old.x);
            movement.Recoil(new Vector2(away * 3f, 0f), 0.12f);
            yield return new WaitForSeconds(0.9f);

            yield return Say(stringsSpeech);

            // He pours his last light into the wall.
            Vector2 wall = gateway.transform.position;
            cameraFollow.FocusOn(new Vector2((old.x + wall.x) * 0.5f, old.y + 2.5f), 5.2f, 0.8f);
            yield return new WaitForSeconds(0.6f);
            yield return gateway.Open(OldTriangleCorners());
            cameraFollow.Shake(0.2f, 0.4f);
            yield return new WaitForSeconds(0.6f);

            cameraFollow.FocusOn(new Vector2((player.position.x + old.x) * 0.5f, old.y + 1.4f), 4.2f, 0.8f);
            yield return Say(lastWords);

            oldTriangle.Collapse();
            oldTriangleTalk.enabled = false;
            yield return new WaitForSeconds(2.2f);

            cameraFollow.ClearFocus();
            yield return overlay.Letterbox(false);
            Unlock();
        }

        /// <summary>World positions of the Old Triangle's corners (where his light leaves him).</summary>
        private Vector2[] OldTriangleCorners()
        {
            var mesh = oldTriangle.GetComponentInChildren<Enemies.RoundedTriangleMesh>();
            if (mesh == null) return new[] { (Vector2)oldTriangle.transform.position + Vector2.up };
            var points = new Vector2[3];
            for (int i = 0; i < 3; i++) points[i] = mesh.transform.TransformPoint(mesh.GetCornerPoint(i));
            return points;
        }

        #endregion

        #region S05 The Hall of Strings

        private IEnumerator PlayHallOfStrings()
        {
            Lock();
            StartCoroutine(overlay.Letterbox(true));
            Transform player = movement.transform;
            cameraFollow.FocusOn((Vector2)player.position + Vector2.up * 1.6f, 5f, 0.9f);
            yield return new WaitForSeconds(1f);

            yield return stringHall.FormCape(player, 2.6f);

            // The strings become the cape: it wraps tight around the circle, then opens.
            abilities.GiveCape(capeAbilities);
            cape.PlayWrap(1f);
            health.SetRespawnPoint(player.position);
            cameraFollow.Shake(0.12f, 0.35f);
            yield return new WaitForSeconds(2f);

            cameraFollow.ClearFocus();
            yield return overlay.ShowTitle(gameTitle, 2.4f);
            yield return overlay.Letterbox(false);

            Completed = true;
            Unlock();
            overlay.ShowHint(capeHint, 9f);
        }

        #endregion

        #region Helpers

        private void Lock() => input.AddControlLock(this);
        private void Unlock() => input.RemoveControlLock(this);

        /// <summary>Plays a dialogue and waits until it has been read.</summary>
        private IEnumerator Say(Dialogue dialogue)
        {
            if (dialogue == null || dialogueBox == null) yield break;
            while (dialogueBox.IsOpen) yield return null;

            bool done = false;
            dialogueBox.Play(dialogue, () => done = true);
            while (!done) yield return null;
        }

        private void Teleport(Vector2 position)
        {
            playerBody.position = position;
            movement.transform.position = position;
            playerBody.linearVelocity = Vector2.zero;
            movement.ResetMotion();
            if (cape != null) cape.ResetCape();
        }

        #endregion
    }
}
