using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class KillZone : MonoBehaviour
{
    private BoxCollider2D zoneCollider;

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider2D>();
        zoneCollider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryKillPlayer(other);
    }

    private static void TryKillPlayer(Collider2D other)
    {
        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health != null)
            health.Kill();
    }

    private void OnDrawGizmos()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null)
            return;

        Gizmos.color = new Color(1f, 0.12f, 0.12f, 0.12f);
        Gizmos.DrawCube(box.bounds.center, box.bounds.size);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
    }
}
