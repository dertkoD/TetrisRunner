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

    [Header("Placed Blocks")]
    [Tooltip("Куда складываются залоченные блоки. Если не задано, будет создан " +
             "дочерний объект 'PlacedBlocks' автоматически.")]
    [SerializeField] private Transform placedBlocksParent;

    /// <summary>cell -> блок, который занимает эту клетку.</summary>
    private readonly Dictionary<Vector2Int, TetrisPlacedBlock> cellsToBlock = new Dictionary<Vector2Int, TetrisPlacedBlock>();

    private static int nextBlockId;

    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;

    /// <summary>Возвращает уникальный идентификатор блока (используется при локе).</summary>
    public static int AllocateBlockId()
    {
        nextBlockId++;
        return nextBlockId;
    }

    public Transform PlacedBlocksParent
    {
        get
        {
            if (placedBlocksParent == null)
            {
                GameObject host = new GameObject("PlacedBlocks");
                host.transform.SetParent(transform, false);
                host.transform.localPosition = Vector3.zero;
                host.transform.localRotation = Quaternion.identity;
                host.transform.localScale = Vector3.one;
                placedBlocksParent = host.transform;
            }

            return placedBlocksParent;
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
        return cellsToBlock.ContainsKey(cell);
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

    /// <summary>Регистрирует уже расположенный блок: записывает все его клетки в карту.</summary>
    public void RegisterBlock(TetrisPlacedBlock block)
    {
        if (block == null)
            return;

        Vector2Int[] offsets = block.CellOffsets;
        if (offsets == null)
            return;

        Vector2Int pivot = block.PivotCell;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int cell = pivot + offsets[i];

            if (!IsInside(cell))
                continue;

            cellsToBlock[cell] = block;
        }
    }

    /// <summary>Снимает блок с карты сетки (но сам объект не уничтожает).</summary>
    public void UnregisterBlock(TetrisPlacedBlock block)
    {
        if (block == null)
            return;

        // Удаляем все клетки, которые принадлежат именно этому блоку (а не просто
        // совпадают по позициям) — это надёжно даже если кто-то переехал поверх.
        List<Vector2Int> toRemove = null;

        foreach (KeyValuePair<Vector2Int, TetrisPlacedBlock> kvp in cellsToBlock)
        {
            if (kvp.Value != block)
                continue;

            if (toRemove == null)
                toRemove = new List<Vector2Int>(8);

            toRemove.Add(kvp.Key);
        }

        if (toRemove == null)
            return;

        for (int i = 0; i < toRemove.Count; i++)
            cellsToBlock.Remove(toRemove[i]);
    }

    /// <summary>
    /// Прогоняет цикл "схлопнули блоки → уронили висящие → проверили снова" до
    /// полной стабилизации. Схлопывание срабатывает только между разными блоками
    /// одного цвета; внутри блока его собственные ячейки друг друга не уничтожают.
    /// Блоки сохраняют свою форму, гравитация перемещает их целиком.
    /// </summary>
    public void ResolveMatches()
    {
        const int safetyLimit = 64;
        int iterations = 0;

        while (iterations < safetyLimit)
        {
            iterations++;

            HashSet<TetrisPlacedBlock> blocksToRemove = FindMatchingBlocks();

            if (blocksToRemove == null || blocksToRemove.Count == 0)
                break;

            foreach (TetrisPlacedBlock block in blocksToRemove)
            {
                if (block == null)
                    continue;

                UnregisterBlock(block);
                Destroy(block.gameObject);
            }

            ApplyGravity();
        }
    }

    private HashSet<TetrisPlacedBlock> FindMatchingBlocks()
    {
        HashSet<TetrisPlacedBlock> blocksToRemove = null;

        foreach (KeyValuePair<Vector2Int, TetrisPlacedBlock> kvp in cellsToBlock)
        {
            TetrisPlacedBlock block = kvp.Value;

            if (block == null)
                continue;

            for (int n = 0; n < FourNeighbors.Length; n++)
            {
                Vector2Int neighborPos = kvp.Key + FourNeighbors[n];

                if (!cellsToBlock.TryGetValue(neighborPos, out TetrisPlacedBlock neighbor))
                    continue;

                if (neighbor == null)
                    continue;

                if (neighbor.BlockId == block.BlockId)
                    continue;

                if (neighbor.ColorIndex != block.ColorIndex)
                    continue;

                if (blocksToRemove == null)
                    blocksToRemove = new HashSet<TetrisPlacedBlock>();

                blocksToRemove.Add(block);
                blocksToRemove.Add(neighbor);
            }
        }

        return blocksToRemove;
    }

    /// <summary>
    /// Гравитация на уровне БЛОКОВ: каждый блок пытается опуститься на одну клетку
    /// вниз, но только если все его клетки в новой позиции свободны и в пределах
    /// сетки. Повторяем, пока хоть кто-то двигается. Форма блоков не нарушается.
    /// </summary>
    public void ApplyGravity()
    {
        int safety = (width + 1) * (height + 1);

        while (safety-- > 0)
        {
            List<TetrisPlacedBlock> blocks = CollectUniqueBlocksOrderedByLowestCell();

            bool anyMoved = false;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (TryDropBlockOneStep(blocks[i]))
                    anyMoved = true;
            }

            if (!anyMoved)
                break;
        }
    }

    private List<TetrisPlacedBlock> CollectUniqueBlocksOrderedByLowestCell()
    {
        HashSet<TetrisPlacedBlock> seen = new HashSet<TetrisPlacedBlock>();
        List<TetrisPlacedBlock> blocks = new List<TetrisPlacedBlock>();

        foreach (TetrisPlacedBlock block in cellsToBlock.Values)
        {
            if (block == null)
                continue;

            if (!seen.Add(block))
                continue;

            blocks.Add(block);
        }

        // Сортируем "снизу вверх", чтобы нижние блоки падали первыми
        // и освобождали место для тех, кто над ними.
        blocks.Sort((a, b) => GetLowestCellY(a).CompareTo(GetLowestCellY(b)));
        return blocks;
    }

    private static int GetLowestCellY(TetrisPlacedBlock block)
    {
        int min = int.MaxValue;
        Vector2Int[] offsets = block.CellOffsets;

        if (offsets == null)
            return 0;

        Vector2Int pivot = block.PivotCell;

        for (int i = 0; i < offsets.Length; i++)
        {
            int y = pivot.y + offsets[i].y;
            if (y < min) min = y;
        }

        return min == int.MaxValue ? 0 : min;
    }

    private bool TryDropBlockOneStep(TetrisPlacedBlock block)
    {
        Vector2Int[] offsets = block.CellOffsets;
        if (offsets == null || offsets.Length == 0)
            return false;

        Vector2Int newPivot = block.PivotCell + Vector2Int.down;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int newCell = newPivot + offsets[i];

            if (!IsInside(newCell))
                return false;

            if (cellsToBlock.TryGetValue(newCell, out TetrisPlacedBlock occupant)
                && occupant != null
                && occupant != block)
            {
                return false;
            }
        }

        UnregisterBlock(block);
        block.MoveToCell(newPivot, CellToWorld(newPivot));
        RegisterBlock(block);
        return true;
    }
}
