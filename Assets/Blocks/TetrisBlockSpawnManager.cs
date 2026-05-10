using UnityEngine;
using UnityEngine.InputSystem;

public class TetrisBlockSpawnManager : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private TetrisBlockConfigSO config;

    [Header("Scene References")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform blocksParent;
    [SerializeField] private TetrisGridBoard board;

    [Header("Spawn Cell")]
    [Tooltip("Желаемая координата (X) пивота нового блока в клетках сетки. " +
             "Y будет автоматически подобран так, чтобы блок целиком влезал в сетку сверху.")]
    [SerializeField] private Vector2Int spawnCell = new Vector2Int(5, 18);

    [Tooltip("Если true, блок всегда спавнится в верхней части поля как в классическом тетрисе. " +
             "Если false, используется ровно spawnCell (только клампится по X).")]
    [SerializeField] private bool spawnAtTopOfBoard = true;

    private TetrisBlockController activeBlock;

    private InputAction toggleSpawnAction;
    private InputAction moveAction;
    private InputAction rotateLeftAction;
    private InputAction rotateRightAction;

    private bool isRunning;

    private void Awake()
    {
        if (config == null)
        {
            Debug.LogError($"{nameof(TetrisBlockSpawnManager)}: Config is not assigned.", this);
            enabled = false;
            return;
        }

        if (board == null)
        {
            Debug.LogError($"{nameof(TetrisBlockSpawnManager)}: Board is not assigned.", this);
            enabled = false;
            return;
        }

        toggleSpawnAction = config.ToggleSpawnAction != null ? config.ToggleSpawnAction.action : null;
        moveAction = config.MoveAction != null ? config.MoveAction.action : null;
        rotateLeftAction = config.RotateLeftAction != null ? config.RotateLeftAction.action : null;
        rotateRightAction = config.RotateRightAction != null ? config.RotateRightAction.action : null;

        if (toggleSpawnAction == null || moveAction == null || rotateLeftAction == null || rotateRightAction == null)
        {
            Debug.LogError($"{nameof(TetrisBlockSpawnManager)}: One or more input actions are missing in config.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (toggleSpawnAction == null)
            return;

        toggleSpawnAction.performed += OnToggleSpawnPerformed;
        moveAction.performed += OnMovePerformed;
        moveAction.canceled += OnMoveCanceled;
        rotateLeftAction.performed += OnRotateLeftPerformed;
        rotateRightAction.performed += OnRotateRightPerformed;

        toggleSpawnAction.Enable();
        moveAction.Enable();
        rotateLeftAction.Enable();
        rotateRightAction.Enable();
    }

    private void OnDisable()
    {
        if (toggleSpawnAction == null)
            return;

        toggleSpawnAction.performed -= OnToggleSpawnPerformed;
        moveAction.performed -= OnMovePerformed;
        moveAction.canceled -= OnMoveCanceled;
        rotateLeftAction.performed -= OnRotateLeftPerformed;
        rotateRightAction.performed -= OnRotateRightPerformed;

        toggleSpawnAction.Disable();
        moveAction.Disable();
        rotateLeftAction.Disable();
        rotateRightAction.Disable();
    }

    private void FixedUpdate()
    {
        if (!isRunning)
            return;

        if (activeBlock == null)
            return;

        activeBlock.FixedTick();
    }

    public void NotifyActiveBlockLocked(TetrisBlockController block)
    {
        if (block == null)
            return;

        if (block != activeBlock)
            return;

        block.LockAndForget();

        activeBlock = null;

        if (isRunning)
            SpawnNextBlock();
    }

    private void OnToggleSpawnPerformed(InputAction.CallbackContext context)
    {
        SetRunning(!isRunning);
    }

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        if (!isRunning || activeBlock == null)
            return;

        Vector2 input = context.ReadValue<Vector2>();
        activeBlock.SetMoveInput(input);
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        if (activeBlock == null)
            return;

        activeBlock.SetMoveInput(Vector2.zero);
    }

    private void OnRotateLeftPerformed(InputAction.CallbackContext context)
    {
        if (!isRunning || activeBlock == null)
            return;

        activeBlock.Rotate(-1);
    }

    private void OnRotateRightPerformed(InputAction.CallbackContext context)
    {
        if (!isRunning || activeBlock == null)
            return;

        activeBlock.Rotate(1);
    }

    private void SetRunning(bool value)
    {
        isRunning = value;

        if (isRunning)
        {
            if (activeBlock != null && !activeBlock.IsLocked)
            {
                activeBlock.SetControlled(true);
                return;
            }

            SpawnNextBlock();
            return;
        }

        if (activeBlock != null && !activeBlock.IsLocked)
            activeBlock.FreezeInAir();
    }

    private void SpawnNextBlock()
    {
        TetrisBlockFacade[] prefabs = config.BlockPrefabs;

        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogError($"{nameof(TetrisBlockSpawnManager)}: No block prefabs assigned in config.", this);
            SetRunning(false);
            return;
        }

        int randomIndex = Random.Range(0, prefabs.Length);
        TetrisBlockFacade prefab = prefabs[randomIndex];

        if (prefab == null)
        {
            Debug.LogError($"{nameof(TetrisBlockSpawnManager)}: Block prefab at index {randomIndex} is null.", this);
            SetRunning(false);
            return;
        }

        TetrisBlockFacade newBlock = Instantiate(
            prefab,
            Vector3.zero,
            Quaternion.identity,
            blocksParent
        );

        // Префабы сделаны с произвольным масштабом и позицией — приводим к единичному
        // состоянию ещё до Initialize, чтобы клетки точно совпали с сеткой.
        newBlock.transform.localScale = Vector3.one;
        newBlock.transform.localRotation = Quaternion.identity;

        TetrisBlockController controller = newBlock.Controller;
        controller.Initialize(config, newBlock, this, board);

        Vector2Int targetCell = ResolveSpawnCell(newBlock.BlockCells);
        Vector3 spawnPosition = board.CellToWorld(targetCell);

        if (newBlock.Body != null)
            newBlock.Body.position = spawnPosition;

        newBlock.transform.position = spawnPosition;

        activeBlock = controller;
        activeBlock.SetControlled(true);
    }

    private Vector2Int ResolveSpawnCell(TetrisBlockCells blockCells)
    {
        Vector2Int desired = spawnCell;

        Vector2Int[] offsets = blockCells != null ? blockCells.CurrentOffsets : null;

        if (offsets == null || offsets.Length == 0)
            return desired;

        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int o = offsets[i];

            if (o.x < minX) minX = o.x;
            if (o.x > maxX) maxX = o.x;
            if (o.y < minY) minY = o.y;
            if (o.y > maxY) maxY = o.y;
        }

        int boardWidth = board.Width;
        int boardHeight = board.Height;

        int x = desired.x;
        int y = spawnAtTopOfBoard ? (boardHeight - 1 - maxY) : desired.y;

        // Клампим по X так, чтобы все клетки фигуры влезали в поле по горизонтали.
        if (x + minX < 0)
            x = -minX;

        if (x + maxX > boardWidth - 1)
            x = boardWidth - 1 - maxX;

        // Клампим по Y, чтобы фигура не вылетела сверху/снизу.
        if (y + maxY > boardHeight - 1)
            y = boardHeight - 1 - maxY;

        if (y + minY < 0)
            y = -minY;

        return new Vector2Int(x, y);
    }
}
