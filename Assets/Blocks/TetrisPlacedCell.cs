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

    public int ColorIndex => colorIndex;
    public SpriteRenderer SpriteRenderer => spriteRenderer;

    public void Setup(int index, Color color, SpriteRenderer renderer)
    {
        colorIndex = index;
        spriteRenderer = renderer != null ? renderer : GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }
}
