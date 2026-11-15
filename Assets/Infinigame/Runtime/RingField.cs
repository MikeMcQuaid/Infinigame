using System.Collections.Generic;
using System.Threading.Tasks;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // The rocks a planet's rings are made of, and the asteroids of a star's belt. Rings in the core
    // says which rocks each cell holds; this builds one mesh per cell for the cells whose rocks are
    // big enough to see, and carries each round with its band. A cell once shown stays until its
    // rocks are too small to make out.
    //
    // A belt is a hundred kilometres from the star it circles, too far for single-precision
    // positions to hold still. So each cell's mesh is built about its own middle, and that middle
    // is placed against the player in double precision.
    public class RingField : MonoBehaviour
    {
        const float Keep = 1.2f;

        class Chunk
        {
            public GameObject item;
            public Task<(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)> building;
            // its middle in its band's frame, before the band has turned at all
            public double3 middle;
            public int seen;
        }

        // How many cells of rock are drawn, for the frame log.
        public int Drawn => chunks.Count;

        BodyDescriptor body;
        CelestialBody owner;
        SolarSystem system;
        Material material;
        // a boulder's shape, a plainer one for pebbles and a craggier one for rocks the size of hills
        (float3[] vertices, float3[] normals, Vector2[] uvs, int[] triangles)[] shapes;
        readonly Dictionary<(int size, int band, int sector), Chunk> chunks = new Dictionary<(int, int, int), Chunk>();
        readonly List<(int, int, int)> stale = new List<(int, int, int)>();
        int tier = -1, building;

        public void Init(BodyDescriptor descriptor)
        {
            body = descriptor;
            owner = GetComponent<CelestialBody>();
            system = GetComponentInParent<SolarSystem>();
            material = Shapes.Solid(Surface.Rock, descriptor.kind == BodyKind.Sun ? new Color(0.5f, 0.46f, 0.42f) : Color.Lerp(Shapes.Colour(descriptor.ground), Color.white, 0.5f));
            shapes = new[] { Shape(Shapes.Rock(3)), Shape(Shapes.Rock(2)), Shape(Shapes.Rock(7)) };
        }

        static (float3[], float3[], Vector2[], int[]) Shape(Mesh mesh)
        {
            var result = (System.Array.ConvertAll(mesh.vertices, v => (float3)v), System.Array.ConvertAll(mesh.normals, n => (float3)n), mesh.uv, mesh.triangles);
            Destroy(mesh);
            return result;
        }

        static double3 TurnY(double3 point, double angle)
        {
            math.sincos(angle, out double sine, out double cosine);
            return new double3(point.x * cosine + point.z * sine, point.y, point.z * cosine - point.x * sine);
        }

        void Update()
        {
            var lod = LodSettings.Current;
            if (tier != LodSettings.Tier)
            {
                tier = LodSettings.Tier;
                foreach (var chunk in chunks.Values) Release(chunk);
                chunks.Clear();
                building = 0;
            }
            // Where the camera is in the body's frame. With the scene riding on the player, far from
            // this body, the body's own place in the scene is the player's position turned about: exact.
            Vector3 eyeScene = Camera.main.transform.position;
            double3 camera = (owner != null && owner.IsReference) ? (double3)(float3)eyeScene + (double3)system.Anchor : (double3)(float3)transform.InverseTransformPoint(eyeScene);
            double radial = math.length(camera.xz);
            int frame = Time.frameCount, applied = 0;
            for (int size = 0; size < Rings.Classes; size++)
            {
                // a rock is worth drawing for as long as its width covers a few pixels
                float rock = Rings.Size(body, size), range = lod.Range(2f * rock), cell = Rings.Cell(body, size), reach = range * Keep + cell;
                if (math.abs(camera.y) > reach + body.ringThick || radial < body.ringInner - reach || radial > body.ringOuter + reach) continue;
                int bands = Rings.Bands(body, size);
                for (int band = math.max(0, (int)math.floor((radial - reach - body.ringInner) / cell)); band <= math.min(bands - 1, (int)math.floor((radial + reach - body.ringInner) / cell)); band++)
                {
                    double turned = Rings.Turned(body, size, band, system.Clock), middle = body.ringInner + (band + 0.5) * cell;
                    // the camera, in the band's own unturned frame
                    double3 eye = TurnY(camera, -turned);
                    int sectors = Rings.Sectors(body, size, band);
                    double arc = 2 * math.PI_DBL / sectors, angle = math.atan2(eye.z, eye.x), spread = math.min(math.PI_DBL, reach / middle);
                    var rotation = Quaternion.AngleAxis((float)math.degrees(turned), Vector3.up);
                    for (int step = (int)math.floor((angle - spread) / arc); step <= (int)math.floor((angle + spread) / arc); step++)
                    {
                        int sector = ((step % sectors) + sectors) % sectors;
                        double at = (sector + 0.5) * arc;
                        var centre = new double3(math.cos(at) * middle, 0, math.sin(at) * middle);
                        double distance = math.distance(eye, centre);
                        if (distance > range * Keep) continue;
                        var key = (size, band, sector);
                        if (!chunks.TryGetValue(key, out var chunk))
                        {
                            if (distance > range || building >= 32) continue;
                            var shape = shapes[rock > 20f ? 2 : rock > 1.5f ? 0 : 1];
                            chunks[key] = chunk = new Chunk { middle = centre, building = Task.Run(() => Build(key.size, key.band, key.sector, centre, shape)) };
                            building++;
                        }
                        chunk.seen = frame;
                        if (chunk.building != null && chunk.building.IsCompleted && applied < 24)
                        {
                            var built = chunk.building.Result;
                            chunk.building = null;
                            building--;
                            if (built.vertices.Length > 0)
                            {
                                applied++;
                                var mesh = new Mesh { vertices = built.vertices, normals = built.normals, uv = built.uvs, triangles = built.triangles };
                                // not hung from the body, whose own place in the scene is too far off to add to cleanly
                                chunk.item = Shapes.Part(system.transform, "Rocks", mesh, material, Vector3.zero, Vector3.one);
                            }
                        }
                        if (chunk.item == null) continue;
                        // carried round with its band, and set down relative to the player
                        double3 now = TurnY(chunk.middle, turned);
                        if ((owner != null && owner.IsReference)) chunk.item.transform.SetPositionAndRotation((Vector3)(float3)(now - (double3)system.Anchor), rotation);
                        else chunk.item.transform.SetPositionAndRotation(transform.TransformPoint((Vector3)(float3)now), transform.rotation * rotation);
                    }
                }
            }
            stale.Clear();
            foreach (var pair in chunks)
                if (pair.Value.seen != frame)
                    stale.Add(pair.Key);
            foreach (var key in stale)
            {
                Release(chunks[key]);
                chunks.Remove(key);
            }
        }

        void Release(Chunk chunk)
        {
            if (chunk.building != null) building--;
            if (chunk.item == null) return;
            Destroy(chunk.item.GetComponent<MeshFilter>().sharedMesh);
            Destroy(chunk.item);
        }

        void OnDestroy()
        {
            foreach (var chunk in chunks.Values) Release(chunk);
        }

        // Runs off the main thread: every rock of one cell as a single mesh about the cell's middle,
        // each rock turned its own way.
        (Vector3[], Vector3[], Vector2[], int[]) Build(int size, int band, int sector, double3 middle, (float3[] vertices, float3[] normals, Vector2[] uvs, int[] triangles) shape)
        {
            var rocks = new List<Rock>();
            Rings.Rocks(body, size, band, sector, rocks);
            int corners = shape.vertices.Length;
            var vertices = new Vector3[rocks.Count * corners];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[rocks.Count * shape.triangles.Length];
            var rnd = new Random(math.hash(new int3(size, band, sector)) | 1u);
            for (int r = 0; r < rocks.Count; r++)
            {
                quaternion turn = rnd.NextQuaternionRotation();
                // the shape is a metre across, and no rock is quite round
                float3 scale = rocks[r].radius * 2f * rnd.NextFloat3(0.7f, 1f), centre = (float3)(rocks[r].position - middle);
                for (int v = 0; v < corners; v++)
                {
                    vertices[r * corners + v] = centre + math.rotate(turn, shape.vertices[v] * scale);
                    normals[r * corners + v] = math.rotate(turn, shape.normals[v]);
                    uvs[r * corners + v] = shape.uvs[v];
                }
                for (int t = 0; t < shape.triangles.Length; t++) triangles[r * shape.triangles.Length + t] = shape.triangles[t] + r * corners;
            }
            return (vertices, normals, uvs, triangles);
        }
    }
}
