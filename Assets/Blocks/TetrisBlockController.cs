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
        blockCells.Initialize(board.CellSize);

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

        bool blockedDown = movement.MoveOnGrid(
            body,
            config,
            board,
            blockCells,
            moveInput
        );

        if (blockedDown)
        {
            spawnManager.NotifyActiveBlockLocked(this);
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

        board.RegisterBlock(pivotCell, blockCells.CurrentOffsets);

        body.gravityScale = 0f;
        body.bodyType = RigidbodyType2D.Static;
        body.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    private void StopMotion()
    {
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }
}
