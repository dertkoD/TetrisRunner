using UnityEngine;

/// <summary>
/// Модулирует «живость» воды в зависимости от того, насколько близко её
/// поверхность подошла к игроку: волны (<see cref="WaterAmbientWaves"/>),
/// пузырьки (<see cref="WaterSurfaceBubbles"/>) и цвет воды становятся тем
/// сильнее/ярче, чем ближе вода к игроку, и плавно слабеют, когда вода далеко.
///
/// Идея: то, что сейчас настроено на префабе воды (высота волн, плотность
/// пузырьков, цвет материала) — это ПИК (сильный эффект), когда вода почти у
/// игрока. Этот компонент масштабирует эффекты ВНИЗ от пика, когда вода далеко.
///
/// Уровни задаются дистанцией в клетках сетки от игрока до поверхности воды:
///   * <see cref="strongWithinCells"/> и ближе — сильный эффект (обычно 1–2);
///   * до <see cref="mediumWithinCells"/> — средний;
///   * до <see cref="weakWithinCells"/> — слабый;
///   * дальше — остаётся слабый «фон» (эффекты не выключаются полностью, просто
///     становятся еле заметными).
///
/// Между уровнями интенсивность интерполируется плавно. Цвет воды берётся из
/// материала на старте как «ближний» (пиковый), а «дальний» цвет задаётся в
/// инспекторе — между ними идёт плавный переход по той же интенсивности.
///
/// Компонент вешается на тот же объект, что и вода (префаб <c>Water</c>):
/// он сам подхватит <see cref="WaterAmbientWaves"/>, <see cref="WaterSurfaceBubbles"/>
/// и <see cref="Renderer"/> с этого объекта, а игрока найдёт по
/// <see cref="PlayerFacade"/>.
/// </summary>
[DisallowMultipleComponent]
public class WaterProximityModulator : MonoBehaviour
{
    [Header("References (пусто — подхватятся автоматически)")]
    [Tooltip("Компонент фоновых волн. Если пусто — берётся с этого же объекта.")]
    [SerializeField] private WaterAmbientWaves ambientWaves;

    [Tooltip("Компонент пузырьков. Если пусто — берётся с этого же объекта.")]
    [SerializeField] private WaterSurfaceBubbles bubbles;

    [Tooltip("Renderer воды, на материале которого меняется цвет. Если пусто — " +
             "берётся Renderer с этого же объекта.")]
    [SerializeField] private Renderer waterRenderer;

    [Tooltip("Сетка (для размера клетки). Если пусто — найдётся в сцене.")]
    [SerializeField] private TetrisGridBoard board;

    [Tooltip("Игрок. Если пусто — найдётся PlayerFacade в сцене.")]
    [SerializeField] private Transform player;

    [Header("Дистанции от игрока до поверхности воды (в клетках)")]
    [Tooltip("На этой дистанции и ближе — СИЛЬНЫЙ эффект (обычно 1–2 клетки).")]
    [SerializeField, Min(0f)] private float strongWithinCells = 2f;

    [Tooltip("До этой дистанции — СРЕДНИЙ эффект.")]
    [SerializeField, Min(0f)] private float mediumWithinCells = 4f;

    [Tooltip("До этой дистанции — СЛАБЫЙ эффект. Дальше остаётся слабый фон.")]
    [SerializeField, Min(0f)] private float weakWithinCells = 7f;

    [Header("Интенсивность по уровням (0..1)")]
    [Tooltip("Интенсивность на сильном уровне (вода вплотную). Обычно 1.")]
    [SerializeField, Range(0f, 1f)] private float strongIntensity = 1f;

    [Tooltip("Интенсивность на среднем уровне.")]
    [SerializeField, Range(0f, 1f)] private float mediumIntensity = 0.55f;

    [Tooltip("Интенсивность на слабом уровне и дальше (фон). 0 — эффекты " +
             "практически пропадают, когда вода далеко.")]
    [SerializeField, Range(0f, 1f)] private float weakIntensity = 0.12f;

