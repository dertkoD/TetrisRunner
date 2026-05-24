using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// «Дверь» — статичная платформа, занимающая клетки в сетке тетриса. По
/// внешнему сигналу (например, от <see cref="GridButton"/>) дверь плавно
/// поднимается на заданное число клеток вверх и остаётся в открытом
/// положении. Грид-сетка всегда знает, какие клетки занимает дверь:
/// при каждом клеточном шаге занятые клетки переоформляются через
/// <see cref="TetrisGridBoard.TryMovePlacedBlockWithStack"/>, а блоки,
/// стоявшие на двери, едут вместе с ней.
///
/// Это специализированный, упрощённый аналог <see cref="TetrisGridMovingPlatform"/>:
/// без зацикленных waypoints и без необходимости ручного управления — только
/// один направленный вверх «выезд» по запросу.
/// </summary>
[DisallowMultipleComponent]
public class GridLiftDoor : MonoBehaviour
{
    public enum ShapeSource
    {
        ManualSize,
        AutoFromCollider,
        AutoFromRenderer,
    }

    [Header("References")]
    [Tooltip("Сетка, в которую регистрируется дверь. Если пусто — будет найдена в сцене.")]
    [SerializeField] private TetrisGridBoard board;

    [Header("Shape")]
    [Tooltip("Откуда брать форму двери (какие клетки она занимает).")]
    [SerializeField] private ShapeSource shapeSource = ShapeSource.ManualSize;

    [Tooltip("ManualSize: размер двери в клетках. Опорной (pivot) считается " +
             "клетка под Transform двери; прямоугольник растёт вправо и вверх. " +
             "Поставь Transform двери в её левую нижнюю клетку.")]
    [SerializeField] private Vector2Int manualSizeInCells = new Vector2Int(1, 3);

    [Tooltip("AutoFromCollider/Renderer: если AABB слегка выходит за границу клетки " +
             "на эту долю или меньше, клетка всё равно НЕ считается занятой.")]
    [SerializeField, Range(0f, 0.5f)] private float overlapTolerance = 0.05f;

    [Tooltip("Если true — Transform двери на старте прижимается к центру опорной клетки. " +
             "Если false — сохраняется визуальное смещение Transform относительно pivot.")]
    [SerializeField] private bool snapTransformToPivotOnStart = false;

    [Header("Lift")]
    [Tooltip("На сколько клеток дверь должна подняться при открытии. Должно быть >= 1.")]
    [SerializeField, Min(1)] private int liftCells = 4;

    [Tooltip("Скорость подъёма в клетках в секунду. На каждый клеточный шаг тратится " +
             "1 / liftCellsPerSecond секунд (плюс плавная интерполяция визуала).")]
    [SerializeField, Min(0.01f)] private float liftCellsPerSecond = 4f;

    [Tooltip("Если true — при упоре в препятствие дверь подождёт следующего такта " +
             "и попробует ещё раз. Если false — открытие просто прерывается.")]
    [SerializeField] private bool retryWhenBlocked = true;

    [Tooltip("Если true — дверь открывается автоматически на старте (полезно для тестов). " +
             "Обычно надо оставить false и открывать через GridButton.")]
    [SerializeField] private bool openOnStart = false;

    [Header("Player Carry")]
    [Tooltip("Если true — игрок, стоящий на верхней грани двери, едет вместе с ней.")]
    [SerializeField] private bool carryPlayer = true;

    [Tooltip("Высота зоны над дверью, в которой ищется игрок-наездник (мировые единицы).")]
    [SerializeField, Min(0.05f)] private float playerCarryDetectHeight = 0.25f;

    [Tooltip("Слои, считающиеся игроком. Если Nothing — детектор ищет компонент PlayerFacade.")]
    [SerializeField] private LayerMask playerLayers = 0;

    [Header("Events")]
    [Tooltip("Вызывается один раз, когда дверь начинает открываться.")]
    [SerializeField] private UnityEvent onOpenStarted;

