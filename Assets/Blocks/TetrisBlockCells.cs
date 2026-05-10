using System.Collections.Generic;
using UnityEngine;

public class TetrisBlockCells : MonoBehaviour
{
    [Header("Visual Cells")]
    [SerializeField] private Transform[] cellVisuals;

    [Header("Shape")]
    [SerializeField] private Vector2Int[] startOffsets;

    [Header("Rotation")]
    [SerializeField] private bool canRotate = true;

    [Header("Parent Collider")]
    [SerializeField] private PolygonCollider2D polygonCollider;

    private Vector2Int[] currentOffsets;

    public bool CanRotate => canRotate;
    public Vector2Int[] CurrentOffsets => currentOffsets;

    public void Initialize(float cellSize)
    {
        currentOffsets = new Vector2Int[startOffsets.Length];

        for (int i = 0; i < startOffsets.Length; i++)
        {
            currentOffsets[i] = startOffsets[i];
        }

        ApplyShape(cellSize);
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
        {
            currentOffsets[i] = newOffsets[i];
        }

        ApplyShape(cellSize);
    }

    private void ApplyShape(float cellSize)
    {
        ApplyVisualPositions(cellSize);
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
                continue;

            Vector2Int offset = currentOffsets[i];

            cellVisuals[i].localPosition = new Vector3(
                offset.x * cellSize,
                offset.y * cellSize,
                0f
            );

            cellVisuals[i].localRotation = Quaternion.identity;

            // Важно: каждая визуальная ячейка должна быть квадратом.
            cellVisuals[i].localScale = new Vector3(cellSize, cellSize, 1f);
        }
    }

    private void ApplyPolygonCollider(float cellSize)
    {
        if (polygonCollider == null || currentOffsets == null)
            return;

        polygonCollider.offset = Vector2.zero;
        polygonCollider.pathCount = currentOffsets.Length;

        float half = cellSize * 0.5f;

        for (int i = 0; i < currentOffsets.Length; i++)
        {
            Vector2 center = new Vector2(
                currentOffsets[i].x * cellSize,
                currentOffsets[i].y * cellSize
            );

            Vector2[] squarePath =
            {
                center + new Vector2(-half, -half),
                center + new Vector2(-half,  half),
                center + new Vector2( half,  half),
                center + new Vector2( half, -half)
            };

            polygonCollider.SetPath(i, squarePath);
        }
    }
}
