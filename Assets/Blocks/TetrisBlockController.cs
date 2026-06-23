using System.Collections.Generic;
using UnityEngine;

public class TetrisBlockController : MonoBehaviour
{
    private TetrisBlockConfigSO config;
    private TetrisBlockSpawnManager spawnManager;
    private TetrisGridBoard board;

    private Rigidbody2D body;
    private Transform blockTransform;
    private TetrisBlockMovement movement;
    private TetrisBlockRotator rotator;
    private TetrisBlockContactReporter contactReporter;
    private TetrisBlockCells blockCells;
    private Collider2D mainCollider;

    private Vector2 moveInput;
    private bool softDropActive;

    private bool initialized;
    private bool controlled;
    private bool locked;

    // Режим предпоказа: блок создан, но «спит» — физика и управление выключены,
    // стоит на месте с уменьшенным масштабом и ждёт своей очереди.
    private bool isPreview;

    // Плавный рост масштаба от previewScale до 1 в момент передачи управления.
    private bool growing;
    private float growElapsed;
    private float growDuration;
    private float growStartScale;

    private const float ActiveScale = 1f;

    public bool IsLocked => locked;

    /// <summary>True, пока блок находится в режиме предпоказа (не активен).</summary>
    public bool IsPreview => isPreview;

    public void Initialize(
        TetrisBlockConfigSO config,
        TetrisBlockFacade facade,
        TetrisBlockSpawnManager spawnManager,
        TetrisGridBoard board)
    {
        Initialize(config, facade, spawnManager, board, forcedColorIndex: -1);
    }

    /// <summary>
    /// Полная версия инициализации. Если <paramref name="forcedColorIndex"/> &gt;= 0,
    /// блок красится строго в этот цвет (его выбрал менеджер спавна, чтобы
    /// соблюсти правила рандомизации). При отрицательном значении цвет, как и
    /// раньше, выбирается случайно внутри самого блока.
    /// </summary>
    public void Initialize(
        TetrisBlockConfigSO config,
        TetrisBlockFacade facade,
        TetrisBlockSpawnManager spawnManager,
        TetrisGridBoard board,
        int forcedColorIndex)
    {
        this.config = config;
        this.spawnManager = spawnManager;
        this.board = board;

        body = facade.Body;
        blockTransform = facade.BlockTransform;
        movement = facade.Movement;
        rotator = facade.Rotator;
        contactReporter = facade.ContactReporter;
        blockCells = facade.BlockCells;

        if (body == null || blockTransform == null || movement == null || rotator == null || blockCells == null || board == null)
        {
            Debug.LogError($"{nameof(TetrisBlockController)}: One or more references are missing.", this);
            enabled = false;
            return;
        }

        // Сначала готовим тело (кинематика, без гравитации) — иначе физика может
        // успеть «уронить» блок до того, как мы выставим его в нужную клетку.
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.rotation = 0f;

        movement.Initialize();

        Color[] palette = config != null ? config.CellColorPalette : null;

        if (forcedColorIndex >= 0)
        {
            // Цвет уже выбран менеджером спавна с учётом правил рандомизации —
            // блок не должен перевыбирать его случайно.
            blockCells.Initialize(board.CellSize, palette, assignRandomColors: false);
            blockCells.SetUniformColorIndex(forcedColorIndex);
        }
        else
        {
            blockCells.Initialize(board.CellSize, palette);
        }

        if (contactReporter != null)
            contactReporter.Initialize(config, this);

        // Кинематические блоки двигаются через MovePosition, поэтому
        // WaterRW (он читает rigidbody.velocity по линейкасту вдоль
        // поверхности) их не видит, когда они полностью под водой.
        // BlockWaveProxy сам поднимает прокси у самой поверхности и
        // отдаёт волне реальную скорость блока. Если по какой-то причине
        // компонент уже сидит на префабе — повторно не добавляем.
        if (body != null && body.GetComponent<BlockWaveProxy>() == null)
            body.gameObject.AddComponent<BlockWaveProxy>();

        // Коллайдер формы нужен, чтобы понимать мировые границы блока (например,
        // для проверки выхода за зону спавна).
        mainCollider = blockCells.Collider;
        if (mainCollider == null)
            mainCollider = GetComponent<Collider2D>();

        locked = false;
        controlled = false;
        isPreview = false;
        growing = false;
        moveInput = Vector2.zero;
        initialized = true;
    }

