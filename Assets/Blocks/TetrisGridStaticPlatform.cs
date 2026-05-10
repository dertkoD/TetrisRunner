using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Помечает свой GameObject как статическое препятствие в сетке тетриса.
/// Подходит для платформ, нарисованных вручную в сцене: они занимают
/// клетки сетки (падающие блоки на них стакаются), но сами не падают
/// и не схлопываются по цвету.
///
/// Способы определения занимаемых клеток:
///   * Auto From Collider — берётся AABB у первого Collider2D на объекте
///     и из него вычисляются все клетки сетки, которые он перекрывает.
///   * Auto From Renderer — то же, но по Renderer.bounds.
///   * Explicit Cells — заданный вручную список клеток сетки (мировые
///     клетки, не относительные оффсеты).
/// </summary>
[DisallowMultipleComponent]
public class TetrisGridStaticPlatform : MonoBehaviour
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

    private TetrisPlacedBlock registeredBlock;

    private void Start()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        if (board == null)
        {
            Debug.LogWarning($"{nameof(TetrisGridStaticPlatform)}: TetrisGridBoard не найден в сцене.", this);
            return;
        }

        List<Vector2Int> cells = ResolveCells();

        if (cells == null || cells.Count == 0)
        {
            Debug.LogWarning($"{nameof(TetrisGridStaticPlatform)}: не удалось определить клетки для платформы '{name}'.", this);
            return;
        }

        registeredBlock = board.RegisterStaticCells(cells, $"StaticPlatform[{name}]");
    }

    private void OnDestroy()
    {
        if (board == null || registeredBlock == null)
            return;

        board.UnregisterBlock(registeredBlock);

        if (registeredBlock != null)
            Destroy(registeredBlock.gameObject);
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
                        Debug.LogWarning($"{nameof(TetrisGridStaticPlatform)}: на '{name}' нет Renderer для AutoFromRenderer.", this);
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
                        Debug.LogWarning($"{nameof(TetrisGridStaticPlatform)}: на '{name}' нет Collider2D для AutoFromCollider.", this);
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

        // Чуть-чуть ужимаем AABB по краям, чтобы не «зацепить» соседнюю клетку
        // из-за того, что коллайдер кончается ровно на границе клетки.
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
