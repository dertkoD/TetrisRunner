using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Двигающаяся платформа, которую сетка тетриса «видит» как обычный занятый
/// набор клеток (статический TetrisPlacedBlock). Платформа умеет занимать
/// СКОЛЬКО УГОДНО клеток (а не одну) — её форма берётся либо из коллайдера,
/// либо из явных размеров в клетках, либо из явного списка клеток.
///
/// Платформа двигается по точкам (waypoints) в сцене. Сетка определит клетку
/// каждой точки через <see cref="TetrisGridBoard.WorldToCell"/>.
///
/// Режим движения управляется флагом <see cref="tetrisMoving"/>:
/// * <b>TetrisMoving = true</b>  — классическое тетрисное движение: один
///   клеточный шаг за <see cref="moveInterval"/> секунд (мгновенный «прыжок»).
/// * <b>TetrisMoving = false</b> — плавное движение: между клетками платформа
///   (и стопка блоков на ней) визуально интерполируется. Длительность одного
///   шага по-прежнему задаётся <see cref="moveInterval"/>.
///
/// Все блоки, опирающиеся сверху на платформу (и друг на друга),
/// двигаются вместе с ней.
/// </summary>
[DisallowMultipleComponent]
public class TetrisGridMovingPlatform : MonoBehaviour
{
    public enum Source
    {
        AutoFromCollider,
        AutoFromRenderer,
        ManualSize,
        ExplicitCells,
    }

    [Header("Board")]
    [Tooltip("Сетка, в которой нужно занять клетки. Если пусто — найдём в сцене сами.")]
    [SerializeField] private TetrisGridBoard board;

    [Header("Shape source")]
    [Tooltip("Откуда брать форму платформы (какие клетки она занимает).")]
    [SerializeField] private Source source = Source.ManualSize;

    [Tooltip("AutoFromCollider/Renderer: если AABB слегка выходит за границу клетки " +
             "на эту долю или меньше, клетка всё равно НЕ считается занятой.")]
    [SerializeField, Range(0f, 0.5f)] private float overlapTolerance = 0.05f;

    [Tooltip("ManualSize: размер платформы в клетках. Опорной (pivot) считается " +
             "клетка, в которой стоит Transform платформы; от неё прямоугольник " +
             "растёт вправо и вверх. Поставь Transform платформы в левую нижнюю клетку.")]
    [SerializeField] private Vector2Int manualSizeInCells = new Vector2Int(3, 1);

    [Tooltip("ExplicitCells: вручную заданный список клеток сетки (мировые координаты сетки).")]
    [SerializeField] private Vector2Int[] explicitCells;

    [Header("Snap")]
    [Tooltip("Прижать Transform к центру опорной клетки на старте. Если выключить, " +
             "визуальное смещение между Transform и pivot-клеткой будет сохраняться " +
             "при движении (полезно, если у платформы свой кастомный визуал).")]
    [SerializeField] private bool snapTransformToPivotOnStart = false;

    [Header("Waypoints")]
    [Tooltip("Точки в сцене, между которыми ходит платформа. Сетка определит " +
             "клетку для каждой точки по её мировой позиции. Платформа едет так, " +
             "чтобы её опорная клетка (pivot) пришла в клетку точки.")]
    [SerializeField] private Transform[] waypoints;

    [Tooltip("Если true — после последней точки платформа возвращается к первой и " +
             "ходит по кругу. Если false — после прибытия в последнюю точку " +
             "платформа останавливается.")]
    [SerializeField] private bool loop = true;

    [Header("Movement")]
    [Tooltip("Если true — платформа двигается «как в тетрисе»: один клеточный шаг " +
             "за раз, мгновенно. Если false — платформа и блоки на ней плавно " +
             "интерполируются между клетками.")]
    [SerializeField] private bool tetrisMoving = true;

    [Tooltip("Сколько секунд занимает один клеточный шаг. В режиме TetrisMoving — " +
             "это пауза между мгновенными прыжками. В плавном режиме — длительность " +
             "анимации одного шага.")]
    [SerializeField, Min(0.05f)] private float moveInterval = 0.5f;

    [Tooltip("Стартовая задержка перед первым шагом.")]
    [SerializeField, Min(0f)] private float startDelay = 0f;

