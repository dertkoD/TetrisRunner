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
///         всю ширину воды и встаёт на её ДНО. Когда
///         <see cref="DeathWaterController"/> поднимает/опускает воду — дно и
///         глубина пересчитываются, и пузырьки всплывают на всю высоту.</item>
///   <item>Пузырьки рождаются у дна и всплывают вверх к поверхности, затухая —
///         получается «кипение со дна». Скорость, плотность, цвет, размер и т.д.
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

    [Tooltip("Компонент фоновых волн (для учёта волнистой границы воды при " +
             "гашении пузырьков). Если пусто — берётся с этого же объекта.")]
    [SerializeField] private WaterAmbientWaves ambientWaves;

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

    [Header("Spawn band (со дна)")]
    [Tooltip("Толщина полосы рождения пузырьков по вертикали (мир. единицы) " +
             "у ДНА воды. Пузырьки рождаются здесь и всплывают вверх.")]
    [SerializeField, Min(0f)] private float spawnBandHeight = 0.15f;

    [Tooltip("Смещение полосы рождения относительно дна воды по Y (мир. " +
             "единицы). Положительное — чуть выше дна.")]
    [SerializeField] private float bottomYOffset = 0.05f;

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
    [Tooltip("Автоматически рассчитывать время жизни так, чтобы пузырьки как раз " +
             "доходили со дна до поверхности (по текущей глубине воды и скорости " +
             "всплытия). Включено — пузырьки всегда всплывают на всю глубину, " +
             "даже когда вода поднимается. Выключено — берутся Lifetime Min/Max.")]
    [SerializeField] private bool autoLifetimeFromDepth = true;

    [Tooltip("Запас времени жизни при авто-расчёте (1 — ровно до поверхности, " +
             "больше — пузырёк ещё немного «живёт» у поверхности и лопается).")]
    [SerializeField, Min(0.1f)] private float lifetimeDepthMargin = 1.1f;

    [Tooltip("Минимальное время жизни пузырька (сек). Используется, если " +
             "авто-расчёт по глубине выключен.")]
    [SerializeField, Min(0.02f)] private float lifetimeMin = 0.5f;

    [Tooltip("Максимальное время жизни пузырька (сек). Используется, если " +
             "авто-расчёт по глубине выключен.")]
    [SerializeField, Min(0.02f)] private float lifetimeMax = 1.4f;

    [Tooltip("Минимальный стартовый размер пузырька.")]
    [SerializeField, Min(0.001f)] private float sizeMin = 0.04f;

    [Tooltip("Максимальный стартовый размер пузырька.")]
    [SerializeField, Min(0.001f)] private float sizeMax = 0.13f;

    [Tooltip("Кривая размера за время жизни (1 = стартовый размер). По умолчанию " +
             "пузырёк слегка растёт и в конце лопается (уменьшается).")]
    [SerializeField] private AnimationCurve sizeOverLifetime =
        new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.2f));

    [Header("Kill at surface (исчезновение у границы воды)")]
    [Tooltip("Гасить пузырьки, как только они доходят до верхней границы воды. " +
             "Граница берётся по текущему уровню воды (она у нас растёт), поэтому " +
             "линия гашения двигается ТОЛЬКО вместе с уровнем — медленно. Каждый " +
             "пузырёк лопается отдельно, когда сам доходит до неё (без «пачек»).")]
    [SerializeField] private bool killAtSurface = true;

    [Tooltip("Смещение линии гашения относительно поверхности по Y (мир. единицы). " +
             "0 — ровно на поверхности; отрицательное — чуть ниже (пузырёк " +
             "лопается чуть раньше под водой — полезно при больших волнах, чтобы " +
             "пузырьки не всплывали в воздух на впадинах волн).")]
    [SerializeField] private float surfaceKillOffset = -0.1f;

    [Tooltip("ОПАСНО: заставить линию гашения повторять волну. По умолчанию " +
             "ВЫКЛЮЧЕНО, потому что бегущая волна двигает линию вверх-вниз и " +
             "уничтожает пузырьки целыми «пачками» на спаде волны. Включай только " +
             "при маленькой амплитуде волн и малом 'Wave Follow Strength'.")]
    [SerializeField] private bool followWaves = false;

    [Tooltip("Множитель к высоте волн при расчёте линии гашения (только если " +
             "'Follow Waves' включён). Высота волн оценивается приблизительно — " +
             "ставь небольшое значение (0.2–0.4), иначе линия будет «нырять» и " +
             "выкашивать пузырьки пачками.")]
    [SerializeField, Min(0f)] private float waveFollowStrength = 0.3f;

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
    private ParticleSystem.Particle[] particleBuffer;
    private bool autoCreated;
    private bool initialized;

    private void Awake()
    {
        if (water == null)
            water = GetComponent<WaterRWCompute>();

        if (ambientWaves == null)
            ambientWaves = GetComponent<WaterAmbientWaves>();

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
        KillParticlesAboveSurface();
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
        // Стартовая скорость = скорость всплытия. Направление вверх задаётся
        // поворотом формы-эмиттера ниже (Box излучает вдоль своей оси +Z).
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            Mathf.Min(riseSpeedMin, riseSpeedMax), Mathf.Max(riseSpeedMin, riseSpeedMax));
        main.startColor = new ParticleSystem.MinMaxGradient(bubbleColor);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
        main.maxParticles = maxParticles;

        emission = activeBubbles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f; // реальное значение проставляется в UpdateEmitter

        shape = activeBubbles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        // Box излучает частицы вдоль локальной оси +Z. Поворачиваем форму на
        // -90° вокруг X, чтобы +Z смотрел в мировой +Y — тогда ВСЕ пузырьки
        // летят строго вверх. Размеры самого бокса задаём в UpdateEmitter.
        shape.rotation = new Vector3(-90f, 0f, 0f);

        // Вертикальную скорость даёт startSpeed (направленно вверх). Через
        // velocityOverLifetime добавляем только лёгкое горизонтальное
        // «покачивание», чтобы пузырьки не шли идеально по прямой.
        var vel = activeBubbles.velocityOverLifetime;
        vel.enabled = horizontalWobble > 0f;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-horizontalWobble, horizontalWobble);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
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

        // Полоса рождения — у ДНА воды, чтобы пузырьки всплывали со дна вверх.
        Transform pst = activeBubbles.transform;
        pst.position = new Vector3(GetSurfaceCenterX(), GetBottomY() + bottomYOffset, pst.position.z);

        shape = activeBubbles.shape;
        // Форма повёрнута на -90° вокруг X (см. ApplyLook), поэтому локальные оси
        // бокса отображаются так: local X -> world X (ширина), local Y -> world Z
        // (глубина «в экран», делаем тонкой), local Z -> world Y (толщина полосы
        // рождения у дна).
        shape.scale = new Vector3(emitterWidth, 0.0001f, Mathf.Max(0.0001f, spawnBandHeight));

        emission = activeBubbles.emission;
        float rate = enableBubbles ? bubblesPerSecondPerUnit * emitterWidth : 0f;
        emission.rateOverTime = rate;

        if (autoLifetimeFromDepth)
        {
            // Время жизни считаем по САМОЙ МЕДЛЕННОЙ скорости всплытия, чтобы
            // даже самый медленный пузырёк гарантированно успел дойти до
            // поверхности на всю текущую глубину воды (она у нас растёт).
            // Более быстрые дойдут раньше и будут погашены прямо у поверхности
            // через KillParticlesAboveSurface, так что «перелёта» не будет.
            float depth = Mathf.Max(0.01f, GetDepth());
            float margin = Mathf.Max(0.1f, lifetimeDepthMargin);
            float slow = Mathf.Max(0.01f, Mathf.Min(riseSpeedMin, riseSpeedMax));
            float life = depth / slow * margin;

            var main = activeBubbles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life);
        }
    }

    /// <summary>
    /// Гасит пузырьки, которые дошли до верхней границы воды. Линия границы
    /// берётся по текущему уровню воды (он у нас растёт) и, при включённом
    /// <see cref="followWaves"/>, повторяет форму постоянных волн. Так пузырьки
    /// исчезают именно у поверхности, а не улетают в воздух и не пропадают
    /// раньше времени под водой.
    /// </summary>
    private void KillParticlesAboveSurface()
    {
        if (!killAtSurface || activeBubbles == null)
            return;

        int capacity = activeBubbles.main.maxParticles;
        if (capacity <= 0)
            return;

        if (particleBuffer == null || particleBuffer.Length < capacity)
            particleBuffer = new ParticleSystem.Particle[capacity];

        int count = activeBubbles.GetParticles(particleBuffer);
        if (count == 0)
            return;

        float baseSurfaceY = GetSurfaceY() + surfaceKillOffset;
        bool useWaves = followWaves && ambientWaves != null;

        bool changed = false;
        for (int i = 0; i < count; i++)
        {
            float killY = baseSurfaceY;
            if (useWaves)
                killY += ambientWaves.SampleSurfaceHeight(particleBuffer[i].position.x) * waveFollowStrength;

            if (particleBuffer[i].position.y >= killY)
            {
                // remainingLifetime <= 0 — частица умирает на следующем шаге.
                particleBuffer[i].remainingLifetime = 0f;
                changed = true;
            }
        }

        if (changed)
            activeBubbles.SetParticles(particleBuffer, count);
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

    private float GetBottomY()
    {
        Transform wt = water != null ? water.transform : transform;
        return wt.position.y - Mathf.Abs(wt.lossyScale.y) * 0.5f;
    }

    private float GetDepth()
    {
        Transform wt = water != null ? water.transform : transform;
        return Mathf.Abs(wt.lossyScale.y);
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
