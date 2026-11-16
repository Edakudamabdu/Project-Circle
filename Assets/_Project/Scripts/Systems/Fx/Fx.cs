using UnityEngine;

namespace ProjectCircle.Systems
{
    /// <summary>
    /// One-shot visual effects, built entirely in code from plain shapes (no textures or sprite assets):
    /// particle bursts of little squares or triangles, expanding rings, ground cracks, charge-ups, light columns.
    ///
    /// Call from anywhere, e.g. <c>Fx.Dust(position, width)</c>. Every effect cleans itself up.
    /// </summary>
    public static class Fx
    {
        /// <summary>Settings for one <see cref="Burst"/>.</summary>
        public struct BurstSettings
        {
            public int count;
            public Color colorA, colorB;      // each particle picks a colour between these
            public float speedMin, speedMax;
            public float sizeMin, sizeMax;
            public float lifeMin, lifeMax;
            public float direction;           // degrees (0 = right, 90 = up)
            public float spread;              // degrees either side of the direction (180 = all round)
            public float gravity;             // multiplier of world gravity
            public float drag;                // how quickly they slow down
            public Vector2 area;              // spawn box size around the position
            public bool triangles;            // triangle shards instead of squares
            public int sortingOrder;
        }

        private static Material material;
        private static Mesh triangleMesh;

