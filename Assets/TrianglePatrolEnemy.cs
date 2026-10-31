using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(PolygonCollider2D))]
public sealed class TrianglePatrolEnemy : MonoBehaviour
{
    private enum RollState
    {
        Moving,
        PoleVaulting,
        Toppling,
        Airborne
    }

    [Header("Platform pursuit")]
    [SerializeField, Min(0f)] private float chaseSpeed = 7f;
    [SerializeField, Min(0.01f)] private float jumpSpeed = 10f;
    [SerializeField, Min(0f)] private float platformHeightTolerance = 0.12f;
    [SerializeField, Min(0f)] private float jumpCooldown = 0.7f;
    [SerializeField, Min(0f)] private float platformEdgeInset = 0.08f;
    [SerializeField, Min(0f)] private float airSteering = 3f;
    [SerializeField, Min(0f)] private float airAcceleration = 24f;

    [Header("Vertex roll")]
    [SerializeField, Range(60f, 120f)] private float poleVaultAngle = 90f;
    [SerializeField, Min(0.05f)] private float poleVaultDuration = 0.42f;
    [SerializeField, Min(0.001f)] private float supportPointTolerance = 0.06f;
    [SerializeField, Min(0.1f)] private float toppleGravityScale = 3f;
    [SerializeField, Min(0f)] private float toppleStartAngularSpeed = 35f;
    [SerializeField] private LayerMask platformLayers = 1 << 3;

    [Header("Player collision")]
    [SerializeField] private Collider2D playerCollider;

