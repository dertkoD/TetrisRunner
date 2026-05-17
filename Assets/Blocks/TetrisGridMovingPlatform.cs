using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Двигающаяся платформа, которую сетка тетриса «видит» как обычный занятый
/// набор клеток (статический TetrisPlacedBlock). Платформа сдвигается на одну
/// клетку в заданном направлении через равный интервал. Если на пути появляется
/// препятствие — направление инвертируется. Все блоки, опирающиеся сверху на
/// платформу (и сверху друг на друга), едут вместе с ней.
///
/// Источник клеток (как и у TetrisGridStaticPlatform):
/// * AutoFromCollider — берётся первый Collider2D на объекте.
/// * AutoFromRenderer — берётся первый Renderer на объекте.
/// * ExplicitCells   — список клеток, заданный вручную (мировые клетки сетки).
///
/// При старте платформа автоматически приклеивается к сетке: её Transform
/// смещается так, чтобы пивот совпал с центром нужной клетки.
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

    [Header("Source")]
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

    [Header("Movement")]
    [Tooltip("Шаг платформы по сетке (в клетках) — например (1,0) для движения вправо.")]
    [SerializeField] private Vector2Int stepDirection = new Vector2Int(1, 0);

    [Tooltip("Сколько секунд между шагами. Шаг = сдвиг на 1 клетку по stepDirection.")]
    [SerializeField, Min(0.05f)] private float moveInterval = 0.5f;

    [Tooltip("Если true — при невозможности сдвинуться (упор в препятствие или край сетки) " +
             "направление меняется на противоположное (платформа курсирует вперёд-назад).")]
    [SerializeField] private bool bounceOnObstacle = true;

    [Tooltip("Стартовая задержка перед первым шагом.")]
    [SerializeField, Min(0f)] private float startDelay = 0f;

    private TetrisPlacedBlock platformBlock;
    private Vector2Int currentDirection;
    private float moveTimer;
    private bool initialized;

    public TetrisPlacedBlock PlatformBlock => platformBlock;

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

        currentDirection = stepDirection;
        moveTimer = -startDelay;
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

        if (currentDirection == Vector2Int.zero)
            return;

        moveTimer += Time.fixedDeltaTime;

        if (moveTimer < moveInterval)
            return;

        moveTimer = 0f;

        if (board.TryMovePlacedBlockWithStack(platformBlock, currentDirection))
            return;

        if (!bounceOnObstacle)
            return;

        currentDirection = -currentDirection;

        // Сразу пробуем сделать шаг в обратную сторону, чтобы не «зависать» на
        // одном такте у стенки.
        board.TryMovePlacedBlockWithStack(platformBlock, currentDirection);
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
