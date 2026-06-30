using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// «Кнопка» — статичный одноклеточный (или больше) объект в сетке тетриса,
/// который игрок может нажать ОДИН РАЗ (без удержания). При нажатии:
///   * запускается <see cref="UnityEvent"/> <see cref="onPressed"/>;
///   * если задана связанная дверь <see cref="door"/>, ей вызывается
///     <see cref="GridLiftDoor.Open"/>;
///   * сама кнопка переходит в «нажатое» состояние и больше не реагирует.
///
/// Активировать кнопку может ТОЛЬКО игрок (объект с <see cref="PlayerFacade"/>):
/// падающие или уже залоченные блоки тетриса кнопку не нажимают. Дополнительно
/// можно ограничить, какие именно слои считаются «игроком», через
/// <see cref="playerLayers"/>.
///
/// Сетка <see cref="TetrisGridBoard"/> знает кнопку как статический
/// <see cref="TetrisPlacedBlock"/>: её клетки заняты, поэтому блоки не падают
/// сквозь неё, а её положение учитывается при размещении других блоков.
/// </summary>
[DisallowMultipleComponent]
public class GridButton : MonoBehaviour
{
    public enum ShapeSource
    {
        ManualSize,
        AutoFromCollider,
        AutoFromRenderer,
    }

    [Header("References")]
    [Tooltip("Сетка, в которую регистрируется кнопка. Если пусто — будет найдена в сцене.")]
    [SerializeField] private TetrisGridBoard board;

    [Tooltip("Дверь, которую открывает эта кнопка. Можно оставить пустым и подписаться " +
             "на событие OnPressed через инспектор.")]
    [SerializeField] private GridLiftDoor door;

    [Header("Shape")]
    [Tooltip("Откуда брать форму кнопки (какие клетки сетки она занимает).")]
    [SerializeField] private ShapeSource shapeSource = ShapeSource.ManualSize;

    [Tooltip("ManualSize: размер кнопки в клетках. Опорной (pivot) считается клетка под " +
             "Transform кнопки; прямоугольник растёт вправо и вверх. Обычно 1x1.")]
    [SerializeField] private Vector2Int manualSizeInCells = new Vector2Int(1, 1);

    [Tooltip("AutoFromCollider/Renderer: если AABB слегка выходит за границу клетки на " +
             "эту долю или меньше, клетка всё равно НЕ считается занятой.")]
    [SerializeField, Range(0f, 0.5f)] private float overlapTolerance = 0.05f;

    [Tooltip("Если true — Transform кнопки на старте прижимается к центру опорной клетки. " +
             "Если false — сохраняется визуальное смещение Transform относительно pivot.")]
    [SerializeField] private bool snapTransformToPivotOnStart = false;

    [Header("Detection")]
    [Tooltip("Размер триггер-зоны, в которой ищется игрок (мировые единицы). " +
             "Обычно достаточно квадрата чуть больше клетки. Если на объекте уже есть " +
             "Collider2D помеченный isTrigger — используется он, а это поле игнорируется.")]
    [SerializeField] private Vector2 triggerSize = new Vector2(1f, 1f);

    [Tooltip("Смещение центра триггер-зоны относительно Transform кнопки (мировые единицы). " +
             "Игнорируется, если на объекте уже есть свой Collider2D-триггер.")]
    [SerializeField] private Vector2 triggerOffset = Vector2.zero;

    [Tooltip("Слои, считающиеся игроком. Если Nothing — фильтр по слою не применяется и " +
             "проверяется только наличие компонента PlayerFacade.")]
    [SerializeField] private LayerMask playerLayers = 0;

    [Header("Pressed Visuals")]
    [Tooltip("SpriteRenderer самой кнопки. Если пусто — будет взят SpriteRenderer на этом объекте или в активных детях.")]
    [SerializeField] private SpriteRenderer buttonSpriteRenderer;

    [Tooltip("Спрайт, на который заменится текущий спрайт кнопки после нажатия.")]
    [SerializeField] private Sprite pressedSprite;

    [Tooltip("GameObject'ы, которые нужно включить после нажатия кнопки. Можно держать их выключенными в сцене.")]
    [SerializeField] private GameObject[] objectsToEnableOnPressed;

    [Tooltip("SpriteRenderer'ы, которые нужно включить после нажатия кнопки. Удобно, если объект должен оставаться активным.")]
    [SerializeField] private SpriteRenderer[] spriteRenderersToEnableOnPressed;

    [Tooltip("На старте выключить все Objects To Enable On Pressed. Оставь включённым, если эти спрайты должны быть невидимы до нажатия.")]
    [SerializeField] private bool hidePressedObjectsOnStart = true;

    [Tooltip("На старте выключить все Sprite Renderers To Enable On Pressed.")]
    [SerializeField] private bool hidePressedRenderersOnStart = true;

