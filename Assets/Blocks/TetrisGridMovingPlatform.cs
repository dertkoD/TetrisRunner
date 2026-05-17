using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Двигающаяся платформа, которую сетка тетриса «видит» как обычный занятый
/// набор клеток (статический TetrisPlacedBlock). Платформа двигается по точкам
/// (waypoints) — каждой точкой считается мировой Transform, который сетка
/// конвертирует в клетку через <see cref="TetrisGridBoard.WorldToCell"/>.
/// На каждом такте интервала платформа делает один клеточный шаг в сторону
/// текущей waypoint-клетки (по X, затем по Y). По достижении точки —
/// переходит к следующей. Если включён <c>Loop</c>, после последней точки
/// возвращается к первой; иначе останавливается.
///
/// Источник стартовых клеток (форма платформы):
/// * AutoFromCollider — берётся первый Collider2D на объекте.
/// * AutoFromRenderer — берётся первый Renderer на объекте.
/// * ExplicitCells   — список клеток, заданный вручную.
/// </summary>
[DisallowMultipleComponent]
public class TetrisGridMovingPlatform : MonoBehaviour
{
    public enum Source
    {
        AutoFromCollider,
        AutoFromRenderer,
        ExplicitCells,
    }

    [Header("Board")]
    [Tooltip("Сетка, в которой нужно занять клетки. Если пусто — найдём в сцене сами.")]
    [SerializeField] private TetrisGridBoard board;

    [Header("Source for initial cells")]
    [SerializeField] private Source source = Source.AutoFromCollider;

    [Tooltip("Если AABB слегка выходит за границу клетки на эту долю или меньше, " +
             "клетка всё равно НЕ считается занятой. Помогает игнорировать суб-пиксельные перехлёсты.")]
    [SerializeField, Range(0f, 0.5f)] private float overlapTolerance = 0.05f;

    [Header("Explicit Cells (если выбран соответствующий source)")]
    [SerializeField] private Vector2Int[] explicitCells;

    [Header("Snap")]
    [Tooltip("Прижать Transform к центру опорной клетки на старте. Если выключить, " +
             "визуал может «уехать» относительно зарегистрированных клеток.")]
    [SerializeField] private bool snapTransformToPivotOnStart = true;

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
    [Tooltip("Сколько секунд между шагами. Шаг = сдвиг на 1 клетку.")]
    [SerializeField, Min(0.05f)] private float moveInterval = 0.5f;

    [Tooltip("Стартовая задержка перед первым шагом.")]
    [SerializeField, Min(0f)] private float startDelay = 0f;

    [Tooltip("Если true — при упоре в препятствие платформа просто ждёт " +
             "следующего такта и пробует снова. Если false — пропускает waypoint и " +
             "идёт к следующему. По умолчанию true.")]
    [SerializeField] private bool retryWhenBlocked = true;

    private TetrisPlacedBlock platformBlock;
    private float moveTimer;
    private int currentWaypointIndex;
    private bool finished;
    private bool initialized;

    public TetrisPlacedBlock PlatformBlock => platformBlock;
    public bool IsFinished => finished;

    private void Start()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        if (board == null)
        {
            Debug.LogWarning($"{nameof(TetrisGridMovingPlatform)}: TetrisGridBoard не найден в сцене.", this);
            return;
        }

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

        if (snapTransformToPivotOnStart)
            transform.position = board.CellToWorld(pivot);

        platformBlock = GetComponent<TetrisPlacedBlock>();
        if (platformBlock == null)
            platformBlock = gameObject.AddComponent<TetrisPlacedBlock>();

        platformBlock.Initialize(TetrisGridBoard.AllocateBlockId(), -1, pivot, offsets);
        platformBlock.MarkAsStatic();

        board.RegisterBlock(platformBlock);

        moveTimer = -startDelay;
        currentWaypointIndex = 0;
        finished = false;
        initialized = true;
    }

    private void OnDestroy()
    {
        if (board == null || platformBlock == null)
            return;

        board.UnregisterBlock(platformBlock);
    }

    private void FixedUpdate()
    {
        if (!initialized || platformBlock == null || board == null)
            return;

        if (finished)
            return;

        if (waypoints == null || waypoints.Length == 0)
            return;

        moveTimer += Time.fixedDeltaTime;

        if (moveTimer < moveInterval)
            return;

        moveTimer = 0f;

        if (!TryResolveCurrentWaypointCell(out Vector2Int targetCell))
        {
            // У этой waypoint нет валидного Transform — переходим к следующей.
            AdvanceWaypoint();
            return;
        }

        Vector2Int currentPivot = platformBlock.PivotCell;
        Vector2Int delta = targetCell - currentPivot;

        if (delta.x == 0 && delta.y == 0)
        {
            // Достигли точки — следующий шаг будет уже в сторону следующей.
            AdvanceWaypoint();
            return;
        }

        Vector2Int step = ComputeStep(delta);

        if (step == Vector2Int.zero)
            return;

        if (board.TryMovePlacedBlockWithStack(platformBlock, step))
            return;

        // Шаг не удался: либо упёрлись в препятствие, либо в границу сетки.
        if (!retryWhenBlocked)
            AdvanceWaypoint();
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

        // Не в цикле — фиксируемся на последней точке и больше не двигаемся.
        currentWaypointIndex = waypoints.Length - 1;
        finished = true;
    }

    private static Vector2Int ComputeStep(Vector2Int delta)
    {
        // Один клеточный шаг по одной оси за такт. Сначала добираем по X,
        // затем по Y — так платформа не двигается по диагонали и не пытается
        // прыгать через занятые клетки.
        if (delta.x != 0)
            return new Vector2Int(delta.x > 0 ? 1 : -1, 0);

        if (delta.y != 0)
            return new Vector2Int(0, delta.y > 0 ? 1 : -1);

        return Vector2Int.zero;
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
            default:
                {
                    Collider2D collider = GetComponentInChildren<Collider2D>();
                    if (collider == null)
                    {
                        Debug.LogWarning($"{nameof(TetrisGridMovingPlatform)}: на '{name}' нет Collider2D для AutoFromCollider.", this);
                        return null;
                    }
                    return CellsInsideAABB(collider.bounds);
                }
        }
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
