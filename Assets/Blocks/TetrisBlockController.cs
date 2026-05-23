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

    private Vector2 moveInput;
    private bool softDropActive;

    private bool initialized;
    private bool controlled;
    private bool locked;

    public bool IsLocked => locked;

    public void Initialize(
        TetrisBlockConfigSO config,
        TetrisBlockFacade facade,
        TetrisBlockSpawnManager spawnManager,
        TetrisGridBoard board)
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
        blockCells.Initialize(board.CellSize, config != null ? config.CellColorPalette : null);

        if (contactReporter != null)
            contactReporter.Initialize(config, this);

        locked = false;
        controlled = false;
        moveInput = Vector2.zero;
        initialized = true;
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

        StackBlock();
    }

    /// <summary>
    /// Сообщает менеджеру, что блок попал в DeathWater и должен исчезнуть
    /// прямо сейчас — без приземления и без регистрации в сетке. Делегирует
    /// уничтожение и планирование следующего блока спавн-менеджеру, чтобы
    /// игра не зависла без активного блока.
    /// </summary>
    public void NotifyFellIntoWater()
    {
        if (!initialized || locked)
            return;

        if (spawnManager == null)
            return;

        spawnManager.NotifyActiveBlockFellOff(this);
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

        // Сообщаем DeathWater о контакте: блок встал на блок того же цвета —
        // вода опустится, на блок другого цвета — вода поднимется. Делаем это
        // ДО ResolveMatches, иначе блок-сосед, с которым произошёл матчинг,
        // уже окажется уничтожен и мы не сможем сравнить цвета.
        NotifyDeathWaterAboutLanding(placedBlock);

        // Проверяем совпадения: если рядом стоит блок такого же цвета — оба
        // полностью исчезнут, висящие сверху осыпятся целиком, не теряя формы.
        board.ResolveMatches();

        // Контроллеру больше делать нечего — отключаем его компонент
        // (но сам объект, его коллайдер и визуал остаются жить).
        enabled = false;
    }

    /// <summary>
    /// После приземления блока сравнивает его цвет с цветами блоков, на
    /// которые он встал (т.е. блоков, стоящих ровно под его клетками-«ножками»).
    /// При попадании на блок такого же цвета — сообщает DeathWater про шринк,
    /// при попадании на блок другого цвета — про рост. Статические блоки
    /// (платформы) в подсчёте не участвуют, т.к. у них нет цвета.
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

        HashSet<TetrisPlacedBlock> belowOthers = new HashSet<TetrisPlacedBlock>();

        for (int i = 0; i < ownOffsets.Length; i++)
        {
            Vector2Int cellBelow = pivot + ownOffsets[i] + Vector2Int.down;

            // Если эта клетка тоже принадлежит самому новому блоку — это его
            // же «внутренняя» соседка, она не считается контактом с другим блоком.
            if (ownCells.Contains(cellBelow))
                continue;

            if (!board.IsInside(cellBelow))
                continue;

            TetrisPlacedBlock occupant = board.GetBlockAt(cellBelow);
            if (occupant == null || occupant == placedBlock)
                continue;

            // Статические платформы цвета не имеют (colorIndex = -1), поэтому
            // приземление на них не считается ни матчингом, ни мисматчингом.
            if (occupant.IsStatic)
                continue;

            belowOthers.Add(occupant);
        }

        if (belowOthers.Count == 0)
            return;

        bool anySameColor = false;
        bool anyDifferentColor = false;

        foreach (TetrisPlacedBlock other in belowOthers)
        {
            if (other.ColorIndex == placedBlock.ColorIndex)
                anySameColor = true;
            else
                anyDifferentColor = true;
        }

        if (anySameColor)
            dw.HandleBlockLandedOnSameColor();

        if (anyDifferentColor)
            dw.HandleBlockLandedOnDifferentColor();
    }

    private void StopMotion()
    {
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }
}
