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

    [Header("Parent Collider")]
    [Tooltip("PolygonCollider2D, которым описываются клетки блока. " +
             "Если оставить пустым, он будет найден или создан автоматически.")]
    [SerializeField] private PolygonCollider2D polygonCollider;

    [Tooltip("Если true, любые BoxCollider2D на этом GameObject будут отключены при инициализации " +
             "(чтобы не возникали лишние коллизии от старых коллайдеров из префаба).")]
    [SerializeField] private bool disableLegacyBoxColliders = true;

    private Vector2Int[] currentOffsets;
    private int[] cellColorIndices;
    private SpriteRenderer[] cellRenderers;
    private Color[] activePalette;

    public bool CanRotate => canRotate;
    public Vector2Int[] CurrentOffsets => currentOffsets;
    public Transform[] CellVisuals => cellVisuals;

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
        ResetParentTransform();
        EnsureColliderSetup();

        activePalette = (palette != null && palette.Length > 0) ? palette : new[] { Color.white };

        Vector2Int[] effectiveOffsets = ResolveStartOffsets();

        currentOffsets = new Vector2Int[effectiveOffsets.Length];

        for (int i = 0; i < effectiveOffsets.Length; i++)
            currentOffsets[i] = effectiveOffsets[i];

        CacheCellRenderers();
        AssignRandomColors();
        ApplyShape(cellSize);
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

        BoxCollider2D[] boxColliders = GetComponents<BoxCollider2D>();

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
        if (cellRenderers == null || activePalette == null || activePalette.Length == 0)
            return;

        for (int i = 0; i < cellRenderers.Length; i++)
        {
            if (cellRenderers[i] == null)
                continue;

            cellRenderers[i].color = GetColor(i);
        }
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
