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
