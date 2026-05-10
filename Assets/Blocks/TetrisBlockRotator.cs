using UnityEngine;

public class TetrisBlockRotator : MonoBehaviour
{
    private static readonly Vector2Int[] basicWallKicks =
    {
        new Vector2Int(0, 0),
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(2, 0),
        new Vector2Int(-2, 0),
        new Vector2Int(0, 1)
    };

    public bool TryRotate(
        Rigidbody2D body,
        TetrisGridBoard board,
        TetrisBlockCells blockCells,
        int direction)
    {
        if (direction == 0)
            return false;

        if (!blockCells.CanRotate)
            return false;

        Vector2Int pivotCell = board.WorldToCell(body.position);
        Vector2Int[] rotatedOffsets = blockCells.GetRotatedOffsets(direction);

        foreach (Vector2Int kick in basicWallKicks)
        {
            Vector2Int kickedPivot = pivotCell + kick;

            if (!board.CanPlaceOffsets(kickedPivot, rotatedOffsets))
                continue;

            body.position = board.CellToWorld(kickedPivot);
            body.rotation = 0f;

            blockCells.SetOffsets(rotatedOffsets, board.CellSize);

            return true;
        }

        return false;
    }
}
