using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class GridVisualizer : MonoBehaviour
{
    [Header("Grid Size")] public int width = 10;
    public int height = 10;

    [Header("Cell Settings")] public float cellSize = 1f;

    [Header("Line Settings")] public Material lineMaterial;
    public float lineWidth = 0.03f;
    public Color lineColor = Color.white;

    // Сигнатура текущей нарисованной сетки. Если параметры не менялись и
    // линии уже есть в сцене — повторно ничего не строим. Так уходит
    // 'постоянная перерисовка'.
    [System.NonSerialized] private int lastBuildSignature;
    [System.NonSerialized] private bool hasBuiltAtLeastOnce;

    private void OnEnable()
    {
        ScheduleRebuild();
    }

    private void OnValidate()
    {
        // ВАЖНО: ничего не строим и не уничтожаем прямо здесь. Unity
        // запрещает Destroy/DestroyImmediate из OnValidate, иначе старые
        // дети не удаляются, а новые тут же добавляются → дублирование.
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Откладываем до окна редактора, когда DestroyImmediate
            // уже безопасен. delayCall гарантирует ровно один rebuild
            // даже если нас дёрнули несколько раз подряд.
            EditorApplication.delayCall -= RebuildIfNeeded;
            EditorApplication.delayCall += RebuildIfNeeded;
            return;
        }
#endif
        // В Play Mode зовём напрямую — но всё равно через сигнатурный
        // кэш, так что повторных перестроек не будет.
        RebuildIfNeeded();
    }

    private void RebuildIfNeeded()
    {
        // Объект мог быть уничтожен между OnValidate и delayCall.
        if (this == null)
            return;

        if (lineMaterial == null)
        {
            ClearGrid();
            hasBuiltAtLeastOnce = false;
            return;
        }

        int currentSignature = ComputeSignature();

        if (hasBuiltAtLeastOnce
            && currentSignature == lastBuildSignature
            && CountExistingLines() > 0)
        {
            // Параметры не менялись и линии на месте — ничего не делаем.
            return;
        }

        GenerateGrid();
        lastBuildSignature = currentSignature;
        hasBuiltAtLeastOnce = true;
    }

    /// <summary>
    /// Принудительно перестроить сетку (можно дёргать из меню/кнопки в
    /// инспекторе или из других скриптов).
    /// </summary>
    [ContextMenu("Rebuild Grid Now")]
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

        // Линии — чисто визуальная вспомогательная штука: НЕ сохраняем их
        // ни в сцене, ни в билде, ни в префабе. Иначе они утекут в
        // m_AddedGameObjects префаб-инстанса и со временем накопятся.
        lineObject.hideFlags = HideFlags.DontSaveInEditor
                              | HideFlags.DontSaveInBuild
                              | HideFlags.NotEditable;

        lineObject.transform.SetParent(transform, false);
        lineObject.transform.localPosition = Vector3.zero;
        lineObject.transform.localRotation = Quaternion.identity;
        lineObject.transform.localScale = Vector3.one;

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
        // Удаляем только своих детей (с LineRenderer), чужих
        // (типа PlacedBlocks у TetrisGridBoard) не трогаем.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (child == null)
                continue;

            if (child.GetComponent<LineRenderer>() == null)
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private int CountExistingLines()
    {
        int count = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null) continue;
            if (child.GetComponent<LineRenderer>() != null) count++;
        }
        return count;
    }

    private int ComputeSignature()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + width;
            hash = hash * 31 + height;
            hash = hash * 31 + cellSize.GetHashCode();
            hash = hash * 31 + lineWidth.GetHashCode();
            hash = hash * 31 + lineColor.GetHashCode();
            hash = hash * 31 + (lineMaterial != null ? lineMaterial.GetInstanceID() : 0);
            return hash;
        }
    }
}
