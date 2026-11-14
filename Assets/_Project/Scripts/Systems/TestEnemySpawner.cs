using System.Collections;
using System.Collections.Generic;
using ProjectCircle.Combat;
using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// TEMPORARY testing aid: endless waves of enemies.
    ///
    /// On Start it takes a snapshot of every enemy in the scene (anything with <see cref="EnemyHealth"/>):
    /// which kind it is and where it stands. When all of them are dead it waits a moment, then spawns the
    /// same enemies again at the same spots. Remove this object (or untick it) when you no longer need it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TestEnemySpawner : MonoBehaviour
    {
        [Tooltip("Seconds between the last enemy dying and the next wave appearing.")]
        [SerializeField, Min(0f)] private float delayBetweenWaves = 1.5f;

        [Tooltip("Extra enemies added to each new wave (0 = always the same set). Extras repeat the set, nudged sideways.")]
        [SerializeField, Min(0)] private int extraPerWave = 0;

        [Tooltip("Upper limit on enemies in one wave.")]
        [SerializeField, Min(1)] private int maxPerWave = 6;

        /// <summary>An inactive copy of an enemy as it was placed, kept as a template.</summary>
        private readonly List<GameObject> templates = new List<GameObject>();
        private readonly List<EnemyHealth> alive = new List<EnemyHealth>();
        private Transform templateRoot;
        private int wave = 1;
        private bool spawning;

        private void Start()
        {
            templateRoot = new GameObject("Spawner Templates").transform;
            templateRoot.gameObject.SetActive(false); // children stay asleep

            // The enemies already placed in the scene are wave 1, and also the templates for later waves.
            foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude))
            {
                GameObject template = Instantiate(enemy.gameObject, enemy.transform.position, enemy.transform.rotation, templateRoot);
                template.name = enemy.gameObject.name;
                templates.Add(template);
                Track(enemy);
            }

            if (templates.Count == 0) Debug.LogWarning($"{nameof(TestEnemySpawner)}: no enemies in the scene to copy.", this);
        }

        private void OnDestroy()
        {
            if (templateRoot != null) Destroy(templateRoot.gameObject);
        }

        private void Update()
        {
            alive.RemoveAll(e => e == null || e.IsDead);
            if (!spawning && alive.Count == 0 && templates.Count > 0)
            {
                StartCoroutine(SpawnWave());
            }
        }

        private IEnumerator SpawnWave()
        {
            spawning = true;
            yield return new WaitForSeconds(delayBetweenWaves);

            wave++;
            int count = Mathf.Min(maxPerWave, templates.Count + extraPerWave * (wave - 1));
            for (int i = 0; i < count; i++)
            {
                // Reuse the templates in turn; repeats are nudged sideways so enemies don't stack.
                GameObject template = templates[i % templates.Count];
                Vector3 point = template.transform.position + Vector3.right * (1.5f * (i / templates.Count));
                GameObject enemy = Instantiate(template, point, template.transform.rotation); // no parent → awake
                enemy.name = $"{template.name} (wave {wave})";
                if (enemy.TryGetComponent(out EnemyHealth health)) Track(health);
            }

            Debug.Log($"[TestEnemySpawner] Wave {wave}: {count} enemies.", this);
            spawning = false;
        }

        private void Track(EnemyHealth enemy)
        {
            if (!alive.Contains(enemy)) alive.Add(enemy);
        }
    }
}