    [Header("Events")]
    [Tooltip("Вызывается один раз, когда кнопку нажал игрок.")]
    [SerializeField] private UnityEvent onPressed;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private TetrisPlacedBlock buttonBlock;
    private Rigidbody2D buttonBody;
    private Collider2D triggerCollider;
    private bool createdTriggerCollider;
    private bool initialized;
    private bool isPressed;

    public bool IsPressed => isPressed;
    public TetrisPlacedBlock ButtonBlock => buttonBlock;
    public GridLiftDoor Door => door;

    /// <summary>Событие срабатывает один раз при нажатии игроком.</summary>
    public UnityEvent OnPressed => onPressed;

    private void Reset()
    {
        EnsureKinematicRigidbody();
    }

    private void Awake()
    {
        EnsureKinematicRigidbody();
        EnsureTriggerCollider();
        InitializePressedVisuals();
    }

    private void Start()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        if (board == null)
        {
            Debug.LogWarning($"{nameof(GridButton)}: TetrisGridBoard не найден в сцене.", this);
            return;
        }

        List<Vector2Int> cells = ResolveCells();

        if (cells == null || cells.Count == 0)
        {
            Debug.LogWarning($"{nameof(GridButton)}: не удалось определить клетки для кнопки '{name}'.", this);
            return;
        }

        Vector2Int pivot = cells[0];
        Vector2Int[] offsets = new Vector2Int[cells.Count];
        for (int i = 0; i < cells.Count; i++)
            offsets[i] = cells[i] - pivot;

        if (snapTransformToPivotOnStart)
            transform.position = board.CellToWorld(pivot);

        buttonBlock = GetComponent<TetrisPlacedBlock>();
        if (buttonBlock == null)
            buttonBlock = gameObject.AddComponent<TetrisPlacedBlock>();

        buttonBlock.Initialize(TetrisGridBoard.AllocateBlockId(), -1, pivot, offsets);
        buttonBlock.MarkAsStatic();

        board.RegisterBlock(buttonBlock);

        if (verboseLogs)
            Debug.Log(
                $"{nameof(GridButton)} '{name}': registered {cells.Count} cell(s). " +
                $"Pivot={pivot}, offsets=[{string.Join(",", offsets)}].",
                this);

