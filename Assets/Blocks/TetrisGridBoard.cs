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

    [Header("Fall Animation")]
    [Tooltip("Скорость падения блоков при гравитации (мировых единиц в секунду). " +
             "0 — мгновенный телепорт на новое место (старое поведение).")]
    [SerializeField, Min(0f)] private float fallAnimationSpeed = 12f;

    /// <summary>cell -> блок, который занимает эту клетку.</summary>
    private readonly Dictionary<Vector2Int, TetrisPlacedBlock> cellsToBlock = new Dictionary<Vector2Int, TetrisPlacedBlock>();

    private static int nextBlockId;

    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;
    public float FallAnimationSpeed => fallAnimationSpeed;

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

    /// <summary>
    /// Возвращает номер самой верхней строки сетки, центр которой не
    /// поднимается выше <paramref name="worldY"/>. Иначе говоря — индекс
    /// последнего ряда, который полностью «накрыт» уровнем по высоте
    /// <paramref name="worldY"/>. Возвращает int.MinValue, если выше клетки 0
    /// (т.е. вода ещё не доросла до сетки).
    /// </summary>
    public int GetHighestRowAtOrBelowWorldY(float worldY)
    {
        float originY = OriginPosition.y;
        float t = (worldY - originY) / cellSize - 0.5f;
        return Mathf.FloorToInt(t);
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

    /// <summary>
    /// Возвращает блок, который занимает клетку <paramref name="cell"/>, или
    /// null, если клетка пуста.
    /// </summary>
    public TetrisPlacedBlock GetBlockAt(Vector2Int cell)
    {
        cellsToBlock.TryGetValue(cell, out TetrisPlacedBlock block);
        return block;
    }

    /// <summary>
    /// Y самой верхней занятой клетки на доске. Возвращает -1, если ни одна
    /// клетка не занята. Используется, чтобы понять, не «доехала» ли стопка
    /// блоков до уровня спавна — тогда уровень нужно перезапустить.
    /// </summary>
    public int GetHighestOccupiedRow()
    {
        int highest = -1;

        foreach (Vector2Int cell in cellsToBlock.Keys)
        {
            if (cell.y > highest)
                highest = cell.y;
        }

        return highest;
    }

    /// <summary>
    /// True, если хотя бы одна занятая клетка имеет Y &gt;= row. Используется как
    /// быстрый «game-over check»: стопка блоков доросла до строки row.
    /// </summary>
    public bool HasOccupiedCellAtOrAbove(int row)
    {
        foreach (Vector2Int cell in cellsToBlock.Keys)
        {
            if (cell.y >= row)
                return true;
        }

        return false;
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

    /// <summary>
    /// Регистрирует набор клеток как статическое препятствие (платформа и т.п.).
    /// Создаёт служебный TetrisPlacedBlock, который занимает клетки, но не
    /// падает по гравитации и не участвует в схлопывании по цвету.
    /// Возвращает созданный объект, чтобы при необходимости его можно было
    /// убрать обратно через UnregisterBlock + Destroy.
    /// </summary>
    public TetrisPlacedBlock RegisterStaticCells(IList<Vector2Int> cells, string name = "StaticBlock")
    {
        if (cells == null || cells.Count == 0)
            return null;

        Vector2Int pivot = cells[0];
        Vector2Int[] offsets = new Vector2Int[cells.Count];

        for (int i = 0; i < cells.Count; i++)
            offsets[i] = cells[i] - pivot;

        GameObject host = new GameObject(name);
        host.transform.SetParent(PlacedBlocksParent, false);
        host.transform.position = CellToWorld(pivot);

        TetrisPlacedBlock placedBlock = host.AddComponent<TetrisPlacedBlock>();
        // colorIndex = -1 — не совпадёт ни с одним цветом из палитры.
        placedBlock.Initialize(AllocateBlockId(), -1, pivot, offsets);
        placedBlock.MarkAsStatic();

        RegisterBlock(placedBlock);

        return placedBlock;
    }

    /// <summary>
    /// Пробует сдвинуть статический блок (например, движущуюся платформу) на delta клеток,
    /// захватив с собой стопку блоков, опирающихся на него (или друг на друга, и в итоге на него).
    ///
    /// Возвращает true, если перемещение удалось. Если на пути есть препятствие или
    /// край сетки — возвращает false и сетка остаётся в исходном состоянии.
    /// </summary>
    public bool TryMovePlacedBlockWithStack(TetrisPlacedBlock platform, Vector2Int delta)
    {
        if (platform == null)
            return false;

        if (delta == Vector2Int.zero)
            return true;

        HashSet<TetrisPlacedBlock> moving = CollectCarryStack(platform);

        if (moving == null || moving.Count == 0)
            return false;

        // Проверка валидности: каждая клетка набора в новой позиции должна
        // быть внутри сетки и либо пуста, либо занята другим перемещаемым блоком.
        foreach (TetrisPlacedBlock block in moving)
        {
            Vector2Int[] offsets = block.CellOffsets;
            if (offsets == null) continue;

            Vector2Int newPivot = block.PivotCell + delta;

            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2Int newCell = newPivot + offsets[i];

                if (!IsInside(newCell))
                    return false;

                if (cellsToBlock.TryGetValue(newCell, out TetrisPlacedBlock occupant)
                    && occupant != null
                    && !moving.Contains(occupant))
                {
                    return false;
                }
            }
        }

        // Атомарно: сначала снимаем со всех клеток, потом перемещаем и
        // регистрируем заново — иначе соседние блоки внутри moving могли бы
        // самозаблокироваться.
        foreach (TetrisPlacedBlock block in moving)
            UnregisterBlock(block);

        foreach (TetrisPlacedBlock block in moving)
        {
            Vector2Int newPivot = block.PivotCell + delta;
            block.MoveToCell(newPivot, CellToWorld(newPivot));
            RegisterBlock(block);
        }

        return true;
    }

    /// <summary>
    /// Возвращает блок и всё, что на нём (рекурсивно). Удобно для предпросмотра,
    /// какие именно блоки уедут вместе с платформой при следующем шаге.
    /// </summary>
    public HashSet<TetrisPlacedBlock> GetCarryStack(TetrisPlacedBlock root)
    {
        return CollectCarryStack(root);
    }

    /// <summary>
    /// Собирает блок и всё, что на нём (рекурсивно). "Опирается" = у блока есть
    /// клетка, прямо под которой стоит клетка из текущего набора.
    /// </summary>
    private HashSet<TetrisPlacedBlock> CollectCarryStack(TetrisPlacedBlock root)
    {
        HashSet<TetrisPlacedBlock> set = new HashSet<TetrisPlacedBlock>();
        if (root == null) return set;

        set.Add(root);

        bool changed = true;
        List<TetrisPlacedBlock> snapshot = new List<TetrisPlacedBlock>();

        while (changed)
        {
            changed = false;
            snapshot.Clear();
            snapshot.AddRange(set);

            for (int s = 0; s < snapshot.Count; s++)
            {
                TetrisPlacedBlock baseBlock = snapshot[s];
                Vector2Int[] offsets = baseBlock.CellOffsets;
                if (offsets == null) continue;

                for (int i = 0; i < offsets.Length; i++)
                {
                    Vector2Int aboveCell = baseBlock.PivotCell + offsets[i] + Vector2Int.up;

                    if (!cellsToBlock.TryGetValue(aboveCell, out TetrisPlacedBlock aboveBlock))
                        continue;

                    if (aboveBlock == null || aboveBlock == baseBlock)
                        continue;

                    // Другие статические блоки (другие платформы, стены и т.п.)
                    // не подцепляем: они либо вообще не двигаются, либо двигаются
                    // своей собственной логикой.
                    if (aboveBlock.IsStatic)
                        continue;

                    // Закреплённые блоки уровня тоже неподвижны и не должны
                    // ехать вместе с платформой.
                    if (aboveBlock.IsAnchored)
                        continue;

                    if (set.Add(aboveBlock))
                        changed = true;
                }
            }
        }

        return set;
    }

    /// <summary>
    /// Убирает из сетки все занятые клетки в указанном диапазоне строк
    /// [minRow..maxRow] (включительно). Если у какого-то блока удалили все
    /// его клетки — блок-объект уничтожается. Возвращает true, если хотя
    /// бы одна клетка действительно была эродирована.
    /// </summary>
    public bool EraseCellsInRowRange(int minRow, int maxRow)
    {
        if (maxRow < minRow)
            return false;

        // Группируем удаляемые клетки по блокам, чтобы за один проход
        // убрать у каждого блока все «утонувшие» клетки и при необходимости
        // уничтожить пустой объект целиком.
        Dictionary<TetrisPlacedBlock, List<Vector2Int>> blocksToCells = null;

        foreach (KeyValuePair<Vector2Int, TetrisPlacedBlock> kvp in cellsToBlock)
        {
            if (kvp.Key.y < minRow || kvp.Key.y > maxRow)
                continue;

            if (kvp.Value == null)
                continue;

            // Статические платформы (земля, движущиеся платформы и т.п.)
            // в воде не разрушаются — это часть геометрии уровня, у них
            // даже цвета нет. Иначе игрок терял бы пол.
            if (kvp.Value.IsStatic)
                continue;

            if (blocksToCells == null)
                blocksToCells = new Dictionary<TetrisPlacedBlock, List<Vector2Int>>();

            if (!blocksToCells.TryGetValue(kvp.Value, out List<Vector2Int> list))
            {
                list = new List<Vector2Int>();
                blocksToCells[kvp.Value] = list;
            }

            list.Add(kvp.Key);
        }

        if (blocksToCells == null)
            return false;

        foreach (KeyValuePair<TetrisPlacedBlock, List<Vector2Int>> pair in blocksToCells)
        {
            TetrisPlacedBlock block = pair.Key;
            List<Vector2Int> cells = pair.Value;

            for (int i = 0; i < cells.Count; i++)
                cellsToBlock.Remove(cells[i]);

            int remaining = block != null ? block.RemoveCellsAtWorldCells(cells) : 0;

            if (remaining <= 0 && block != null)
                Destroy(block.gameObject);
        }

        return true;
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
    /// Снимает блок с карты сетки и тут же пытается уронить всё, что на нём
    /// держалось. Нужен внешним «опорам» (статические платформы, двери, лифты,
    /// KillBlock и т.п.), которые исчезают по своим причинам и без этого вызова
    /// оставляли бы блоки игрока висеть в воздухе. Этот же путь корректно
    /// «дораскладывает» сетку после исчезновения опоры, даже если матчинг
    /// при этом не сработал.
    /// </summary>
    public void UnregisterBlockAndDropAbove(TetrisPlacedBlock block)
    {
        if (block == null)
            return;

        UnregisterBlock(block);
        ApplyGravity();
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

            // Статические блоки (платформы и т.п.) в матчинге не участвуют:
            // они не имеют цвета и должны просто служить препятствием.
            if (block.IsStatic)
                continue;

            for (int n = 0; n < FourNeighbors.Length; n++)
            {
                Vector2Int neighborPos = kvp.Key + FourNeighbors[n];

                if (!cellsToBlock.TryGetValue(neighborPos, out TetrisPlacedBlock neighbor))
                    continue;

                if (neighbor == null)
                    continue;

                if (neighbor.IsStatic)
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
        // Статические блоки (платформы из сцены) висят там, где их поставили.
        if (block.IsStatic)
            return false;

        // Закреплённые блоки уровня тоже остаются на своих клетках: если в
        // конструкции образовалась дыра после матчинга, остальные блоки уровня
        // НЕ должны сыпаться вниз и заваливать структуру.
        if (block.IsAnchored)
            return false;

        Vector2Int[] offsets = block.CellOffsets;
        if (offsets == null || offsets.Length == 0)
            return false;

        Vector2Int newPivot = block.PivotCell + Vector2Int.down;
        bool anyBelowBoard = false;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int newCell = newPivot + offsets[i];

            if (!IsInside(newCell))
            {
                // Сегмент уходит за нижний край сетки — отметим, но проверим
                // остальные клетки: вдруг рядом обычная опора.
                if (newCell.y < 0)
                {
                    anyBelowBoard = true;
                    continue;
                }
                return false;
            }

            if (cellsToBlock.TryGetValue(newCell, out TetrisPlacedBlock occupant)
                && occupant != null
                && occupant != block)
            {
                return false;
            }
        }

        if (anyBelowBoard)
        {
            // Под блоком ничего нет, и часть клеток уже за нижней границей —
            // блок проваливается. Снимаем с сетки и запускаем последнюю
            // анимацию падения «в никуда», по её завершении GameObject
            // уничтожится. Гравитация других блоков на следующих итерациях
            // увидит освобождённые клетки.
            UnregisterBlock(block);
            block.SetLogicalCell(newPivot);

            Vector3 belowGridWorld = CellToWorld(newPivot) + Vector3.down * cellSize * 1.5f;
            block.BeginAnimatedMoveTo(belowGridWorld, fallAnimationSpeed);
            block.SetAnimationDestroyOnEnd(true);
            return true;
        }

        // Обычное падение на одну клетку с анимацией.
        UnregisterBlock(block);
        block.MoveToCellAnimated(newPivot, CellToWorld(newPivot), fallAnimationSpeed);
        RegisterBlock(block);
        return true;
    }
}
