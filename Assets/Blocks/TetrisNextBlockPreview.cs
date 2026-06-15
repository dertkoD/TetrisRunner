using UnityEngine;

/// <summary>
/// Предпоказ («next») следующего блока. Берёт префаб из пула
/// <see cref="TetrisBlockConfigSO.BlockPrefabs"/>, который заранее выбрал
/// <see cref="TetrisBlockSpawnManager"/>, и показывает его в заданной точке.
///
/// Превью полностью «бутафорское»: у него отключаются физика (Rigidbody2D),
/// все коллайдеры и игровые скрипты, поэтому оно не сталкивается ни с другими
/// блоками, ни с игроком, ни с чем-либо ещё — только отображается.
/// </summary>
public class TetrisNextBlockPreview : MonoBehaviour
{
    [Header("Где показывать")]
    [Tooltip("Точка, в центре которой показывается следующий блок. Поставь сюда " +
             "пустой объект (Create Empty) там, где хочешь видеть предпоказ — " +
             "геометрический центр блока совмещается именно с этой точкой.")]
    [SerializeField] private Transform previewPoint;

    [Tooltip("Необязательно: родитель для объекта-превью в иерархии. Если пусто — " +
             "превью становится дочерним к previewPoint.")]
    [SerializeField] private Transform previewParent;

    [Header("Размер")]
    [Tooltip("Масштаб модельки превью. 1 — как в игре, меньше 1 — мельче, " +
             "больше 1 — крупнее.")]
    [SerializeField, Min(0.0001f)] private float previewScale = 1f;

    private GameObject currentPreview;

    /// <summary>
    /// Показывает превью блока: создаёт копию префаба, строит нужную форму и
    /// цвет, отключает у неё всю физику/логику, масштабирует и ставит по центру
    /// в <see cref="previewPoint"/>. Предыдущее превью удаляется.
    /// </summary>
    public void ShowNext(TetrisBlockFacade prefab, int colorIndex, float cellSize, Color[] palette)
    {
        Clear();

        if (prefab == null || previewPoint == null)
            return;

        Transform parent = previewParent != null ? previewParent : previewPoint;

        TetrisBlockFacade instance = Instantiate(prefab, previewPoint.position, Quaternion.identity, parent);
        currentPreview = instance.gameObject;
        currentPreview.name = $"NextBlockPreview ({prefab.name})";

        instance.transform.localScale = Vector3.one;
        instance.transform.localRotation = Quaternion.identity;

        // Строим визуал нужной формы и красим в выбранный цвет (без рандома).
        TetrisBlockCells cells = instance.BlockCells;
        if (cells != null)
        {
            cells.Initialize(cellSize, palette, assignRandomColors: false);
            cells.SetUniformColorIndex(Mathf.Max(0, colorIndex));
        }

        // Превью не должно ни с чем взаимодействовать физически.
        MakeNonPhysical(instance);

        // Пользовательский масштаб применяем ПОСЛЕ Initialize: он сбрасывает
        // localScale блока в единицу.
        instance.transform.localScale = Vector3.one * Mathf.Max(0.0001f, previewScale);

        // Геометрический центр блока совмещаем с точкой предпоказа.
        CenterOnPoint(instance, previewPoint.position);
    }

    /// <summary>Удаляет текущее превью (если есть).</summary>
    public void Clear()
    {
        if (currentPreview != null)
            Destroy(currentPreview);

        currentPreview = null;
    }

    private static void MakeNonPhysical(TetrisBlockFacade instance)
    {
        Rigidbody2D[] bodies = instance.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null)
                continue;

            bodies[i].simulated = false;
            bodies[i].bodyType = RigidbodyType2D.Kinematic;
            bodies[i].linearVelocity = Vector2.zero;
            bodies[i].angularVelocity = 0f;
        }

        Collider2D[] colliders = instance.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        // Отключаем игровые скрипты, чтобы превью не двигалось и не реагировало
        // на столкновения, даже если что-то их случайно дёрнет.
        DisableBehaviour(instance.Controller);
        DisableBehaviour(instance.Movement);
        DisableBehaviour(instance.Rotator);
        DisableBehaviour(instance.ContactReporter);

        BlockWaveProxy[] proxies = instance.GetComponentsInChildren<BlockWaveProxy>(true);
        for (int i = 0; i < proxies.Length; i++)
            DisableBehaviour(proxies[i]);
    }

    private static void DisableBehaviour(Behaviour behaviour)
    {
        if (behaviour != null)
            behaviour.enabled = false;
    }

    private void CenterOnPoint(TetrisBlockFacade instance, Vector3 worldPoint)
    {
        Bounds? bounds = ComputeVisualBounds(instance);

        if (bounds == null)
        {
            instance.transform.position = worldPoint;
            return;
        }

        Vector3 delta = worldPoint - bounds.Value.center;
        instance.transform.position += delta;
    }

    /// <summary>
    /// Считает общие мировые границы по всем видимым SpriteRenderer'ам блока,
    /// чтобы можно было поставить именно визуальный центр фигуры в точку.
    /// </summary>
    private static Bounds? ComputeVisualBounds(TetrisBlockFacade instance)
    {
        SpriteRenderer[] renderers = instance.GetComponentsInChildren<SpriteRenderer>(false);

        Bounds bounds = default;
        bool has = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];

            if (sr == null || !sr.enabled || sr.sprite == null)
                continue;

            if (!has)
            {
                bounds = sr.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(sr.bounds);
            }
        }

        if (!has)
            return null;

        return bounds;
    }
}
