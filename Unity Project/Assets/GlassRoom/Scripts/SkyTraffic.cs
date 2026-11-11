using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GlassRoom
{
    /// <summary>
    /// Endless streams of floating trains crossing the night sky, all heading the same way.
    /// Each lane is a continuous chain: when a train leaves the far end it is moved back behind
    /// the last train of its lane (re-coloured and re-sized), so the chain never has gaps or
    /// bunches and nothing is created or destroyed while running.
    /// </summary>
    public class SkyTraffic : MonoBehaviour
    {
        [Serializable]
        public class Lane
        {
            public float height = 50f;
            public float z = 40f;
            [Tooltip("Metres per second along +X.")]
            public float speed = 15f;
            public int minCars = 3;
            public int maxCars = 7;
            [Tooltip("Empty sky between one train and the next, in metres.")]
            public float minGap = 30f;
            public float maxGap = 90f;
            [Tooltip("Car scale; distant lanes can be smaller.")]
            public float scale = 1f;
        }

        public Material bodyMaterial;
        public Material[] stripMaterials;
        public Material headlightMaterial;
        public Material taillightMaterial;
        public Lane[] lanes;
        public float startX = -260f;
        public float endX = 260f;

        const float CarLength = 14f;
        const float CarGap = 1.2f;
        const float CarDiameter = 3.2f;
        const int MaxCarsPerTrain = 8;
        // Each car slowly rolls around its own length as it glides, every car a little behind
        // the one in front, so the twist travels back along the train.
        const float SpinDegreesPerSecond = 25f;
        const float SpinLagPerCar = 18f;

        class Train
        {
            public Transform root;
            public Transform[] cars;
            public Renderer[] strips;
            public Transform tail;
            public float length;
            public float spinPhase;
            public Renderer[][] carRings;
            public Color glow;
        }

        static readonly int k_EmissionId = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock m_Block;
        Mesh m_Seam, m_Cylinder;
        Material m_Pipe, m_SeamMaterial;

        readonly List<List<Train>> m_Lanes = new List<List<Train>>();
        Mesh m_Capsule, m_Cube, m_Sphere;

        void Start()
        {
            m_Capsule = PrimitiveMesh(PrimitiveType.Capsule);
            m_Cube = PrimitiveMesh(PrimitiveType.Cube);
            m_Sphere = PrimitiveMesh(PrimitiveType.Sphere);
            m_Seam = TorusMesh(CarDiameter * 0.5f + 0.004f, 0.03f, 64, 6);
            m_Cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            m_Block = new MaterialPropertyBlock();
            // Polished steel for the pipes and near-black for the panel seams, made from the
            // body's own material so they share its look.
            m_Pipe = new Material(bodyMaterial) { name = "Train Pipe" };
            m_Pipe.SetColor("_BaseColor", new Color(0.62f, 0.64f, 0.68f));
            m_Pipe.SetFloat("_Metallic", 1f);
            m_Pipe.SetFloat("_Smoothness", 0.88f);
            m_Pipe.SetColor("_EmissionColor", Color.black);
            m_SeamMaterial = new Material(bodyMaterial) { name = "Train Seam" };
            m_SeamMaterial.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f));
            m_SeamMaterial.SetFloat("_Smoothness", 0.4f);
            m_SeamMaterial.SetColor("_EmissionColor", Color.black);

            foreach (var lane in lanes)
            {
                var trains = new List<Train>();
                m_Lanes.Add(trains);
                // Fill the whole lane up front so the sky is already busy.
                float x = endX;
                while (x > startX)
                {
                    var train = BuildTrain(lane);
                    Configure(train, lane);
                    Place(train, lane, x);
                    trains.Add(train);
                    x -= train.length * lane.scale + Random.Range(lane.minGap, lane.maxGap);
                }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // After scripts reload during Play, the trains list is empty until restarted.
            if (m_Lanes.Count != lanes.Length) return;
            for (int l = 0; l < lanes.Length; l++)
            {
                var lane = lanes[l];
                var trains = m_Lanes[l];
                foreach (var t in trains)
                {
                    t.root.position += Vector3.right * (lane.speed * dt);
                    float spin = t.spinPhase + Time.time * SpinDegreesPerSecond;
                    for (int i = 0; i < t.cars.Length; i++)
                    {
                        if (!t.cars[i].gameObject.activeSelf) continue;
                        float angle = spin - i * SpinLagPerCar;
                        t.cars[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
                        // The rings glow brighter and softer as the car turns, so the pulse
                        // travels back along the train with the twist.
                        float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(angle * Mathf.Deg2Rad * 2f));
                        m_Block.SetColor(k_EmissionId, t.glow * pulse);
                        foreach (var ring in t.carRings[i]) ring.SetPropertyBlock(m_Block);
                    }
                }

                foreach (var t in trains)
                {
                    float tailX = t.root.position.x - t.length * lane.scale;
                    if (tailX <= endX)
                        continue;
                    // Send it to the back of the chain as a "new" train.
                    float rearmost = float.MaxValue;
                    foreach (var other in trains)
                        if (other != t)
                            rearmost = Mathf.Min(rearmost, other.root.position.x - other.length * lane.scale);
                    Configure(t, lane);
                    Place(t, lane, Mathf.Min(rearmost, startX) - Random.Range(lane.minGap, lane.maxGap));
                }
            }
        }

        // A ring (torus) around the X axis: the car's length.
        static Mesh TorusMesh(float radius, float thickness, int segments, int sides)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var centre = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    Vector3 n = centre * Mathf.Cos(b) + Vector3.right * Mathf.Sin(b);
                    vertices.Add(centre * radius + n * thickness);
                    normals.Add(n);
                }
            }
            int row = sides + 1;
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                int v = i * row + j;
                // Both windings, so it shows whichever way it faces.
                triangles.AddRange(new[] { v, v + 1, v + row, v + 1, v + row + 1, v + row });
                triangles.AddRange(new[] { v, v + row, v + 1, v + 1, v + row, v + row + 1 });
            }
            var mesh = new Mesh { name = "Train Ring" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return mesh;
        }

        void Place(Train t, Lane lane, float frontX) => t.root.position = new Vector3(frontX, lane.height, lane.z);

        // Picks a new length and light colour for a train.
        void Configure(Train t, Lane lane)
        {
            int count = Random.Range(lane.minCars, lane.maxCars + 1);
            var strip = stripMaterials[Random.Range(0, stripMaterials.Length)];
            for (int i = 0; i < t.cars.Length; i++)
                t.cars[i].gameObject.SetActive(i < count);
            foreach (var r in t.strips)
                r.sharedMaterial = strip;
            t.glow = strip.GetColor(k_EmissionId);
            t.length = count * (CarLength + CarGap);
            t.spinPhase = Random.Range(0f, 360f);
            t.tail.localPosition = new Vector3(-t.length + CarGap + 0.1f, 0f, 0f);
        }

        Train BuildTrain(Lane lane)
        {
            var root = new GameObject("Train") { layer = gameObject.layer }.transform;
            root.SetParent(transform, false);
            root.localScale = Vector3.one * lane.scale;
            var t = new Train { root = root, cars = new Transform[MaxCarsPerTrain], carRings = new Renderer[MaxCarsPerTrain][] };
            var strips = new List<Renderer>();

            for (int i = 0; i < MaxCarsPerTrain; i++)
            {
                var car = new GameObject("Car") { layer = gameObject.layer }.transform;
                car.SetParent(root, false);
                car.localPosition = new Vector3(-(i + 0.5f) * (CarLength + CarGap), 0f, 0f);
                t.cars[i] = car;

                // Brushed graphite hull lying along X, rounded at both ends.
                Part("Body", car, m_Capsule, bodyMaterial, Vector3.zero, new Vector3(CarDiameter, CarLength / 2f, CarDiameter), new Vector3(0f, 0f, 90f));
                float straight = CarLength - CarDiameter;
                // Panel seams: thin dark lines around the hull, dividing it into plates.
                foreach (float along in new[] { -0.42f, -0.14f, 0.14f, 0.42f })
                    Part("Seam", car, m_Seam, m_SeamMaterial, new Vector3(along * straight, 0f, 0f), Vector3.one);
                // Six steel pipes along the hull, evenly around it, and three light strips set
                // between them: as the car rolls you see them turn.
                var lights = new List<Renderer>();
                for (int k = 0; k < 6; k++)
                {
                    float a = k * 60f * Mathf.Deg2Rad;
                    float pipeRadius = CarDiameter * 0.5f + 0.09f;
                    var at = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    Part("Pipe", car, m_Cylinder, m_Pipe, at * pipeRadius, new Vector3(0.2f, straight * 0.47f, 0.2f), new Vector3(0f, 0f, 90f));
                    if (k % 2 == 0)
                    {
                        float b = a + 30f * Mathf.Deg2Rad;
                        var stripAt = new Vector3(0f, Mathf.Cos(b), Mathf.Sin(b)) * (CarDiameter * 0.5f + 0.01f);
                        var strip = Part("Light Strip", car, m_Cube, stripMaterials[0], stripAt,
                            new Vector3(straight * 0.8f, 0.05f, 0.24f), new Vector3(b * Mathf.Rad2Deg, 0f, 0f)).GetComponent<Renderer>();
                        lights.Add(strip);
                        strips.Add(strip);
                    }
                }
                t.carRings[i] = lights.ToArray();
            }
            t.strips = strips.ToArray();

            Part("Headlight", root, m_Sphere, headlightMaterial, new Vector3(-0.1f, 0f, 0f), Vector3.one * 0.9f);
            t.tail = Part("Taillight", root, m_Sphere, taillightMaterial, Vector3.zero, Vector3.one * 0.7f).transform;
            return t;
        }

        static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 position, Vector3 scale, Vector3 euler = default)
        {
            // Same layer as the traffic, so the moonlight that only lights the trains reaches it.
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return go;
        }
    }
}
