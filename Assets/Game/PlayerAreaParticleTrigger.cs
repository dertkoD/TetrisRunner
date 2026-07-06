using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Game/Player Area Particle Trigger")]
public class PlayerAreaParticleTrigger : MonoBehaviour
{
    [Header("Trigger")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private LayerMask playerLayers = ~0;
    [SerializeField] private bool requirePlayerFacade = false;
    [SerializeField] private bool playOnlyOnce = true;

    [Header("Spawn")]
    [SerializeField] private Transform effectPoint;
    [SerializeField] private Vector3 positionOffset;

    [Header("Particle Visual")]
    [SerializeField] private Sprite particleSprite;
    [SerializeField] private Material particleMaterial;
    [SerializeField] private Color startColor = Color.white;
    [SerializeField] private Color endColor = new Color(1f, 1f, 1f, 0f);
    [SerializeField, Min(0)] private int particleCount = 18;

    [Header("Particle Lifetime")]
    [SerializeField, Min(0.01f)] private float minLifetime = 0.35f;
    [SerializeField, Min(0.01f)] private float maxLifetime = 0.75f;

    [Header("Particle Size")]
    [SerializeField, Min(0.001f)] private float minSize = 0.08f;
    [SerializeField, Min(0.001f)] private float maxSize = 0.22f;
    [SerializeField] private bool shrinkOverLifetime = true;

    [Header("Particle Motion")]
    [SerializeField, Min(0f)] private float minSpeed = 0.6f;
    [SerializeField, Min(0f)] private float maxSpeed = 2.2f;
    [SerializeField, Min(0f)] private float spawnRadius = 0.15f;
    [SerializeField] private float gravityModifier = 0f;
    [SerializeField] private float minRotationDegrees = 0f;
    [SerializeField] private float maxRotationDegrees = 360f;
    [SerializeField, Min(0.01f)] private float simulationSpeed = 1f;

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "";
    [SerializeField] private int sortingOrder = 60;

    private ParticleSystem particles;
    private Material generatedMaterial;
    private bool hasPlayed;

    private void Reset()
    {
        Collider2D triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider == null)
            triggerCollider = gameObject.AddComponent<BoxCollider2D>();

        triggerCollider.isTrigger = true;
    }

    private void Awake()
    {
        EnsureTriggerCollider();
        EnsureParticleSystem();
    }

    private void OnDestroy()
    {
        if (generatedMaterial != null)
            Destroy(generatedMaterial);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (playOnlyOnce && hasPlayed)
            return;

        if (!IsPlayer(other))
            return;

        PlayParticles();
        hasPlayed = true;
    }

    private void EnsureTriggerCollider()
    {
        Collider2D triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<BoxCollider2D>();
            Debug.LogWarning($"{nameof(PlayerAreaParticleTrigger)}: added BoxCollider2D to '{name}'.", this);
        }

        if (!triggerCollider.isTrigger)
            triggerCollider.isTrigger = true;
    }

    private void EnsureParticleSystem()
    {
        if (particles != null)
            return;

        GameObject go = new GameObject("Area Particles (auto)");
        go.transform.SetParent(transform, false);

        particles = go.AddComponent<ParticleSystem>();
        ConfigureParticleSystem();
    }

    private void ConfigureParticleSystem()
    {
        if (particles == null)
            return;

        float lifetimeMin = Mathf.Min(minLifetime, maxLifetime);
        float lifetimeMax = Mathf.Max(minLifetime, maxLifetime);
        float sizeMin = Mathf.Min(minSize, maxSize);
        float sizeMax = Mathf.Max(minSize, maxSize);
        float speedMin = Mathf.Min(minSpeed, maxSpeed);
        float speedMax = Mathf.Max(minSpeed, maxSpeed);

        var main = particles.main;
        main.duration = lifetimeMax;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startColor = new ParticleSystem.MinMaxGradient(startColor);
        main.startRotation = new ParticleSystem.MinMaxCurve(
            minRotationDegrees * Mathf.Deg2Rad,
            maxRotationDegrees * Mathf.Deg2Rad);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(gravityModifier);
        main.simulationSpeed = simulationSpeed;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(1, particleCount);

        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        short burstCount = (short)Mathf.Clamp(particleCount, 0, short.MaxValue);
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });

        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = spawnRadius;
        shape.radiusThickness = 1f;

        ConfigureSizeOverLifetime();
        ConfigureColorOverLifetime();
        ConfigureSprite();
        ConfigureRenderer();

        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void ConfigureSizeOverLifetime()
    {
        var sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = shrinkOverLifetime;

        if (!shrinkOverLifetime)
            return;

        AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0f));

        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private void ConfigureColorOverLifetime()
    {
        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(startColor, 0f),
                new GradientColorKey(endColor, 1f),
            },
            new[]
            {
                new GradientAlphaKey(startColor.a, 0f),
                new GradientAlphaKey(endColor.a, 1f),
            });

        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private void ConfigureSprite()
    {
        var textureSheet = particles.textureSheetAnimation;
        textureSheet.enabled = particleSprite != null;

        if (particleSprite == null)
            return;

        textureSheet.mode = ParticleSystemAnimationMode.Sprites;
        while (textureSheet.spriteCount > 0)
            textureSheet.RemoveSprite(0);

        textureSheet.AddSprite(particleSprite);
    }

    private void ConfigureRenderer()
    {
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
            return;

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortingOrder = sortingOrder;

        if (!string.IsNullOrEmpty(sortingLayerName))
            renderer.sortingLayerName = sortingLayerName;

        renderer.material = particleMaterial != null ? particleMaterial : CreateGeneratedMaterial();
    }

    private Material CreateGeneratedMaterial()
    {
        if (generatedMaterial != null)
            return generatedMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader == null)
            return null;

        generatedMaterial = new Material(shader)
        {
            name = "Generated Area Particle Material",
        };

        if (particleSprite != null)
            generatedMaterial.mainTexture = particleSprite.texture;

        return generatedMaterial;
    }

    private void PlayParticles()
    {
        EnsureParticleSystem();
        ConfigureParticleSystem();

        Transform source = effectPoint != null ? effectPoint : transform;
        particles.transform.position = source.position + positionOffset;
        particles.Play(true);
    }

    private bool IsPlayer(Collider2D other)
    {
        if (other == null)
            return false;

        if (playerLayers.value != 0)
        {
            int layerBit = 1 << other.gameObject.layer;
            if ((playerLayers.value & layerBit) == 0)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(playerTag) && !HasTagInParents(other.transform, playerTag))
            return false;

        if (requirePlayerFacade && other.GetComponentInParent<PlayerFacade>() == null)
            return false;

        return true;
    }

    private static bool HasTagInParents(Transform start, string tag)
    {
        Transform current = start;
        while (current != null)
        {
            if (current.CompareTag(tag))
                return true;

            current = current.parent;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxLifetime < minLifetime)
            maxLifetime = minLifetime;
        if (maxSize < minSize)
            maxSize = minSize;
        if (maxSpeed < minSpeed)
            maxSpeed = minSpeed;
        if (simulationSpeed < 0.01f)
            simulationSpeed = 0.01f;
    }
#endif
}