    [Header("Сглаживание")]
    [Tooltip("Скорость подстройки эффекта под изменение дистанции (единиц " +
             "интенсивности в секунду). 0 — менять мгновенно.")]
    [SerializeField, Min(0f)] private float responseSpeed = 4f;

    [Header("Что модулировать")]
    [SerializeField] private bool driveWaves = true;
    [SerializeField] private bool driveBubbles = true;
    [SerializeField] private bool driveWaterColor = true;
    [SerializeField] private bool driveBubbleColor = true;

    [Header("Цвет воды — FAR (когда вода далеко)")]
    [Tooltip("«Ближний» (пиковый) цвет берётся из материала воды на старте, а " +
             "эти значения — то, к чему цвет уходит, когда вода далеко.")]
    [SerializeField] private bool driveAddend = true;
    [Tooltip("Дальнее значение свойства _Addend (основной оттенок воды).")]
    [SerializeField] private Color farAddend = new Color(0.10f, 0.16f, 0.28f, 0f);

    [SerializeField] private bool driveMultiplier = false;
    [Tooltip("Дальнее значение свойства _Multiplier.")]
    [SerializeField] private Color farMultiplier = new Color(0.35f, 0.55f, 0.85f, 1f);

    [SerializeField] private bool driveSurfaceColor = false;
    [Tooltip("Дальнее значение свойства _SurfaceColor.")]
    [SerializeField] private Color farSurfaceColor = new Color(0.30f, 0.40f, 0.60f, 0.8f);

    [Header("Цвет пузырьков — FAR (когда вода далеко)")]
    [Tooltip("«Ближний» цвет пузырьков берётся из их настроек, а это — дальнее " +
             "значение, к которому цвет уходит, когда вода далеко.")]
    [SerializeField] private Color farBubbleColor = new Color(0.5f, 0.7f, 0.9f, 0.4f);

    private static readonly int AddendID = Shader.PropertyToID("_Addend");
    private static readonly int MultiplierID = Shader.PropertyToID("_Multiplier");
    private static readonly int SurfaceColorID = Shader.PropertyToID("_SurfaceColor");

    private Material waterMaterial;
    private Color nearAddend, nearMultiplier, nearSurfaceColor;
    private bool hasAddend, hasMultiplier, hasSurfaceColor;

    private Color nearBubbleColor;
    private bool hasNearBubbleColor;

    private float currentIntensity = -1f;
    private float lastAppliedIntensity = -1f;

    private void Awake()
    {
        if (ambientWaves == null)
            ambientWaves = GetComponent<WaterAmbientWaves>();

        if (bubbles == null)
            bubbles = GetComponent<WaterSurfaceBubbles>();

        if (waterRenderer == null)
            waterRenderer = GetComponent<Renderer>();

        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        CaptureNearColors();
    }

    private void OnEnable()
    {
        // Заставляем первый кадр применить значение без сглаживания.
        currentIntensity = -1f;
        lastAppliedIntensity = -1f;
    }

    private void CaptureNearColors()
    {
        // Берём ИНСТАНС материала: WaterRWCompute тоже работает с
        // meshRenderer.material, поэтому мы делим один и тот же инстанс и не
        // конфликтуем (он пишет mainTexture/float'ы, мы — только цвета).
        if (driveWaterColor && waterRenderer != null)
        {
            waterMaterial = waterRenderer.material;

            if (waterMaterial != null)
            {
                if (driveAddend && waterMaterial.HasProperty(AddendID))
                {
                    nearAddend = waterMaterial.GetColor(AddendID);
                    hasAddend = true;
                }

                if (driveMultiplier && waterMaterial.HasProperty(MultiplierID))
                {
                    nearMultiplier = waterMaterial.GetColor(MultiplierID);
                    hasMultiplier = true;
                }

                if (driveSurfaceColor && waterMaterial.HasProperty(SurfaceColorID))
                {
                    nearSurfaceColor = waterMaterial.GetColor(SurfaceColorID);
                    hasSurfaceColor = true;
                }
            }
        }

        if (driveBubbleColor && bubbles != null)
        {
            nearBubbleColor = bubbles.ConfiguredBubbleColor;
            hasNearBubbleColor = true;
        }
    }

