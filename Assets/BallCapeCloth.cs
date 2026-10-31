using UnityEngine;

[DisallowMultipleComponent]
public sealed class BallCapeCloth : MonoBehaviour
{
    [Header("Cape shape")]
    [Min(0.1f)] public float width = 1.5f;
    [Min(0.1f)] public float length = 1.7f;
    [SerializeField, Range(0.15f, 0.8f)] private float attachmentWidthRatio = 0.28f;
    [SerializeField, Range(2, 16)] private int columns = 9;
    [SerializeField, Range(2, 20)] private int rows = 13;
    [SerializeField, Min(0f)] private float attachmentHeight = 0.42f;
    [SerializeField, Min(0f)] private float restingFoldAmount = 0.04f;
    [SerializeField] private Color capeColor = new Color(0.025f, 0.025f, 0.035f, 1f);

    [Header("Cloth motion")]
    [SerializeField, Min(0f)] private float gravity = 18f;
    [SerializeField, Range(0f, 1f)] private float damping = 0.94f;
    [SerializeField, Min(1)] private int constraintPasses = 16;
    [SerializeField, Min(0f)] private float movementInfluence = 0.45f;
    [SerializeField, Min(0.01f)] private float idleSettleSpeed = 2.5f;
    [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.12f;

    [Header("Cape attack")]
    [SerializeField, Min(0.01f)] private float wrapTransitionSpeed = 24f;

    private Rigidbody2D body;
    private Transform visual;
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private SpriteRenderer ballSpriteRenderer;
    private Color originalSpriteColor = Color.white;
    private Vector3[] positions;
    private Vector3[] previousPositions;
    private Vector3[] vertices;
    private Color[] vertexColors;
    private int[] triangles;
    private float restVertical;
    private float simulationAccumulator;
    private float wrapAmount;
    private bool wrappingForAttack;
    private int normalSortingOrder;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        ballSpriteRenderer = spriteRenderer;
        if (ballSpriteRenderer != null)
            originalSpriteColor = ballSpriteRenderer.color;
        visual = spriteRenderer != null ? spriteRenderer.transform : transform;
        CreateCapeMesh();
        InitializeCloth();
    }

    private void LateUpdate()
    {
        if (mesh == null || positions == null)
            return;

        const float step = 1f / 60f;
        simulationAccumulator = Mathf.Min(simulationAccumulator + Time.deltaTime, step * 4f);
        while (simulationAccumulator >= step)
        {
            SimulateCloth(step);
            simulationAccumulator -= step;
        }

        UpdateMesh();
    }

    private void OnDestroy()
    {
        if (mesh != null)
            Destroy(mesh);

        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            Destroy(meshRenderer.sharedMaterial);
    }

