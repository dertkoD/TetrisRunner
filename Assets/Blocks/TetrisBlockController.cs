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

        // Распиливаем блок на отдельные ячейки и кладём каждую в сетку
        // как самостоятельный объект — чтобы они могли по одной исчезать
        // от совпадений по цвету и независимо падать вниз.
        Vector2Int[] offsets = blockCells.CurrentOffsets;
        Transform[] visuals = blockCells.CellVisuals;

        if (offsets != null && visuals != null)
        {
            Transform cellsParent = board.PlacedCellsParent;

            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] == null)
                    continue;

                if (i >= offsets.Length)
                    continue;

                Vector2Int cellPos = pivotCell + offsets[i];

                if (!board.IsInside(cellPos))
                    continue;

                Transform visual = visuals[i];
                int colorIndex = blockCells.GetColorIndex(i);
                Color color = blockCells.GetColor(i);
                SpriteRenderer renderer = blockCells.GetRenderer(i);

                visual.SetParent(cellsParent, true);
                visual.position = board.CellToWorld(cellPos);
                visual.rotation = Quaternion.identity;
                visual.localScale = new Vector3(board.CellSize, board.CellSize, 1f);

                TetrisPlacedCell placedCell = visual.gameObject.GetComponent<TetrisPlacedCell>();

                if (placedCell == null)
                    placedCell = visual.gameObject.AddComponent<TetrisPlacedCell>();

                placedCell.Setup(colorIndex, color, renderer);

                board.RegisterCell(cellPos, placedCell);
            }
        }

        // Запускаем разрешение совпадений: пары соседних ячеек одного цвета исчезают,
        // а оставшиеся ячейки осыпаются вниз по сетке.
        board.ResolveMatches();

        // Сам корень блока больше не нужен — все его ячейки уже отдельно живут в сетке.
        Destroy(gameObject);
    }

    private void StopMotion()
    {
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }
}