    /// <summary>
    /// Переводит блок в режим предпоказа: выключает физику и управление,
    /// останавливает движение и уменьшает масштаб до <paramref name="previewScale"/>.
    /// Блок просто стоит на месте в точке спавна и ждёт своей очереди.
    /// </summary>
    public void EnterPreviewState(float previewScale)
    {
        if (!initialized || locked)
            return;

        isPreview = true;
        growing = false;
        controlled = false;
        moveInput = Vector2.zero;
        softDropActive = false;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            // Спящий блок-предпоказ не должен ни с кем сталкиваться: гасим симуляцию.
            body.simulated = false;
        }

        if (contactReporter != null)
            contactReporter.enabled = false;

        float s = Mathf.Max(0.0001f, previewScale);
        transform.localScale = new Vector3(s, s, 1f);
    }

    /// <summary>
    /// Выводит блок из режима предпоказа в активное состояние: включает физику,
    /// передаёт управление игроку и запускает плавный рост масштаба до 1 за
    /// <paramref name="growDurationSeconds"/> секунд (0 — мгновенно).
    /// </summary>
    public void ActivateFromPreview(float growDurationSeconds)
    {
        if (!initialized || locked)
            return;

        isPreview = false;

        if (body != null)
            body.simulated = true;

        if (contactReporter != null)
            contactReporter.enabled = true;

        growStartScale = transform.localScale.x;
        growElapsed = 0f;
        growDuration = Mathf.Max(0f, growDurationSeconds);
        growing = growDuration > 0f && growStartScale < ActiveScale - 0.0001f;

        if (!growing)
            transform.localScale = Vector3.one;

        // Управление и падение начинаются сразу — масштаб дорастает параллельно.
        SetControlled(true);
    }

    /// <summary>
    /// True, если блок полностью вышел за границы переданной зоны (их AABB
    /// больше не пересекаются). Используется менеджером спавна, чтобы понять,
    /// когда показывать следующий блок-предпоказ.
    /// </summary>
    public bool HasExitedZone(Collider2D zone)
    {
        if (zone == null || mainCollider == null)
            return false;

        return !zone.bounds.Intersects(mainCollider.bounds);
    }

    private void Update()
    {
        if (!growing)
            return;

        // Растём только пока блок реально под управлением (на паузе рост стоит).
        if (!controlled || locked)
            return;

        growElapsed += Time.deltaTime;

        float t = growDuration > 0f ? Mathf.Clamp01(growElapsed / growDuration) : 1f;
        float s = Mathf.Lerp(growStartScale, ActiveScale, t);
        transform.localScale = new Vector3(s, s, 1f);

        if (t >= 1f)
        {
            growing = false;
            transform.localScale = Vector3.one;
        }
    }

    public void FixedTick()
    {
        if (!initialized || locked || !controlled)
            return;

        TetrisBlockMoveResult result = movement.MoveOnGrid(
            body,
            config,
            board,
            blockCells,
            moveInput,
            softDropActive
        );

        if (result == TetrisBlockMoveResult.BlockedDown)
        {
            spawnManager.NotifyActiveBlockLocked(this);
        }
        else if (result == TetrisBlockMoveResult.FellOffBoard)
        {
            spawnManager.NotifyActiveBlockFellOff(this);
        }
    }

    public void SetMoveInput(Vector2 value)
    {
        if (!initialized || locked || !controlled)
            return;

        moveInput = new Vector2(
            Mathf.Clamp(value.x, -1f, 1f),
            Mathf.Clamp(value.y, -1f, 1f)
        );
    }

    // Обратная совместимость со старым API (на случай если кто-то ещё вызывает).
    public void SetHorizontalInput(float value)
    {
        SetMoveInput(new Vector2(value, moveInput.y));
    }

    /// <summary>Включает/выключает ускоренное падение (soft-drop) для активного блока.</summary>
    public void SetSoftDrop(bool value)
    {
        softDropActive = value;
    }

    public void Rotate(int direction)
    {
        if (!initialized || locked || !controlled)
            return;

        rotator.TryRotate(body, board, blockCells, direction);
    }

    public void SetControlled(bool value)
    {
        if (!initialized || locked)
            return;

        controlled = value;

        if (controlled)
            ApplyControlledPhysics();
        else
            StopMotion();
    }

    public void FreezeInAir()
    {
        if (!initialized || locked)
            return;

        controlled = false;
        moveInput = Vector2.zero;

        StopMotion();

        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
    }

    public void NotifyTouchedLockTarget()
    {
        if (!initialized || locked || !controlled)
            return;

        spawnManager.NotifyActiveBlockLocked(this);
    }

    public void LockAndForget()
    {
        if (!initialized || locked)
            return;

        controlled = false;
        locked = true;
        moveInput = Vector2.zero;

        // Если блок залочился, не успев дорасти — мгновенно доводим до полного
        // масштаба, чтобы в стопку он встал нормального размера.
        growing = false;
        transform.localScale = Vector3.one;

        StackBlock();
    }

    private void ApplyControlledPhysics()
    {
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private void StackBlock()
    {
        StopMotion();

        Vector2Int pivotCell = board.WorldToCell(body.position);
        body.position = board.CellToWorld(pivotCell);
        body.rotation = 0f;

        // НИЧЕГО не делим: блок остаётся одним GameObject со своим
        // PolygonCollider2D и формой. Просто превращаем его в TetrisPlacedBlock,
        // регистрируем в сетке и уходим.
        int blockId = TetrisGridBoard.AllocateBlockId();
        int colorIndex = blockCells != null ? blockCells.GetColorIndex(0) : 0;
        Vector2Int[] offsets = blockCells != null ? blockCells.CurrentOffsets : null;

        // Тело блока теперь стоит на месте, но остаётся Kinematic — нам ещё
        // могут двигать его сеточной гравитацией. FreezeAll специально не
        // ставим, иначе нельзя будет аккуратно переставить блок ниже.
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.gravityScale = 0f;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;

        // Контроллер больше не должен ни тикать, ни реагировать на повторные
        // контакты — блок уже залочен.
        if (contactReporter != null)
            contactReporter.enabled = false;

        // Перенесём блок под общий контейнер залоченных блоков (для порядка
        // в иерархии). Это сохраняет мировую позицию.
        Transform parent = board.PlacedBlocksParent;
        if (parent != null)
            transform.SetParent(parent, true);

        TetrisPlacedBlock placedBlock = GetComponent<TetrisPlacedBlock>();
        if (placedBlock == null)
            placedBlock = gameObject.AddComponent<TetrisPlacedBlock>();

        placedBlock.Initialize(blockId, colorIndex, pivotCell, offsets);

        board.RegisterBlock(placedBlock);

        // Juice: фонтанчик частиц цвета блока в момент стакинга (как брызги воды).
        EmitStackParticles(placedBlock);

        // Сообщаем DeathWater о контакте: если у нового блока есть сосед
        // того же цвета (в любой из 4 сторон) — сейчас сработает матчинг и
        // вода опустится; в любом другом случае (блок встал на блок другого
        // цвета, на статическую платформу или на самую нижнюю клетку сетки) —
        // вода поднимется. Делаем это ДО ResolveMatches, иначе сосед, с
        // которым произошёл матчинг, уже окажется уничтожен и мы не сможем
        // сравнить цвета.
        NotifyDeathWaterAboutLanding(placedBlock);

        // Проверяем совпадения: если рядом стоит блок такого же цвета — оба
        // полностью исчезнут, висящие сверху осыпятся целиком, не теряя формы.
        board.ResolveMatches();

        // Контроллеру больше делать нечего — отключаем его компонент
        // (но сам объект, его коллайдер и визуал остаются жить).
        enabled = false;
    }

    private static readonly Vector2Int[] FourNeighbors =
    {
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1),
    };

    /// <summary>
    /// Выбрасывает частицы цвета блока в его центре в момент стакинга.
    /// </summary>
    private void EmitStackParticles(TetrisPlacedBlock placedBlock)
    {
        BlockJuiceController juice = BlockJuiceController.Instance;
        if (juice == null)
            return;

        Color color = blockCells != null ? blockCells.GetColor(0) : Color.white;
        juice.EmitStackParticles(ComputeBlockCenter(placedBlock), color);
    }

    /// <summary>
    /// Центр блока в мировых координатах: берём центр коллайдера, а если его
    /// нет — позицию тела.
    /// </summary>
    private Vector3 ComputeBlockCenter(TetrisPlacedBlock placedBlock)
    {
        Collider2D collider = GetComponent<Collider2D>();
        if (collider == null)
            collider = GetComponentInChildren<Collider2D>();

        if (collider != null)
            return collider.bounds.center;

        if (body != null)
            return body.position;

        return transform.position;
    }

    /// <summary>
    /// После приземления блока решает, что должно произойти с DeathWater:
    ///   * если у только что залоченного блока есть сосед ТАКОГО ЖЕ цвета по
    ///     любой из 4 сторон (т.е. сейчас сработает матчинг и блоки исчезнут) —
    ///     вода ОПУСКАЕТСЯ;
    ///   * иначе (нет ни одного цветного соседа того же цвета, в т.ч. блок
    ///     просто встал на блок другого цвета, на статическую платформу
    ///     или на самую нижнюю клетку сетки) — вода ПОДНИМАЕТСЯ.
    ///
    /// Сравнение идёт по соседям со ВСЕХ сторон, а не только снизу: иначе,
    /// если матчинг сработал сбоку (например, блок съехал по сетке и встал
    /// рядом с блоком того же цвета), вода не реагировала бы вообще.
    /// Статические препятствия (платформы) в подсчёте не участвуют — у них
    /// нет цвета.
    /// </summary>
    private void NotifyDeathWaterAboutLanding(TetrisPlacedBlock placedBlock)
    {
        DeathWaterController dw = DeathWaterController.Instance;
        if (dw == null || placedBlock == null || board == null)
            return;

        Vector2Int[] ownOffsets = placedBlock.CellOffsets;
        if (ownOffsets == null || ownOffsets.Length == 0)
            return;

        Vector2Int pivot = placedBlock.PivotCell;

        HashSet<Vector2Int> ownCells = new HashSet<Vector2Int>();
        for (int i = 0; i < ownOffsets.Length; i++)
            ownCells.Add(pivot + ownOffsets[i]);

        bool hasSameColorNeighbor = false;

        for (int i = 0; i < ownOffsets.Length && !hasSameColorNeighbor; i++)
        {
            Vector2Int cell = pivot + ownOffsets[i];

            for (int n = 0; n < FourNeighbors.Length; n++)
            {
                Vector2Int neighborCell = cell + FourNeighbors[n];

                // Соседняя клетка принадлежит самому новому блоку — это его
                // внутренняя сторона, она не считается контактом с другим блоком.
                if (ownCells.Contains(neighborCell))
                    continue;

                if (!board.IsInside(neighborCell))
                    continue;

                TetrisPlacedBlock occupant = board.GetBlockAt(neighborCell);
                if (occupant == null || occupant == placedBlock)
                    continue;

                // Статические платформы цвета не имеют (colorIndex = -1),
                // поэтому к матчингу не приводят.
                if (occupant.IsStatic)
                    continue;

                if (occupant.ColorIndex != placedBlock.ColorIndex)
                    continue;

                hasSameColorNeighbor = true;
                break;
            }
        }

        if (hasSameColorNeighbor)
        {
            // Сейчас ResolveMatches уберёт оба блока — это успех игрока,
            // и вода уходит вниз. По желанию (флаг в конфиге) из места
            // схлопывания тоже можно пустить ударную волну.
            BlockJuiceController juice = BlockJuiceController.Instance;

            if (juice != null && config != null && config.ShockWaveOnSameColor)
            {
                Vector3 origin = ComputeBlockCenter(placedBlock);
                juice.PlayShockWave(origin, dw.HandleBlockLandedOnSameColor);
            }
            else
            {
                dw.HandleBlockLandedOnSameColor();
            }
        }
        else
        {
            // Блок просто застрял в стопке: встал на блок другого цвета,
            // на статическую платформу или прямо на нижнюю клетку сетки —
            // во всех этих случаях ничего не схлопнется. Сначала из места
            // приземления расходится ударная волна (если включена флагом
            // в конфиге), и только после неё поднимается вода.
            BlockJuiceController juice = BlockJuiceController.Instance;

            if (juice != null && config != null && config.ShockWaveOnDifferentColor)
            {
                Vector3 origin = ComputeBlockCenter(placedBlock);
                juice.PlayShockWave(origin, dw.HandleBlockLandedOnDifferentColor);
            }
            else
            {
                dw.HandleBlockLandedOnDifferentColor();
            }
        }
    }

    private void StopMotion()
    {
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }
}