    [Tooltip("Вызывается один раз, когда дверь полностью открылась.")]
    [SerializeField] private UnityEvent onOpenFinished;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private TetrisPlacedBlock doorBlock;
    private Rigidbody2D doorBody;
    private Collider2D doorCollider;
    private Vector3 visualOffset;

    private bool initialized;
    private bool isOpening;
    private bool isFullyOpen;
    private int cellsRemaining;

    private float stepTimer;
    private Vector3 stepFrom;
    private Vector3 stepTo;
    private float stepDuration;
    private bool hasActiveStep;

    private readonly Collider2D[] playerScanBuffer = new Collider2D[8];

    public bool IsFullyOpen => isFullyOpen;
    public bool IsOpening => isOpening;
    public TetrisPlacedBlock DoorBlock => doorBlock;

    private void Reset()
    {
        EnsureKinematicRigidbody();
    }

    private void Awake()
    {
        EnsureKinematicRigidbody();
    }

    private void Start()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        if (board == null)
        {
            Debug.LogWarning($"{nameof(GridLiftDoor)}: TetrisGridBoard не найден в сцене.", this);
            return;
        }

        if (doorCollider == null)
            doorCollider = GetComponentInChildren<Collider2D>();

        List<Vector2Int> cells = ResolveCells();

        if (cells == null || cells.Count == 0)
        {
            Debug.LogWarning($"{nameof(GridLiftDoor)}: не удалось определить клетки для двери '{name}'.", this);
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
            visualOffset = transform.position - pivotWorld;
            visualOffset.z = 0f;
        }

        doorBlock = GetComponent<TetrisPlacedBlock>();
        if (doorBlock == null)
            doorBlock = gameObject.AddComponent<TetrisPlacedBlock>();

        doorBlock.Initialize(TetrisGridBoard.AllocateBlockId(), -1, pivot, offsets);
        doorBlock.MarkAsStatic();

        board.RegisterBlock(doorBlock);

        if (verboseLogs)
            Debug.Log(
                $"{nameof(GridLiftDoor)} '{name}': registered {cells.Count} cell(s). " +
                $"Pivot={pivot}, offsets=[{string.Join(",", offsets)}], visualOffset={visualOffset}.",
                this);

        initialized = true;

