using System.Collections.Generic;
using UnityEngine;

public class TetrisGridBoard : MonoBehaviour
{
    private static readonly Vector2Int[] FourNeighbors =
    {
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1),
    };

    [Header("Grid")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 20;
    [SerializeField] private float cellSize = 1f;

    [Header("Origin")]
    [SerializeField] private Transform origin;

    [Header("Placed Cells")]
    [Tooltip("Куда складываются отдельные ячейки залоченных блоков. " +
             "Если не задано, будет создан дочерний объект 'PlacedCells' автоматически.")]
    [SerializeField] private Transform placedCellsParent;

    private readonly Dictionary<Vector2Int, TetrisPlacedCell> cells = new Dictionary<Vector2Int, TetrisPlacedCell>();

    private static int nextBlockId;

    /// <summary>Возвращает уникальный идентификатор блока (используется при спавне/локе).</summary>
    public static int AllocateBlockId()
    {
        nextBlockId++;
        return nextBlockId;
    }

    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;

    public Transform PlacedCellsParent
    {
        get
        {
            if (placedCellsParent == null)
            {
                GameObject host = new GameObject("PlacedCells");
                host.transform.SetParent(transform, false);
                host.transform.localPosition = Vector3.zero;
                host.transform.localRotation = Quaternion.identity;
                host.transform.localScale = Vector3.one;
                placedCellsParent = host.transform;
            }

            return placedCellsParent;
        }
    }

    private Vector3 OriginPosition
    {
        get
        {
            if (origin != null)
                return origin.position;

            return transform.position;
        }
    }

    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - OriginPosition;

        int x = Mathf.FloorToInt(local.x / cellSize);
        int y = Mathf.FloorToInt(local.y / cellSize);

        return new Vector2Int(x, y);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return OriginPosition + new Vector3(
            (cell.x + 0.5f) * cellSize,
            (cell.y + 0.5f) * cellSize,
            0f
        );
    }

    public bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 &&
               cell.x < width &&
               cell.y >= 0 &&
               cell.y < height;
    }

    public bool IsOccupied(Vector2Int cell)
    {
        return cells.ContainsKey(cell);
    }

    public bool CanPlaceOffsets(Vector2Int pivotCell, Vector2Int[] offsets)
    {
        if (offsets == null)
            return false;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int cell = pivotCell + offsets[i];

            if (!IsInside(cell))
                return false;

            if (IsOccupied(cell))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Регистрирует уже существующую ячейку в указанной клетке сетки.
    /// Если клетка занята другой — старая ячейка будет уничтожена и заменена.
    /// </summary>
    public void RegisterCell(Vector2Int cell, TetrisPlacedCell placedCell)
    {
        if (placedCell == null)
            return;

        if (!IsInside(cell))
            return;

        if (cells.TryGetValue(cell, out TetrisPlacedCell existing) && existing != null && existing != placedCell)
            Destroy(existing.gameObject);

        cells[cell] = placedCell;
        placedCell.transform.position = CellToWorld(cell);
    }

    /// <summary>
    /// Прогоняет схлопывание одинаковых соседних цветов до полной стабилизации,
    /// с применением «гравитации» после каждого прохода.
    /// </summary>
    public void ResolveMatches()
    {
        const int safetyLimit = 64;

        int iterations = 0;

        while (iterations < safetyLimit)
        {
            iterations++;

            HashSet<Vector2Int> toRemove = FindCellsToRemove();

            if (toRemove == null || toRemove.Count == 0)
                break;

            foreach (Vector2Int cell in toRemove)
            {
                if (!cells.TryGetValue(cell, out TetrisPlacedCell placed))
                    continue;

                if (placed != null)
                    Destroy(placed.gameObject);

                cells.Remove(cell);
            }

            ApplyGravity();
        }
    }

    private HashSet<Vector2Int> FindCellsToRemove()
    {
        // Сначала ищем пары "ячейка из блока A соприкасается с ячейкой из блока B,
        // оба одного цвета, но это разные блоки" — такие два блока полностью
        // исчезают целиком. Ячейки внутри одного блока (общий BlockId) друг
        // друга не уничтожают, поэтому свежепоставленный блок просто стоит,
        // когда касается пола или блоков чужих цветов.
        HashSet<int> blocksToRemove = null;

        foreach (KeyValuePair<Vector2Int, TetrisPlacedCell> kvp in cells)
        {
            TetrisPlacedCell cell = kvp.Value;

            if (cell == null)
                continue;

            int colorIndex = cell.ColorIndex;
            int blockId = cell.BlockId;

            for (int n = 0; n < FourNeighbors.Length; n++)
            {
                Vector2Int neighborPos = kvp.Key + FourNeighbors[n];

                if (!cells.TryGetValue(neighborPos, out TetrisPlacedCell neighbor))
                    continue;

                if (neighbor == null)
                    continue;

                if (neighbor.ColorIndex != colorIndex)
                    continue;

                if (neighbor.BlockId == blockId)
                    continue;

                if (blocksToRemove == null)
                    blocksToRemove = new HashSet<int>();

                blocksToRemove.Add(blockId);
                blocksToRemove.Add(neighbor.BlockId);
            }
        }

        if (blocksToRemove == null || blocksToRemove.Count == 0)
            return null;

        HashSet<Vector2Int> toRemove = new HashSet<Vector2Int>();

        foreach (KeyValuePair<Vector2Int, TetrisPlacedCell> kvp in cells)
        {
            TetrisPlacedCell cell = kvp.Value;

            if (cell == null)
                continue;

            if (blocksToRemove.Contains(cell.BlockId))
                toRemove.Add(kvp.Key);
        }

        return toRemove;
    }

    /// <summary>
    /// Поячеечная гравитация: каждая колонка сжимается вниз так, чтобы
    /// «висящие в воздухе» ячейки упали на самый низ или на ячейки под ними.
    /// </summary>
    public void ApplyGravity()
    {
        for (int x = 0; x < width; x++)
        {
            List<TetrisPlacedCell> column = new List<TetrisPlacedCell>(height);

            for (int y = 0; y < height; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);

                if (!cells.TryGetValue(pos, out TetrisPlacedCell placed))
                    continue;

                column.Add(placed);
                cells.Remove(pos);
            }

            for (int i = 0; i < column.Count; i++)
            {
                Vector2Int newPos = new Vector2Int(x, i);
                cells[newPos] = column[i];

                if (column[i] != null)
                    column[i].transform.position = CellToWorld(newPos);
            }
        }
    }
}
