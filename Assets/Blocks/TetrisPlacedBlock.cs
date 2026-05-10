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

    public int BlockId => blockId;
    public int ColorIndex => colorIndex;
    public Vector2Int PivotCell => pivotCell;
    public Vector2Int[] CellOffsets => cellOffsets;

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

    /// <summary>Перемещает блок в новую клетку-пивот целиком, не разбирая на части.</summary>
    public void MoveToCell(Vector2Int cell, Vector3 worldPosition)
    {
        pivotCell = cell;
        transform.position = worldPosition;

        if (body != null)
            body.position = new Vector2(worldPosition.x, worldPosition.y);
    }
}
