using System;
using UnityEngine;

/// <summary>
/// Центральный хаб «сочности» (juice) для блоков. Живёт в сцене как singleton
/// и владеет двумя вещами:
///
/// <list type="bullet">
///   <item><b>Партиклы стакинга.</b> Когда блок встаёт в стопку, из него
///         вылетает фонтанчик частиц цвета самого блока (по аналогии с
///         брызгами воды).</item>
///   <item><b>Ударная волна.</b> Когда блок встал на блок ДРУГОГО цвета или
///         пропал за нижним краем сетки, из этого места запускается
///         полноэкранная волна (шейдер ShockWaveSprite на префабе
///         <c>ShockWaveRender</c>), и только после её окончания поднимается
///         вода.</item>
/// </list>
///
/// Контроллер не нужно вручную ставить в сцену: его создаёт
/// <see cref="TetrisBlockSpawnManager"/> / <see cref="DeathWaterController"/>
/// через <see cref="Ensure"/>, передавая <see cref="TetrisBlockConfigSO"/> со
/// всеми ссылками и настройками. На перезапуске сцены singleton пересоздаётся.
/// </summary>
[DisallowMultipleComponent]
public class BlockJuiceController : MonoBehaviour
{
    private static BlockJuiceController instance;

    /// <summary>Текущий активный контроллер сцены (может быть null).</summary>
    public static BlockJuiceController Instance => instance;

    private TetrisBlockConfigSO config;
    private ParticleSystem stackParticles;
    private ShockWaveController shockWave;

    /// <summary>Конфиг тетриса, из которого берутся все настройки juice.</summary>
    public TetrisBlockConfigSO Config => config;

    /// <summary>
    /// Гарантирует, что singleton существует в сцене и сконфигурирован. Если
    /// он уже есть, но был создан без конфига — донастраивает его переданным.
    /// </summary>
    public static BlockJuiceController Ensure(TetrisBlockConfigSO config)
    {
        if (instance != null)
        {
            if (instance.config == null && config != null)
                instance.Configure(config);

            return instance;
        }

        GameObject go = new GameObject(nameof(BlockJuiceController));
        instance = go.AddComponent<BlockJuiceController>();
        instance.Configure(config);
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Configure(TetrisBlockConfigSO newConfig)
    {
        config = newConfig;
        EnsureParticles();
        EnsureShockWave();
    }

    /// <summary>
    /// Выбрасывает фонтанчик частиц в точке <paramref name="worldPosition"/>
    /// цветом <paramref name="color"/>. Вызывается, когда блок встаёт в стопку.
    /// </summary>
    public void EmitStackParticles(Vector3 worldPosition, Color color)
    {
        if (stackParticles == null)
            return;

        int count = config != null ? Mathf.Max(0, config.StackParticleCount) : 0;
        if (count <= 0)
            return;

        // Полностью непрозрачный «сочный» цвет, чтобы партиклы читались.
        color.a = 1f;

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = new Vector3(worldPosition.x, worldPosition.y, stackParticles.transform.position.z),
            applyShapeToPosition = true,
            startColor = color,
        };

        stackParticles.Emit(emitParams, count);
    }

    /// <summary>
    /// Запускает ударную волну из мировой точки <paramref name="worldPosition"/>
    /// и по её завершении вызывает <paramref name="onComplete"/>. Если шейдер
    /// волны недоступен (нет префаба) — <paramref name="onComplete"/> вызывается
    /// сразу, чтобы вода всё равно отреагировала.
    /// </summary>
    public void PlayShockWave(Vector3 worldPosition, Action onComplete)
    {
        if (shockWave != null)
            shockWave.Enqueue(worldPosition, onComplete);
        else
            onComplete?.Invoke();
    }

    private void EnsureParticles()
    {
        if (stackParticles != null)
            return;

        GameObject go = new GameObject("BlockStackParticles (auto)");
        go.transform.SetParent(transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ConfigureParticles(ps);
        stackParticles = ps;
    }

    private void ConfigureParticles(ParticleSystem ps)
    {
        float lifetime = config != null ? config.StackParticleLifetime : 0.6f;
        float speed = config != null ? config.StackParticleSpeed : 4f;
        float size = config != null ? config.StackParticleSize : 0.18f;

        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 2048;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.25f;
        shape.radiusThickness = 1f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0.1f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader != null)
                renderer.material = new Material(shader);

            renderer.sortingOrder = 50;
        }
    }

    private void EnsureShockWave()
    {
        if (shockWave != null)
            return;

        if (config == null || config.ShockWaveRenderPrefab == null)
            return;

        GameObject instanceGo = Instantiate(config.ShockWaveRenderPrefab, transform);
        instanceGo.name = "ShockWaveRender (auto)";

        ShockWaveController controller = instanceGo.GetComponent<ShockWaveController>();
        if (controller == null)
            controller = instanceGo.AddComponent<ShockWaveController>();

        controller.Initialize(config);
        shockWave = controller;
    }
}