    private void CreateCapeMesh()
    {
        GameObject capeObject = new GameObject("Black Cape (Cloth)");
        capeObject.transform.SetParent(transform, false);
        MeshFilter meshFilter = capeObject.AddComponent<MeshFilter>();
        meshRenderer = capeObject.AddComponent<MeshRenderer>();
        SpriteRenderer spriteRenderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;

        mesh = new Mesh { name = "Ball Cape Cloth Mesh" };
        mesh.MarkDynamic();
        meshFilter.sharedMesh = mesh;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader) { color = Color.white };
        meshRenderer.sharedMaterial = material;
        if (spriteRenderer != null)
        {
            meshRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            normalSortingOrder = spriteRenderer.sortingOrder - 1;
            meshRenderer.sortingOrder = normalSortingOrder;
        }
        else
        {
            meshRenderer.sortingOrder = -1;
        }
    }

    private void InitializeCloth()
    {
        int count = columns * rows;
        positions = new Vector3[count];
        previousPositions = new Vector3[count];
        vertices = new Vector3[count];
        vertexColors = new Color[count];
        triangles = new int[(columns - 1) * (rows - 1) * 6];
        restVertical = length / (rows - 1);

        for (int y = 0; y < rows; y++)
        {
            float v = (float)y / (rows - 1);
            for (int x = 0; x < columns; x++)
            {
                float u = (float)x / (columns - 1);
                int index = Index(x, y);
                float rowWidth = GetRowWidth(y);
                Vector3 point = transform.TransformPoint(new Vector3(
                    (u - 0.5f) * rowWidth,
                    0.16f - v * length,
                    0f));
                positions[index] = point;
                previousPositions[index] = point;
            }
        }

        int triangle = 0;
        for (int y = 0; y < rows - 1; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                int topLeft = Index(x, y);
                int topRight = Index(x + 1, y);
                int bottomLeft = Index(x, y + 1);
                int bottomRight = Index(x + 1, y + 1);

                triangles[triangle++] = topLeft;
                triangles[triangle++] = topRight;
                triangles[triangle++] = bottomLeft;
                triangles[triangle++] = topRight;
                triangles[triangle++] = bottomRight;
                triangles[triangle++] = bottomLeft;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        for (int i = 0; i < vertexColors.Length; i++)
            vertexColors[i] = capeColor;
        mesh.colors = vertexColors;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(width * 4f, length * 4f, 2f));
    }

    private void SimulateCloth(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        Vector2 velocity = body != null ? body.linearVelocity : Vector2.zero;
        float anchorWidth = width * attachmentWidthRatio;
        Vector3 acceleration = Vector3.down * gravity +
            new Vector3(-velocity.x * movementInfluence, 0f, 0f);

        for (int x = 0; x < columns; x++)
        {
            float u = (float)x / (columns - 1);
            Vector3 anchor = transform.TransformPoint(new Vector3(
                (u - 0.5f) * anchorWidth,
                attachmentHeight,
                0f));
            positions[Index(x, 0)] = anchor;
            previousPositions[Index(x, 0)] = anchor;
        }

        float dtSquared = deltaTime * deltaTime;
        for (int y = 1; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int index = Index(x, y);
                Vector3 current = positions[index];
                Vector3 velocityStep = (current - previousPositions[index]) * damping;
                previousPositions[index] = current;
                positions[index] = current + velocityStep + acceleration * dtSquared;
            }
        }

        for (int pass = 0; pass < constraintPasses; pass++)
        {
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (x < columns - 1 && y > 0)
                        Constrain(Index(x, y), Index(x + 1, y), GetHorizontalRestLength(y), false);
                    if (y < rows - 1)
                        Constrain(Index(x, y), Index(x, y + 1), GetVerticalRestLength(x, y), y == 0);
                    if (x < columns - 1 && y < rows - 1)
                        Constrain(Index(x, y), Index(x + 1, y + 1),
                            GetDiagonalRestLength(x, y, 1), y == 0);
                    if (x > 0 && y < rows - 1)
                        Constrain(Index(x, y), Index(x - 1, y + 1),
                            GetDiagonalRestLength(x, y, -1), y == 0);
                    if (x < columns - 2 && y > 0)
                        Constrain(Index(x, y), Index(x + 2, y),
                            GetHorizontalRestLength(y) * 2f, false, 0.2f);
                    if (y < rows - 2)
                    {
                        Constrain(Index(x, y), Index(x, y + 2),
                            GetVerticalBendRestLength(x, y), y == 0, 0.2f);
                    }
                }
            }
        }

        SettleWhenIdle(deltaTime);
    }

    private void SettleWhenIdle(float deltaTime)
    {
        if (body != null && (body.linearVelocity.sqrMagnitude > idleSpeedThreshold * idleSpeedThreshold ||
                             Mathf.Abs(body.angularVelocity) > idleSpeedThreshold))
            return;

        float blend = 1f - Mathf.Exp(-idleSettleSpeed * deltaTime);
        for (int y = 1; y < rows; y++)
        {
            float v = (float)y / (rows - 1);
            for (int x = 0; x < columns; x++)
            {
                float u = (float)x / (columns - 1);
                int index = Index(x, y);
                float fold = Mathf.Sin(u * Mathf.PI * 4f) * restingFoldAmount * v;
                Vector3 restPosition = transform.TransformPoint(new Vector3(
                    (u - 0.5f) * GetRowWidth(y) + fold,
                    attachmentHeight - v * length,
                    0f));

                positions[index] = Vector3.Lerp(positions[index], restPosition, blend);
                previousPositions[index] = Vector3.Lerp(previousPositions[index], restPosition, blend);
            }
        }
    }

    private void Constrain(int first, int second, float restLength, bool firstIsPinned, float stiffness = 1f)
    {
        Vector3 difference = positions[second] - positions[first];
        float distance = difference.magnitude;
        if (distance <= Mathf.Epsilon)
            return;

        Vector3 correction = difference * ((distance - restLength) / distance) * stiffness;
        if (firstIsPinned)
            positions[second] -= correction;
        else
        {
            positions[first] += correction * 0.5f;
            positions[second] -= correction * 0.5f;
        }
    }

    private float GetRowWidth(int y)
    {
        float progress = (float)y / (rows - 1);
        progress = progress * progress * (3f - 2f * progress);
        return Mathf.Lerp(width * attachmentWidthRatio, width, progress);
    }

    private float GetHorizontalRestLength(int y) => GetRowWidth(y) / (columns - 1);

    private float GetVerticalRestLength(int x, int y)
    {
        float u = (float)x / (columns - 1) - 0.5f;
        float horizontalOffset = u * (GetRowWidth(y + 1) - GetRowWidth(y));
        return Mathf.Sqrt(restVertical * restVertical + horizontalOffset * horizontalOffset);
    }

    private float GetVerticalBendRestLength(int x, int y)
    {
        float u = (float)x / (columns - 1) - 0.5f;
        float horizontalOffset = u * (GetRowWidth(y + 2) - GetRowWidth(y));
        float verticalOffset = restVertical * 2f;
        return Mathf.Sqrt(verticalOffset * verticalOffset + horizontalOffset * horizontalOffset);
    }

    private float GetDiagonalRestLength(int x, int y, int horizontalStep)
    {
        float currentU = (float)x / (columns - 1) - 0.5f;
        float nextU = (float)(x + horizontalStep) / (columns - 1) - 0.5f;
        float horizontalOffset = nextU * GetRowWidth(y + 1) - currentU * GetRowWidth(y);
        return Mathf.Sqrt(restVertical * restVertical + horizontalOffset * horizontalOffset);
    }

    private void UpdateMesh()
    {
        wrapAmount = Mathf.MoveTowards(wrapAmount, wrappingForAttack ? 1f : 0f,
            wrapTransitionSpeed * Time.deltaTime);
        // Raise the broad cloth surface over the ball while it closes around it.
        meshRenderer.sortingOrder = normalSortingOrder + (wrapAmount > 0.02f ? 2 : 0);
        if (ballSpriteRenderer != null)
            ballSpriteRenderer.color = originalSpriteColor;

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 clothPosition = transform.InverseTransformPoint(positions[i]);
            int x = i % columns;
            int y = i / columns;
            float u = (float)x / (columns - 1);
            float v = (float)y / (rows - 1);
            float normalizedY = (v - 0.5f) * 2f;
            float ovalWidth = Mathf.Sqrt(Mathf.Max(0f, 1f - normalizedY * normalizedY));
            float foldPhase = u * Mathf.PI * 12f + v * Mathf.PI * 1.5f;
            float fold = Mathf.Cos(foldPhase);
            float hangingFold = Mathf.Sin(u * Mathf.PI * 8f + v * Mathf.PI * 1.2f);
            float edgeWave = Mathf.Sin(u * Mathf.PI * 6f + v * Mathf.PI * 0.7f) * 0.035f;
            Vector3 wrappedPosition = new Vector3(
                (u - 0.5f) * (1.08f + edgeWave) * ovalWidth + hangingFold * 0.035f * ovalWidth,
                normalizedY * 0.54f + 0.02f + hangingFold * 0.018f * ovalWidth,
                -0.035f + fold * 0.012f);
            vertices[i] = Vector3.Lerp(clothPosition, wrappedPosition, wrapAmount);

            float foldHighlight = Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, fold));
            Color foldedCape = Color.Lerp(capeColor, new Color(0.16f, 0.16f, 0.18f, 1f),
                0.12f + foldHighlight * 0.52f);
            vertexColors[i] = Color.Lerp(capeColor, foldedCape, wrapAmount);
        }

        mesh.vertices = vertices;
        mesh.colors = vertexColors;
        mesh.RecalculateNormals();
    }

    public void SetAttackWrap(bool wrapped)
    {
        wrappingForAttack = wrapped;
    }

    public MeshRenderer CreateAfterimage(float opacity, out Mesh snapshotMesh, out Material snapshotMaterial)
    {
        snapshotMesh = null;
        snapshotMaterial = null;
        if (mesh == null || meshRenderer == null || meshRenderer.sharedMaterial == null)
            return null;

        GameObject capeGhost = new GameObject("Cape Dash Afterimage");
        capeGhost.transform.SetPositionAndRotation(transform.position, transform.rotation);
        capeGhost.transform.localScale = transform.lossyScale;

        snapshotMesh = Instantiate(mesh);
        snapshotMesh.name = "Cape Dash Afterimage Mesh";
        capeGhost.AddComponent<MeshFilter>().sharedMesh = snapshotMesh;

        MeshRenderer ghostRenderer = capeGhost.AddComponent<MeshRenderer>();
        snapshotMaterial = new Material(meshRenderer.sharedMaterial);
        Color capeColor = snapshotMaterial.color;
        capeColor.a *= Mathf.Clamp01(opacity);
        snapshotMaterial.color = capeColor;
        ghostRenderer.sharedMaterial = snapshotMaterial;
        ghostRenderer.sortingLayerID = meshRenderer.sortingLayerID;
        ghostRenderer.sortingOrder = meshRenderer.sortingOrder - 2;
        ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ghostRenderer.receiveShadows = false;
        return ghostRenderer;
    }

    private int Index(int x, int y) => y * columns + x;

    private void OnValidate()
    {
        width = Mathf.Max(0.1f, width);
        length = Mathf.Max(0.1f, length);
        columns = Mathf.Clamp(columns, 2, 16);
        rows = Mathf.Clamp(rows, 2, 20);
        attachmentWidthRatio = Mathf.Clamp(attachmentWidthRatio, 0.15f, 0.8f);
        attachmentHeight = Mathf.Max(0f, attachmentHeight);
        restingFoldAmount = Mathf.Max(0f, restingFoldAmount);
        constraintPasses = Mathf.Max(1, constraintPasses);
        idleSettleSpeed = Mathf.Max(0.01f, idleSettleSpeed);
        idleSpeedThreshold = Mathf.Max(0f, idleSpeedThreshold);
        wrapTransitionSpeed = Mathf.Max(0.01f, wrapTransitionSpeed);

        if (Application.isPlaying && mesh != null)
            InitializeCloth();
    }
}
