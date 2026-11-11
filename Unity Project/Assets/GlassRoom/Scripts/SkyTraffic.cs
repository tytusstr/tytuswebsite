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
        }

        readonly List<List<Train>> m_Lanes = new List<List<Train>>();
        Mesh m_Capsule, m_Cube, m_Sphere;

        void Start()
        {
            m_Capsule = PrimitiveMesh(PrimitiveType.Capsule);
            m_Cube = PrimitiveMesh(PrimitiveType.Cube);
            m_Sphere = PrimitiveMesh(PrimitiveType.Sphere);

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
                        if (t.cars[i].gameObject.activeSelf)
                            t.cars[i].localRotation = Quaternion.Euler(spin - i * SpinLagPerCar, 0f, 0f);
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
            t.length = count * (CarLength + CarGap);
            t.spinPhase = Random.Range(0f, 360f);
            t.tail.localPosition = new Vector3(-t.length + CarGap + 0.1f, 0f, 0f);
        }

        Train BuildTrain(Lane lane)
        {
            var root = new GameObject("Train") { layer = gameObject.layer }.transform;
            root.SetParent(transform, false);
            root.localScale = Vector3.one * lane.scale;
            var t = new Train { root = root, cars = new Transform[MaxCarsPerTrain] };
            var strips = new List<Renderer>();

            for (int i = 0; i < MaxCarsPerTrain; i++)
            {
                var car = new GameObject("Car") { layer = gameObject.layer }.transform;
                car.SetParent(root, false);
                car.localPosition = new Vector3(-(i + 0.5f) * (CarLength + CarGap), 0f, 0f);
                t.cars[i] = car;

                // Capsule body lying along X.
                Part("Body", car, m_Capsule, bodyMaterial, Vector3.zero, new Vector3(CarDiameter, CarLength / 2f, CarDiameter), new Vector3(0f, 0f, 90f));
                // Window strips along both sides, and an underglow that makes it look like it floats.
                float side = CarDiameter * 0.49f;
                strips.Add(Part("Strip L", car, m_Cube, stripMaterials[0], new Vector3(0f, 0.25f, side), new Vector3(CarLength * 0.72f, 0.38f, 0.06f)).GetComponent<Renderer>());
                strips.Add(Part("Strip R", car, m_Cube, stripMaterials[0], new Vector3(0f, 0.25f, -side), new Vector3(CarLength * 0.72f, 0.38f, 0.06f)).GetComponent<Renderer>());
                strips.Add(Part("Strip Under", car, m_Cube, stripMaterials[0], new Vector3(0f, -CarDiameter * 0.49f, 0f), new Vector3(CarLength * 0.8f, 0.06f, CarDiameter * 0.45f)).GetComponent<Renderer>());
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
