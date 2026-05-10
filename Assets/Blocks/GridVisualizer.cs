using UnityEngine;

[ExecuteAlways]
public class GridVisualizer : MonoBehaviour
{
    [Header("Grid Size")] public int width = 10;
    public int height = 10;

    [Header("Cell Settings")] public float cellSize = 1f;

    [Header("Line Settings")] public Material lineMaterial;
    public float lineWidth = 0.03f;
    public Color lineColor = Color.white;

    private void OnEnable()
    {
        GenerateGrid();
    }

    private void OnValidate()
    {
        GenerateGrid();
    }

    public void GenerateGrid()
    {
        ClearGrid();

        if (lineMaterial == null)
            return;

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = new Vector3(x * cellSize, 0, 0);
            Vector3 end = new Vector3(x * cellSize, height * cellSize, 0);

            CreateLine(start, end, $"Vertical {x}");
        }

        for (int y = 0; y <= height; y++)
        {
            Vector3 start = new Vector3(0, y * cellSize, 0);
            Vector3 end = new Vector3(width * cellSize, y * cellSize, 0);

            CreateLine(start, end, $"Horizontal {y}");
        }
    }

    private void CreateLine(Vector3 start, Vector3 end, string lineName)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(transform);
        lineObject.transform.localPosition = Vector3.zero;

        LineRenderer line = lineObject.AddComponent<LineRenderer>();

        line.positionCount = 2;
        line.useWorldSpace = false;

        line.SetPosition(0, start);
        line.SetPosition(1, end);

        line.startWidth = lineWidth;
        line.endWidth = lineWidth;

        line.material = lineMaterial;
        line.startColor = lineColor;
        line.endColor = lineColor;

        line.sortingOrder = 10;
    }

    private void ClearGrid()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            else
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }
        }
    }
}