        /// <summary>A vertex-colour unlit material shared by every effect.</summary>
        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    material = new Material(shader) { name = "Fx (runtime)" };
                }
                return material;
            }
        }

        #region Presets

        /// <summary>Heavy landing / slam: dust and grit flung out both ways along the floor, plus a flat dust ring.</summary>
        public static void Dust(Vector2 floorPoint, float width, float strength = 1f)
        {
            var dust = new Color(0.62f, 0.6f, 0.66f, 0.85f);
            var dark = new Color(0.36f, 0.34f, 0.4f, 0.85f);
            int count = Mathf.RoundToInt(14 * strength);
            for (int side = -1; side <= 1; side += 2)
            {
                Burst(floorPoint + new Vector2(side * width * 0.4f, 0.1f), new BurstSettings
                {
                    count = count, colorA = dust, colorB = dark,
                    speedMin = 3f * strength, speedMax = 8f * strength, sizeMin = 0.12f, sizeMax = 0.32f,
                    lifeMin = 0.35f, lifeMax = 0.7f, direction = side > 0 ? 15f : 165f, spread = 20f,
                    gravity = 0.6f, drag = 4f, area = new Vector2(width * 0.2f, 0.1f), sortingOrder = 12,
                });
            }
            // A few chunks thrown upward.
            Burst(floorPoint + Vector2.up * 0.1f, new BurstSettings
            {
                count = Mathf.RoundToInt(6 * strength), colorA = dark, colorB = dust,
                speedMin = 4f, speedMax = 8f * strength, sizeMin = 0.1f, sizeMax = 0.22f,
                lifeMin = 0.4f, lifeMax = 0.7f, direction = 90f, spread = 45f, gravity = 2.5f, drag = 1f,
                area = new Vector2(width * 0.6f, 0.1f), triangles = true, sortingOrder = 12,
            });
            Ring(floorPoint + Vector2.up * 0.05f, new Color(0.7f, 0.68f, 0.74f, 0.5f), width * 1.1f * strength, 0.35f, 0.12f, 0.25f);
        }

        /// <summary>A small soft puff (a footstep, a light landing, a tumble thud).</summary>
        public static void Puff(Vector2 point, float size = 1f, Color? color = null)
        {
            Color c = color ?? new Color(0.62f, 0.6f, 0.66f, 0.7f);
            Burst(point, new BurstSettings
            {
                count = Mathf.RoundToInt(6 + 4 * size), colorA = c, colorB = new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, c.a),
                speedMin = 0.8f * size, speedMax = 2.5f * size, sizeMin = 0.08f * size, sizeMax = 0.2f * size,
                lifeMin = 0.25f, lifeMax = 0.5f, direction = 90f, spread = 80f, gravity = -0.1f, drag = 4f,
                area = new Vector2(0.4f * size, 0.05f), sortingOrder = 12,
            });
        }

        /// <summary>Hit sparks: bright shards spraying in a direction (degrees).</summary>
        public static void Sparks(Vector2 point, float direction, Color color, int count = 10, float speed = 9f, float spread = 50f)
        {
            Burst(point, new BurstSettings
            {
                count = count, colorA = Color.white, colorB = color,
                speedMin = speed * 0.4f, speedMax = speed, sizeMin = 0.08f, sizeMax = 0.2f,
                lifeMin = 0.15f, lifeMax = 0.35f, direction = direction, spread = spread, gravity = 0.8f, drag = 3f,
                triangles = true, sortingOrder = 14,
            });
        }

        /// <summary>Shards flying out in every direction (breaking, being hurt).</summary>
        public static void Shards(Vector2 point, Color color, int count = 12, float speed = 6f, float size = 0.18f)
        {
            Burst(point, new BurstSettings
            {
                count = count, colorA = color, colorB = Color.Lerp(color, Color.white, 0.5f),
                speedMin = speed * 0.4f, speedMax = speed, sizeMin = size * 0.6f, sizeMax = size * 1.3f,
                lifeMin = 0.3f, lifeMax = 0.6f, direction = 90f, spread = 180f, gravity = 1.2f, drag = 1.5f,
                triangles = true, sortingOrder = 14,
            });
        }

        /// <summary>
        /// An expanding ring that fades out. <paramref name="flatten"/> squashes it vertically (0.25 = a ring
        /// lying on the floor).
        /// </summary>
        public static void Ring(Vector2 point, Color color, float radius, float time = 0.3f, float thickness = 0.12f, float flatten = 1f)
        {
            var go = new GameObject("Fx Ring");
            go.transform.position = point;
            go.transform.localScale = new Vector3(1f, flatten, 1f);
            go.AddComponent<FxRing>().Play(color, radius, time, thickness);
        }

        /// <summary>A bright jagged crack along the floor that fades.</summary>
        public static void Crack(Vector2 floorPoint, float width, Color color, float time = 0.5f)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var points = new Vector3[6];
                for (int i = 0; i < points.Length; i++)
                {
                    float t = i / (points.Length - 1f);
                    points[i] = new Vector3(floorPoint.x + side * width * t, floorPoint.y + 0.02f + (i % 2 == 0 ? 0f : 0.08f), 0f);
                }
                FxLine.Spawn(points, color, 0.09f, time, 13);
            }
        }

        /// <summary>A vertical crack (e.g. across a wall), jagged, from <paramref name="from"/> towards <paramref name="to"/>.</summary>
        public static void WallCrack(Vector2 from, Vector2 to, Color color, float time, float width = 0.08f)
        {
            var points = new Vector3[7];
            Vector2 side = Vector2.Perpendicular((to - from).normalized);
            for (int i = 0; i < points.Length; i++)
            {
                float t = i / (points.Length - 1f);
                Vector2 p = Vector2.Lerp(from, to, t) + side * (i == 0 || i == points.Length - 1 ? 0f : Random.Range(-0.25f, 0.25f));
                points[i] = new Vector3(p.x, p.y, 0f);
            }
            FxLine.Spawn(points, color, width, time, 6);
        }

        /// <summary>Motes pulled in towards a point over <paramref name="time"/> (a charge-up).</summary>
        public static void Converge(Vector2 point, Color color, float time, float radius = 1.6f, int count = 14)
        {
            var ps = NewSystem("Fx Converge", point, 14, triangles: false);
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float r = radius * Random.Range(0.7f, 1.1f);
                float life = Mathf.Max(0.12f, time * Random.Range(0.5f, 1f));
                Vector2 offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                emit.position = (Vector3)(point + offset);
                emit.velocity = -(Vector3)offset / life; // arrive at the centre as it fades
                emit.startLifetime = life;
                emit.startSize = Random.Range(0.06f, 0.14f);
                emit.startColor = Color.Lerp(color, Color.white, Random.value * 0.5f);
                ps.Emit(emit, 1);
            }
            ps.Play();
        }

        /// <summary>Embers drifting off something for <paramref name="time"/> seconds (a hanging bolt).</summary>
        public static void Embers(Transform follow, Color color, float time)
        {
            if (follow == null) return;
            var go = new GameObject("Fx Embers");
            go.AddComponent<FxEmbers>().Play(follow, color, time);
        }

        /// <summary>A column of light standing on the floor that fades (something arriving from above).</summary>
        public static void LightColumn(Vector2 floorPoint, float height, Color color, float time = 0.8f, float width = 1.4f)
        {
            var points = new[] { new Vector3(floorPoint.x, floorPoint.y, 0f), new Vector3(floorPoint.x, floorPoint.y + height, 0f) };
            FxLine.Spawn(points, color, width, time, -1, fadeWidth: true, endAlpha: 0f);
        }

        /// <summary>A few speed lines left behind something moving fast (the dash).</summary>
        public static void SpeedLines(Vector2 point, int direction, Color color)
        {
            for (int i = 0; i < 4; i++)
            {
                float y = point.y + Random.Range(-0.35f, 0.35f);
                float x = point.x - direction * Random.Range(0.2f, 0.8f);
                float length = Random.Range(0.8f, 1.6f);
                FxLine.Spawn(new[] { new Vector3(x, y, 0f), new Vector3(x - direction * length, y, 0f) }, color, 0.05f, 0.22f, 9);
            }
        }

        #endregion

        #region Core

        /// <summary>Emits a one-shot burst of little squares or triangles.</summary>
        public static void Burst(Vector2 point, BurstSettings s)
        {
            if (s.count <= 0) return;
            var ps = NewSystem("Fx Burst", point, s.sortingOrder, s.triangles);
            var main = ps.main;
            main.gravityModifier = s.gravity;

            if (s.drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = s.drag;
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }

            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < s.count; i++)
            {
                float angle = (s.direction + Random.Range(-s.spread, s.spread)) * Mathf.Deg2Rad;
                float speed = Random.Range(s.speedMin, s.speedMax);
                Vector2 offset = new Vector2(Random.Range(-0.5f, 0.5f) * s.area.x, Random.Range(-0.5f, 0.5f) * s.area.y);
                emit.position = (Vector3)(point + offset);
                emit.velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;
                emit.startLifetime = Random.Range(s.lifeMin, Mathf.Max(s.lifeMin, s.lifeMax));
                emit.startSize = Random.Range(s.sizeMin, s.sizeMax);
                emit.startColor = Color.Lerp(s.colorA, s.colorB, Random.value);
                emit.rotation = Random.Range(0f, 360f);
                emit.angularVelocity = Random.Range(-360f, 360f);
                ps.Emit(emit, 1);
            }
            ps.Play();
        }

        /// <summary>A one-shot particle system that shrinks and fades its particles, then destroys itself.</summary>
        private static ParticleSystem NewSystem(string name, Vector2 point, int sortingOrder, bool triangles)
        {
            var go = new GameObject(name);
            go.transform.position = point;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.1f;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (triangles)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = TriangleMesh;
                renderer.alignment = ParticleSystemRenderSpace.View;
            }
            return ps;
        }

        /// <summary>A unit equilateral triangle (for shard particles).</summary>
        private static Mesh TriangleMesh
        {
            get
            {
                if (triangleMesh != null) return triangleMesh;
                triangleMesh = new Mesh { name = "Fx Triangle" };
                float h = 0.5f;
                triangleMesh.vertices = new[]
                {
                    new Vector3(0f, h, 0f),
                    new Vector3(-h * 0.866f, -h * 0.5f, 0f),
                    new Vector3(h * 0.866f, -h * 0.5f, 0f),
                };
                triangleMesh.triangles = new[] { 0, 2, 1 };
                triangleMesh.colors = new[] { Color.white, Color.white, Color.white };
                triangleMesh.RecalculateBounds();
                return triangleMesh;
            }
        }

        #endregion
    }
}
