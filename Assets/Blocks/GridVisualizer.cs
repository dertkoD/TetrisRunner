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
        // В Play Mode ничего не перерисовываем: линии уже сохранены в сцене.
        // Раньше тут вызывался GenerateGrid → ClearGrid с Destroy() (отложенным),
        // из-за чего старые линии оставались до конца кадра, а новые тут же
        // создавались — визуально это давало 'дублирование сетки'. Плюс
        // ClearGrid сносил вообще всех детей трансформа, включая авто-созданный
        // контейнер PlacedBlocks у TetrisGridBoard.
        if (Application.isPlaying)
            return;

        GenerateGrid();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

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

        // Линии — чисто визуальная вспомогательная штука: не сохраняем их
        // ни в сцене, ни в билде, ни в префабе. Раньше они утекали в
        // m_AddedGameObjects префаб-инстанса и со временем накапливались
        // (визуально это выглядело как 'сетка дублируется').
        lineObject.hideFlags = HideFlags.DontSaveInEditor
                              | HideFlags.DontSaveInBuild
                              | HideFlags.NotEditable;

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
        // Удаляем только то, что мы сами и нагенерировали — детей с LineRenderer.
        // Раньше тут уничтожались все дочерние объекты подряд (включая, например,
        // авто-созданный 'PlacedBlocks' у TetrisGridBoard).
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (child == null)
                continue;

            if (child.GetComponent<LineRenderer>() == null)
                continue;

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }
}
