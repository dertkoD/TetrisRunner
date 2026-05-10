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
    [SerializeField] private Vector2Int spawnCell = new Vector2Int(5, 18);

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
        activeBlock.SetHorizontalInput(input.x);
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        if (activeBlock == null)
            return;

        activeBlock.SetHorizontalInput(0f);
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

        Vector3 spawnPosition = board.CellToWorld(spawnCell);

        TetrisBlockFacade newBlock = Instantiate(
            prefab,
            spawnPosition,
            Quaternion.identity,
            blocksParent
        );

        activeBlock = newBlock.Controller;
        activeBlock.Initialize(config, newBlock, this, board);
        activeBlock.SetControlled(true);
    }
}
