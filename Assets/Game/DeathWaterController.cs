using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Зона «воды», уровень которой меняется в зависимости от действий игрока:
///  * каждый раз, когда у только что приземлённого блока ЕСТЬ сосед того же
///    цвета (по любой из 4 сторон) — это будет матчинг, блоки исчезнут, и
///    вода ОПУСКАЕТСЯ;
///  * в любом другом случае (блок встал на блок другого цвета, на статическую
///    платформу или прямо на самую нижнюю клетку сетки) — матчинг не
///    срабатывает и вода ПОДНИМАЕТСЯ;
///  * блок, провалившийся за нижний край сетки, тоже считается «попавшим
///    в воду» — вода поднимается на <see cref="TetrisBlockConfigSO.DeathWaterGrowOnBlockEnteringWater"/>
///    клеток.
///
/// Величина каждого изменения берётся из <see cref="TetrisBlockConfigSO"/>
/// и масштабируется по <see cref="TetrisGridBoard.CellSize"/>: то есть
/// «+1 клетка» = +1 клетке сетки в мировых единицах.
///
/// Бэйз-уровень воды (низ) фиксируется на старте: рост и падение меняют
/// только её верхнюю границу. Внутри объекта сохраняется текущий «надбавок»
/// в клетках, который применяется к Transform.position.y и Transform.localScale.y.
///
/// Блоки сквозь воду свободно проходят и не разрушаются: и игровые блоки,
/// и анкоры уровня остаются на своих клетках даже под водой. А вот игрок
/// тонет: как только его pivot опускается ниже верхней границы воды,
/// текущая сцена перезагружается (поведение управляется
/// <see cref="killPlayerWhenSubmerged"/> и <see cref="playerSubmergeSlack"/>).
/// Дополнительно, как и раньше, сцена перезапускается, когда вода
/// поднимается до мировой Y-координаты метки <see cref="doorMarker"/>.
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

    [Header("Player Drowning")]
    [Tooltip("Если true — игрок умирает (сцена перезагружается), как только его pivot " +
             "оказывается ниже верхней границы воды. Без этого вода вообще не убивает " +
             "игрока, даже если он полностью под водой.")]
    [SerializeField] private bool killPlayerWhenSubmerged = true;

    [Tooltip("Запас в мировых единицах: игрок считается утонувшим, когда его pivot " +
             "опускается НИЖЕ (CurrentTopY − этот запас). 0 — как только pivot ушёл " +
             "под уровень воды. Положительное значение означает «дать игроку немного " +
             "уйти под воду, прежде чем убивать».")]
    [SerializeField] private float playerSubmergeSlack = 0f;

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

    private readonly List<PlayerFacade> playerCache = new List<PlayerFacade>();
    private bool playerCacheValid;

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

    private void Update()
    {
        if (killPlayerWhenSubmerged)
            CheckPlayersDrowned();
    }

    /// <summary>
    /// Проверяет всех игроков на сцене: если pivot игрока опустился ниже
    /// верхней границы воды (с учётом запаса <see cref="playerSubmergeSlack"/>) —
    /// перезагружает сцену. Раньше игрок мог быть полностью под водой и при
    /// этом продолжать бегать и двигать блоки, потому что вода вообще не
    /// проверяла его положение.
    /// </summary>
    private void CheckPlayersDrowned()
    {
        if (!playerCacheValid)
            RebuildPlayerCache();

        if (playerCache.Count == 0)
            return;

        float drownAtY = CurrentTopY - playerSubmergeSlack;

        for (int i = 0; i < playerCache.Count; i++)
        {
            PlayerFacade player = playerCache[i];

            // Игрока могли уничтожить или перезагрузить сцену между кадрами —
            // в этом случае надо обновить кэш и выйти, чтобы не дёргать
            // RequestReload по «мёртвой» ссылке.
            if (player == null)
            {
                playerCacheValid = false;
                return;
            }

            Vector3 pos = player.transform.position;
            if (pos.y < drownAtY)
            {
                LevelReloader.RequestReload();
                return;
            }
        }
    }

    private void RebuildPlayerCache()
    {
        playerCache.Clear();

        PlayerFacade[] found = FindObjectsByType<PlayerFacade>(FindObjectsSortMode.None);
        if (found != null)
        {
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    playerCache.Add(found[i]);
            }
        }

        playerCacheValid = true;
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
        if (board != null)
        {
            int newTopRow = ComputeCurrentTopRow();

            if (!erosionInitialized)
            {
                lastErodedRow = newTopRow;
                erosionInitialized = true;
            }
            else if (newTopRow > lastErodedRow)
            {
                lastErodedRow = newTopRow;
            }
        }

        // Уже поставленные блоки под водой не разрушаются (ни залоченные
        // игроком, ни закреплённые блоки уровня). Дверь — единственный
        // триггер game-over по воде; проверяем её на каждое изменение
        // уровня воды, чтобы перезапуск произошёл точно тогда, когда вода
        // дошла до метки.
        CheckDoorFlooded();
    }

    /// <summary>
    /// Если задана метка двери и верхняя граница воды дотянулась до её
    /// мировой Y-координаты — перезагружаем сцену. Сравнение идёт напрямую
    /// по мировой высоте, а не по клеткам сетки: куда игрок поставил пустой
    /// GameObject — ровно туда вода и должна «затопить» сцену, не раньше.
    /// Это единственный триггер game-over по воде.
    /// </summary>
    private void CheckDoorFlooded()
    {
        if (doorMarker == null)
            return;

        if (CurrentTopY + 1e-4f >= doorMarker.position.y)
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

}
