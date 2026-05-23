using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Зона смертельной «воды», уровень которой меняется в зависимости от
/// действий игрока:
///  * каждый раз, когда падающий блок встал на блок ДРУГОГО цвета
///    (т.е. матчинг не сработал) — вода поднимается;
///  * каждый раз, когда падающий блок встал на блок ТАКОГО ЖЕ цвета
///    (т.е. сработал матчинг) — вода опускается;
///  * если падающий блок попал прямо в DeathWater — вода поднимается.
///
/// Величина каждого изменения берётся из <see cref="TetrisBlockConfigSO"/>
/// и масштабируется по <see cref="TetrisGridBoard.CellSize"/>: то есть
/// «+1 клетка» = +1 клетке сетки в мировых единицах.
///
/// Бэйз-уровень воды (низ) фиксируется на старте: рост и падение меняют
/// только её верхнюю границу. Внутри объекта сохраняется текущий «надбавок»
/// в клетках, который применяется к Transform.position.y и Transform.localScale.y.
/// </summary>
[DisallowMultipleComponent]
public class DeathWaterController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Сетка, из которой берётся CellSize. Если пусто — найдём в сцене.")]
    [SerializeField] private TetrisGridBoard board;

    [Tooltip("Конфиг тетриса. Из него берутся значения, на сколько клеток " +
             "поднимать/опускать DeathWater на каждое событие.")]
    [SerializeField] private TetrisBlockConfigSO config;

    [Header("Behaviour")]
    [Tooltip("Минимальная высота воды в клетках. Опускание ниже этой границы " +
             "игнорируется. 0 — воду можно полностью «осушить».")]
    [SerializeField, Min(0)] private int minHeightInCells = 0;

    [Tooltip("Максимальная высота воды в клетках. 0 — без верхнего ограничения.")]
    [SerializeField, Min(0)] private int maxHeightInCells = 0;

    private static DeathWaterController instance;

    /// <summary>
    /// Глобальный доступ к контроллеру. Используется, например,
    /// <see cref="TetrisBlockController"/>, чтобы сообщить о приземлении блока.
    /// </summary>
    public static DeathWaterController Instance => instance;

    private Vector3 initialPosition;
    private Vector3 initialScale;
    private float initialBottomY;
    private float initialTopY;
    private int extraCellsAbove;
    private int lastErodedRow = int.MinValue;
    private bool erosionInitialized;
    private readonly HashSet<TetrisBlockController> alreadyEntered = new HashSet<TetrisBlockController>();

    /// <summary>Текущая верхняя граница воды в мировых координатах Y.</summary>
    public float CurrentTopY => initialTopY + extraCellsAbove * CellSize;

    /// <summary>Сколько клеток сейчас «надстроено» сверху относительно стартового объёма.</summary>
    public int ExtraCellsAbove => extraCellsAbove;

    private float CellSize => board != null ? board.CellSize : 1f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning(
                $"{nameof(DeathWaterController)}: в сцене больше одного контроллера. " +
                $"'{instance.name}' уже зарегистрирован, '{name}' будет работать как локальный.",
                this);
        }
        else
        {
            instance = this;
        }

        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        Transform t = transform;
        initialPosition = t.localPosition;
        initialScale = t.localScale;
        initialBottomY = initialPosition.y - initialScale.y * 0.5f;
        initialTopY = initialPosition.y + initialScale.y * 0.5f;

        // На старте «дельта» нулевая — DeathWater остаётся в той форме,
        // в которой расставлена в сцене.
        extraCellsAbove = 0;
    }

    private void Start()
    {
        // На момент Start блоки уровня уже зарегистрированы в сетке (их
        // TetrisGridLevelBlocks делает в Start). Если стартовый объём воды
        // покрывает какие-то клетки — они тоже эродируются «постепенно».
        // Чтобы не сжирать сразу все нижние ряды разом, начинаем эрозию
        // с того ряда, который уже накрыт водой на старте.
        lastErodedRow = ComputeCurrentTopRow();
        erosionInitialized = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>Падающий блок встал на блок другого цвета — поднимаем воду.</summary>
    public void HandleBlockLandedOnDifferentColor()
    {
        Grow(config != null ? config.DeathWaterGrowOnDifferentColorLanding : 1);
    }

    /// <summary>Падающий блок встал на блок такого же цвета — опускаем воду.</summary>
    public void HandleBlockLandedOnSameColor()
    {
        Shrink(config != null ? config.DeathWaterShrinkOnSameColorLanding : 1);
    }

    /// <summary>
    /// Падающий блок попал прямо в DeathWater. Сам управляемый блок исчезает
    /// мгновенно (через спавн-менеджер, чтобы корректно появился следующий),
    /// плюс уровень воды поднимается на N клеток (значение N — из конфига).
    /// </summary>
    public void HandleBlockFellIntoWater(TetrisBlockController controller)
    {
        if (controller != null && !alreadyEntered.Add(controller))
            return;

        Grow(config != null ? config.DeathWaterGrowOnBlockEnteringWater : 1);

        if (controller != null)
            controller.NotifyFellIntoWater();
    }

    /// <summary>Поднимает воду на <paramref name="cells"/> клеток вверх.</summary>
    public void Grow(int cells)
    {
        if (cells <= 0)
            return;

        SetExtraCellsAbove(extraCellsAbove + cells);
    }

    /// <summary>Опускает воду на <paramref name="cells"/> клеток вниз.</summary>
    public void Shrink(int cells)
    {
        if (cells <= 0)
            return;

        SetExtraCellsAbove(extraCellsAbove - cells);
    }

    private void SetExtraCellsAbove(int target)
    {
        // -extraCellsAboveMinClampedToInitialHeight — это «ноль клеток выше базы»
        // плюс предельное опускание до minHeightInCells. Считаем границу так,
        // чтобы итоговая высота никогда не уходила ниже minHeightInCells.

        int minExtra = ComputeMinExtraAbove();

        if (target < minExtra)
            target = minExtra;

        if (maxHeightInCells > 0)
        {
            int maxExtra = ComputeMaxExtraAbove();
            if (target > maxExtra)
                target = maxExtra;
        }

        if (target == extraCellsAbove)
            return;

        int previousExtra = extraCellsAbove;
        extraCellsAbove = target;
        ApplyTransform();

        // После того как вода поднялась — стираем у блоков сетки клетки,
        // которые оказались под новым уровнем воды. Шринк ничего не
        // восстанавливает: блоки, которые вода уже «съела», не возвращаются.
        if (target > previousExtra)
            ErodeNewlyCoveredRows();
    }

    /// <summary>
    /// Текущая верхняя строка сетки, центр которой находится под уровнем воды.
    /// Используется и при росте воды (нужно проредить блоки в новых строках),
    /// и при старте сцены (фиксируем начальный уровень).
    /// </summary>
    private int ComputeCurrentTopRow()
    {
        if (board == null)
            return int.MinValue;

        return board.GetHighestRowAtOrBelowWorldY(CurrentTopY);
    }

    private void ErodeNewlyCoveredRows()
    {
        if (board == null)
            return;

        int newTopRow = ComputeCurrentTopRow();

        // Если эрозию ещё не инициализировали (например, Grow вызвали до Start),
        // считаем, что текущий уровень — это «база» и сжигать пока нечего.
        if (!erosionInitialized)
        {
            lastErodedRow = newTopRow;
            erosionInitialized = true;
            return;
        }

        if (newTopRow <= lastErodedRow)
            return;

        int minRow = lastErodedRow + 1;
        int maxRow = newTopRow;

        board.EraseCellsInRowRange(minRow, maxRow);
        lastErodedRow = newTopRow;
    }

    private int ComputeMinExtraAbove()
    {
        // Стартовая высота в клетках (с округлением вниз — мы не делим клетку).
        float cellSize = CellSize;
        if (cellSize <= 0f)
            return 0;

        int initialCells = Mathf.Max(0, Mathf.FloorToInt(initialScale.y / cellSize));
        int target = minHeightInCells - initialCells;
        return target;
    }

    private int ComputeMaxExtraAbove()
    {
        float cellSize = CellSize;
        if (cellSize <= 0f)
            return 0;

        int initialCells = Mathf.Max(0, Mathf.FloorToInt(initialScale.y / cellSize));
        return Mathf.Max(0, maxHeightInCells - initialCells);
    }

    private void ApplyTransform()
    {
        float cellSize = CellSize;
        float newTopY = initialTopY + extraCellsAbove * cellSize;
        float newHeight = newTopY - initialBottomY;

        if (newHeight < 0f) newHeight = 0f;

        float newCenterY = initialBottomY + newHeight * 0.5f;

        Vector3 pos = transform.localPosition;
        pos.y = newCenterY;
        transform.localPosition = pos;

        Vector3 scale = transform.localScale;
        scale.y = newHeight;
        transform.localScale = scale;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
            return;

        // Реагируем только на ИГРОВОЙ падающий блок — статические платформы,
        // anchored-блоки уровня и уже застывшие блоки в воду не «проваливаются».
        TetrisBlockController controller = other.GetComponent<TetrisBlockController>()
                                           ?? other.GetComponentInParent<TetrisBlockController>();

        if (controller == null)
            return;

        if (controller.IsLocked || !controller.enabled)
            return;

        // Если объект уже зарегистрирован в сетке как застывший блок,
        // он не считается «свежеупавшим» — это, например, anchored-блок
        // уровня, которого вода накрыла при росте.
        TetrisPlacedBlock placed = other.GetComponent<TetrisPlacedBlock>()
                                   ?? other.GetComponentInParent<TetrisPlacedBlock>();

        if (placed != null)
            return;

        HandleBlockFellIntoWater(controller);
    }
}