        initialized = true;
    }

    private void OnDestroy()
    {
        if (board == null || buttonBlock == null)
            return;

        board.UnregisterBlock(buttonBlock);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryPress(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // На случай, если игрок уже стоял на кнопке в момент инициализации
        // (OnTriggerEnter2D мог отработать раньше Start) — продолжаем проверять,
        // пока кнопка не нажата.
        if (isPressed)
            return;

        TryPress(other);
    }

    private void TryPress(Collider2D other)
    {
        if (isPressed)
            return;

        if (other == null)
            return;

        if (playerLayers.value != 0)
        {
            int otherLayerBit = 1 << other.gameObject.layer;
            if ((playerLayers.value & otherLayerBit) == 0)
            {
                if (verboseLogs)
                    Debug.Log($"{nameof(GridButton)} '{name}': '{other.name}' не подходит по слою — игнорирую.", this);
                return;
            }
        }

        PlayerFacade player = other.GetComponent<PlayerFacade>()
                              ?? other.GetComponentInParent<PlayerFacade>();

        if (player == null)
        {
            // Главное правило: блоки тетриса кнопку не нажимают. Здесь мы
            // просто молча игнорируем всё, что не игрок — это и есть защита
            // от срабатывания при падении/приземлении блока на кнопку.
            if (verboseLogs)
                Debug.Log($"{nameof(GridButton)} '{name}': '{other.name}' не игрок — кнопка не сработает.", this);
            return;
        }

        Press();
    }

    /// <summary>
    /// Принудительно «нажать» кнопку (например, из другого скрипта). Срабатывает
    /// только один раз: повторные вызовы после нажатия игнорируются.
    /// </summary>
    public void Press()
    {
        if (isPressed)
            return;

        isPressed = true;

        if (verboseLogs)
            Debug.Log($"{nameof(GridButton)} '{name}': нажата игроком — открываю дверь.", this);

        ApplyPressedVisuals();

        onPressed?.Invoke();

        if (door != null)
            door.Open();
    }

    private void InitializePressedVisuals()
    {
        ResolveButtonSpriteRenderer();

        if (hidePressedObjectsOnStart && objectsToEnableOnPressed != null)
        {
            for (int i = 0; i < objectsToEnableOnPressed.Length; i++)
            {
                if (objectsToEnableOnPressed[i] != null)
                    objectsToEnableOnPressed[i].SetActive(false);
            }
        }

        if (hidePressedRenderersOnStart && spriteRenderersToEnableOnPressed != null)
        {
            for (int i = 0; i < spriteRenderersToEnableOnPressed.Length; i++)
            {
                if (spriteRenderersToEnableOnPressed[i] != null)
                    spriteRenderersToEnableOnPressed[i].enabled = false;
            }
        }
    }

    private void ApplyPressedVisuals()
    {
        ResolveButtonSpriteRenderer();

        if (buttonSpriteRenderer != null && pressedSprite != null)
            buttonSpriteRenderer.sprite = pressedSprite;

        if (objectsToEnableOnPressed != null)
        {
            for (int i = 0; i < objectsToEnableOnPressed.Length; i++)
            {
                if (objectsToEnableOnPressed[i] != null)
                    objectsToEnableOnPressed[i].SetActive(true);
            }
        }

        if (spriteRenderersToEnableOnPressed != null)
        {
            for (int i = 0; i < spriteRenderersToEnableOnPressed.Length; i++)
            {
                if (spriteRenderersToEnableOnPressed[i] != null)
                    spriteRenderersToEnableOnPressed[i].enabled = true;
            }
        }
    }

    private void ResolveButtonSpriteRenderer()
    {
        if (buttonSpriteRenderer != null)
            return;

        buttonSpriteRenderer = GetComponent<SpriteRenderer>();

        if (buttonSpriteRenderer != null)
            return;

        buttonSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void EnsureKinematicRigidbody()
    {
        // Unity 2D надёжно вызывает OnTrigger2D только когда хотя бы у одного из
        // участников есть Rigidbody2D. У игрока он есть; у статичной кнопки
        // обычно нет — добавим Kinematic, чтобы не зависеть от того, какой
        // именно коллайдер активирует триггер.
        buttonBody = GetComponent<Rigidbody2D>();

        if (buttonBody == null)
            buttonBody = gameObject.AddComponent<Rigidbody2D>();

        buttonBody.bodyType = RigidbodyType2D.Kinematic;
        buttonBody.simulated = true;
        buttonBody.gravityScale = 0f;
        buttonBody.linearVelocity = Vector2.zero;
        buttonBody.angularVelocity = 0f;
        buttonBody.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    private void EnsureTriggerCollider()
    {
        Collider2D[] colliders = GetComponents<Collider2D>();

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].isTrigger)
            {
                triggerCollider = colliders[i];
                return;
            }
        }

        // Готового триггера нет — создаём BoxCollider2D на самом объекте.
        BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = triggerSize;
        box.offset = triggerOffset;

        triggerCollider = box;
        createdTriggerCollider = true;
    }

    private List<Vector2Int> ResolveCells()
    {
        switch (shapeSource)
        {
            case ShapeSource.AutoFromRenderer:
                {
                    Renderer renderer = GetComponentInChildren<Renderer>();
                    if (renderer == null)
                    {
                        Debug.LogWarning($"{nameof(GridButton)}: на '{name}' нет Renderer для AutoFromRenderer.", this);
                        return null;
                    }
                    return CellsInsideAABB(renderer.bounds);
                }

            case ShapeSource.AutoFromCollider:
                {
                    Collider2D nonTrigger = FindNonTriggerCollider();
                    if (nonTrigger == null)
                    {
                        Debug.LogWarning(
                            $"{nameof(GridButton)}: на '{name}' нет не-триггерного Collider2D для AutoFromCollider. " +
                            "Триггер-зона детекции игрока для этого не подходит — добавь отдельный коллайдер с формой кнопки.",
                            this);
                        return null;
                    }
                    return CellsInsideAABB(nonTrigger.bounds);
                }

            case ShapeSource.ManualSize:
            default:
                return CellsFromManualSize();
        }
    }

    private Collider2D FindNonTriggerCollider()
    {
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D c = colliders[i];
            if (c == null) continue;
            if (c == triggerCollider && createdTriggerCollider) continue;
            if (c.isTrigger) continue;
            return c;
        }
        return null;
    }

    private List<Vector2Int> CellsFromManualSize()
    {
        if (board == null)
            return null;

        int w = Mathf.Max(1, manualSizeInCells.x);
        int h = Mathf.Max(1, manualSizeInCells.y);

        Vector2Int pivot = board.WorldToCell(transform.position);

        List<Vector2Int> cells = new List<Vector2Int>(w * h);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                Vector2Int cell = new Vector2Int(pivot.x + x, pivot.y + y);
                if (board.IsInside(cell))
                    cells.Add(cell);
            }
        }

        return cells;
    }

    private List<Vector2Int> CellsInsideAABB(Bounds bounds)
    {
        if (board == null)
            return null;

        float pad = overlapTolerance * board.CellSize;

        Vector3 min = bounds.min + new Vector3(pad, pad, 0f);
        Vector3 max = bounds.max - new Vector3(pad, pad, 0f);

        Vector2Int minCell = board.WorldToCell(min);
        Vector2Int maxCell = board.WorldToCell(max);

        List<Vector2Int> cells = new List<Vector2Int>();

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (board.IsInside(cell))
                    cells.Add(cell);
            }
        }

        return cells;
    }
}
