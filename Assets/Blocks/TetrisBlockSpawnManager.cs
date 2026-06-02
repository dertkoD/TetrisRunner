using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class TetrisBlockSpawnManager : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private TetrisBlockConfigSO config;

    [Header("Scene References")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform blocksParent;
    [SerializeField] private TetrisGridBoard board;

    [Header("Spawn Fallback (используется, если spawnPoint не задан)")]
    [Tooltip("Если spawnPoint выше не задан, блок спавнится сверху сетки. " +
             "Этот X (в клетках) определяет, где именно появится пивот, " +
             "а Y подбирается так, чтобы фигура целиком влезла в верхнюю строку.")]
    [SerializeField] private int fallbackSpawnColumn = -1;

    [Tooltip("Если true и spawnPoint не задан, X спавна берётся как центр сетки. " +
             "fallbackSpawnColumn в этом случае игнорируется.")]
    [SerializeField] private bool fallbackToBoardCenter = true;

    [Header("Game Over")]
    [Tooltip("Если true — как только хотя бы одна залоченная клетка достигнет " +
             "строки спавна (или выше), текущая сцена будет перезагружена.")]
    [SerializeField] private bool reloadSceneWhenStackReachesSpawn = true;

    private bool reloadScheduled;

    private TetrisBlockController activeBlock;

    private InputAction toggleSpawnAction;
    private InputAction moveAction;
    private InputAction rotateLeftAction;
    private InputAction rotateRightAction;
    private InputAction softDropAction;

    private bool isRunning;
    private float spawnDelayTimer;
    private bool spawnPending;
    private bool externalFreeze;

    /// <summary>True, если активирована внешняя заморозка (PlayerBlockFreeze).</summary>
    public bool IsExternallyFrozen => externalFreeze;

    /// <summary>
    /// Включает/выключает внешнюю заморозку (используется PlayerBlockFreeze).
    /// Пока активна — блоки не падают и не спавнятся, текущий активный блок
    /// замирает в воздухе. На обычное состояние P (start/stop) не влияет.
    /// </summary>
    public void SetExternalFreeze(bool freeze)
    {
        if (externalFreeze == freeze)
            return;

        externalFreeze = freeze;

        if (activeBlock == null || activeBlock.IsLocked)
            return;

        if (freeze)
            activeBlock.FreezeInAir();
        else if (isRunning)
            activeBlock.SetControlled(true);
    }

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

        // Поднимаем singleton с эффектами (частицы стакинга + ударная волна).
        // Делаем это в Awake, чтобы он уже существовал к моменту, когда блоки
        // уровня (TetrisGridLevelBlocks) и спавнящиеся блоки начнут стакаться.
        BlockJuiceController.Ensure(config);

        toggleSpawnAction = config.ToggleSpawnAction != null ? config.ToggleSpawnAction.action : null;
        moveAction = config.MoveAction != null ? config.MoveAction.action : null;
        rotateLeftAction = config.RotateLeftAction != null ? config.RotateLeftAction.action : null;
        rotateRightAction = config.RotateRightAction != null ? config.RotateRightAction.action : null;
        softDropAction = config.SoftDropAction != null ? config.SoftDropAction.action : null;

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

        if (softDropAction != null)
        {
            softDropAction.performed += OnSoftDropPerformed;
            softDropAction.canceled += OnSoftDropCanceled;
            softDropAction.Enable();
        }
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

        if (softDropAction != null)
        {
            softDropAction.performed -= OnSoftDropPerformed;
            softDropAction.canceled -= OnSoftDropCanceled;
            softDropAction.Disable();
        }
    }

    private void FixedUpdate()
    {
        if (!isRunning || externalFreeze)
            return;

        // Если блока ещё нет, но включён таймер задержки — ждём, потом спавним.
        if (activeBlock == null)
        {
            if (!spawnPending)
                return;

            spawnDelayTimer -= Time.fixedDeltaTime;

            if (spawnDelayTimer <= 0f)
            {
                spawnPending = false;
                SpawnNextBlock();
            }

            return;
        }

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

        // После того как блок встал в стопку и провёлся matching/гравитация,
        // проверяем, не доросла ли стопка до уровня спавна. Если да —
        // игрок «проиграл», и текущая сцена перезагружается.
        if (CheckStackReachedSpawnRow())
            return;

        if (!isRunning)
            return;

        // Запускаем задержку перед появлением следующего блока. Если задержка
        // нулевая, спавним сразу — поведение остаётся как раньше.
        ScheduleNextSpawn();
    }

    /// <summary>
    /// Сообщает менеджеру, что текущий активный блок вышел за нижнюю границу
    /// сетки и под ним нет ни ground, ни другого блока. Блок уничтожается,
    /// после чего запускается обычная задержка перед следующим спавном.
    /// Дополнительно поднимаем уровень воды: блок «упал в саму DeathWater»
    /// (или, что то же самое в этой игре, в дно сетки) — это считается
    /// промахом игрока, и вода должна вырасти.
    /// </summary>
    public void NotifyActiveBlockFellOff(TetrisBlockController block)
    {
        if (block == null)
            return;

        if (block != activeBlock)
            return;

        activeBlock = null;

        // Запоминаем место, где блок пропал — оттуда стартует ударная волна.
        Vector3 lostPosition = block.transform.position;

        DeathWaterController dw = DeathWaterController.Instance;
        if (dw != null)
        {
            int growCells = config != null
                ? Mathf.Max(0, config.DeathWaterGrowOnBlockEnteringWater)
                : 1;

            if (growCells > 0)
            {
                BlockJuiceController juice = BlockJuiceController.Instance;

                if (juice != null && config != null && config.ShockWaveOnBlockFellToBottom)
                {
                    // Сначала волна из места исчезновения блока, потом вода
                    // поднимается.
                    juice.PlayShockWave(lostPosition, () => dw.Grow(growCells));
                }
                else
                {
                    dw.Grow(growCells);
                }
            }
        }

        if (block.gameObject != null)
            Destroy(block.gameObject);

        if (!isRunning)
            return;

        ScheduleNextSpawn();
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

    private void OnSoftDropPerformed(InputAction.CallbackContext context)
    {
        if (activeBlock == null)
            return;

        activeBlock.SetSoftDrop(true);
    }

    private void OnSoftDropCanceled(InputAction.CallbackContext context)
    {
        if (activeBlock == null)
            return;

        activeBlock.SetSoftDrop(false);
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

            // Первый блок появляется без задержки — иначе после нажатия P
            // будет неприятная пауза.
            spawnPending = false;
            spawnDelayTimer = 0f;
            SpawnNextBlock();
            return;
        }

        // Останавливая, отменяем и отложенный спавн.
        spawnPending = false;
        spawnDelayTimer = 0f;

        if (activeBlock != null && !activeBlock.IsLocked)
            activeBlock.FreezeInAir();
    }

    private void ScheduleNextSpawn()
    {
        float delay = config != null ? Mathf.Max(0f, config.SpawnDelay) : 0f;

        if (delay <= 0f)
        {
            spawnPending = false;
            spawnDelayTimer = 0f;
            SpawnNextBlock();
            return;
        }

        spawnPending = true;
        spawnDelayTimer = delay;
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
        Vector2Int[] offsets = blockCells != null ? blockCells.CurrentOffsets : null;

        int minX = 0, maxX = 0, minY = 0, maxY = 0;

        if (offsets != null && offsets.Length > 0)
        {
            minX = int.MaxValue;
            maxX = int.MinValue;
            minY = int.MaxValue;
            maxY = int.MinValue;

            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2Int o = offsets[i];

                if (o.x < minX) minX = o.x;
                if (o.x > maxX) maxX = o.x;
                if (o.y < minY) minY = o.y;
                if (o.y > maxY) maxY = o.y;
            }
        }

        int boardWidth = board.Width;
        int boardHeight = board.Height;

        int x;
        int y;

        if (spawnPoint != null)
        {
            // Если в инспекторе задан spawnPoint — конвертируем его мировую
            // позицию в клетку сетки и спавним блок ровно там.
            Vector2Int spawnPointCell = board.WorldToCell(spawnPoint.position);
            x = spawnPointCell.x;
            y = spawnPointCell.y;
        }
        else
        {
            // Нет spawnPoint — используем фоллбек: верх сетки, X либо из
            // настройки fallbackSpawnColumn, либо центр поля.
            x = fallbackToBoardCenter || fallbackSpawnColumn < 0
                ? boardWidth / 2
                : fallbackSpawnColumn;

            y = boardHeight - 1 - maxY;
        }

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

    /// <summary>
    /// Возвращает Y клетки, на уровне которой появляются новые блоки. Если в
    /// инспекторе задан <see cref="spawnPoint"/>, берём его клетку; иначе —
    /// верхнюю строку сетки.
    /// </summary>
    private int ResolveSpawnRow()
    {
        if (board == null)
            return int.MaxValue;

        if (spawnPoint != null)
            return board.WorldToCell(spawnPoint.position).y;

        return board.Height - 1;
    }

    /// <summary>
    /// Если в стопке появилась клетка на уровне строки спавна (или выше) —
    /// перезагружаем сцену. Возвращает true, если сцена начала перезагружаться,
    /// чтобы вызывающая сторона не пыталась дальше планировать спавн.
    /// </summary>
    private bool CheckStackReachedSpawnRow()
    {
        if (!reloadSceneWhenStackReachesSpawn)
            return false;

        if (reloadScheduled)
            return true;

        if (board == null)
            return false;

        int spawnRow = ResolveSpawnRow();

        if (spawnRow == int.MaxValue)
            return false;

        // Учитываем только ИГРОВЫЕ застывшие блоки. Статические препятствия
        // (платформы, двери-лифты, KillBlock и т.п.) и anchored-блоки уровня
        // могут законно занимать клетки на строке спавна, и сами по себе они
        // не должны заваливать сцену.
        if (!board.HasPlayerBlockAtOrAbove(spawnRow))
            return false;

        ReloadActiveScene();
        return true;
    }

    private void ReloadActiveScene()
    {
        if (reloadScheduled)
            return;

        reloadScheduled = true;
        isRunning = false;
        spawnPending = false;
        spawnDelayTimer = 0f;

        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex, LoadSceneMode.Single);
    }
}
