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
            // и вода уходит вниз.
            dw.HandleBlockLandedOnSameColor();
        }
        else
        {
            // Блок просто застрял в стопке: встал на блок другого цвета,
            // на статическую платформу или прямо на нижнюю клетку сетки —
            // во всех этих случаях ничего не схлопнется, и вода поднимается.
            dw.HandleBlockLandedOnDifferentColor();
        }
    }

    private void StopMotion()
    {
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }
}
