using UnityEngine;

/// <summary>
/// Одна ячейка, уже стоящая в сетке (после того как родительский блок залочился).
/// Хранит свой цветовой индекс и умеет применять цвет к спрайту.
/// </summary>
[DisallowMultipleComponent]
public class TetrisPlacedCell : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    private int colorIndex = -1;
    private int blockId = -1;

    public int ColorIndex => colorIndex;
    public int BlockId => blockId;
    public SpriteRenderer SpriteRenderer => spriteRenderer;

    public void Setup(int blockId, int colorIndex, Color color, SpriteRenderer renderer)
    {
        this.blockId = blockId;
        this.colorIndex = colorIndex;
        spriteRenderer = renderer != null ? renderer : GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }
}
