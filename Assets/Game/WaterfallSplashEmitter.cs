using System;
using UnityEngine;

/// <summary>
/// Constant waterfall/stream splashes. Put this component on an empty object near
/// the point where the waterfall hits the water, then tune the spawn zones in the
/// inspector. The ParticleSystem is created in code at runtime.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Game/Waterfall Splash Emitter")]
public class WaterfallSplashEmitter : MonoBehaviour
{
    [Serializable]
    public class SplashZone
    {
        [Tooltip("Inspector-only name.")]
        public string name = "Waterfall";

        [Tooltip("Enable this splash zone.")]
        public bool enabled = true;

        [Tooltip("Center of the spawn zone in this object's local space.")]
        public Vector2 localOffset = Vector2.zero;

        [Tooltip("Spawn zone width on local X. Use this to match the waterfall width.")]
        [Min(0.01f)] public float width = 1.2f;

        [Tooltip("Spawn zone height on local Y. Usually a thin band near the water surface.")]
        [Min(0.01f)] public float height = 0.18f;

        [Tooltip("Particles emitted per second from this zone.")]
        [Min(0f)] public float particlesPerSecond = 35f;

        [Tooltip("Per-zone multiplier for vertical splash speed.")]
        [Min(0f)] public float upwardSpeedMultiplier = 1f;

        [Tooltip("Per-zone multiplier for horizontal spread.")]
        [Min(0f)] public float horizontalSpeedMultiplier = 1f;

        public void Validate()
        {
            width = Mathf.Max(0.01f, width);
            height = Mathf.Max(0.01f, height);
            particlesPerSecond = Mathf.Max(0f, particlesPerSecond);
            upwardSpeedMultiplier = Mathf.Max(0f, upwardSpeedMultiplier);
            horizontalSpeedMultiplier = Mathf.Max(0f, horizontalSpeedMultiplier);
        }
    }

    [Header("Particles")]
    [Tooltip("Optional custom ParticleSystem. Leave empty to auto-create one in code.")]
    [SerializeField] private ParticleSystem particlesOverride;

    [Tooltip("Enable constant emission.")]
    [SerializeField] private bool emit = true;

    [Tooltip("Global density multiplier for all zones.")]
    [SerializeField, Min(0f)] private float emissionMultiplier = 1f;

    [Tooltip("Caps one-frame emission after frame spikes.")]
    [SerializeField, Range(1, 500)] private int maxParticlesPerFrame = 80;

    [Tooltip("Maximum live particles.")]
    [SerializeField, Range(16, 10000)] private int maxParticles = 1200;

    [Header("Splash Zones")]
    [Tooltip("Use two zones here for two waterfalls, or put one component at each waterfall.")]
    [SerializeField] private SplashZone[] zones =
    {
        new SplashZone()
    };

    [Header("Motion")]
    [Tooltip("Minimum upward splash speed.")]
    [SerializeField, Min(0f)] private float minUpwardSpeed = 1.2f;

    [Tooltip("Maximum upward splash speed.")]
    [SerializeField, Min(0f)] private float maxUpwardSpeed = 3.6f;

    [Tooltip("Minimum horizontal spread speed.")]
    [SerializeField, Min(0f)] private float minHorizontalSpeed = 0f;

    [Tooltip("Maximum horizontal spread speed.")]
    [SerializeField, Min(0f)] private float maxHorizontalSpeed = 1.1f;

    [Tooltip("Constant velocity added to every particle, useful for wind/current bias.")]
    [SerializeField] private Vector2 velocityOffset = Vector2.zero;

    [Tooltip("Particle gravity: 0 means no falling, 1 means normal Physics gravity.")]
    [SerializeField, Min(0f)] private float gravityModifier = 0.8f;

    [Header("Lifetime & Size")]
    [SerializeField, Min(0.01f)] private float minLifetime = 0.28f;
    [SerializeField, Min(0.01f)] private float maxLifetime = 0.75f;
    [SerializeField, Min(0.001f)] private float minStartSize = 0.04f;
    [SerializeField, Min(0.001f)] private float maxStartSize = 0.14f;

