using UnityEngine;

[DisallowMultipleComponent]
public sealed class CameraFollow2D : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
    [SerializeField, Min(0f)] private float smoothTime = 0.08f;
    [SerializeField] private float minimumY = 0f;

    private Vector3 followVelocity;

    private void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 destination = target.position + offset;
        destination.y = Mathf.Max(destination.y, minimumY);
        destination.z = offset.z + target.position.z;

        if (smoothTime <= 0f)
        {
            transform.position = destination;
            followVelocity = Vector3.zero;
            return;
        }

        transform.position = Vector3.SmoothDamp(
            transform.position,
            destination,
            ref followVelocity,
            smoothTime);
    }
}
