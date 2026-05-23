using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Блок, уже залоченный в сетке. Сохраняет свою форму, цвет, общий
/// коллайдер и геометрию — никаких поячеечных распилов. Двигается
/// при гравитации сетки целиком.
/// </summary>
[DisallowMultipleComponent]
public class TetrisPlacedBlock : MonoBehaviour
{
    private int blockId;
    private int colorIndex;
    private Vector2Int pivotCell;
    private Vector2Int[] cellOffsets;
    private Rigidbody2D body;
    private bool isStatic;
    private bool isAnchored;

    // Состояние плавной анимации (для гравитации и т.п.)
    private Vector3 animationTargetWorld;
    private float animationSpeed;
    private bool isAnimating;
    private bool destroyOnAnimationEnd;

    public int BlockId => blockId;
    public int ColorIndex => colorIndex;
    public Vector2Int PivotCell => pivotCell;
    public Vector2Int[] CellOffsets => cellOffsets;
    public bool IsAnimating => isAnimating;

    /// <summary>
    /// Статические блоки (например, платформы из сцены) занимают клетки сетки,
    /// но не падают по гравитации и не участвуют в схлопывании по цвету.
    /// </summary>
    public bool IsStatic => isStatic;

    /// <summary>
    /// Закреплённые блоки (нарисованные вручную в сцене статичные блоки уровня)
    /// занимают клетки сетки и УЧАСТВУЮТ в схлопывании по цвету, но НЕ падают
    /// по гравитации. Таким образом конструкция уровня не разваливается, даже
    /// если в её середине образовалась дыра от матчинга.
    /// </summary>
    public bool IsAnchored => isAnchored;