    private Rigidbody2D body;
    private Rigidbody2D playerBody;
    private PolygonCollider2D polygon;
    private Collider2D enemyCollider;
    private Collider2D[] platforms;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
    private readonly List<Collider2D> searchQueue = new List<Collider2D>(16);
    private readonly Dictionary<Collider2D, Collider2D> previousPlatform =
        new Dictionary<Collider2D, Collider2D>(16);
    private float baseGravityScale;
    private float direction = 1f;
    private float jumpCooldownRemaining;
    private float poleVaultProgress;
    private float poleVaultStartRotation;
    private float bodyHalfWidth;
    private Vector2 poleVaultPivot;
    private Vector2 pivotOffsetFromBody;
    private Collider2D airTargetPlatform;
    private float airTargetX;
    private RollState state;
    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        polygon = GetComponent<PolygonCollider2D>();
        enemyCollider = polygon;
        playerBody = playerCollider != null ? playerCollider.attachedRigidbody : null;
        baseGravityScale = body.gravityScale;
        bodyHalfWidth = polygon.bounds.extents.x;
        groundFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = platformLayers,
            useTriggers = false
        };
        CachePlatforms();
        IgnorePlayerCollision();
    }

    private void OnEnable()
    {
        IgnorePlayerCollision();
    }

    private void CachePlatforms()
    {
        Collider2D[] colliders = FindObjectsByType<Collider2D>();
        List<Collider2D> validPlatforms = new List<Collider2D>(colliders.Length);
        foreach (Collider2D candidate in colliders)
        {
            if (candidate == null || candidate == polygon || candidate.isTrigger ||
                (platformLayers.value & (1 << candidate.gameObject.layer)) == 0)
                continue;

            validPlatforms.Add(candidate);
        }

        platforms = validPlatforms.ToArray();
    }

    private void IgnorePlayerCollision()
    {
        if (playerCollider != null && enemyCollider != null &&
            playerCollider.enabled && enemyCollider.enabled)
        {
            Physics2D.IgnoreCollision(playerCollider, enemyCollider, true);
        }
    }

    private void FixedUpdate()
    {
        jumpCooldownRemaining = Mathf.Max(0f, jumpCooldownRemaining - Time.fixedDeltaTime);
        Collider2D currentPlatform = FindPlatformBelow(body.position, 2.5f);
        Collider2D playerPlatform = playerBody != null
            ? FindPlatformBelow(playerBody.position, 50f)
            : null;
        Collider2D nextPlatform = FindNextPlatform(currentPlatform, playerPlatform);
        float destinationX = GetDestinationX(nextPlatform, playerPlatform);

        switch (state)
        {
            case RollState.Moving:
                UpdateDirection(destinationX);
                if (currentPlatform != null && nextPlatform != null &&
                    IsGrounded() &&
                    ShouldJumpToPlatform(currentPlatform, nextPlatform, destinationX))
                {
                    BeginPlatformJump(nextPlatform, destinationX);
                }
                else if (TryGetStableForwardPivot(out Vector2 pivot))
                {
                    BeginPoleVault(pivot);
                }
                else if (body.linearVelocity.y < -0.05f)
                {
                    BeginFallingChase(nextPlatform, destinationX);
                }
                break;

            case RollState.PoleVaulting:
                UpdatePoleVault();
                break;

            case RollState.Toppling:
                UpdateToppling(currentPlatform, nextPlatform, destinationX);
                break;

            case RollState.Airborne:
                UpdateAirChase();
                break;
        }
    }

    private void UpdateDirection(float targetX)
    {
        float offset = targetX - body.position.x;
        if (Mathf.Abs(offset) > 0.08f)
            direction = Mathf.Sign(offset);
    }

    private float GetDestinationX(Collider2D nextPlatform, Collider2D playerPlatform)
    {
        if (nextPlatform == null)
            return playerBody != null ? playerBody.position.x : body.position.x;

        float inset = Mathf.Min(platformEdgeInset + bodyHalfWidth,
            Mathf.Max(0f, nextPlatform.bounds.extents.x - 0.05f));
        float minX = nextPlatform.bounds.min.x + inset;
        float maxX = nextPlatform.bounds.max.x - inset;
        float preferredX = playerPlatform == nextPlatform && playerBody != null
            ? playerBody.position.x
            : nextPlatform.bounds.center.x;
        return Mathf.Clamp(preferredX, minX, maxX);
    }

    private Collider2D FindPlatformBelow(Vector2 position, float distance)
    {
        RaycastHit2D hit = Physics2D.Raycast(
            position + Vector2.up * 0.15f, Vector2.down, distance, platformLayers);
        return hit.collider;
    }

    private Collider2D FindNextPlatform(Collider2D current, Collider2D target)
    {
        if (current == null || target == null || current == target || platforms == null)
            return target;

        searchQueue.Clear();
        previousPlatform.Clear();
        searchQueue.Add(current);
        previousPlatform[current] = null;

        for (int index = 0; index < searchQueue.Count; index++)
        {
            Collider2D from = searchQueue[index];
            for (int i = 0; i < platforms.Length; i++)
            {
                Collider2D to = platforms[i];
                if (to == null || previousPlatform.ContainsKey(to) ||
                    !CanJumpBetween(from, to))
                    continue;

                previousPlatform[to] = from;
                if (to == target)
                {
                    Collider2D step = to;
                    while (previousPlatform[step] != current && previousPlatform[step] != null)
                        step = previousPlatform[step];
                    return step;
                }

                searchQueue.Add(to);
            }
        }

        return target;
    }

    private bool CanJumpBetween(Collider2D from, Collider2D to)
    {
        if (from == to || chaseSpeed <= 0f)
            return false;

        float rise = to.bounds.max.y - from.bounds.max.y;
        float gravity = Mathf.Abs(Physics2D.gravity.y * baseGravityScale);
        if (gravity < 0.01f)
            return false;

        float discriminant = jumpSpeed * jumpSpeed - 2f * gravity * rise;
        if (discriminant < 0f)
            return false;

        float flightTime = (jumpSpeed + Mathf.Sqrt(discriminant)) / gravity;
        float horizontalReach = chaseSpeed * flightTime;
        float fromMin = from.bounds.min.x + bodyHalfWidth;
        float fromMax = from.bounds.max.x - bodyHalfWidth;
        float toMin = to.bounds.min.x + bodyHalfWidth + platformEdgeInset;
        float toMax = to.bounds.max.x - bodyHalfWidth - platformEdgeInset;
        float horizontalGap = toMin > fromMax
            ? toMin - fromMax
            : fromMin > toMax ? fromMin - toMax : 0f;

        return horizontalGap <= horizontalReach;
    }

    private bool ShouldJumpToPlatform(Collider2D from, Collider2D to, float targetX)
    {
        float heightDifference = to.bounds.max.y - from.bounds.max.y;
        if (heightDifference <= platformHeightTolerance || jumpCooldownRemaining > 0f ||
            !CanJumpBetween(from, to))
            return false;

        float gravity = Mathf.Abs(Physics2D.gravity.y * baseGravityScale);
        float discriminant = jumpSpeed * jumpSpeed - 2f * gravity * heightDifference;
        if (gravity < 0.01f || discriminant < 0f)
            return false;

        float flightTime = (jumpSpeed + Mathf.Sqrt(discriminant)) / gravity;
        return Mathf.Abs(targetX - body.position.x) <= chaseSpeed * flightTime;
    }

    private void BeginPlatformJump(Collider2D destination, float targetX)
    {
        direction = Mathf.Sign(targetX - body.position.x);
        airTargetPlatform = destination;
        airTargetX = targetX;
        body.gravityScale = baseGravityScale;
        body.angularVelocity = 0f;
        body.linearVelocity = new Vector2(direction * chaseSpeed, jumpSpeed);
        jumpCooldownRemaining = jumpCooldown;
        state = RollState.Airborne;
    }

    private void BeginFallingChase(Collider2D destination, float targetX)
    {
        airTargetPlatform = destination;
        airTargetX = targetX;
        state = RollState.Airborne;
    }

    private void UpdateAirChase()
    {
        if (airTargetPlatform == null)
        {
            Collider2D playerPlatform = playerBody != null
                ? FindPlatformBelow(playerBody.position, 50f)
                : null;
            airTargetPlatform = playerPlatform;
            airTargetX = GetDestinationX(playerPlatform, playerPlatform);
        }

        float offset = airTargetX - body.position.x;
        float desiredVelocityX = Mathf.Clamp(offset * airSteering, -chaseSpeed, chaseSpeed);
        float velocityX = Mathf.MoveTowards(
            body.linearVelocity.x, desiredVelocityX, airAcceleration * Time.fixedDeltaTime);
        body.linearVelocity = new Vector2(velocityX, body.linearVelocity.y);

        if (body.linearVelocity.y <= 0f && IsGrounded())
        {
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            body.gravityScale = baseGravityScale;
            airTargetPlatform = null;
            state = RollState.Moving;
        }
    }

    private bool IsGrounded()
    {
        int hitCount = polygon.Cast(Vector2.down, groundFilter, groundHits, 0.06f);
        for (int i = 0; i < hitCount; i++)
        {
            if (groundHits[i].collider != null && groundHits[i].normal.y > 0.5f)
                return true;
        }

        return false;
    }

    private bool TryGetStableForwardPivot(out Vector2 pivot)
    {
        pivot = default;
        if (!body.IsTouchingLayers(platformLayers))
            return false;

        Vector2[] points = polygon.points;
        if (points.Length < 3)
            return false;

        float lowestY = float.PositiveInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 worldPoint = polygon.transform.TransformPoint(points[i] + polygon.offset);
            lowestY = Mathf.Min(lowestY, worldPoint.y);
        }

        int supportCount = 0;
        float leadingX = direction > 0f ? float.NegativeInfinity : float.PositiveInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 worldPoint = polygon.transform.TransformPoint(points[i] + polygon.offset);
            if (worldPoint.y > lowestY + supportPointTolerance)
                continue;

            supportCount++;
            if ((direction > 0f && worldPoint.x > leadingX) ||
                (direction < 0f && worldPoint.x < leadingX))
            {
                leadingX = worldPoint.x;
                pivot = worldPoint;
            }
        }

        return supportCount >= 2;
    }

    private void BeginPoleVault(Vector2 pivot)
    {
        state = RollState.PoleVaulting;
        poleVaultProgress = 0f;
        poleVaultPivot = pivot;
        pivotOffsetFromBody = poleVaultPivot - body.position;
        poleVaultStartRotation = body.rotation;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.WakeUp();
    }

    private void UpdatePoleVault()
    {
        poleVaultProgress = Mathf.Min(1f,
            poleVaultProgress + Time.fixedDeltaTime / poleVaultDuration);

        float angleDelta = -direction * poleVaultAngle * poleVaultProgress;
        Vector2 rotatedOffset = Rotate(pivotOffsetFromBody, angleDelta);
        body.position = poleVaultPivot - rotatedOffset;
        body.rotation = poleVaultStartRotation + angleDelta;

        if (poleVaultProgress < 1f)
            return;

        body.gravityScale = toppleGravityScale;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = -direction * toppleStartAngularSpeed;
        state = RollState.Toppling;
    }

    private void UpdateToppling(Collider2D currentPlatform, Collider2D nextPlatform, float targetX)
    {
        if (!TryGetStableForwardPivot(out Vector2 pivot))
        {
            if (body.linearVelocity.y < -0.05f)
                BeginFallingChase(nextPlatform, targetX);
            return;
        }

        UpdateDirection(targetX);
        if (!TryGetStableForwardPivot(out pivot))
            return;

        if (currentPlatform != null && nextPlatform != null &&
            ShouldJumpToPlatform(currentPlatform, nextPlatform, targetX))
        {
            BeginPlatformJump(nextPlatform, targetX);
        }
        else
        {
            BeginPoleVault(pivot);
        }
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cosine = Mathf.Cos(radians);
        float sine = Mathf.Sin(radians);
        return new Vector2(
            vector.x * cosine - vector.y * sine,
            vector.x * sine + vector.y * cosine);
    }

    private void OnValidate()
    {
        chaseSpeed = Mathf.Max(0f, chaseSpeed);
        jumpSpeed = Mathf.Max(0.01f, jumpSpeed);
        platformHeightTolerance = Mathf.Max(0f, platformHeightTolerance);
        jumpCooldown = Mathf.Max(0f, jumpCooldown);
        platformEdgeInset = Mathf.Max(0f, platformEdgeInset);
        airSteering = Mathf.Max(0f, airSteering);
        airAcceleration = Mathf.Max(0f, airAcceleration);
        poleVaultAngle = Mathf.Clamp(poleVaultAngle, 60f, 120f);
        poleVaultDuration = Mathf.Max(0.05f, poleVaultDuration);
        supportPointTolerance = Mathf.Max(0.001f, supportPointTolerance);
        toppleGravityScale = Mathf.Max(0.1f, toppleGravityScale);
        toppleStartAngularSpeed = Mathf.Max(0f, toppleStartAngularSpeed);
    }
}