    [Tooltip("Если true — при упоре в препятствие платформа просто ждёт " +
             "следующего такта и пробует снова. Если false — пропускает waypoint и " +
             "идёт к следующему.")]
    [SerializeField] private bool retryWhenBlocked = true;

    [Header("Player Carry")]
    [Tooltip("Если true — игрок, стоящий на верхней грани платформы, едет вместе с ней.")]
    [SerializeField] private bool carryPlayer = true;

    [Tooltip("Высота зоны над платформой, в которой ищется игрок-наездник (мировые единицы). " +
             "Должна быть немного больше, чем зазор между ногами игрока и поверхностью платформы.")]
    [SerializeField, Min(0.05f)] private float playerCarryDetectHeight = 0.25f;

    [Tooltip("Слои, считающиеся игроком. Если Nothing — детектор ищет компонент PlayerFacade.")]
    [SerializeField] private LayerMask playerLayers = 0;

    [Header("Debug")]
    [Tooltip("По умолчанию включено — в консоль пишутся стартовая регистрация клеток, " +
             "каждый шаг и причина блокировки шага. Когда всё работает, можно отключить.")]
    [SerializeField] private bool verboseLogs = true;

    private TetrisPlacedBlock platformBlock;
    private Rigidbody2D platformBody;
    private Collider2D platformCollider;
    private Vector3 visualOffset;
    private float stepTimer;
    private int currentWaypointIndex;
    private bool finished;
    private bool initialized;
    private bool firstStepDone;
    private readonly Collider2D[] playerScanBuffer = new Collider2D[8];

    // Состояние плавной анимации одного шага.
    private struct AnimEntry
    {
        public TetrisPlacedBlock block;
        public Vector3 from;
        public Vector3 to;
    }
    private List<AnimEntry> animEntries;
    private float animProgress;

    public TetrisPlacedBlock PlatformBlock => platformBlock;
    public bool IsFinished => finished;

    private void Reset()
    {
        EnsureKinematicRigidbody();
    }

    private void Awake()
    {
        // Unity 2D физика надёжно вызывает OnTriggerEnter/Exit на triggerах только
        // когда движущийся коллайдер имеет Rigidbody2D. Без него платформа считается
        // СТАТИКОЙ — перемещение через transform.position не всегда обновляет
        // overlaps, и PlatformLiftTrigger «не видит», что платформа вышла из зоны.
        // Поэтому подмешиваем Kinematic Rigidbody2D автоматически.
        EnsureKinematicRigidbody();
    }

    private void EnsureKinematicRigidbody()
    {
        platformBody = GetComponent<Rigidbody2D>();

        if (platformBody == null)
            platformBody = gameObject.AddComponent<Rigidbody2D>();

        platformBody.bodyType = RigidbodyType2D.Kinematic;
        platformBody.simulated = true;
        platformBody.gravityScale = 0f;
        platformBody.linearVelocity = Vector2.zero;
        platformBody.angularVelocity = 0f;
        platformBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        // Интерполяция выключена: дискретные прыжки и плавная анимация
        // обрабатываются сами в коде. Если позволить Unity интерполировать,
        // визуал будет «отставать» на кадр.
        platformBody.interpolation = RigidbodyInterpolation2D.None;
    }

    private void Start()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        if (board == null)
        {
            Debug.LogWarning($"{nameof(TetrisGridMovingPlatform)}: TetrisGridBoard не найден в сцене.", this);
            return;
        }

        if (platformCollider == null)
            platformCollider = GetComponentInChildren<Collider2D>();

        List<Vector2Int> cells = ResolveCells();

        if (cells == null || cells.Count == 0)
        {
            Debug.LogWarning($"{nameof(TetrisGridMovingPlatform)}: не удалось определить клетки для платформы '{name}'.", this);
            return;
        }

        Vector2Int pivot = cells[0];
        Vector2Int[] offsets = new Vector2Int[cells.Count];
        for (int i = 0; i < cells.Count; i++)
            offsets[i] = cells[i] - pivot;

        Vector3 pivotWorld = board.CellToWorld(pivot);

        if (snapTransformToPivotOnStart)
        {
            transform.position = pivotWorld;
            visualOffset = Vector3.zero;
        }
        else
        {
            // Запомним, насколько визуал смещён относительно pivot-клетки.
            // При каждом клеточном шаге будем восстанавливать этот сдвиг,
            // чтобы спрайт платформы не «прыгал» в центр клетки.
            visualOffset = transform.position - pivotWorld;
            visualOffset.z = 0f;
        }

