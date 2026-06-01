using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обрабатывает блоки уровня, расставленные вручную в сцене как дети этого
/// объекта (например, "Blocks"). Для каждого такого блока:
///   * вычисляет, какие клетки сетки он занимает, исходя из его текущего
///     положения и поворота в сцене;
///   * привязывает блок к ближайшей клетке (snap к сетке);
///   * раскрашивает все блоки так, чтобы любые два соседних по сетке блока
///     имели разные цвета (жадная раскраска графа соседства); конкретные
///     цвета выбираются случайно при каждом запуске/перезапуске уровня,
///     поэтому одна и та же геометрия каждый раз выглядит по-разному;
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

            // Снап по ВИДИМОМУ спрайту: двигаем блок так, чтобы центр габаритов
            // его формы (а значит и центр спрайта) попал ровно в сетку, а угол —
            // на кратный 90°. Это совпадает с тем, как блок встаёт в рантайме.
            int steps = ComputeRotationSteps(child);
            Vector2Int[] canonical = cells.GetStartOffsetsCopy();
            if (canonical == null || canonical.Length == 0)
                continue;

            Vector2Int[] rotated = ApplyRotationSteps(canonical, steps);
            Vector2 bboxCenter = ComputeBboxCenter(rotated);

            Transform spriteTransform = cells.ResolveMainRendererTransform();
            Vector3 spriteWorld = spriteTransform != null ? spriteTransform.position : child.position;

            Vector3 pivotWorld = spriteWorld - new Vector3(bboxCenter.x, bboxCenter.y, 0f) * board.CellSize;
            Vector2Int pivot = RoundWorldToCell(pivotWorld);

            Vector3 targetSpriteWorld = board.CellToWorld(pivot)
                + new Vector3(bboxCenter.x, bboxCenter.y, 0f) * board.CellSize;

            child.position += targetSpriteWorld - spriteWorld;
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

            // Источник истины — где стоит ВИДИМЫЙ спрайт блока (Rendering) и его
            // каноническая форма (startOffsets), повёрнутая на угол корня.
            // Раньше форма бралась из позиций поячеечных визуалов, но теперь
            // ячейки не рисуются и стоят на «мусорных» локальных позициях, не
            // совпадающих с тем, что видит игрок, — поэтому уровень собирался
            // криво. Теперь pivot вычисляется так, чтобы центр габаритов формы
            // совпал с центром спрайта, который игрок и выравнивает.
            int rotationSteps = ComputeRotationSteps(child);
            Vector2Int[] canonical = cells.GetStartOffsetsCopy();

            Vector2Int[] offsets;
            Vector2Int pivot;

            if (canonical != null && canonical.Length > 0)
            {
                offsets = ApplyRotationSteps(canonical, rotationSteps);

                Vector2 bboxCenter = ComputeBboxCenter(offsets);

                Transform spriteTransform = cells.ResolveMainRendererTransform();
                Vector3 spriteWorld = spriteTransform != null ? spriteTransform.position : child.position;

                Vector3 pivotWorld = spriteWorld - new Vector3(bboxCenter.x, bboxCenter.y, 0f) * board.CellSize;
                pivot = RoundWorldToCell(pivotWorld);
            }
            else if (!TryResolveCellsFromVisuals(cells, out offsets, out pivot))
            {
                offsets = new Vector2Int[0];
                pivot = board.WorldToCell(child.position);
            }

            entries.Add(new BlockEntry
            {
                cells = cells,
                offsets = offsets,
                pivot = pivot,
                rotationSteps = rotationSteps,
            });
        }

        return entries;
    }

    /// <summary>
    /// Считывает текущие мировые позиции cellVisuals блока, превращает их в
    /// клетки сетки и выбирает первую из них в качестве pivot. Возвращает
    /// false, если у блока нет cellVisuals — тогда вызывающий код должен
    /// откатиться к startOffsets + поворот.
    /// </summary>
    private bool TryResolveCellsFromVisuals(TetrisBlockCells cells, out Vector2Int[] offsets, out Vector2Int pivot)
    {
        offsets = null;
        pivot = Vector2Int.zero;

        Transform[] visuals = cells != null ? cells.CellVisuals : null;
        if (visuals == null || visuals.Length == 0)
            return false;

        List<Vector2Int> uniqueCells = new List<Vector2Int>(visuals.Length);
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();

        for (int i = 0; i < visuals.Length; i++)
        {
            Transform visual = visuals[i];
            if (visual == null || !visual.gameObject.activeInHierarchy)
                continue;

            Vector2Int cell = board.WorldToCell(visual.position);

            if (seen.Add(cell))
                uniqueCells.Add(cell);
        }

        if (uniqueCells.Count == 0)
            return false;

        pivot = uniqueCells[0];

        offsets = new Vector2Int[uniqueCells.Count];
        for (int i = 0; i < uniqueCells.Count; i++)
            offsets[i] = uniqueCells[i] - pivot;

        return true;
    }

    private List<int> ValidateAndReserveCells(List<BlockEntry> entries, Dictionary<Vector2Int, int> occupancy)
    {
        List<int> accepted = new List<int>();

        for (int idx = 0; idx < entries.Count; idx++)
        {
            BlockEntry e = entries[idx];

            if (e.offsets == null || e.offsets.Length == 0)
                continue;

            string failReason = null;

            for (int i = 0; i < e.offsets.Length; i++)
            {
                Vector2Int cell = e.pivot + e.offsets[i];

                if (!board.IsInside(cell))
                {
                    failReason = $"клетка {cell} вне границ сетки";
                    break;
                }

                if (board.IsOccupied(cell))
                {
                    failReason = $"клетка {cell} уже занята другим объектом сетки";
                    break;
                }

                if (occupancy.TryGetValue(cell, out int otherIdx))
                {
                    string otherName = entries[otherIdx].cells != null
                        ? entries[otherIdx].cells.name
                        : "?";
                    failReason = $"клетка {cell} налезает на блок '{otherName}'";
                    break;
                }
            }

            if (failReason != null)
            {
                Debug.LogWarning(
                    $"{nameof(TetrisGridLevelBlocks)}: блок '{e.cells.name}' (pivot={e.pivot}) " +
                    $"не будет зарегистрирован: {failReason}.",
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

            // Инициализируем КАНОНИЧЕСКОЙ формой (как в префабе) — чтобы спрайт
            // встал по центру и корректно посчитался домашний поворот,
            // а затем применяем постановочный поворот: и offsets, и спрайт
            // довернутся одинаково, повторяя то, что видно в редакторе.
            cells.Initialize(board.CellSize, palette, assignRandomColors: false);
            cells.ApplyPlacementRotation(e.rotationSteps, board.CellSize);
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

        // Сначала сортируем по убыванию степени (Welsh-Powell): чем больше у
        // блока соседей, тем раньше его красим — иначе можно «застрять» в
        // конце с блоком, у которого все соседи уже забрали палитру. Среди
        // блоков с одинаковой степенью порядок выбираем случайно — это даёт
        // разную раскраску на каждом перезапуске уровня.
        List<int> ordered = new List<int>(accepted);
        ShuffleInPlace(ordered);
        ordered.Sort((a, b) => adjacency[b].Count.CompareTo(adjacency[a].Count));

        Dictionary<int, int> assignedColor = new Dictionary<int, int>();
        List<int> candidates = paletteSize > 0 ? new List<int>(paletteSize) : null;

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
                candidates.Clear();
                for (int c = 0; c < paletteSize; c++)
                {
                    if (!usedByNeighbors.Contains(c))
                        candidates.Add(c);
                }

                if (candidates.Count > 0)
                {
                    // Выбираем случайный цвет из тех, что не используются соседями —
                    // главное правило (никаких одинаковых цветов рядом) сохраняется,
                    // но при каждом перезапуске уровня раскраска получается другая.
                    chosen = candidates[Random.Range(0, candidates.Count)];
                }
                else
                {
                    // Если соседи покрыли всю палитру (палитра меньше количества
                    // соседей), всё равно берём какой-то цвет — лучше визуальная
                    // путаница, чем отказ от регистрации.
                    chosen = Random.Range(0, paletteSize);
                }
            }
            else
            {
                chosen = 0;
            }

            assignedColor[idx] = chosen;
        }

        return assignedColor;
    }

    private static void ShuffleInPlace(List<int> list)
    {
        if (list == null)
            return;

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            if (j == i) continue;
            int tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
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

    private static Vector2 ComputeBboxCenter(Vector2Int[] offsets)
    {
        if (offsets == null || offsets.Length == 0)
            return Vector2.zero;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int o = offsets[i];
            if (o.x < minX) minX = o.x;
            if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;
            if (o.y > maxY) maxY = o.y;
        }

        return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
    }

    /// <summary>
    /// Округляет мировую точку до ближайшей КЛЕТКИ (по центрам клеток), а не
    /// «вниз», как <see cref="TetrisGridBoard.WorldToCell"/>. Так блок ловит
    /// нужную клетку, даже если спрайт стоит не идеально по сетке.
    /// </summary>
    private Vector2Int RoundWorldToCell(Vector3 world)
    {
        Vector3 cellZero = board.CellToWorld(Vector2Int.zero);
        float cellSize = board.CellSize;

        if (cellSize <= 0f)
            return board.WorldToCell(world);

        int x = Mathf.RoundToInt((world.x - cellZero.x) / cellSize);
        int y = Mathf.RoundToInt((world.y - cellZero.y) / cellSize);
        return new Vector2Int(x, y);
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