    public void Initialize(int blockId, int colorIndex, Vector2Int pivotCell, Vector2Int[] offsets)
    {
        this.blockId = blockId;
        this.colorIndex = colorIndex;
        this.pivotCell = pivotCell;

        if (offsets != null)
        {
            cellOffsets = new Vector2Int[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
                cellOffsets[i] = offsets[i];
        }
        else
        {
            cellOffsets = new Vector2Int[0];
        }

        body = GetComponent<Rigidbody2D>();
    }

    /// <summary>Помечает блок как статический — гравитация и матчинг будут его игнорировать.</summary>
    public void MarkAsStatic()
    {
        isStatic = true;
    }

    /// <summary>
    /// Помечает блок как «закреплённый»: он останется на месте при гравитации,
    /// но при этом будет участвовать в схлопывании по цвету и исчезнет, если
    /// рядом с ним окажется блок такого же цвета. Используется для статичных
    /// блоков уровня, расставленных вручную в сцене.
    /// </summary>
    public void MarkAsAnchored()
    {
        isAnchored = true;
    }

    /// <summary>Перемещает блок в новую клетку-пивот целиком МГНОВЕННО (без анимации).</summary>
    public void MoveToCell(Vector2Int cell, Vector3 worldPosition)
    {
        pivotCell = cell;
        ApplyWorldPosition(worldPosition);
        CancelAnimation();
    }

    /// <summary>
    /// Чисто визуально передвигает блок (Transform + Rigidbody2D.position),
    /// не трогая логическую опорную клетку. Используется для плавной анимации
    /// между шагами, когда логика сетки уже знает новую клетку, а визуал ещё
    /// должен «доехать» из старой позиции.
    /// </summary>
    public void SetVisualPosition(Vector3 worldPosition)
    {
        ApplyWorldPosition(worldPosition);
        CancelAnimation();
    }

    /// <summary>
    /// Обновляет ТОЛЬКО логическую клетку, не трогая Transform. Используется
    /// гравитационной анимацией: сетка уже считает блок в новой клетке,
    /// а визуал доезжает позже через <see cref="BeginAnimatedMoveTo"/>.
    /// </summary>
    public void SetLogicalCell(Vector2Int cell)
    {
        pivotCell = cell;
    }

    /// <summary>
    /// Запускает плавное движение Transform к worldPosition со скоростью speed (мировых единиц в секунду).
    /// Если speed &lt;= 0 — снап без анимации.
    /// </summary>
    public void BeginAnimatedMoveTo(Vector3 worldPosition, float speedUnitsPerSec)
    {
        animationTargetWorld = worldPosition;
        animationSpeed = Mathf.Max(0f, speedUnitsPerSec);

        if (animationSpeed <= 0f)
        {
            ApplyWorldPosition(worldPosition);
            isAnimating = false;
            if (destroyOnAnimationEnd)
                Destroy(gameObject);
            return;
        }

        isAnimating = true;
    }

    /// <summary>Updates pivot и сразу же стартует анимацию визуала к новой клетке.</summary>
    public void MoveToCellAnimated(Vector2Int cell, Vector3 worldPosition, float speedUnitsPerSec)
    {
        pivotCell = cell;
        BeginAnimatedMoveTo(worldPosition, speedUnitsPerSec);
    }

    /// <summary>Если true — по завершении анимации блок уничтожится. Используется для «упал за нижний край сетки».</summary>
    public void SetAnimationDestroyOnEnd(bool flag)
    {
        destroyOnAnimationEnd = flag;
    }

    /// <summary>Прерывает текущую анимацию (визуал останавливается там, где есть сейчас).</summary>
    public void CancelAnimation()
    {
        isAnimating = false;
        destroyOnAnimationEnd = false;
    }

    /// <summary>
    /// Убирает у блока перечисленные мировые клетки (например, ушедшие под
    /// уровень воды). Внутри переводит мировые клетки в смещения относительно
    /// пивота, чистит cellOffsets и просит TetrisBlockCells обновить визуал
    /// и коллайдер. Возвращает количество оставшихся клеток (0 — блок можно
    /// уничтожать).
    /// </summary>
    public int RemoveCellsAtWorldCells(IEnumerable<Vector2Int> worldCells)
    {
        if (worldCells == null || cellOffsets == null || cellOffsets.Length == 0)
            return cellOffsets != null ? cellOffsets.Length : 0;

        HashSet<Vector2Int> removeSet = new HashSet<Vector2Int>();
        foreach (Vector2Int worldCell in worldCells)
            removeSet.Add(worldCell - pivotCell);

        if (removeSet.Count == 0)
            return cellOffsets.Length;

        List<Vector2Int> kept = new List<Vector2Int>(cellOffsets.Length);

        for (int i = 0; i < cellOffsets.Length; i++)
        {
            if (removeSet.Contains(cellOffsets[i]))
                continue;

            kept.Add(cellOffsets[i]);
        }

        cellOffsets = kept.ToArray();

        // Синхронизируем визуал. CellSize узнаём у TetrisBlockCells через
        // его текущее представление — она знает свой cellSize неявно через
        // localPositions, но для пересборки коллайдера ей нужно явное число.
        TetrisBlockCells cells = GetComponent<TetrisBlockCells>();
        if (cells != null)
        {
            float cellSize = ResolveCellSize();
            cells.RemoveOffsets(removeSet, cellSize);
        }

        return cellOffsets.Length;
    }

    /// <summary>
    /// Пытается выяснить cellSize, по которому был сложен блок. Берём
    /// первый сосед среди cellVisuals — расстояние между ними и центром
    /// блока есть кратное cellSize. Если ничего не нашли, возвращаем 1
    /// (стандартный размер клетки в этой игре).
    /// </summary>
    private float ResolveCellSize()
    {
        TetrisBlockCells cells = GetComponent<TetrisBlockCells>();
        if (cells == null)
            return 1f;

        Transform[] visuals = cells.CellVisuals;
        if (visuals == null || visuals.Length == 0)
            return 1f;

        for (int i = 0; i < visuals.Length; i++)
        {
            if (visuals[i] == null) continue;
            float sx = Mathf.Abs(visuals[i].localScale.x);
            if (sx > 0.0001f) return sx;
        }

        return 1f;
    }

    private void Update()
    {
        if (!isAnimating)
            return;

        Vector3 cur = transform.position;
        Vector3 next = Vector3.MoveTowards(cur, animationTargetWorld, animationSpeed * Time.deltaTime);

        ApplyWorldPosition(next);

        if ((next - animationTargetWorld).sqrMagnitude <= 1e-6f)
        {
            ApplyWorldPosition(animationTargetWorld);
            isAnimating = false;

            if (destroyOnAnimationEnd)
                Destroy(gameObject);
        }
    }

    private void ApplyWorldPosition(Vector3 worldPosition)
    {
        transform.position = worldPosition;
        if (body != null)
            body.position = new Vector2(worldPosition.x, worldPosition.y);
    }
}