        platformBlock = GetComponent<TetrisPlacedBlock>();
        if (platformBlock == null)
            platformBlock = gameObject.AddComponent<TetrisPlacedBlock>();

        platformBlock.Initialize(TetrisGridBoard.AllocateBlockId(), -1, pivot, offsets);
        platformBlock.MarkAsStatic();

        board.RegisterBlock(platformBlock);

        if (verboseLogs)
            Debug.Log(
                $"{nameof(TetrisGridMovingPlatform)} '{name}': registered {cells.Count} cell(s). " +
                $"Pivot={pivot}, offsets=[{string.Join(",", offsets)}], visualOffset={visualOffset}",
                this);

        stepTimer = 0f;
        firstStepDone = false;
        currentWaypointIndex = 0;
        finished = false;
        initialized = true;
    }

    private void OnDestroy()
    {
        if (board == null || platformBlock == null)
            return;

        // Если сцена ещё активна — после ухода платформы могут зависнуть
        // блоки, которые на ней стояли. Просим сетку сразу их уронить.
        if (gameObject.scene.isLoaded)
            board.UnregisterBlockAndDropAbove(platformBlock);
        else
            board.UnregisterBlock(platformBlock);
    }

    private void Update()
    {
        // Плавная анимация одного шага идёт по обычному Update (визуал).
        if (animEntries == null || animEntries.Count == 0)
            return;

        float prevProgress = Mathf.Clamp01(animProgress);
        animProgress += Time.deltaTime / Mathf.Max(0.0001f, moveInterval);
        float t = Mathf.Clamp01(animProgress);

        Vector3 platformDelta = Vector3.zero;

        for (int i = 0; i < animEntries.Count; i++)
        {
            AnimEntry e = animEntries[i];
            if (e.block == null) continue;
            Vector3 prevPos = Vector3.Lerp(e.from, e.to, prevProgress);
            Vector3 pos = Vector3.Lerp(e.from, e.to, t);
            e.block.SetVisualPosition(pos);

            if (e.block == platformBlock)
                platformDelta = pos - prevPos;
        }

        if (platformDelta.sqrMagnitude > 1e-8f)
            CarryPlayerOnTop(platformDelta);

        if (t >= 1f)
            animEntries = null;
    }

    private void FixedUpdate()
    {
        if (!initialized || platformBlock == null || board == null)
            return;

        if (finished)
            return;

        if (waypoints == null || waypoints.Length == 0)
            return;

        // Пока активна плавная анимация — следующий шаг не делаем.
        if (!tetrisMoving && animEntries != null)
            return;

        // Стартовая задержка (один раз перед первым шагом).
        if (!firstStepDone)
        {
            stepTimer += Time.fixedDeltaTime;
            if (stepTimer < startDelay)
                return;
            stepTimer = 0f;
            firstStepDone = true;
        }
        else if (tetrisMoving)
        {
            // Дискретный режим: ждём паузу между мгновенными прыжками.
            stepTimer += Time.fixedDeltaTime;
            if (stepTimer < moveInterval)
                return;
            stepTimer = 0f;
        }
        // В плавном режиме после анимации сразу начинаем следующий шаг —
        // никакой дополнительной паузы нет.

        // Можем «прокрутить» подряд несколько waypoint'ов, если они уже совпадают
        // с текущей клеткой или невалидны — иначе платформа тратит целый
        // moveInterval только на детектирование «я уже на месте».
        int safety = (waypoints?.Length ?? 0) + 2;

        while (safety-- > 0)
        {
            if (!TryResolveCurrentWaypointCell(out Vector2Int targetCell))
            {
                if (verboseLogs)
                    Debug.Log($"{nameof(TetrisGridMovingPlatform)} '{name}': waypoint[{currentWaypointIndex}] невалиден, пропускаю.", this);
                AdvanceWaypoint();
                if (finished) return;
                continue;
            }

            Vector2Int currentPivot = platformBlock.PivotCell;
            Vector2Int delta = targetCell - currentPivot;

            if (delta.x == 0 && delta.y == 0)
            {
                if (verboseLogs)
                    Debug.Log($"{nameof(TetrisGridMovingPlatform)} '{name}': достигнут waypoint[{currentWaypointIndex}] = {targetCell}, переключаюсь дальше.", this);
                AdvanceWaypoint();
                if (finished) return;
                continue;
            }

            Vector2Int step = ComputeStep(delta);

            if (step == Vector2Int.zero)
                return;

            if (verboseLogs)
                Debug.Log($"{nameof(TetrisGridMovingPlatform)} '{name}': шаг {step} к waypoint[{currentWaypointIndex}] = {targetCell} (текущая клетка {currentPivot}, delta {delta}).", this);

            PerformStep(step);
            return;
        }
    }

    private void PerformStep(Vector2Int step)
    {
        // Захватываем стопку и их текущие визуальные позиции ДО шага,
        // чтобы потом либо откатить (плавный режим) либо просто пересчитать смещения.
        HashSet<TetrisPlacedBlock> carry = board.GetCarryStack(platformBlock);

        Dictionary<TetrisPlacedBlock, Vector3> preVisual = null;
        if (!tetrisMoving)
        {
            preVisual = new Dictionary<TetrisPlacedBlock, Vector3>(carry.Count);
            foreach (TetrisPlacedBlock b in carry)
            {
                if (b == null) continue;
                preVisual[b] = b.transform.position;
            }
        }

        // Запоминаем стартовую позицию платформы — нужна, чтобы потом передвинуть игрока.
        Vector3 platformPreStep = transform.position;

        bool moved = board.TryMovePlacedBlockWithStack(platformBlock, step);

        if (!moved)
        {
            if (verboseLogs)
            {
                Vector2Int newPivot = platformBlock.PivotCell + step;
                string blockedBy = DescribeBlocker(newPivot);
                Debug.LogWarning(
                    $"{nameof(TetrisGridMovingPlatform)} '{name}': шаг {step} не удался. Целевая клетка пивота {newPivot}. " +
                    $"Препятствие: {blockedBy}. " +
                    (retryWhenBlocked
                        ? "retryWhenBlocked=true — буду пытаться снова на следующем такте."
                        : "retryWhenBlocked=false — пропускаю текущий waypoint."),
                    this);
            }

            if (!retryWhenBlocked)
                AdvanceWaypoint();
            return;
        }

        // Платформе нужно сохранить визуальный сдвиг (visualOffset), а
        // стопке блоков — оставаться в центре своих клеток.
        Vector3 platformPostSnap = board.CellToWorld(platformBlock.PivotCell) + visualOffset;

        if (tetrisMoving)
        {
            // Дискретный режим — мгновенно ставим всех в финальные позиции.
            platformBlock.SetVisualPosition(platformPostSnap);
            CarryPlayerOnTop(platformPostSnap - platformPreStep);
            return;
        }

        // Плавный режим: всех откатываем визуально на старые позиции, ставим в очередь анимацию.
        List<AnimEntry> anim = new List<AnimEntry>(carry.Count);

        foreach (TetrisPlacedBlock b in carry)
        {
            if (b == null) continue;

            Vector3 from = preVisual != null && preVisual.TryGetValue(b, out Vector3 cached)
                ? cached
                : b.transform.position;

            Vector3 to = (b == platformBlock)
                ? platformPostSnap
                : board.CellToWorld(b.PivotCell);

            anim.Add(new AnimEntry { block = b, from = from, to = to });
            b.SetVisualPosition(from);
        }

        animEntries = anim;
        animProgress = 0f;
    }

    private bool TryResolveCurrentWaypointCell(out Vector2Int cell)
    {
        cell = default;

        if (waypoints == null || currentWaypointIndex < 0 || currentWaypointIndex >= waypoints.Length)
            return false;

        Transform wp = waypoints[currentWaypointIndex];
        if (wp == null)
            return false;

        cell = board.WorldToCell(wp.position);
        return true;
    }

    private void AdvanceWaypoint()
    {
        currentWaypointIndex++;

        if (currentWaypointIndex < waypoints.Length)
            return;

        if (loop)
        {
            currentWaypointIndex = 0;
            return;
        }

        currentWaypointIndex = waypoints.Length - 1;
        finished = true;
    }

    /// <summary>
    /// Если на верхней грани платформы стоит игрок — двигает его на ту же
    /// дельту, что и платформа. Делает это и в дискретном, и в плавном режиме.
    /// </summary>
    private void CarryPlayerOnTop(Vector3 worldDelta)
    {
        if (!carryPlayer)
            return;

        if (worldDelta.sqrMagnitude < 1e-8f)
            return;

        if (platformCollider == null)
            platformCollider = GetComponentInChildren<Collider2D>();

        if (platformCollider == null)
            return;

        Bounds b = platformCollider.bounds;
        Vector2 center = new Vector2(b.center.x, b.max.y + playerCarryDetectHeight * 0.5f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, b.size.x), playerCarryDetectHeight);

        int n;
        if (playerLayers.value != 0)
            n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, playerScanBuffer, playerLayers);
        else
            n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, playerScanBuffer);

        for (int i = 0; i < n; i++)
        {
            Collider2D c = playerScanBuffer[i];
            if (c == null) continue;

            PlayerFacade pf = c.GetComponent<PlayerFacade>()
                              ?? c.GetComponentInParent<PlayerFacade>();
            if (pf == null) continue;

            // Двигаем тело игрока. Динамическому RB достаточно body.position,
            // но обновим и transform для согласованности.
            Vector3 newPos = pf.transform.position + worldDelta;
            pf.transform.position = newPos;
            if (pf.Body != null)
                pf.Body.position = new Vector2(newPos.x, newPos.y);
        }
    }

    private static Vector2Int ComputeStep(Vector2Int delta)
    {
        if (delta.x != 0)
            return new Vector2Int(delta.x > 0 ? 1 : -1, 0);

        if (delta.y != 0)
            return new Vector2Int(0, delta.y > 0 ? 1 : -1);

        return Vector2Int.zero;
    }

    private string DescribeBlocker(Vector2Int targetPivot)
    {
        if (platformBlock == null || board == null)
            return "?";

        Vector2Int[] offsets = platformBlock.CellOffsets;
        if (offsets == null) return "нет offsets";

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int newCell = targetPivot + offsets[i];

            if (!board.IsInside(newCell))
                return $"клетка {newCell} вне сетки";

            if (board.IsOccupied(newCell))
                return $"клетка {newCell} уже занята";
        }

        return "неизвестно (валидация считает место свободным)";
    }

    private List<Vector2Int> ResolveCells()
    {
        switch (source)
        {
            case Source.ExplicitCells:
                return explicitCells != null ? new List<Vector2Int>(explicitCells) : null;

            case Source.AutoFromRenderer:
                {
                    Renderer renderer = GetComponentInChildren<Renderer>();
                    if (renderer == null)
                    {
                        Debug.LogWarning($"{nameof(TetrisGridMovingPlatform)}: на '{name}' нет Renderer для AutoFromRenderer.", this);
                        return null;
                    }
                    return CellsInsideAABB(renderer.bounds);
                }

            case Source.AutoFromCollider:
                {
                    Collider2D collider = GetComponentInChildren<Collider2D>();
                    if (collider == null)
                    {
                        Debug.LogWarning($"{nameof(TetrisGridMovingPlatform)}: на '{name}' нет Collider2D для AutoFromCollider.", this);
                        return null;
                    }
                    return CellsInsideAABB(collider.bounds);
                }

            case Source.ManualSize:
            default:
                return CellsFromManualSize();
        }
    }

    private List<Vector2Int> CellsFromManualSize()
    {
        if (board == null)
            return null;

        int w = Mathf.Max(1, manualSizeInCells.x);
        int h = Mathf.Max(1, manualSizeInCells.y);

        Vector2Int pivot = board.WorldToCell(transform.position);

        List<Vector2Int> cells = new List<Vector2Int>(w * h);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                Vector2Int cell = new Vector2Int(pivot.x + x, pivot.y + y);
                if (board.IsInside(cell))
                    cells.Add(cell);
            }
        }

        return cells;
    }

    private List<Vector2Int> CellsInsideAABB(Bounds bounds)
    {
        if (board == null)
            return null;

        float pad = overlapTolerance * board.CellSize;

        Vector3 min = bounds.min + new Vector3(pad, pad, 0f);
        Vector3 max = bounds.max - new Vector3(pad, pad, 0f);

        Vector2Int minCell = board.WorldToCell(min);
        Vector2Int maxCell = board.WorldToCell(max);

        List<Vector2Int> cells = new List<Vector2Int>();

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (board.IsInside(cell))
                    cells.Add(cell);
            }
        }

        return cells;
    }
}