    private void EnsurePlayer()
    {
        if (player != null)
            return;

        PlayerFacade facade = FindFirstObjectByType<PlayerFacade>();
        if (facade != null)
            player = facade.transform;
    }

    private void LateUpdate()
    {
        EnsurePlayer();

        float target = ComputeTargetIntensity();

        if (currentIntensity < 0f || responseSpeed <= 0f)
            currentIntensity = target;
        else
            currentIntensity = Mathf.MoveTowards(currentIntensity, target, responseSpeed * Time.deltaTime);

        // В установившемся состоянии не пересчитываем эффекты каждый кадр
        // (иначе зря пересоздаём градиенты пузырьков и т.п.).
        if (Mathf.Approximately(currentIntensity, lastAppliedIntensity))
            return;

        Apply(currentIntensity);
        lastAppliedIntensity = currentIntensity;
    }

    private float ComputeTargetIntensity()
    {
        // Нет игрока — держим слабый фон.
        if (player == null)
            return weakIntensity;

        float cellSize = board != null ? board.CellSize : 1f;
        if (cellSize <= 0f)
            cellSize = 1f;

        float waterTopY = GetWaterTopY();

        // Сколько клеток между поверхностью воды и игроком по вертикали.
        // Игрок над водой → gap > 0; вода поднялась до/выше игрока → gap <= 0.
        float gapCells = (player.position.y - waterTopY) / cellSize;

        return EvaluateIntensity(gapCells);
    }

    private float GetWaterTopY()
    {
        DeathWaterController dw = DeathWaterController.Instance;
        if (dw != null)
            return dw.CurrentTopY;

        if (waterRenderer != null)
            return waterRenderer.bounds.max.y;

        return transform.position.y;
    }

    /// <summary>
    /// Преобразует дистанцию (в клетках) в интенсивность по контрольным точкам
    /// сильный/средний/слабый с линейной интерполяцией между ними. Дальше
    /// слабого уровня держится слабая интенсивность (эффекты не выключаются).
    /// </summary>
    private float EvaluateIntensity(float gapCells)
    {
        // Упорядочиваем пороги на случай некорректного ввода в инспекторе.
        float medium = Mathf.Max(strongWithinCells, mediumWithinCells);
        float weak = Mathf.Max(medium, weakWithinCells);

        if (gapCells <= strongWithinCells)
            return strongIntensity;

        if (gapCells <= medium)
            return Mathf.Lerp(strongIntensity, mediumIntensity,
                Mathf.InverseLerp(strongWithinCells, medium, gapCells));

        if (gapCells <= weak)
            return Mathf.Lerp(mediumIntensity, weakIntensity,
                Mathf.InverseLerp(medium, weak, gapCells));

        return weakIntensity;
    }

    private void Apply(float intensity)
    {
        if (driveWaves && ambientWaves != null)
            ambientWaves.SetIntensity(intensity);

        if (driveBubbles && bubbles != null)
            bubbles.SetIntensity(intensity);

        if (driveWaterColor && waterMaterial != null)
        {
            if (hasAddend)
                waterMaterial.SetColor(AddendID, Color.Lerp(farAddend, nearAddend, intensity));

            if (hasMultiplier)
                waterMaterial.SetColor(MultiplierID, Color.Lerp(farMultiplier, nearMultiplier, intensity));

            if (hasSurfaceColor)
                waterMaterial.SetColor(SurfaceColorID, Color.Lerp(farSurfaceColor, nearSurfaceColor, intensity));
        }

        if (driveBubbleColor && bubbles != null && hasNearBubbleColor)
            bubbles.SetRuntimeColor(Color.Lerp(farBubbleColor, nearBubbleColor, intensity));
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (mediumWithinCells < strongWithinCells)
            mediumWithinCells = strongWithinCells;

        if (weakWithinCells < mediumWithinCells)
            weakWithinCells = mediumWithinCells;
    }
#endif
}
