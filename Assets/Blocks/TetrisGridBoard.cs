using System.Collections.Generic;
using UnityEngine;

public class TetrisGridBoard : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 20;
    [SerializeField] private float cellSize = 1f;

    [Header("Origin")]
    [SerializeField] private Transform origin;

    private readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();

    public float CellSize => cellSize;

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
        return occupiedCells.Contains(cell);
    }

    public bool CanPlaceOffsets(Vector2Int pivotCell, Vector2Int[] offsets)
    {
        if (offsets == null)
            return false;

        foreach (Vector2Int offset in offsets)
        {
            Vector2Int cell = pivotCell + offset;

            if (!IsInside(cell))
                return false;

            if (IsOccupied(cell))
                return false;
        }

        return true;
    }

    public void RegisterBlock(Vector2Int pivotCell, Vector2Int[] offsets)
    {
        if (offsets == null)
            return;

        foreach (Vector2Int offset in offsets)
        {
            Vector2Int cell = pivotCell + offset;

            if (!IsInside(cell))
                continue;

            occupiedCells.Add(cell);
        }
    }
}