    [Tooltip("Particle size over lifetime.")]
    [SerializeField] private AnimationCurve sizeOverLifetime =
        new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.15f));

    [Header("Look")]
    [SerializeField] private Color splashColor = new Color(0.72f, 0.92f, 1f, 0.85f);

    [Tooltip("How much alpha fades by the end of lifetime.")]
    [SerializeField, Range(0f, 1f)] private float fadeOut = 1f;

    [Tooltip("Sorting Layer for the auto-created system. Empty means Default.")]
    [SerializeField] private string sortingLayerName = "";

    [SerializeField] private int sortingOrder = 3;

    [Header("Gizmos")]
    [Tooltip("Draw zones even when this object is not selected.")]
    [SerializeField] private bool drawGizmosAlways = true;

    [SerializeField] private Color gizmoColor = new Color(0.25f, 0.8f, 1f, 0.35f);

    private ParticleSystem activeParticles;
    private float[] emissionAccumulators;
    private bool autoCreatedParticles;
    private bool initialized;

    private void Awake()
    {
        EnsureParticleSystem();
        ConfigureParticleSystem();
        initialized = true;
    }

    private void OnEnable()
    {
        EnsureParticleSystem();
        ResetAccumulators();

        if (activeParticles != null)
            activeParticles.Play();
    }

    private void OnDisable()
    {
        if (activeParticles != null)
            activeParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDestroy()
    {
        if (!autoCreatedParticles || activeParticles == null)
            return;

        if (Application.isPlaying)
            Destroy(activeParticles.gameObject);
        else
            DestroyImmediate(activeParticles.gameObject);
    }

    private void Update()
    {
        if (!emit || activeParticles == null || zones == null || zones.Length == 0)
            return;

        EnsureAccumulators();

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
            return;

        int frameBudget = maxParticlesPerFrame;

        for (int i = 0; i < zones.Length && frameBudget > 0; i++)
        {
            SplashZone zone = zones[i];
            if (zone == null || !zone.enabled || zone.particlesPerSecond <= 0f)
                continue;

            float rate = zone.particlesPerSecond * emissionMultiplier;
            if (rate <= 0f)
                continue;

            emissionAccumulators[i] = Mathf.Min(
                emissionAccumulators[i] + rate * deltaTime,
                maxParticlesPerFrame);

            int count = Mathf.FloorToInt(emissionAccumulators[i]);
            if (count <= 0)
                continue;

            count = Mathf.Min(count, frameBudget);
            emissionAccumulators[i] -= count;
            frameBudget -= count;

            EmitZone(zone, count);
        }
    }

    private void EnsureParticleSystem()
    {
        if (activeParticles != null)
            return;

        if (particlesOverride != null)
        {
            activeParticles = particlesOverride;
            autoCreatedParticles = false;
            return;
        }

        var go = new GameObject("WaterfallSplashParticles (auto)")
        {
            hideFlags = HideFlags.DontSave
        };
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        activeParticles = go.AddComponent<ParticleSystem>();
        autoCreatedParticles = true;
    }

    private void ConfigureParticleSystem()
    {
        if (activeParticles == null)
            return;

        var main = activeParticles.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        main.startSize = new ParticleSystem.MinMaxCurve(minStartSize, maxStartSize);
        main.startColor = new ParticleSystem.MinMaxGradient(splashColor);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(gravityModifier);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;

        var emission = activeParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = activeParticles.shape;
        shape.enabled = false;

        var velocityOverLifetime = activeParticles.velocityOverLifetime;
        velocityOverLifetime.enabled = false;

        var size = activeParticles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeOverLifetime);

        ApplyParticleColor();

        ParticleSystemRenderer renderer = activeParticles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null && autoCreatedParticles)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader != null)
                renderer.material = new Material(shader) { color = splashColor };

            if (!string.IsNullOrEmpty(sortingLayerName))
                renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
        }
    }

    private void ApplyParticleColor()
    {
        if (activeParticles == null)
            return;

        var main = activeParticles.main;
        main.startColor = new ParticleSystem.MinMaxGradient(splashColor);

        var color = activeParticles.colorOverLifetime;
        color.enabled = true;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(splashColor, 0f),
                new GradientColorKey(splashColor, 1f),
            },
            new[]
            {
                new GradientAlphaKey(splashColor.a, 0f),
                new GradientAlphaKey(Mathf.Lerp(splashColor.a, 0f, fadeOut), 1f),
            });
        color.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private void EmitZone(SplashZone zone, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 position = GetRandomSpawnPosition(zone);
            Vector2 velocity = GetRandomVelocity(zone);

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = new Vector3(velocity.x, velocity.y, 0f),
                startColor = splashColor,
                startLifetime = UnityEngine.Random.Range(minLifetime, maxLifetime),
                startSize = UnityEngine.Random.Range(minStartSize, maxStartSize),
                applyShapeToPosition = false,
            };

            activeParticles.Emit(emitParams, 1);
        }
    }

    private Vector3 GetRandomSpawnPosition(SplashZone zone)
    {
        float x = UnityEngine.Random.Range(-zone.width * 0.5f, zone.width * 0.5f);
        float y = UnityEngine.Random.Range(-zone.height * 0.5f, zone.height * 0.5f);
        Vector3 local = new Vector3(zone.localOffset.x + x, zone.localOffset.y + y, 0f);
        return transform.TransformPoint(local);
    }

    private Vector2 GetRandomVelocity(SplashZone zone)
    {
        float horizontalSpeed = UnityEngine.Random.Range(minHorizontalSpeed, maxHorizontalSpeed);
        if (UnityEngine.Random.value < 0.5f)
            horizontalSpeed = -horizontalSpeed;

        float upwardSpeed = UnityEngine.Random.Range(minUpwardSpeed, maxUpwardSpeed);

        return new Vector2(
            horizontalSpeed * zone.horizontalSpeedMultiplier + velocityOffset.x,
            upwardSpeed * zone.upwardSpeedMultiplier + velocityOffset.y);
    }

    private void EnsureAccumulators()
    {
        if (zones == null)
            return;

        if (emissionAccumulators != null && emissionAccumulators.Length == zones.Length)
            return;

        emissionAccumulators = new float[zones.Length];
    }

    private void ResetAccumulators()
    {
        EnsureAccumulators();

        if (emissionAccumulators == null)
            return;

        for (int i = 0; i < emissionAccumulators.Length; i++)
            emissionAccumulators[i] = 0f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (minUpwardSpeed > maxUpwardSpeed) maxUpwardSpeed = minUpwardSpeed;
        if (minHorizontalSpeed > maxHorizontalSpeed) maxHorizontalSpeed = minHorizontalSpeed;
        if (minLifetime > maxLifetime) maxLifetime = minLifetime;
        if (minStartSize > maxStartSize) maxStartSize = minStartSize;

        if (zones == null || zones.Length == 0)
            zones = new[] { new SplashZone() };

        for (int i = 0; i < zones.Length; i++)
            zones[i]?.Validate();

        if (Application.isPlaying && initialized)
            ConfigureParticleSystem();
    }
#endif

    private void OnDrawGizmos()
    {
        if (drawGizmosAlways)
            DrawZonesGizmos(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawZonesGizmos(true);
    }

    private void DrawZonesGizmos(bool selected)
    {
        if (zones == null)
            return;

        Color fill = gizmoColor;
        if (!selected)
            fill.a *= 0.55f;

        Color wire = new Color(fill.r, fill.g, fill.b, Mathf.Clamp01(fill.a * 2.5f));

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;

        for (int i = 0; i < zones.Length; i++)
        {
            SplashZone zone = zones[i];
            if (zone == null)
                continue;

            Vector3 center = new Vector3(zone.localOffset.x, zone.localOffset.y, 0f);
            Vector3 size = new Vector3(Mathf.Max(0.01f, zone.width), Mathf.Max(0.01f, zone.height), 0.01f);

            Gizmos.color = fill;
            Gizmos.DrawCube(center, size);

            Gizmos.color = wire;
            Gizmos.DrawWireCube(center, size);
        }

        Gizmos.color = previousColor;
        Gizmos.matrix = previousMatrix;
    }
}
