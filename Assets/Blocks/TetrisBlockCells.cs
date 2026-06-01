using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TetrisBlockCells : MonoBehaviour
{
    [Header("Visual Cells")]
    [SerializeField] private Transform[] cellVisuals;

    [Header("Shape")]
    [Tooltip("Координаты ячеек блока относительно его пивота (в клетках сетки).")]
    [SerializeField] private Vector2Int[] startOffsets;

    [Header("Rotation")]
    [SerializeField] private bool canRotate = true;

    [Header("Main Sprite (single artist-drawn block)")]
    [Tooltip("Спрайт-рендерер, на котором рисуется цельный нарисованный блок. " +
             "Если пусто — будет найден дочерний объект с именем 'Rendering' " +
             "(или первый включённый SpriteRenderer среди детей).")]
    [SerializeField] private SpriteRenderer mainRenderer;

    [Tooltip("6 спрайтов этого блока — по одному на каждый индекс цвета из палитры. " +
             "Порядок ДОЛЖЕН совпадать с порядком cellColorPalette в конфиге: " +
             "элемент [i] — это блок цвета i. Когда блок спавнится, выбирается " +
             "случайный индекс и сюда подставляется нужный спрайт.")]
    [SerializeField] private Sprite[] colorSprites;

    [Tooltip("Ручная поправка ориентации спрайта (шаги по 90°), если художник " +
             "нарисовал фигуру повёрнутой относительно её формы (offsets). " +
             "Обычно 0 — поворот по разнице габаритов вычисляется автоматически.")]
    [SerializeField] private int spriteRotationOffsetSteps = 0;

    [Tooltip("Отразить спрайт по X (если зеркальная фигура нарисована наоборот).")]
    [SerializeField] private bool flipSprite = false;

    [Header("Parent Collider")]
    [Tooltip("PolygonCollider2D, которым описываются клетки блока. " +
             "Если оставить пустым, он будет найден или создан автоматически.")]
    [SerializeField] private PolygonCollider2D polygonCollider;

    [Tooltip("Если true, любые BoxCollider2D в иерархии блока будут отключены при инициализации " +
             "(чтобы не возникали лишние коллизии от старых поячеечных коллайдеров из префаба).")]
    [SerializeField] private bool disableLegacyBoxColliders = true;

    private Vector2Int[] currentOffsets;
    private int[] cellColorIndices;
    private SpriteRenderer[] cellRenderers;
    private Color[] activePalette;

    // Состояние одиночного спрайта блока.
    private SpriteRenderer resolvedMainRenderer;
    private Vector2 homeCenterCells;     // центр габаритов фигуры (в клетках) при ориентации 0
    private int baseRotationSteps;       // авто-поворот, выравнивающий арт под форму (0/1)
    private int rotationStep;            // накопленный игровой поворот в шагах CCW (+1 = 90° CCW)
    private float currentCellSize = 1f;

    public bool CanRotate => canRotate;
    public Vector2Int[] CurrentOffsets => currentOffsets;
    public Transform[] CellVisuals => cellVisuals;

    /// <summary>
    /// Копия исходных оффсетов блока (так как они сериализованы в префабе).
    /// Используется внешними системами, чтобы заранее, ещё до Initialize, знать,
    /// какие клетки сетки занимает фигура.
    /// </summary>
    public Vector2Int[] GetStartOffsetsCopy()
    {
        if (startOffsets == null)
            return new Vector2Int[0];

        Vector2Int[] copy = new Vector2Int[startOffsets.Length];
        for (int i = 0; i < startOffsets.Length; i++)
            copy[i] = startOffsets[i];
        return copy;
    }

    /// <summary>
    /// Полностью переопределяет startOffsets перед вызовом
    /// <see cref="Initialize(float, Color[])"/>. Полезно для статичных блоков
    /// уровня, у которых форма зависит от того, под каким углом блок был
    /// расставлен в сцене.
    /// </summary>
    public void OverrideStartOffsets(Vector2Int[] offsets)
    {
        if (offsets == null)
        {
            startOffsets = new Vector2Int[0];
            return;
        }

        startOffsets = new Vector2Int[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
            startOffsets[i] = offsets[i];
    }

    /// <summary>Цветовой индекс для i-й ячейки (соответствует cellVisuals[i] и CurrentOffsets[i]).</summary>
    public int GetColorIndex(int index)
    {
        if (cellColorIndices == null || index < 0 || index >= cellColorIndices.Length)
            return -1;

        return cellColorIndices[index];
    }

    public Color GetColor(int index)
    {
        if (activePalette == null || activePalette.Length == 0)
            return Color.white;

        int colorIndex = GetColorIndex(index);

        if (colorIndex < 0)
            return Color.white;

        return activePalette[colorIndex % activePalette.Length];
    }

    public SpriteRenderer GetRenderer(int index)
    {
        if (cellRenderers == null || index < 0 || index >= cellRenderers.Length)
            return null;

        return cellRenderers[index];
    }

    public void Initialize(float cellSize, Color[] palette)
    {
        Initialize(cellSize, palette, assignRandomColors: true);
    }

    /// <summary>
    /// Полная версия Initialize. При <paramref name="assignRandomColors"/> = false
    /// случайный цвет НЕ выбирается — все индексы цветов выставляются в 0,
    /// и предполагается, что вызывающая сторона задаст конкретный цвет через
    /// <see cref="SetUniformColorIndex"/>.
    /// </summary>
    public void Initialize(float cellSize, Color[] palette, bool assignRandomColors)
    {
        ResetParentTransform();
        EnsureColliderSetup();

        activePalette = (palette != null && palette.Length > 0) ? palette : new[] { Color.white };

        Vector2Int[] effectiveOffsets = ResolveStartOffsets();

        currentOffsets = new Vector2Int[effectiveOffsets.Length];

        for (int i = 0; i < effectiveOffsets.Length; i++)
            currentOffsets[i] = effectiveOffsets[i];

        CacheCellRenderers();

        currentCellSize = cellSize;
        rotationStep = 0;
        ResolveMainRenderer();
        ComputeHomePose();

        if (assignRandomColors)
            AssignRandomColors();
        else
            AssignFixedColors(0);

        ApplyShape(cellSize);
        ApplyColors();
    }

    /// <summary>
    /// Устанавливает один и тот же цветовой индекс для всех ячеек блока и
    /// сразу же обновляет визуалы. Если палитра задана, индекс приводится
    /// в её границы по модулю.
    /// </summary>
    public void SetUniformColorIndex(int colorIndex)
    {
        if (cellColorIndices == null)
            return;

        int paletteLength = activePalette != null ? activePalette.Length : 0;
        int normalized;

        if (paletteLength > 0)
            normalized = ((colorIndex % paletteLength) + paletteLength) % paletteLength;
        else
            normalized = 0;

        for (int i = 0; i < cellColorIndices.Length; i++)
            cellColorIndices[i] = normalized;

        ApplyColors();
    }

    public Vector2Int[] GetRotatedOffsets(int direction)
    {
        Vector2Int[] result = new Vector2Int[currentOffsets.Length];

        for (int i = 0; i < currentOffsets.Length; i++)
        {
            Vector2Int offset = currentOffsets[i];

            if (direction > 0)
            {
                // clockwise: (x, y) -> (y, -x)
                result[i] = new Vector2Int(offset.y, -offset.x);
            }
            else
            {
                // counterclockwise: (x, y) -> (-y, x)
                result[i] = new Vector2Int(-offset.y, offset.x);
            }
        }

        return result;
    }

    public void SetOffsets(Vector2Int[] newOffsets, float cellSize)
    {
        currentOffsets = new Vector2Int[newOffsets.Length];

        for (int i = 0; i < newOffsets.Length; i++)
            currentOffsets[i] = newOffsets[i];

        ApplyShape(cellSize);
    }

    /// <summary>
    /// Удаляет указанные ячейки (по смещению относительно пивота) из блока:
    /// прячет их визуалы, сжимает параллельные массивы currentOffsets /
    /// cellVisuals / cellColorIndices / cellRenderers и перестраивает
    /// PolygonCollider2D под уменьшившуюся фигуру. Возвращает количество
    /// оставшихся (живых) ячеек.
    /// </summary>
    public int RemoveOffsets(IEnumerable<Vector2Int> offsetsToRemove, float cellSize)
    {
        if (currentOffsets == null || offsetsToRemove == null)
            return currentOffsets != null ? currentOffsets.Length : 0;

        HashSet<Vector2Int> removeSet = new HashSet<Vector2Int>(offsetsToRemove);

        if (removeSet.Count == 0)
            return currentOffsets.Length;

        List<Vector2Int> keptOffsets = new List<Vector2Int>(currentOffsets.Length);
        List<Transform> keptVisuals = cellVisuals != null
            ? new List<Transform>(cellVisuals.Length)
            : null;
        List<SpriteRenderer> keptRenderers = cellRenderers != null
            ? new List<SpriteRenderer>(cellRenderers.Length)
            : null;
        List<int> keptColorIndices = cellColorIndices != null
            ? new List<int>(cellColorIndices.Length)
            : null;

        int n = currentOffsets.Length;

        for (int i = 0; i < n; i++)
        {
            Vector2Int offset = currentOffsets[i];

            if (removeSet.Contains(offset))
            {
                // Скрываем визуал у выпиленной клетки. Сам объект не уничтожаем —
                // это дочка блока, её Destroy может ударить по чистке Unity.
                if (cellVisuals != null && i < cellVisuals.Length && cellVisuals[i] != null)
                    cellVisuals[i].gameObject.SetActive(false);

                continue;
            }

            keptOffsets.Add(offset);

            if (keptVisuals != null && i < cellVisuals.Length)
                keptVisuals.Add(cellVisuals[i]);

            if (keptRenderers != null && i < cellRenderers.Length)
                keptRenderers.Add(cellRenderers[i]);

            if (keptColorIndices != null && i < cellColorIndices.Length)
                keptColorIndices.Add(cellColorIndices[i]);
        }

        currentOffsets = keptOffsets.ToArray();

        if (keptVisuals != null)
            cellVisuals = keptVisuals.ToArray();

        if (keptRenderers != null)
            cellRenderers = keptRenderers.ToArray();

        if (keptColorIndices != null)
            cellColorIndices = keptColorIndices.ToArray();

        // Контур и позиции оставшихся ячеек могли стать «дырявыми» — пересчитаем.
        if (currentOffsets.Length > 0)
        {
            ApplyPolygonCollider(cellSize);
        }
        else
        {
            if (polygonCollider != null)
                polygonCollider.pathCount = 0;
        }

        return currentOffsets.Length;
    }

    private void ResetParentTransform()
    {
        // В исходных префабах у корня блока стоят произвольные scale/position,
        // из-за чего клетки растягиваются и перестают совпадать с сеткой.
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.identity;
    }

    private void EnsureColliderSetup()
    {
        if (polygonCollider == null)
            polygonCollider = GetComponent<PolygonCollider2D>();

        if (polygonCollider == null)
            polygonCollider = gameObject.AddComponent<PolygonCollider2D>();

        polygonCollider.isTrigger = false;

        if (!disableLegacyBoxColliders)
            return;

        // Отключаем ЛЮБЫЕ BoxCollider2D в иерархии блока (и на корне, и на
        // ячейках-детях). Раньше отключались только коллайдеры на корне, из-за
        // чего вручную выставленные «по-клеточные» BoxCollider2D на детях
        // оставались включёнными и накладывались на PolygonCollider2D —
        // получался двойной слой коллайдеров с миллиметровыми перекосами,
        // на которых игрок цеплялся при прыжках.
        BoxCollider2D[] boxColliders = GetComponentsInChildren<BoxCollider2D>(true);

        for (int i = 0; i < boxColliders.Length; i++)
        {
            if (boxColliders[i] == null)
                continue;

            boxColliders[i].enabled = false;
        }
    }

    private Vector2Int[] ResolveStartOffsets()
    {
        if (startOffsets != null && startOffsets.Length > 0)
            return startOffsets;

        // Защита от неконфигурированных префабов: строим сетку из доступных визуалов.
        int count = cellVisuals != null ? cellVisuals.Length : 0;

        if (count <= 0)
            return new Vector2Int[0];

        Vector2Int[] fallback = new Vector2Int[count];

        // Авто-раскладка по строкам, чтобы блок не "терялся" в одной точке.
        int side = Mathf.CeilToInt(Mathf.Sqrt(count));

        for (int i = 0; i < count; i++)
        {
            int x = i % side;
            int y = -(i / side);
            fallback[i] = new Vector2Int(x, y);
        }

        return fallback;
    }

    private void CacheCellRenderers()
    {
        int count = cellVisuals != null ? cellVisuals.Length : 0;

        cellRenderers = new SpriteRenderer[count];

        for (int i = 0; i < count; i++)
        {
            if (cellVisuals[i] == null)
                continue;

            cellRenderers[i] = cellVisuals[i].GetComponent<SpriteRenderer>();

            if (cellRenderers[i] == null)
                cellRenderers[i] = cellVisuals[i].GetComponentInChildren<SpriteRenderer>();
        }
    }

    private void AssignFixedColors(int colorIndex)
    {
        if (currentOffsets == null)
        {
            cellColorIndices = new int[0];
            return;
        }

        cellColorIndices = new int[currentOffsets.Length];

        for (int i = 0; i < cellColorIndices.Length; i++)
            cellColorIndices[i] = colorIndex;
    }

    private void AssignRandomColors()
    {
        if (currentOffsets == null)
        {
            cellColorIndices = new int[0];
            return;
        }

        cellColorIndices = new int[currentOffsets.Length];

        int paletteLength = activePalette != null ? activePalette.Length : 0;

        // Один цвет на весь блок: выбираем его один раз и красим в него все ячейки.
        // Так каждая фигура получается монохромной (например, целиком красная или
        // целиком зелёная), а уже разные блоки могут быть разных цветов.
        int blockColor = paletteLength > 0 ? Random.Range(0, paletteLength) : 0;

        for (int i = 0; i < cellColorIndices.Length; i++)
            cellColorIndices[i] = blockColor;
    }

    private void ApplyColors()
    {
        // Цельный спрайт: вместо покраски ячеек подставляем спрайт нужного цвета.
        ApplyMainSprite();

        if (cellRenderers == null || activePalette == null || activePalette.Length == 0)
            return;

        for (int i = 0; i < cellRenderers.Length; i++)
        {
            if (cellRenderers[i] == null)
                continue;

            cellRenderers[i].color = GetColor(i);
        }
    }

    /// <summary>
    /// Находит спрайт-рендерер цельного блока: либо заданный в инспекторе, либо
    /// дочерний объект с именем "Rendering", либо первый SpriteRenderer среди
    /// детей, который не входит в список поячеечных визуалов.
    /// </summary>
    private void ResolveMainRenderer()
    {
        if (mainRenderer != null)
        {
            resolvedMainRenderer = mainRenderer;
            return;
        }

        Transform renderingChild = transform.Find("Rendering");
        if (renderingChild != null)
        {
            SpriteRenderer sr = renderingChild.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                resolvedMainRenderer = sr;
                return;
            }
        }

        SpriteRenderer[] all = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !IsCellRenderer(all[i]))
            {
                resolvedMainRenderer = all[i];
                return;
            }
        }

        resolvedMainRenderer = null;
    }

    private bool IsCellRenderer(SpriteRenderer renderer)
    {
        if (cellVisuals == null || renderer == null)
            return false;

        for (int i = 0; i < cellVisuals.Length; i++)
        {
            if (cellVisuals[i] != null && renderer.transform == cellVisuals[i])
                return true;
        }

        return false;
    }

    /// <summary>
    /// Считает «домашнюю» позу спрайта: центр габаритов фигуры (в клетках) и
    /// базовый поворот, выравнивающий нарисованный спрайт под форму, если они
    /// различаются по ориентации (например, арт нарисован горизонтально, а
    /// фигура вертикальная).
    /// </summary>
    private void ComputeHomePose()
    {
        homeCenterCells = Vector2.zero;
        baseRotationSteps = 0;

        if (currentOffsets == null || currentOffsets.Length == 0)
            return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

        for (int i = 0; i < currentOffsets.Length; i++)
        {
            Vector2Int o = currentOffsets[i];
            if (o.x < minX) minX = o.x;
            if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;
            if (o.y > maxY) maxY = o.y;
        }

        homeCenterCells = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        int shapeWidth = maxX - minX + 1;
        int shapeHeight = maxY - minY + 1;

        Sprite sprite = ResolveSampleSprite();
        if (sprite == null)
            return;

        Vector2 artSize = sprite.bounds.size;
        bool artLandscape = artSize.x > artSize.y * 1.01f;
        bool artPortrait = artSize.y > artSize.x * 1.01f;
        bool shapeLandscape = shapeWidth > shapeHeight;
        bool shapePortrait = shapeHeight > shapeWidth;

        // Если арт и форма «перевёрнуты» друг относительно друга по габаритам —
        // доворачиваем спрайт на 90°, чтобы он лёг по форме.
        if ((artLandscape && shapePortrait) || (artPortrait && shapeLandscape))
            baseRotationSteps = 1;
    }

    private Sprite ResolveSampleSprite()
    {
        if (resolvedMainRenderer != null && resolvedMainRenderer.sprite != null)
            return resolvedMainRenderer.sprite;

        if (colorSprites != null)
        {
            for (int i = 0; i < colorSprites.Length; i++)
            {
                if (colorSprites[i] != null)
                    return colorSprites[i];
            }
        }

        return null;
    }

    private void ApplyMainSprite()
    {
        if (resolvedMainRenderer == null || colorSprites == null || colorSprites.Length == 0)
            return;

        int idx = (cellColorIndices != null && cellColorIndices.Length > 0) ? cellColorIndices[0] : 0;
        idx = ((idx % colorSprites.Length) + colorSprites.Length) % colorSprites.Length;

        if (colorSprites[idx] != null)
            resolvedMainRenderer.sprite = colorSprites[idx];
    }

    /// <summary>
    /// Ставит спрайт по центру габаритов фигуры с учётом базового авто-поворота
    /// и накопленного игрового поворота. Масштаб НЕ трогаем — арт нарисован в
    /// тех же единицах, что и клетки (PPU = размер клетки).
    /// </summary>
    private void ApplyMainSpriteTransform()
    {
        if (resolvedMainRenderer == null)
            return;

        Transform t = resolvedMainRenderer.transform;

        float orbitAngle = rotationStep * 90f;
        float spriteAngle = (baseRotationSteps + spriteRotationOffsetSteps + rotationStep) * 90f;

        Vector3 home = new Vector3(
            homeCenterCells.x * currentCellSize,
            homeCenterCells.y * currentCellSize,
            0f);

        // Игровой поворот «орбитой» переносит центр габаритов вокруг пивота (0,0),
        // ровно как смещаются клетки фигуры при повороте.
        Vector3 orbited = Quaternion.Euler(0f, 0f, orbitAngle) * home;

        t.localPosition = new Vector3(orbited.x, orbited.y, t.localPosition.z);
        t.localRotation = Quaternion.Euler(0f, 0f, spriteAngle);

        resolvedMainRenderer.flipX = flipSprite;
    }

    /// <summary>
    /// Сообщает визуалу, что фигуру повернули в игре. direction &gt; 0 — по часовой
    /// (как в <see cref="GetRotatedOffsets"/>), &lt; 0 — против часовой.
    /// </summary>
    public void RegisterRotation(int direction)
    {
        if (direction > 0)
            rotationStep--;
        else if (direction < 0)
            rotationStep++;

        ApplyMainSpriteTransform();
    }

    private void ApplyShape(float cellSize)
    {
        currentCellSize = cellSize;

        if (resolvedMainRenderer != null)
        {
            // Новый режим: цельный спрайт. Старые поячеечные визуалы не трогаем
            // (они выключены), только располагаем единый спрайт по форме.
            ApplyMainSpriteTransform();
        }
        else
        {
            ApplyVisualPositions(cellSize);
        }

        // Коллайдер строится по offsets независимо от режима отрисовки —
        // именно он задаёт занимаемые клетки, форму и физику.
        ApplyPolygonCollider(cellSize);
    }

    private void ApplyVisualPositions(float cellSize)
    {
        if (cellVisuals == null || currentOffsets == null)
            return;

        for (int i = 0; i < cellVisuals.Length; i++)
        {
            if (cellVisuals[i] == null)
                continue;

            if (i >= currentOffsets.Length)
            {
                cellVisuals[i].gameObject.SetActive(false);
                continue;
            }

            cellVisuals[i].gameObject.SetActive(true);

            Vector2Int offset = currentOffsets[i];

            cellVisuals[i].localPosition = new Vector3(
                offset.x * cellSize,
                offset.y * cellSize,
                0f
            );

            cellVisuals[i].localRotation = Quaternion.identity;

            // Каждая визуальная ячейка должна занимать ровно одну клетку сетки.
            cellVisuals[i].localScale = new Vector3(cellSize, cellSize, 1f);
        }
    }

    private void ApplyPolygonCollider(float cellSize)
    {
        if (polygonCollider == null || currentOffsets == null)
            return;

        polygonCollider.offset = Vector2.zero;

        if (currentOffsets.Length == 0)
        {
            polygonCollider.pathCount = 0;
            return;
        }

        // Раньше тут создавался отдельный квадратный путь на КАЖДУЮ ячейку.
        // Из-за этого у соседних ячеек получались общие внутренние рёбра, и
        // игрок цеплялся за «шов» между клетками, хотя визуально фигура была
        // ровной. Теперь мы выстраиваем ОДИН внешний контур по всем клеткам:
        //   * берём 4 ребра каждой ячейки;
        //   * выкидываем рёбра, у которых есть соседняя ячейка с другой стороны;
        //   * соединяем оставшиеся рёбра в замкнутый цикл и склеиваем
        //     коллинеарные сегменты.
        // На выходе — одна аккуратная многоугольная граница без швов внутри
        // фигуры; стороны идеально прямые, углы строго 90°.

        List<Vector2[]> outlinePaths = BuildOutlinePaths(currentOffsets, cellSize);

        polygonCollider.pathCount = outlinePaths.Count;

        for (int i = 0; i < outlinePaths.Count; i++)
            polygonCollider.SetPath(i, outlinePaths[i]);
    }

    /// <summary>
    /// Строит внешний контур (контуры) фигуры по списку клеток. Работает в
    /// удвоенных целых координатах углов клетки, чтобы все сравнения вершин
    /// были точными и не накапливались float-погрешности.
    /// </summary>
    private static List<Vector2[]> BuildOutlinePaths(Vector2Int[] offsets, float cellSize)
    {
        List<Vector2[]> result = new List<Vector2[]>();

        if (offsets == null || offsets.Length == 0)
            return result;

        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();

        for (int i = 0; i < offsets.Length; i++)
            cells.Add(offsets[i]);

        // В удвоенной системе координат центр клетки (cx, cy) — это (2*cx, 2*cy),
        // а её углы — это (2*cx ± 1, 2*cy ± 1). Каждая клетка обходится против
        // часовой стрелки (CCW), чтобы итоговый полигон тоже был CCW
        // (PolygonCollider2D трактует CCW-путь как «solid»).

        Dictionary<Vector2Int, Vector2Int> edgeNext = new Dictionary<Vector2Int, Vector2Int>();

        foreach (Vector2Int c in cells)
        {
            int x0 = c.x * 2 - 1;
            int x1 = c.x * 2 + 1;
            int y0 = c.y * 2 - 1;
            int y1 = c.y * 2 + 1;

            Vector2Int bl = new Vector2Int(x0, y0);
            Vector2Int br = new Vector2Int(x1, y0);
            Vector2Int tr = new Vector2Int(x1, y1);
            Vector2Int tl = new Vector2Int(x0, y1);

            // Ребро попадает во внешний контур только если по ту сторону клетки
            // нет соседней клетки фигуры — тогда это граница фигуры с пустотой.
            if (!cells.Contains(new Vector2Int(c.x, c.y - 1)))
                edgeNext[bl] = br;

            if (!cells.Contains(new Vector2Int(c.x + 1, c.y)))
                edgeNext[br] = tr;

            if (!cells.Contains(new Vector2Int(c.x, c.y + 1)))
                edgeNext[tr] = tl;

            if (!cells.Contains(new Vector2Int(c.x - 1, c.y)))
                edgeNext[tl] = bl;
        }

        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        float scale = 0.5f * cellSize;

        foreach (Vector2Int startCorner in edgeNext.Keys)
        {
            if (visited.Contains(startCorner))
                continue;

            List<Vector2Int> loop = new List<Vector2Int>();
            Vector2Int current = startCorner;
            int safety = edgeNext.Count + 1;

            while (!visited.Contains(current) && safety-- > 0)
            {
                visited.Add(current);
                loop.Add(current);

                if (!edgeNext.TryGetValue(current, out Vector2Int next))
                    break;

                current = next;
            }

            if (loop.Count < 3)
                continue;

            // Склеиваем коллинеарные сегменты: если два соседних ребра идут
            // в одном направлении, средняя вершина не нужна — иначе у
            // PolygonCollider2D окажутся лишние точки прямо посередине
            // прямой стороны (и физика теоретически может на них цепляться).
            List<Vector2> simplified = new List<Vector2>(loop.Count);

            for (int i = 0; i < loop.Count; i++)
            {
                Vector2Int prev = loop[(i - 1 + loop.Count) % loop.Count];
                Vector2Int cur = loop[i];
                Vector2Int next = loop[(i + 1) % loop.Count];

                int dx1 = cur.x - prev.x;
                int dy1 = cur.y - prev.y;
                int dx2 = next.x - cur.x;
                int dy2 = next.y - cur.y;

                if (dx1 == dx2 && dy1 == dy2)
                    continue;

                simplified.Add(new Vector2(cur.x * scale, cur.y * scale));
            }

            if (simplified.Count >= 3)
                result.Add(simplified.ToArray());
        }

        return result;
    }
}
