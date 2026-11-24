using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Small runtime-built explosion: fireball, smoke, sparks, light flash and a synthesised boom.
    /// Self-destructs after a few seconds. Use <see cref="Spawn"/>.
    /// </summary>
    public class BoxExplosionFx : MonoBehaviour
    {
        private static Material _fireMat, _smokeMat;
        private static AudioClip _boom;
        private static AudioClip _customClip;

        /// <summary>Resources path of the explosion sound; falls back to a synthesised boom if missing.</summary>
        public const string SoundResource = "Sounds/GoldShipAhh";

        private Light _light;
        private float _age;
        private const float FlashTime = 0.4f;
        private const float LifeTime = 3.5f;

        public static void Spawn(Vector3 position, float scale = 1f)
        {
            EnsureAssets();
            var go = new GameObject("BoxExplosionFx");
            go.transform.position = position;
            var fx = go.AddComponent<BoxExplosionFx>();

            fx.CreateFire(scale);
            fx.CreateSmoke(scale);
            fx.CreateSparks(scale);

            fx._light = go.AddComponent<Light>();
            fx._light.type = LightType.Point;
            fx._light.color = new Color(1f, 0.6f, 0.25f);
            fx._light.range = 14f * scale;
            fx._light.intensity = 10f;

            var audio = go.AddComponent<AudioSource>();
            audio.clip = _customClip != null ? _customClip : _boom;
            audio.spatialBlend = _customClip != null ? 0.3f : 0.75f; // keep the voice clearly audible
            audio.minDistance = 8f;
            audio.maxDistance = 120f;
            audio.volume = 0.85f;
            audio.pitch = _customClip != null ? 1f : Random.Range(0.9f, 1.1f);
            audio.Play();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_light != null) _light.intensity = Mathf.Lerp(10f, 0f, _age / FlashTime);
            if (_age >= LifeTime) Destroy(gameObject);
        }

        // ---------- particle layers ----------

        private void CreateFire(float s)
        {
            ParticleSystem ps = NewSystem("Fire", _fireMat, out var main);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f * s, 11f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f * s, 2.0f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 0.75f, 0.3f, 1f);
            main.gravityModifier = -0.15f;
            Burst(ps, 45);
            Sphere(ps, 0.6f * s);

            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = Gradient(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.35f), new GradientColorKey(new Color(0.5f, 0.1f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 3f; drag.limit = 100f;
            ps.Play();
        }

        private void CreateSmoke(float s)
        {
            ParticleSystem ps = NewSystem("Smoke", _smokeMat, out var main);
            main.startDelay = 0.05f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * s, 4.5f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(1.4f * s, 2.6f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.25f, 0.23f, 0.22f, 0.7f);
            main.gravityModifier = -0.08f;
            Burst(ps, 22);
            Sphere(ps, 0.8f * s);

            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = Gradient(
                new[] { new GradientColorKey(new Color(0.35f, 0.3f, 0.28f), 0f), new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.12f), new GradientAlphaKey(0f, 1f) });
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 2.2f));
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 2f; drag.limit = 100f;
            ps.Play();
        }

        private void CreateSparks(float s)
        {
            ParticleSystem ps = NewSystem("Sparks", _fireMat, out var main);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f * s, 22f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * s, 0.16f * s);
            main.startColor = new Color(1f, 0.85f, 0.45f, 1f);
            main.gravityModifier = 1.2f;
            Burst(ps, 35);
            Sphere(ps, 0.3f * s);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1f;
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = Gradient(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            ps.Play();
        }

        private ParticleSystem NewSystem(string name, Material mat, out ParticleSystem.MainModule main)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        private static void Burst(ParticleSystem ps, int count)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        private static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        private static Gradient Gradient(GradientColorKey[] c, GradientAlphaKey[] a)
        {
            var g = new Gradient();
            g.SetKeys(c, a);
            return g;
        }

        // ---------- shared assets ----------

        /// <summary>Soft transparent puff material, shared with the cars' tyre smoke.</summary>
        public static Material SmokeMaterial { get { EnsureAssets(); return _smokeMat; } }

        private static void EnsureAssets()
        {
            if (_fireMat != null && _smokeMat != null && _boom != null) return;
            Texture2D puff = MakePuffTexture(64);
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _fireMat = MakeParticleMat(sh, puff, additive: true);
            _smokeMat = MakeParticleMat(sh, puff, additive: false);
            _boom = MakeBoomClip();
            _customClip = Resources.Load<AudioClip>(SoundResource);
        }

        private static Material MakeParticleMat(Shader sh, Texture2D tex, bool additive)
        {
            var m = new Material(sh) { name = additive ? "BoxFx_Fire" : "BoxFx_Smoke" };
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        private static Texture2D MakePuffTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var c = new Vector2(n * 0.5f, n * 0.5f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (n * 0.5f);
                float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.35f + 0.65f;
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * noise));
            }
            t.Apply();
            return t;
        }

        private static AudioClip MakeBoomClip()
        {
            const int rate = 44100;
            int len = (int)(rate * 0.9f);
            var data = new float[len];
            var rng = new System.Random(1234);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 6f);
                float cutoff = Mathf.Lerp(0.35f, 0.03f, Mathf.Clamp01(t * 2.5f)); // darkens as it decays
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * cutoff;
                float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 40f, Mathf.Clamp01(t * 3f)) * t) * Mathf.Exp(-t * 9f);
                data[i] = Mathf.Clamp((lp * 2.2f + thump * 0.8f) * env, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("BoxBoom", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
