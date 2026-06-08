using UnityEngine;
using Ruccho;

/// <summary>
/// Заставляет воду префаба <c>Water</c> (компонент <see cref="WaterRWCompute"/>)
/// ВСЕГДА быть живой: постоянно гонит по поверхности мягкие волны, даже когда
/// в воду ничего не падает.
///
/// Как это работает (важно — мы НЕ трогаем сам пакет WaterRW):
/// <list type="bullet">
///   <item><see cref="WaterRWCompute"/> умеет «толкать» воду от любых объектов,
///         которые пересекают линию поверхности и лежат на нужном слое
///         (<c>Layers To Interact With</c>). Для объектов без Rigidbody2D он
///         спрашивает скорость через интерфейс <see cref="IWaterRWInteractionProvider"/>.</item>
///   <item>Поэтому здесь мы в рантайме раскидываем вдоль поверхности набор
///         невидимых «возмутителей» — маленьких триггер-коллайдеров, каждый из
///         которых реализует <see cref="IWaterRWInteractionProvider"/> и сообщает
///         волне свою вертикальную «скорость» по формуле бегущей волны
///         (сумма синусов). Это и есть постоянное волнение.</item>
///   <item>Реактивные брызги/волны от блоков и игрока продолжают работать как
///         раньше — мы ничего у них не отбираем, а лишь добавляем фоновую
///         анимацию поверхности.</item>
/// </list>
///
/// Высоту и характер волн крутим прямо в инспекторе: <see cref="waveHeight"/>
/// (главная «высота/сила»), <see cref="waveLength"/>, <see cref="waveSpeed"/>
/// и параметры второй октавы для более «живой» поверхности.
///
/// Компонент вешается на тот же объект, что и <see cref="WaterRWCompute"/>
/// (т.е. на сам префаб <c>Water</c>). Возмутители создаются в рантайме как
/// отдельные объекты в корне сцены (чтобы не наследовать большой масштаб
/// воды), в префабе ничего настраивать руками не нужно.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class WaterAmbientWaves : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Симуляция воды WaterRW. Если пусто — берётся с этого же объекта.")]
    [SerializeField] private WaterRWCompute water;

    [Header("Ambient Waves")]
    [Tooltip("Включить постоянное волнение. Выключишь — останутся только " +
             "реактивные волны от падающих блоков/игрока.")]
    [SerializeField] private bool enableAmbientWaves = true;

    [Tooltip("ГЛАВНАЯ ручка: высота (сила) постоянных волн в мировых единицах. " +
             "0 — поверхность стоит ровно. Больше — выше и заметнее волны.")]
    [SerializeField, Min(0f)] private float waveHeight = 0.12f;

    [Tooltip("Длина волны в мировых единицах — расстояние между гребнями. " +
             "Меньше — частая мелкая рябь, больше — длинные плавные валы.")]
    [SerializeField, Min(0.1f)] private float waveLength = 5f;

    [Tooltip("Скорость бега волны вдоль поверхности (мир/сек). Знак задаёт " +
             "направление.")]
    [SerializeField] private float waveSpeed = 1.5f;

    [Header("Ambient Waves — Secondary Octave (для естественности)")]
    [Tooltip("Доля второй, более мелкой волны относительно основной (0..1). " +
             "0 — чистый синус, больше — поверхность «живее» и нерегулярнее.")]
    [SerializeField, Range(0f, 1f)] private float secondaryStrength = 0.45f;

    [Tooltip("Во сколько раз вторая волна короче основной.")]
    [SerializeField, Min(0.05f)] private float secondaryLengthScale = 0.5f;

    [Tooltip("Во сколько раз вторая волна быстрее основной (и в обратную " +
             "сторону для красивой интерференции).")]
    [SerializeField] private float secondarySpeedScale = -1.6f;

    [Header("Sources (возмутители поверхности)")]
    [Tooltip("Сколько невидимых источников волн раскидать вдоль поверхности. " +
             "Больше — глаже волна, но помни про лимит 'Max Interaction Items' " +
             "у самой воды: фоновые источники делят его с блоками и игроком.")]
    [SerializeField, Range(2, 28)] private int sourceCount = 16;

    [Tooltip("Покрывать источниками ВСЮ ширину поверхности воды (по " +
             "lossyScale.x объекта воды). Включено — волны идут по всей воде. " +
             "ВАЖНО: 'Max Surface Width' у воды должен быть не меньше ширины " +
             "воды, иначе симуляция всё равно обрежется этим окном.")]
    [SerializeField] private bool coverFullSurface = true;

    [Tooltip("Ширина участка поверхности (мир. единицы), который покрывают " +
             "источники. Используется, только если 'Cover Full Surface' выключен. " +
             "Не больше, чем 'Max Surface Width' у воды, иначе крайние источники " +
             "не попадут в зону симуляции.")]
    [SerializeField, Min(1f)] private float coverageWidth = 18f;

    [Tooltip("Высота коллайдеров-источников. Должна быть больше нуля, чтобы они " +
             "гарантированно пересекали линию поверхности воды.")]
    [SerializeField, Min(0.05f)] private float sourceColliderHeight = 0.5f;

    [Tooltip("Слой для невидимых источников. ОБЯЗАТЕЛЬНО должен входить в " +
             "'Layers To Interact With' у воды (по умолчанию туда входит Default). " +
             "Не ставь сюда слой игрока/блоков, иначе пойдут лишние брызги.")]
    [SerializeField] private int sourceLayer = 0;

    [Header("Advanced")]
    [Tooltip("Усиление отклика поверхности. Вода «догоняет» заданную высоту не " +
             "мгновенно, поэтому небольшое усиление помогает реальной амплитуде " +
             "совпасть с Wave Height. Слишком большое — волны станут резкими.")]
    [SerializeField, Min(0f)] private float surfaceResponse = 2f;

    private const float TwoPi = Mathf.PI * 2f;

    private AmbientWaveSource[] sources;
    private bool sourcesBuilt;

    private void Awake()
    {
        if (water == null)
            water = GetComponent<WaterRWCompute>();

        if (water == null)
        {
            Debug.LogWarning(
                $"{nameof(WaterAmbientWaves)} на '{name}': рядом нет {nameof(WaterRWCompute)}. " +
                $"Постоянные волны работать не будут.", this);
        }
    }

    private void OnEnable()
    {
        EnsureSources();
        SetSourcesActive(enableAmbientWaves);
    }

    private void OnDisable()
    {
        SetSourcesActive(false);
    }

    private void FixedUpdate()
    {
        UpdateSources();
    }

    private void Update()
    {
        // На случай, если у воды updateMode = Update — обновляем позиции и тут,
        // чтобы источники всегда стояли там, где их «увидит» линкаст.
        UpdateSources();
    }

    private void EnsureSources()
    {
        if (sourcesBuilt && sources != null && sources.Length == sourceCount)
            return;

        if (sources != null)
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null)
                    Destroy(sources[i].gameObject);
            }
        }

        sources = new AmbientWaveSource[Mathf.Max(2, sourceCount)];

        for (int i = 0; i < sources.Length; i++)
        {
            // ВАЖНО: источники НЕ делаем детьми воды. У префаба воды большой
            // неравномерный масштаб (например, X≈44), который при наследовании
            // раздул бы коллайдеры. Держим их в корне сцены и каждый кадр сами
            // ставим в мировые координаты поверхности.
            var go = new GameObject($"AmbientWaveSource {i} (auto)")
            {
                hideFlags = HideFlags.DontSave
            };
            go.layer = Mathf.Clamp(sourceLayer, 0, 31);

            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;

            var src = go.AddComponent<AmbientWaveSource>();
            src.Init(this);

            sources[i] = src;
        }

        sourcesBuilt = true;
    }

    private void DestroySources()
    {
        if (sources == null)
            return;

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != null)
                Destroy(sources[i].gameObject);
        }

        sources = null;
        sourcesBuilt = false;
    }

    private void OnDestroy()
    {
        DestroySources();
    }

    private void SetSourcesActive(bool active)
    {
        if (sources == null)
            return;

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != null)
                sources[i].gameObject.SetActive(active);
        }
    }

    private void UpdateSources()
    {
        if (!enableAmbientWaves || sources == null || water == null)
            return;

        float centerX = GetSurfaceCenterX();
        float surfaceY = GetSurfaceY();
        float coverage = GetCoverageWidth();

        int count = sources.Length;
        float spacing = coverage / count;
        float left = centerX - coverage * 0.5f + spacing * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var src = sources[i];
            if (src == null)
                continue;

            float x = left + spacing * i;

            Transform t = src.transform;
            Vector3 world = new Vector3(x, surfaceY, t.position.z);
            t.position = world;

            src.Resize(spacing, sourceColliderHeight);
        }
    }

    private float GetCoverageWidth()
    {
        if (coverFullSurface && water != null)
            return Mathf.Max(1f, Mathf.Abs(water.transform.lossyScale.x));

        return coverageWidth;
    }

    private float GetSurfaceCenterX()
    {
        // Если вода скроллится за главной камерой (scrollToMainCamera) — окно
        // симуляции едет за камерой, поэтому и источники центрируем по камере.
        // Иначе окно стоит на самой воде, и источники центрируем по её центру,
        // чтобы покрыть всю поверхность.
        if (water != null && water.scrollToMainCamera)
        {
            Camera cam = Camera.main;
            if (cam != null)
                return cam.transform.position.x;
            return water.WavePosition;
        }

        if (water != null)
            return water.transform.position.x;

        return transform.position.x;
    }

    private float GetSurfaceY()
    {
        Transform wt = water != null ? water.transform : transform;
        return wt.position.y + Mathf.Abs(wt.lossyScale.y) * 0.5f;
    }

    /// <summary>
    /// Возвращает «скорость» источника в заданной мировой X-координате так,
    /// чтобы <see cref="WaterRWCompute"/> вытолкнул поверхность на нужную
    /// высоту. Высота поверхности под источником ≈ velY * 0.5 (см. шейдер
    /// WaterRW), поэтому здесь velY = 2 * targetHeight * surfaceResponse.
    /// </summary>
    public Vector2 SampleVelocity(float worldX)
    {
        if (!enableAmbientWaves || waveHeight <= 0f)
            return Vector2.zero;

        float h = SampleHeight(worldX, Time.time);
        float velY = h * 2f * Mathf.Max(0f, surfaceResponse);
        return new Vector2(0f, velY);
    }

    private float SampleHeight(float x, float time)
    {
        float k1 = TwoPi / Mathf.Max(0.1f, waveLength);
        float h = Mathf.Sin(k1 * x - waveSpeed * k1 * time);

        if (secondaryStrength > 0f)
        {
            float secondLength = Mathf.Max(0.05f, waveLength * secondaryLengthScale);
            float k2 = TwoPi / secondLength;
            float secondSpeed = waveSpeed * secondarySpeedScale;
            h += secondaryStrength * Mathf.Sin(k2 * x - secondSpeed * k2 * time + 1.3f);
        }

        // Нормируем, чтобы суммарный размах октав не «раздувал» заданную высоту.
        float norm = 1f + secondaryStrength;
        return waveHeight * (h / norm);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Меняем число/слой источников на лету в режиме игры.
        if (Application.isPlaying && sourcesBuilt)
        {
            sourcesBuilt = false;
            EnsureSources();
            SetSourcesActive(enableAmbientWaves && enabled);
        }
    }
#endif

    /// <summary>
    /// Невидимый источник волн: триггер-коллайдер на линии поверхности, который
    /// сообщает воде вертикальную «скорость» из <see cref="WaterAmbientWaves"/>.
    /// </summary>
    [DisallowMultipleComponent]
    private sealed class AmbientWaveSource : MonoBehaviour, IWaterRWInteractionProvider
    {
        private WaterAmbientWaves owner;
        private BoxCollider2D box;

        public void Init(WaterAmbientWaves owner)
        {
            this.owner = owner;
            box = GetComponent<BoxCollider2D>();
        }

        public void Resize(float width, float height)
        {
            if (box == null)
                box = GetComponent<BoxCollider2D>();
            if (box != null)
                box.size = new Vector2(Mathf.Max(0.01f, width), Mathf.Max(0.01f, height));
        }

        public Vector2 Velocity =>
            owner != null ? owner.SampleVelocity(transform.position.x) : Vector2.zero;
    }
}
