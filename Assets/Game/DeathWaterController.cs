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
///
/// Сами блоки сетки под водой НЕ разрушаются: всё, что игрок или уровень уже
/// поставили, остаётся на месте даже если оказалось под водой. Вода всё ещё
/// убивает игрока при контакте и триггерит перезагрузку сцены, когда доходит
/// до двери.
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

    [Header("Door (game-over когда вода доходит)")]
    [Tooltip("Пустой GameObject в сцене, который отмечает «дверь». Когда DeathWater " +
             "поднимается до клетки, в которой стоит этот объект, сцена перезагружается. " +
             "Если поле пустое — проверка двери выключена.")]
    [SerializeField] private Transform doorMarker;

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
        // На момент Start блоки уровня уже зарегистрированы в сетке
        // (TetrisGridLevelBlocks делает это в Start). Фиксируем стартовый
        // верхний ряд воды — от него отсчитываются дальнейшие подъёмы.
        // Сами блоки под водой не разрушаются, поэтому здесь нечего
        // эродировать «постепенно» — просто стартовая точка для проверок
        // двери.
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

        // После того как вода поднялась — отмечаем строки, которые ушли под
        // воду, и проверяем триггер двери. Сами блоки при этом НЕ стираются:
        // всё, что игрок или уровень уже поставили в сетку, остаётся на месте
        // даже если оказалось под водой.
        if (target > previousExtra)
            AdvanceFloodedRows();
    }

    /// <summary>
    /// Текущая верхняя строка сетки, центр которой находится под уровнем воды.
    /// Используется при росте воды (для проверки двери и обновления отметки
    /// «затопленного» ряда) и при старте сцены (фиксируем начальный уровень).
    /// </summary>
    private int ComputeCurrentTopRow()
    {
        if (board == null)
            return int.MinValue;

        return board.GetHighestRowAtOrBelowWorldY(CurrentTopY);
    }

    private void AdvanceFloodedRows()
    {
        if (board == null)
            return;

        int newTopRow = ComputeCurrentTopRow();

        // Если ещё не инициализировали (например, Grow вызвали до Start),
        // считаем, что текущий уровень — это «база».
        if (!erosionInitialized)
        {
            lastErodedRow = newTopRow;
            erosionInitialized = true;
            return;
        }

        if (newTopRow <= lastErodedRow)
            return;

        // Раньше здесь стирались клетки блоков, оказавшихся под уровнем воды
        // (board.EraseCellsInRowRange). Теперь уже поставленные блоки —
        // и закреплённые блоки уровня, и блоки, которые игрок уронил, —
        // под водой не разрушаются. Просто двигаем «верхнюю отметку» и
        // проверяем, не дошла ли вода до двери.
        lastErodedRow = newTopRow;

        CheckDoorFlooded();
    }

    /// <summary>
    /// Если задана метка двери и текущий уровень воды дорос до её клетки —
    /// перезагружаем сцену. «Дорос» означает, что центр клетки двери
    /// находится не выше текущей верхней границы воды.
    /// </summary>
    private void CheckDoorFlooded()
    {
        if (doorMarker == null || board == null)
            return;

        Vector2Int doorCell = board.WorldToCell(doorMarker.position);

        // Та же логика, что и для эрозии: клетка считается «накрытой», если
        // её Y-индекс не превышает текущий верхний ряд воды.
        if (doorCell.y <= lastErodedRow)
            LevelReloader.RequestReload();
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

        // Игрок при первом контакте с водой НЕ умирает мгновенно: он умирает
        // только когда полностью погружён (см. OnTriggerStay2D ниже). Это даёт
        // короткое окно, чтобы выпрыгнуть из воды до того, как голова уйдёт
        // под уровень.
        PlayerFacade player = other.GetComponent<PlayerFacade>()
                              ?? other.GetComponentInParent<PlayerFacade>();
        if (player != null)
        {
            TryKillPlayerIfSubmerged(other, player);
            return;
        }

        TryHandleFallingBlock(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other == null)
            return;

        // Пока игрок касается воды — на каждом физическом такте проверяем,
        // ушёл ли он полностью под верхнюю границу. Только тогда — смерть.
        PlayerFacade player = other.GetComponent<PlayerFacade>()
                              ?? other.GetComponentInParent<PlayerFacade>();
        if (player != null)
            TryKillPlayerIfSubmerged(other, player);
    }

    private void TryKillPlayerIfSubmerged(Collider2D playerCollider, PlayerFacade player)
    {
        // «Полностью погружён» = верхняя грань коллайдера игрока находится
        // НЕ ВЫШЕ текущей верхней границы воды. Маленький отрицательный
        // допуск (epsilon) спасает от граничного «дребезжания» по float'у,
        // когда игрок ровно по уровню воды.
        const float submersionEpsilon = 0.02f;

        if (playerCollider == null)
            return;

        float playerTopY = playerCollider.bounds.max.y;
        float waterTopY = CurrentTopY;

        if (playerTopY > waterTopY - submersionEpsilon)
            return;

        LevelReloader.RequestReload();
    }

    private void TryHandleFallingBlock(Collider2D other)
    {
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
