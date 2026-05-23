using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обрабатывает блоки уровня, расставленные вручную в сцене как дети этого
/// объекта (например, "Blocks"). Для каждого такого блока:
///   * вычисляет, какие клетки сетки он занимает, исходя из его текущего
///     положения и поворота в сцене;
///   * привязывает блок к ближайшей клетке (snap к сетке);
///   * раскрашивает все блоки так, чтобы любые два соседних по сетке блока
///     имели разные цвета (жадная раскраска графа соседства);
///   * регистрирует блок в <see cref="TetrisGridBoard"/> как закреплённый
///     (anchored) TetrisPlacedBlock — он участвует в схлопывании по цвету,
///     но не падает по гравитации, поэтому конструкция уровня сохраняется.
/// </summary>
[DisallowMultipleComponent]
public class TetrisGridLevelBlocks : MonoBehaviour
{
    private static readonly Vector2Int[] FourNeighbors =
    {
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1),
    };

    [Header("References")]
    [Tooltip("Сетка, в которую регистрируются блоки. Если не задано — будет найдена в сцене.")]
    [SerializeField] private TetrisGridBoard board;

    [Tooltip("Конфиг тетриса, откуда берётся палитра цветов. Если не задано — будет использована " +
             "белая палитра.")]
    [SerializeField] private TetrisBlockConfigSO config;

    [Header("Behaviour")]
    [Tooltip("Если блок выходит за пределы сетки или налезает на занятую клетку, его GameObject " +
             "будет деактивирован. Без этой опции такой блок просто не регистрируется.")]
    [SerializeField] private bool disableInvalidBlocks = true;

    [Tooltip("Если true, к блоку добавляется TetrisPlacedBlock с пометкой Anchored. " +
             "Anchored-блоки не падают по гравитации, но при попадании рядом блока такого же " +
             "цвета — исчезают вместе с ним.")]
    [SerializeField] private bool registerAsAnchored = true;

    private void Awake()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();
    }

    private void Start()
    {
        if (board == null)
        {
            Debug.LogWarning($"{nameof(TetrisGridLevelBlocks)}: TetrisGridBoard не найден в сцене.", this);
            return;
        }

        BuildLevel();
    }

    /// <summary>
    /// Снимает все дочерние блоки уровня, привязывает их к ближайшим клеткам
    /// сетки, раскрашивает по графу соседства и регистрирует в TetrisGridBoard.
    /// </summary>
    public void BuildLevel()
    {
        Color[] palette = (config != null && config.CellColorPalette != null && config.CellColorPalette.Length > 0)
            ? config.CellColorPalette
            : null;

        int paletteSize = palette != null ? palette.Length : 0;

        List<BlockEntry> entries = CollectBlockEntries();

        Dictionary<Vector2Int, int> occupancy = new Dictionary<Vector2Int, int>();
        List<int> accepted = ValidateAndReserveCells(entries, occupancy);

        InitializeAcceptedBlocks(entries, accepted, palette);

        Dictionary<int, int> assignedColor = AssignColors(entries, accepted, occupancy, paletteSize);

        RegisterBlocks(entries, accepted, assignedColor);
    }

    /// <summary>
    /// Кнопка для редактора: явно «выровнять» все дочерние блоки в позиции
    /// центров клеток сетки (без запуска сцены). Сам цвет НЕ красится — это
    /// произойдёт в Play Mode при <see cref="BuildLevel"/>.
    /// </summary>
    [ContextMenu("Snap Level Blocks To Grid")]
    public void EditorSnapToGrid()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        if (board == null)
        {
            Debug.LogWarning($"{nameof(TetrisGridLevelBlocks)}: TetrisGridBoard не найден в сцене.", this);
            return;
        }

        int snapped = 0;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
                continue;

            TetrisBlockCells cells = child.GetComponent<TetrisBlockCells>();
            if (cells == null)
                continue;

            Vector2Int pivot = board.WorldToCell(child.position);
            Vector3 snapPos = board.CellToWorld(pivot);
            child.position = snapPos;

            float angle = NormalizeAngle(child.eulerAngles.z);
            int steps = Mathf.RoundToInt(angle / 90f) & 3;
            child.rotation = Quaternion.Euler(0f, 0f, steps * 90f);

            snapped++;
        }

        Debug.Log($"{nameof(TetrisGridLevelBlocks)}: snapped {snapped} blocks to grid.", this);
    }

    private List<BlockEntry> CollectBlockEntries()
    {
        List<BlockEntry> entries = new List<BlockEntry>();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            TetrisBlockCells cells = child.GetComponent<TetrisBlockCells>();
            if (cells == null)
                continue;

            Vector2Int[] rawOffsets = cells.GetStartOffsetsCopy();
            int rotationSteps = ComputeRotationSteps(child);
            Vector2Int[] rotatedOffsets = ApplyRotationSteps(rawOffsets, rotationSteps);

            Vector2Int pivot = board.WorldToCell(child.position);

            entries.Add(new BlockEntry
            {
                cells = cells,
                offsets = rotatedOffsets,
                pivot = pivot,
                rotationSteps = rotationSteps,
            });
        }

        return entries;
    }

    private List<int> ValidateAndReserveCells(List<BlockEntry> entries, Dictionary<Vector2Int, int> occupancy)
    {
        List<int> accepted = new List<int>();

        for (int idx = 0; idx < entries.Count; idx++)
        {
            BlockEntry e = entries[idx];

            if (e.offsets == null || e.offsets.Length == 0)
                continue;

            bool ok = true;

            for (int i = 0; i < e.offsets.Length; i++)
            {
                Vector2Int cell = e.pivot + e.offsets[i];

                if (!board.IsInside(cell))
                {
                    ok = false;
                    break;
                }

                if (board.IsOccupied(cell))
                {
                    ok = false;
                    break;
                }

                if (occupancy.ContainsKey(cell))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
            {
                Debug.LogWarning(
                    $"{nameof(TetrisGridLevelBlocks)}: блок '{e.cells.name}' расположен вне сетки " +
                    $"или налезает на занятые клетки (pivot={e.pivot}) и не будет зарегистрирован.",
                    e.cells);

                if (disableInvalidBlocks)
                    e.cells.gameObject.SetActive(false);

                continue;
            }

            for (int i = 0; i < e.offsets.Length; i++)
                occupancy[e.pivot + e.offsets[i]] = idx;

            accepted.Add(idx);
        }

        return accepted;
    }

    private void InitializeAcceptedBlocks(List<BlockEntry> entries, List<int> accepted, Color[] palette)
    {
        for (int k = 0; k < accepted.Count; k++)
        {
            BlockEntry e = entries[accepted[k]];
            TetrisBlockCells cells = e.cells;

            cells.transform.position = board.CellToWorld(e.pivot);
            cells.transform.rotation = Quaternion.identity;
            cells.transform.localScale = Vector3.one;

            DisableInteractiveComponents(cells);
            PrepareRigidbody(cells);

            cells.OverrideStartOffsets(e.offsets);
            cells.Initialize(board.CellSize, palette, assignRandomColors: false);
        }
    }

    private Dictionary<int, int> AssignColors(
        List<BlockEntry> entries,
        List<int> accepted,
        Dictionary<Vector2Int, int> occupancy,
        int paletteSize)
    {
        Dictionary<int, HashSet<int>> adjacency = new Dictionary<int, HashSet<int>>();

        for (int k = 0; k < accepted.Count; k++)
            adjacency[accepted[k]] = new HashSet<int>();

        for (int k = 0; k < accepted.Count; k++)
        {
            int idx = accepted[k];
            BlockEntry e = entries[idx];

            for (int i = 0; i < e.offsets.Length; i++)
            {
                Vector2Int cell = e.pivot + e.offsets[i];

                for (int n = 0; n < FourNeighbors.Length; n++)
                {
                    Vector2Int neighborCell = cell + FourNeighbors[n];

                    if (!occupancy.TryGetValue(neighborCell, out int otherIdx))
                        continue;

                    if (otherIdx == idx)
                        continue;

                    adjacency[idx].Add(otherIdx);
                    adjacency[otherIdx].Add(idx);
                }
            }
        }

        // Welsh-Powell: сортируем по убыванию степени, чтобы максимально "тяжёлые"
        // вершины раскрашивались первыми.
        List<int> ordered = new List<int>(accepted);
        ordered.Sort((a, b) => adjacency[b].Count.CompareTo(adjacency[a].Count));

        Dictionary<int, int> assignedColor = new Dictionary<int, int>();

        foreach (int idx in ordered)
        {
            HashSet<int> usedByNeighbors = new HashSet<int>();

            foreach (int neighbor in adjacency[idx])
            {
                if (assignedColor.TryGetValue(neighbor, out int nc))
                    usedByNeighbors.Add(nc);
            }

            int chosen;

            if (paletteSize > 0)
            {
                chosen = 0;
                for (int c = 0; c < paletteSize; c++)
                {
                    if (!usedByNeighbors.Contains(c))
                    {
                        chosen = c;
                        break;
                    }
                }

                // Если соседи покрыли всю палитру (палитра меньше количества
                // соседей), всё равно берём 0 — лучше визуальная путаница, чем
                // отказ от регистрации.
            }
            else
            {
                chosen = 0;
            }

            assignedColor[idx] = chosen;
        }

        return assignedColor;
    }

    private void RegisterBlocks(List<BlockEntry> entries, List<int> accepted, Dictionary<int, int> assignedColor)
    {
        for (int k = 0; k < accepted.Count; k++)
        {
            int idx = accepted[k];
            BlockEntry e = entries[idx];
            TetrisBlockCells cells = e.cells;

            int color = assignedColor.TryGetValue(idx, out int c) ? c : 0;
            cells.SetUniformColorIndex(color);

            TetrisPlacedBlock placed = cells.GetComponent<TetrisPlacedBlock>();
            if (placed == null)
                placed = cells.gameObject.AddComponent<TetrisPlacedBlock>();

            int blockId = TetrisGridBoard.AllocateBlockId();
            placed.Initialize(blockId, color, e.pivot, e.offsets);

            if (registerAsAnchored)
                placed.MarkAsAnchored();

            board.RegisterBlock(placed);
        }
    }

    private static int ComputeRotationSteps(Transform t)
    {
        float angle = NormalizeAngle(t.eulerAngles.z);
        int steps = Mathf.RoundToInt(angle / 90f) & 3;
        return steps;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f) angle += 360f;
        return angle;
    }

    private static Vector2Int[] ApplyRotationSteps(Vector2Int[] offsets, int steps)
    {
        if (offsets == null || offsets.Length == 0)
            return new Vector2Int[0];

        steps &= 3;

        Vector2Int[] result = new Vector2Int[offsets.Length];

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int o = offsets[i];

            // Поворот на CCW: (x, y) -> (-y, x). Eulerz положительный = CCW.
            for (int s = 0; s < steps; s++)
                o = new Vector2Int(-o.y, o.x);

            result[i] = o;
        }

        return result;
    }

    private static void DisableInteractiveComponents(TetrisBlockCells cells)
    {
        // Контроллер и связанная с ним логика управляемого блока не нужны
        // для статичного блока уровня — отключаем, чтобы они не реагировали
        // на ввод или не пытались обрабатывать столкновения с игроком.
        TetrisBlockFacade facade = cells.GetComponent<TetrisBlockFacade>();
        if (facade != null)
        {
            if (facade.Controller != null) facade.Controller.enabled = false;
            if (facade.Movement != null) facade.Movement.enabled = false;
            if (facade.Rotator != null) facade.Rotator.enabled = false;
            if (facade.ContactReporter != null) facade.ContactReporter.enabled = false;
        }
    }

    private static void PrepareRigidbody(TetrisBlockCells cells)
    {
        Rigidbody2D body = cells.GetComponent<Rigidbody2D>();
        if (body == null)
            return;

        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.gravityScale = 0f;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.constraints = RigidbodyConstraints2D.FreezeAll;
        body.position = cells.transform.position;
        body.rotation = 0f;
    }

    private struct BlockEntry
    {
        public TetrisBlockCells cells;
        public Vector2Int[] offsets;
        public Vector2Int pivot;
        public int rotationSteps;
    }
}