        if (openOnStart)
            Open();
    }

    private void OnDestroy()
    {
        if (board == null || doorBlock == null)
            return;

        board.UnregisterBlock(doorBlock);
    }

    /// <summary>
    /// Просит дверь начать открываться. Повторные вызовы во время уже идущего
    /// открытия игнорируются. После полного открытия дверь остаётся в верхней
    /// позиции — закрыть её обратно нельзя (по требованию задачи).
    /// </summary>
    public void Open()
    {
        if (!initialized)
        {
            if (verboseLogs)
                Debug.Log($"{nameof(GridLiftDoor)} '{name}': Open() вызван до инициализации — открою при первой возможности.", this);
            openOnStart = true;
            return;
        }

        if (isFullyOpen || isOpening)
            return;

        if (liftCells <= 0)
        {
            isFullyOpen = true;
            onOpenStarted?.Invoke();
            onOpenFinished?.Invoke();
            return;
        }

        isOpening = true;
        cellsRemaining = liftCells;

        if (verboseLogs)
            Debug.Log($"{nameof(GridLiftDoor)} '{name}': начинаю открытие, поднять на {liftCells} клеток.", this);

        onOpenStarted?.Invoke();
    }

    private void EnsureKinematicRigidbody()
    {
        doorBody = GetComponent<Rigidbody2D>();

        if (doorBody == null)
            doorBody = gameObject.AddComponent<Rigidbody2D>();

        doorBody.bodyType = RigidbodyType2D.Kinematic;
        doorBody.simulated = true;
        doorBody.gravityScale = 0f;
        doorBody.linearVelocity = Vector2.zero;
        doorBody.angularVelocity = 0f;
        doorBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        doorBody.interpolation = RigidbodyInterpolation2D.None;
    }

    private void Update()
    {
        if (!hasActiveStep || doorBlock == null)
            return;

        stepTimer += Time.deltaTime;
        float t = stepDuration > 0f ? Mathf.Clamp01(stepTimer / stepDuration) : 1f;

        Vector3 prevPos = doorBlock.transform.position;
        Vector3 nextPos = Vector3.Lerp(stepFrom, stepTo, t);

        doorBlock.SetVisualPosition(nextPos);

        Vector3 delta = nextPos - prevPos;
        if (delta.sqrMagnitude > 1e-8f)
            CarryPlayerOnTop(delta);

        if (t >= 1f)
            hasActiveStep = false;
    }

    private void FixedUpdate()
    {
        if (!initialized || !isOpening || doorBlock == null || board == null)
            return;

        if (hasActiveStep)
            return;

        if (cellsRemaining <= 0)
        {
            isOpening = false;
            isFullyOpen = true;

            if (verboseLogs)
                Debug.Log($"{nameof(GridLiftDoor)} '{name}': дверь полностью открыта.", this);

            onOpenFinished?.Invoke();
            return;
        }

        Vector3 fromWorld = doorBlock.transform.position;

        bool moved = board.TryMovePlacedBlockWithStack(doorBlock, Vector2Int.up);

        if (!moved)
        {
            if (verboseLogs)
            {
                Vector2Int newPivot = doorBlock.PivotCell + Vector2Int.up;
                Debug.LogWarning(
                    $"{nameof(GridLiftDoor)} '{name}': шаг вверх не удался. Целевая клетка пивота {newPivot}. " +
                    (retryWhenBlocked
                        ? "retryWhenBlocked=true — буду пытаться снова на следующем такте."
                        : "retryWhenBlocked=false — прерываю открытие."),
                    this);
            }

            if (!retryWhenBlocked)
            {
                isOpening = false;
            }
            return;
        }

        Vector3 toWorld = board.CellToWorld(doorBlock.PivotCell) + visualOffset;

        // Откатываем визуал на старую позицию и запускаем плавный шаг.
        doorBlock.SetVisualPosition(fromWorld);

        stepFrom = fromWorld;
        stepTo = toWorld;
        stepDuration = 1f / Mathf.Max(0.01f, liftCellsPerSecond);
        stepTimer = 0f;
        hasActiveStep = true;

        cellsRemaining--;
    }

    private void CarryPlayerOnTop(Vector3 worldDelta)
    {
        if (!carryPlayer)
            return;

        if (worldDelta.sqrMagnitude < 1e-8f)
            return;

        if (doorCollider == null)
            doorCollider = GetComponentInChildren<Collider2D>();

        if (doorCollider == null)
            return;

        Bounds b = doorCollider.bounds;
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

            Vector3 newPos = pf.transform.position + worldDelta;
            pf.transform.position = newPos;
            if (pf.Body != null)
                pf.Body.position = new Vector2(newPos.x, newPos.y);
        }
    }

    private List<Vector2Int> ResolveCells()
    {
        switch (shapeSource)
        {
            case ShapeSource.AutoFromRenderer:
                {
                    Renderer renderer = GetComponentInChildren<Renderer>();
                    if (renderer == null)
                    {
                        Debug.LogWarning($"{nameof(GridLiftDoor)}: на '{name}' нет Renderer для AutoFromRenderer.", this);
                        return null;
                    }
                    return CellsInsideAABB(renderer.bounds);
                }

            case ShapeSource.AutoFromCollider:
                {
                    Collider2D collider = GetComponentInChildren<Collider2D>();
                    if (collider == null)
                    {
                        Debug.LogWarning($"{nameof(GridLiftDoor)}: на '{name}' нет Collider2D для AutoFromCollider.", this);
                        return null;
                    }
                    return CellsInsideAABB(collider.bounds);
                }

            case ShapeSource.ManualSize:
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
