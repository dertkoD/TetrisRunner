using UnityEngine;
using Ruccho;

/// <summary>
/// Постоянные пузырьки по ВСЕЙ поверхности воды — эффект «кипения» (или
/// кислоты). Работает как фоновые частицы и никак не мешает волнам
/// (<see cref="WaterAmbientWaves"/>) и брызгам (<see cref="WaterDynamicEffects"/>).
///
/// Как устроено:
/// <list type="bullet">
///   <item>В рантайме создаётся дочерний <see cref="ParticleSystem"/> (в префабе
///         ничего тащить не надо). Можно подменить своим через
///         <see cref="bubblesOverride"/>.</item>
///   <item>Эмиттер — широкая полоса (Box), которая каждый кадр растягивается на
///         всю ширину поверхности воды и встаёт на её верхнюю границу. Когда
///         <see cref="DeathWaterController"/> поднимает/опускает воду — пузырьки
///         следуют за уровнем.</item>
///   <item>Пузырьки рождаются вдоль поверхности и всплывают вверх, затухая —
///         получается «кипение». Скорость, плотность, цвет, размер и т.д.
///         настраиваются в инспекторе.</item>
/// </list>
///
/// Компонент вешается на тот же объект, что и вода (префаб <c>Water</c>).
/// </summary>
[DisallowMultipleComponent]
public class WaterSurfaceBubbles : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Объект воды (для ширины и уровня поверхности). Если пусто — берётся " +
             "WaterRWCompute с этого же объекта, иначе этот Transform.")]
    [SerializeField] private WaterRWCompute water;

    [Tooltip("Свой ParticleSystem вместо авто-создаваемого. Удобно, если хочешь " +
             "свои спрайты/материал пузырьков. Если пусто — система создаётся сама.")]
    [SerializeField] private ParticleSystem bubblesOverride;

    [Header("On/Off")]
    [Tooltip("Включить пузырьки.")]
    [SerializeField] private bool enableBubbles = true;

    [Header("Density (плотность кипения)")]
    [Tooltip("Сколько пузырьков в секунду рождается на КАЖДУЮ единицу ширины " +
             "поверхности. Итоговая скорость эмиссии = это число × ширина воды. " +
             "Больше — гуще «кипение».")]
    [SerializeField, Min(0f)] private float bubblesPerSecondPerUnit = 6f;

    [Tooltip("Потолок частиц в системе (защита от перегруза при большой ширине/" +
             "плотности).")]
    [SerializeField, Range(16, 20000)] private int maxParticles = 4000;

    [Header("Spawn band (где рождаются)")]
    [Tooltip("Толщина полосы рождения пузырьков по вертикали (мир. единицы) " +
             "вокруг линии поверхности.")]
    [SerializeField, Min(0f)] private float spawnBandHeight = 0.15f;

    [Tooltip("Смещение полосы рождения относительно поверхности по Y (мир. " +
             "единицы). Отрицательное — чуть ниже поверхности, чтобы пузырьки " +
             "«всплывали» к ней.")]
    [SerializeField] private float surfaceYOffset = -0.05f;

    [Tooltip("Насколько ужать ширину эмиттера относительно полной ширины воды " +
             "(мир. единицы, с каждой стороны). Чтобы пузырьки не лезли за края.")]
    [SerializeField, Min(0f)] private float widthPadding = 0.25f;

    [Header("Motion (всплытие)")]
    [Tooltip("Минимальная скорость всплытия пузырька вверх (мир/сек).")]
    [SerializeField, Min(0f)] private float riseSpeedMin = 0.4f;

    [Tooltip("Максимальная скорость всплытия пузырька вверх (мир/сек).")]
    [SerializeField, Min(0f)] private float riseSpeedMax = 1.1f;

    [Tooltip("Случайное горизонтальное «покачивание» пузырьков (мир/сек).")]
    [SerializeField, Min(0f)] private float horizontalWobble = 0.15f;

    [Header("Lifetime & Size")]
    [Tooltip("Минимальное время жизни пузырька (сек).")]
    [SerializeField, Min(0.02f)] private float lifetimeMin = 0.5f;

    [Tooltip("Максимальное время жизни пузырька (сек).")]
    [SerializeField, Min(0.02f)] private float lifetimeMax = 1.4f;

    [Tooltip("Минимальный стартовый размер пузырька.")]
    [SerializeField, Min(0.001f)] private float sizeMin = 0.04f;

    [Tooltip("Максимальный стартовый размер пузырька.")]
    [SerializeField, Min(0.001f)] private float sizeMax = 0.13f;

    [Tooltip("Кривая размера за время жизни (1 = стартовый размер). По умолчанию " +
             "пузырёк слегка растёт и в конце лопается (уменьшается).")]
    [SerializeField] private AnimationCurve sizeOverLifetime =
        new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.2f));

    [Header("Look (цвет — вода/кислота)")]
    [Tooltip("Цвет пузырьков. Для кислоты поставь зеленоватый, для кипятка — " +
             "бело-голубой.")]
    [SerializeField] private Color bubbleColor = new Color(0.65f, 1f, 0.8f, 0.55f);

    [Tooltip("Затухание прозрачности к концу жизни (1 — полностью к нулю в конце).")]
    [SerializeField, Range(0f, 1f)] private float fadeOut = 1f;

    [Tooltip("Sorting Layer для пузырьков. Пусто — Default. Если вода рисуется в " +
             "отдельном слое — поставь тот же, чтобы пузырьки были видны над водой.")]
    [SerializeField] private string sortingLayerName = "";

    [Tooltip("Sorting Order пузырьков. Поставь выше, чем у воды, чтобы пузырьки " +
             "были поверх поверхности.")]
    [SerializeField] private int sortingOrder = 2;

    private ParticleSystem activeBubbles;
    private ParticleSystem.EmissionModule emission;
    private ParticleSystem.ShapeModule shape;
    private bool autoCreated;
    private bool initialized;

    private void Awake()
    {
        if (water == null)
            water = GetComponent<WaterRWCompute>();

        EnsureParticleSystem();
        ApplyLook();
        initialized = true;
    }

    private void OnEnable()
    {
        if (activeBubbles == null)
            return;

        if (enableBubbles)
            activeBubbles.Play();
        else
            activeBubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDisable()
    {
        if (activeBubbles != null)
            activeBubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDestroy()
    {
        // Авто-созданную систему держим в корне сцены — убираем за собой.
        if (autoCreated && activeBubbles != null)
            Destroy(activeBubbles.gameObject);
    }

    private void LateUpdate()
    {
        UpdateEmitter();
    }

    private void EnsureParticleSystem()
    {
        if (bubblesOverride != null)
        {
            activeBubbles = bubblesOverride;
            autoCreated = false;
        }
        else
        {
            // НЕ делаем систему дочерней к воде: у префаба воды большой
            // неравномерный масштаб (X≈44), который мог бы исказить эмиттер.
            // Держим её в корне сцены и ведём за поверхностью в LateUpdate.
            var go = new GameObject("WaterSurfaceBubbles (auto)")
            {
                hideFlags = HideFlags.DontSave
            };

            activeBubbles = go.AddComponent<ParticleSystem>();
            autoCreated = true;
        }

        emission = activeBubbles.emission;
        shape = activeBubbles.shape;
    }

    private void ApplyLook()
    {
        if (activeBubbles == null)
            return;

        var main = activeBubbles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            Mathf.Min(lifetimeMin, lifetimeMax), Mathf.Max(lifetimeMin, lifetimeMax));
        main.startSize = new ParticleSystem.MinMaxCurve(
            Mathf.Min(sizeMin, sizeMax), Mathf.Max(sizeMin, sizeMax));
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        main.startColor = new ParticleSystem.MinMaxGradient(bubbleColor);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
        main.maxParticles = maxParticles;

        emission = activeBubbles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f; // реальное значение проставляется в UpdateEmitter

        shape = activeBubbles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.rotation = Vector3.zero;
        // startSpeed = 0, поэтому направление формы не важно — Box задаёт только
        // область рождения. Размеры выставляем в UpdateEmitter.

        var vel = activeBubbles.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-horizontalWobble, horizontalWobble);
        vel.y = new ParticleSystem.MinMaxCurve(
            Mathf.Min(riseSpeedMin, riseSpeedMax), Mathf.Max(riseSpeedMin, riseSpeedMax));
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        var col = activeBubbles.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(bubbleColor, 0f),
                new GradientColorKey(bubbleColor, 1f),
            },
            new[]
            {
                new GradientAlphaKey(bubbleColor.a, 0f),
                new GradientAlphaKey(Mathf.Lerp(bubbleColor.a, 0f, fadeOut), 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var size = activeBubbles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeOverLifetime);

        if (autoCreated)
        {
            var renderer = activeBubbles.GetComponent<ParticleSystemRenderer>();
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
                    renderer.material = new Material(shader) { color = bubbleColor };

                if (!string.IsNullOrEmpty(sortingLayerName))
                    renderer.sortingLayerName = sortingLayerName;
                renderer.sortingOrder = sortingOrder;
            }
        }

        if (enableBubbles && isActiveAndEnabled)
            activeBubbles.Play();
    }

    private void UpdateEmitter()
    {
        if (activeBubbles == null)
            return;

        float width = GetSurfaceWidth();
        float emitterWidth = Mathf.Max(0.01f, width - widthPadding * 2f);

        Transform pst = activeBubbles.transform;
        pst.position = new Vector3(GetSurfaceCenterX(), GetSurfaceY() + surfaceYOffset, pst.position.z);

        shape = activeBubbles.shape;
        shape.scale = new Vector3(emitterWidth, Mathf.Max(0.0001f, spawnBandHeight), 0.0001f);

        emission = activeBubbles.emission;
        float rate = enableBubbles ? bubblesPerSecondPerUnit * emitterWidth : 0f;
        emission.rateOverTime = rate;
    }

    private float GetSurfaceWidth()
    {
        Transform wt = water != null ? water.transform : transform;
        return Mathf.Max(0.01f, Mathf.Abs(wt.lossyScale.x));
    }

    private float GetSurfaceCenterX()
    {
        Transform wt = water != null ? water.transform : transform;
        return wt.position.x;
    }

    private float GetSurfaceY()
    {
        Transform wt = water != null ? water.transform : transform;
        return wt.position.y + Mathf.Abs(wt.lossyScale.y) * 0.5f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (lifetimeMin > lifetimeMax) lifetimeMax = lifetimeMin;
        if (sizeMin > sizeMax) sizeMax = sizeMin;
        if (riseSpeedMin > riseSpeedMax) riseSpeedMax = riseSpeedMin;

        if (Application.isPlaying && initialized && activeBubbles != null)
        {
            ApplyLook();
            UpdateEmitter();
        }
    }
#endif
}
