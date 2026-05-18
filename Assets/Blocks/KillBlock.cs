using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Kill block" — гильотина: висит над опорной платформой, занимает клетки
/// сетки (как обычный статический блок), и при появлении игрока над платформой
/// падает вниз клетка-за-клеткой. Каждый шаг падения честно перерегистрируется
/// в <see cref="TetrisGridBoard"/>, поэтому сетка всегда видит, какие клетки
/// сейчас заняты Kill Block'ом.
///
/// При падении за блоком "едут" привязанные дочерние объекты
/// (<see cref="attachedRiders"/>) — например, спрайт шипов
/// или его <c>Deathzone</c>, висящие сверху.
///
/// Поведение при столкновении:
///   * физический <see cref="Physics2D.BoxCast"/> на 1 клетку вниз ищет игрока.
///     Если попал — наносит урон (<see cref="PlayerHealth.TakeDamage"/>) и при
///     необходимости телепортирует игрока в чекпоинт.
///   * затем пытается сделать клеточный шаг через
///     <see cref="TetrisGridBoard.TryMovePlacedBlockWithStack"/>. Если сетка
///     не пускает (например, игрок заранее уложил тетрисный блок на путь) —
///     Kill Block "заедает" и остаётся в новой клетке.
///
/// Если на том же GameObject обнаружен <see cref="TetrisGridStaticPlatform"/>,
/// он автоматически отключается, чтобы регистрация клеток шла только через
/// KillBlock (иначе будет два блока на тех же клетках).
/// </summary>
[DisallowMultipleComponent]
public class KillBlock : MonoBehaviour
{
    public enum State
    {
        Idle,
        Falling,
        Jammed,
    }

    public enum CellSource
    {
        AutoFromCollider,
        AutoFromRenderer,
        ManualSize,
        ExplicitCells,
    }

    [Header("Refs")]
    [Tooltip("Коллайдер платформы, на которую игрок встаёт. Когда игрок появляется в " +
             "детекторной зоне над этой платформой — Kill Block срывается вниз.")]
    [SerializeField] private Collider2D triggerPlatformCollider;

    [Tooltip("Дополнительные объекты, которые едут вместе с Kill Block (например, " +
             "спрайт шипов и его Deathzone). Их позиции сдвигаются на ту же дельту, " +
             "что и Kill Block, при каждом клеточном шаге.")]
    [SerializeField] private Transform[] attachedRiders;

    [Tooltip("Автоматически найти и подцепить к Kill Block все объекты, которые " +
             "находятся в полосе ровно над верхним краем коллайдера (1 клетка вверх). " +
             "Так шипы / Deathzone, лежащие сверху, поедут вниз даже без явной привязки.")]
    [SerializeField] private bool autoDiscoverRidersOnTop = true;

    [Tooltip("Высота полосы поиска райдеров над верхним краем Kill Block, в клетках.")]
    [SerializeField, Min(0.1f)] private float autoDiscoverHeightCells = 1.0f;

    [Header("Grid")]
    [Tooltip("Сетка, в которой Kill Block занимает клетки. Если пусто — найдём в сцене сами.")]
    [SerializeField] private TetrisGridBoard board;

    [Tooltip("Как определять занимаемые клетки на старте.")]
    [SerializeField] private CellSource cellSource = CellSource.AutoFromCollider;

    [Tooltip("AutoFromCollider/Renderer: насколько ужать AABB по краям, чтобы не цеплять соседние клетки. " +
             "Доля от размера клетки.")]
    [SerializeField, Range(0f, 0.5f)] private float overlapTolerance = 0.05f;

    [Tooltip("ManualSize: размер в клетках. Опорная клетка — та, в которой стоит Transform Kill Block'а, " +
             "форма растёт вправо/вверх.")]
    [SerializeField] private Vector2Int manualSizeInCells = new Vector2Int(6, 1);

    [Tooltip("ExplicitCells: вручную заданный список клеток сетки.")]
    [SerializeField] private Vector2Int[] explicitCells;

    [Header("Detection")]
    [Tooltip("Высота зоны над платформой, в которой ищется игрок (мировые единицы).")]
    [SerializeField, Min(0.05f)] private float detectionHeight = 1.5f;

    [Tooltip("Слои, которые считаются игроком. Если оставить Nothing — " +
             "детектор ищет компонент PlayerFacade на любом объекте сверху от платформы.")]
    [SerializeField] private LayerMask playerLayers = 0;

    [Header("Fall")]
    [Tooltip("Скорость падения, клеток в секунду. 'Резко' = 20..40. " +
             "При cellSize = 1 это совпадает с мировыми единицами в секунду.")]
    [SerializeField, Min(0.5f)] private float fallSpeedCellsPerSec = 30f;

    [Tooltip("Слои, на которых ищется НЕ-grid препятствие (в основном — игрок). " +
             "Если пусто (Nothing) — обрабатываются все слои; триггер-коллайдеры всегда игнорируются.")]
    [SerializeField] private LayerMask physicsScanLayers = ~0;

    [Header("Damage")]
    [Tooltip("Сколько HP отнимать у игрока при прямом попадании.")]
    [SerializeField, Min(0)] private int damage = 1;

    [Tooltip("После прямого попадания также телепортировать игрока в его " +
             "последнюю точку прыжка (PlayerRespawnAnchor).")]
    [SerializeField] private bool teleportPlayerOnHit = true;

    [Tooltip("Минимальный интервал в секундах между повторными ударами по тому же игроку. " +
             "Защищает от повторного нанесения урона за один и тот же 'проход', пока " +
             "телепорт ещё не успел увести игрока из колонки падения.")]
    [SerializeField, Min(0f)] private float hitCooldown = 0.2f;

    [Header("Reset")]
    [Tooltip("Через сколько секунд после остановки Kill Block вернётся на исходную позицию. " +
             "0 — не возвращать (одноразовая ловушка).")]
    [SerializeField, Min(0f)] private float autoResetDelay = 0f;

    [Tooltip("Если true — Kill Block возвращается на исходную позицию автоматически, " +
             "когда игрок выходит из детекторной зоны (после того как блок успел остановиться).")]
    [SerializeField] private bool resetWhenPlayerLeavesArea = false;

    [Header("Debug")]
    [Tooltip("Включи на время настройки — будут логи в консоль (триггер, падение, попадание, заедание, ресет).")]
    [SerializeField] private bool verboseLogs = true;

    private Collider2D ownCollider;
    private Rigidbody2D ownBody;
    private TetrisPlacedBlock placedBlock;

    private Vector3 startWorldPosition;
    private Quaternion startWorldRotation;
    private Vector2Int startPivotCell;
    private Vector3 visualOffset;

    // Полный список райдеров (привязанные вручную + найденные автоматически),
    // плюс их стартовые offset'ы относительно Kill Block (для ResetToStart).
    private readonly List<Transform> riders = new List<Transform>();
    private readonly List<Vector3> riderOffsets = new List<Vector3>();

    private State state = State.Idle;
    private float resetTimer;
    private float cellAccumulator;
    private PlayerFacade lastHitPlayer;
    private float lastHitTime = -999f;

    // Буферы.
    private readonly Collider2D[] overlapBuffer = new Collider2D[16];
    private readonly RaycastHit2D[] castBuffer = new RaycastHit2D[16];

    public State CurrentState => state;

    private void Reset()
    {
        EnsureCollider();
        EnsureKinematicRigidbody();
    }

    private void Awake()
    {
        EnsureCollider();
        EnsureKinematicRigidbody();
        DisableConflictingStaticPlatform();

        startWorldPosition = transform.position;
        startWorldRotation = transform.rotation;
    }

    private void Start()
    {
        if (board == null)
            board = FindFirstObjectByType<TetrisGridBoard>();

        if (board == null)
        {
            Debug.LogWarning($"{nameof(KillBlock)} '{name}': TetrisGridBoard не найден — Kill Block не будет занимать клетки сетки.", this);
            BuildRiderList();
            return;
        }

        List<Vector2Int> cells = ResolveCells();
        if (cells == null || cells.Count == 0)
        {
            Debug.LogWarning($"{nameof(KillBlock)} '{name}': не удалось определить клетки.", this);
            BuildRiderList();
            return;
        }

        Vector2Int pivot = cells[0];
        Vector2Int[] offsets = new Vector2Int[cells.Count];
        for (int i = 0; i < cells.Count; i++)
            offsets[i] = cells[i] - pivot;

        startPivotCell = pivot;

        // Запоминаем визуальный сдвиг между Transform и центром pivot-клетки —
        // его нужно будет восстанавливать после каждого шага, потому что
        // TryMovePlacedBlockWithStack снапает Transform в центр клетки.
        Vector3 pivotWorld = board.CellToWorld(pivot);
        visualOffset = transform.position - pivotWorld;
        visualOffset.z = 0f;

        placedBlock = GetComponent<TetrisPlacedBlock>();
        if (placedBlock == null)
            placedBlock = gameObject.AddComponent<TetrisPlacedBlock>();

        placedBlock.Initialize(TetrisGridBoard.AllocateBlockId(), -1, pivot, offsets);
        placedBlock.MarkAsStatic();
        board.RegisterBlock(placedBlock);

        if (verboseLogs)
            Debug.Log(
                $"{nameof(KillBlock)} '{name}': зарегистрирован в сетке. Cells={cells.Count}, pivot={pivot}, visualOffset={visualOffset}.",
                this);

        BuildRiderList();
    }

    private void OnDestroy()
    {
        if (board != null && placedBlock != null)
            board.UnregisterBlock(placedBlock);
    }

    private void EnsureCollider()
    {
        ownCollider = GetComponent<Collider2D>();
        if (ownCollider == null)
        {
            ownCollider = gameObject.AddComponent<BoxCollider2D>();
            Debug.LogWarning(
                $"{nameof(KillBlock)}: на '{name}' не было Collider2D — добавлен BoxCollider2D автоматически.",
                this);
        }
    }

    private void EnsureKinematicRigidbody()
    {
        ownBody = GetComponent<Rigidbody2D>();
        if (ownBody == null)
            ownBody = gameObject.AddComponent<Rigidbody2D>();

        ownBody.bodyType = RigidbodyType2D.Kinematic;
        ownBody.simulated = true;
        ownBody.gravityScale = 0f;
        ownBody.linearVelocity = Vector2.zero;
        ownBody.angularVelocity = 0f;
        ownBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        ownBody.interpolation = RigidbodyInterpolation2D.None;
    }

    private void DisableConflictingStaticPlatform()
    {
        // TetrisGridStaticPlatform сам зарегистрирует клетки и больше их не
        // обновляет. Это конфликтует с KillBlock (мы хотим двигать клетки
        // при падении). Поэтому отключаем — KillBlock делает всю работу сам.
        TetrisGridStaticPlatform staticPlatform = GetComponent<TetrisGridStaticPlatform>();
        if (staticPlatform != null && staticPlatform.enabled)
        {
            staticPlatform.enabled = false;
            if (verboseLogs)
                Debug.Log(
                    $"{nameof(KillBlock)} '{name}': обнаружен TetrisGridStaticPlatform — отключаю, " +
                    "KillBlock сам управляет регистрацией клеток.",
                    this);
        }
    }

    private void BuildRiderList()
    {
        riders.Clear();
        riderOffsets.Clear();

        // 1) Явно заданные в инспекторе.
        if (attachedRiders != null)
        {
            for (int i = 0; i < attachedRiders.Length; i++)
            {
                Transform t = attachedRiders[i];
                if (t == null) continue;
                if (riders.Contains(t)) continue;
                riders.Add(t);
            }
        }

        // 2) Автоматический поиск над верхним краем коллайдера. Берём всё, что
        //    НЕ относится к нам самим, НЕ игрок и НЕ зарегистрированный в сетке
        //    блок (тот уедет через carry-stack).
        if (autoDiscoverRidersOnTop && ownCollider != null)
        {
            float cellSize = board != null ? board.CellSize : 1f;
            float band = Mathf.Max(0.1f, autoDiscoverHeightCells) * cellSize;

            Bounds b = ownCollider.bounds;
            Vector2 center = new Vector2(b.center.x, b.max.y + band * 0.5f);
            Vector2 size = new Vector2(Mathf.Max(0.05f, b.size.x), band);

            int hitCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, overlapBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D c = overlapBuffer[i];
                if (c == null || c == ownCollider) continue;
                if (c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<PlayerFacade>() != null) continue;
                if (c.GetComponentInParent<TetrisPlacedBlock>() != null) continue;

                Transform t = c.transform;
                if (!riders.Contains(t))
                    riders.Add(t);
            }

            if (verboseLogs && hitCount > 0)
                Debug.Log($"{nameof(KillBlock)} '{name}': авто-подцеп нашёл {riders.Count} объект(ов) сверху.", this);
        }

        // Запоминаем offset каждого райдера для ResetToStart.
        for (int i = 0; i < riders.Count; i++)
        {
            Transform t = riders[i];
            riderOffsets.Add(t != null ? t.position - transform.position : Vector3.zero);
        }
    }

    private void FixedUpdate()
    {
        switch (state)
        {
            case State.Idle:
                if (IsPlayerOnTriggerPlatform())
                    TransitionToFalling();
                break;

            case State.Falling:
                FallTick();
                break;

            case State.Jammed:
                TickJammed();
                break;
        }
    }

    private bool IsPlayerOnTriggerPlatform()
    {
        if (triggerPlatformCollider == null)
            return false;

        Bounds b = triggerPlatformCollider.bounds;
        Vector2 center = new Vector2(b.center.x, b.max.y + detectionHeight * 0.5f);
        Vector2 size = new Vector2(Mathf.Max(0.05f, b.size.x), detectionHeight);

        if (playerLayers.value != 0)
        {
            int filteredCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, overlapBuffer, playerLayers);
            for (int i = 0; i < filteredCount; i++)
            {
                if (overlapBuffer[i] != null)
                    return true;
            }
            return false;
        }

        int hitCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, overlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D c = overlapBuffer[i];
            if (c == null) continue;
            if (c.GetComponent<PlayerFacade>() != null) return true;
            if (c.GetComponentInParent<PlayerFacade>() != null) return true;
        }

        return false;
    }

    private void TransitionToFalling()
    {
        state = State.Falling;
        cellAccumulator = 0f;
        if (verboseLogs)
            Debug.Log($"{nameof(KillBlock)} '{name}': игрок над платформой — падаю.", this);
    }

    private void FallTick()
    {
        cellAccumulator += fallSpeedCellsPerSec * Time.fixedDeltaTime;
        int safety = 16;
        while (cellAccumulator >= 1f && state == State.Falling && safety-- > 0)
        {
            cellAccumulator -= 1f;
            DropOneCell();
        }
    }

    /// <summary>Пытается опуститься на одну клетку вниз. Сначала проверяет игрока через BoxCast, потом — сетку.</summary>
    private void DropOneCell()
    {
        float cellSize = board != null ? board.CellSize : 1f;

        // 1) Физический скан на игрока (он не лежит в сетке). При попадании
        //    наносим урон + телепортируем игрока в чекпоинт, но НЕ
        //    останавливаемся — KillBlock продолжает падать дальше, пока не
        //    встретит реальный блок / Ground / низ сетки. Так и просил геймдиз.
        if (TryHitPlayerOneCellBelow(cellSize, out PlayerFacade hitPlayer, out _))
        {
            ApplyHitToPlayer(hitPlayer);
            // Сразу проходим к шагу в сетке ниже — игрок уже улетел в чекпоинт.
        }

        // 2) Шаг в сетке (или без неё, если board не задан).
        if (board != null && placedBlock != null)
        {
            Vector3 oldPos = transform.position;
            bool moved = board.TryMovePlacedBlockWithStack(placedBlock, Vector2Int.down);

            if (moved)
            {
                // TryMove… снапает Transform в центр новой pivot-клетки. Восстановим
                // наш визуальный сдвиг — иначе спрайт прыгает в центр клетки.
                Vector3 desired = board.CellToWorld(placedBlock.PivotCell) + visualOffset;
                transform.position = desired;
                if (ownBody != null) ownBody.position = desired;

                MoveRidersBy(transform.position - oldPos);
                return;
            }

            // Шаг не удался. Разбираемся: реальное препятствие или дно сетки.
            if (WouldFallOffGridBottom())
            {
                if (verboseLogs)
                    Debug.Log($"{nameof(KillBlock)} '{name}': вышел за нижнюю границу сетки без опоры — самоуничтожение вместе с райдерами.", this);
                DestroySelfAndRiders();
                return;
            }

            if (verboseLogs)
                Debug.Log($"{nameof(KillBlock)} '{name}': заело на сетке (целевые клетки заняты).", this);
            EnterJammed();
            return;
        }

        // Fallback: нет сетки — просто двигаем Transform.
        Vector3 fallbackOld = transform.position;
        Vector3 fallbackNew = fallbackOld + Vector3.down * cellSize;
        transform.position = fallbackNew;
        if (ownBody != null) ownBody.position = fallbackNew;
        MoveRidersBy(fallbackNew - fallbackOld);
    }

    private bool TryHitPlayerOneCellBelow(float cellSize, out PlayerFacade player, out float hitDistance)
    {
        player = null;
        hitDistance = 0f;

        if (ownCollider == null)
            return false;

        Vector2 origin = transform.position;
        Vector2 size = ownCollider.bounds.size;

        int hitCount;
        if (physicsScanLayers.value != 0)
        {
            hitCount = Physics2D.BoxCastNonAlloc(origin, size, 0f, Vector2.down, castBuffer, cellSize, physicsScanLayers);
        }
        else
        {
            hitCount = Physics2D.BoxCastNonAlloc(origin, size, 0f, Vector2.down, castBuffer, cellSize);
        }

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D h = castBuffer[i];
            if (h.collider == null) continue;
            if (h.collider == ownCollider) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.isTrigger) continue;
            // Игнорируем коллайдеры наших же райдеров (например, спрайт шипов).
            if (IsRiderCollider(h.collider)) continue;

            PlayerFacade pf = h.collider.GetComponent<PlayerFacade>()
                              ?? h.collider.GetComponentInParent<PlayerFacade>();
            if (pf == null)
                continue;

            // Берём самое ближнее попадание в игрока.
            if (player == null || h.distance < hitDistance)
            {
                player = pf;
                hitDistance = h.distance;
            }
        }

        return player != null;
    }

    private bool IsRiderCollider(Collider2D c)
    {
        if (riders == null || riders.Count == 0) return false;
        Transform t = c.transform;
        for (int i = 0; i < riders.Count; i++)
        {
            Transform rider = riders[i];
            if (rider == null) continue;
            if (t == rider) return true;
            if (t.IsChildOf(rider)) return true;
        }
        return false;
    }

    private void MoveRidersBy(Vector3 worldDelta)
    {
        if (riders == null || riders.Count == 0)
            return;

        worldDelta.z = 0f;
        for (int i = 0; i < riders.Count; i++)
        {
            Transform t = riders[i];
            if (t == null) continue;
            t.position += worldDelta;
        }
    }

    private void ApplyHitToPlayer(PlayerFacade pf)
    {
        if (pf == null) return;

        // Защита от повторного удара по тому же игроку за один "пролёт":
        // BoxCast мог зацепить игрока несколько тактов подряд, пока он не
        // успел телепортироваться, или соседняя клетка ловит его ещё раз.
        if (pf == lastHitPlayer && Time.time - lastHitTime < hitCooldown)
            return;

        lastHitPlayer = pf;
        lastHitTime = Time.time;

        PlayerHealth health = pf.Health != null ? pf.Health : pf.GetComponent<PlayerHealth>();
        if (health != null && damage > 0)
            health.TakeDamage(damage);

        if (teleportPlayerOnHit)
        {
            PlayerRespawnAnchor anchor = pf.RespawnAnchor != null
                ? pf.RespawnAnchor
                : pf.GetComponent<PlayerRespawnAnchor>();
            if (anchor != null)
                anchor.Respawn();
        }

        if (verboseLogs)
            Debug.Log($"{nameof(KillBlock)} '{name}': попал в игрока. damage={damage}, teleport={teleportPlayerOnHit}. Падение продолжается.", this);
    }

    private void EnterJammed()
    {
        state = State.Jammed;
        resetTimer = 0f;
        cellAccumulator = 0f;
    }

    /// <summary>
    /// Проверяет: после шага вниз все клетки Kill Block оказались бы либо ниже
    /// сетки, либо внутри неё, но пустыми? Если да — это "пролетел сквозь сетку",
    /// без реальной опоры. Если хотя бы одна клетка ВНУТРИ сетки занята чем-то
    /// чужим — это нормальное застревание.
    /// </summary>
    private bool WouldFallOffGridBottom()
    {
        if (board == null || placedBlock == null)
            return false;

        Vector2Int[] offsets = placedBlock.CellOffsets;
        if (offsets == null || offsets.Length == 0)
            return false;

        // Свои клетки исключаем — они в новой позиции могут пересекаться с
        // самими собой (для блоков высотой 2+ клеток).
        HashSet<Vector2Int> selfCells = new HashSet<Vector2Int>(offsets.Length);
        Vector2Int pivot = placedBlock.PivotCell;
        for (int i = 0; i < offsets.Length; i++)
            selfCells.Add(pivot + offsets[i]);

        Vector2Int newPivot = pivot + Vector2Int.down;
        bool anyBelowBoard = false;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int cell = newPivot + offsets[i];

            if (board.IsInside(cell))
            {
                // Реальное препятствие — это НЕ собственные клетки.
                if (board.IsOccupied(cell) && !selfCells.Contains(cell))
                    return false;
                continue;
            }

            // Клетка снаружи. Боковой выход не считается падением, только нижний.
            if (cell.y >= 0)
                return false;

            anyBelowBoard = true;
        }

        return anyBelowBoard;
    }

    private void DestroySelfAndRiders()
    {
        // Снимаем регистрацию в сетке заранее: после Destroy OnDestroy всё равно
        // снимет, но лучше освободить клетки до удаления связанных райдеров.
        if (board != null && placedBlock != null)
        {
            board.UnregisterBlock(placedBlock);
            placedBlock = null;
        }

        // Уничтожаем райдеров (шипы, deathzone и т.п.) — они визуально часть Kill Block.
        if (riders != null)
        {
            for (int i = 0; i < riders.Count; i++)
            {
                Transform t = riders[i];
                if (t == null) continue;
                Destroy(t.gameObject);
            }
            riders.Clear();
            riderOffsets.Clear();
        }

        Destroy(gameObject);
    }

    private void TickJammed()
    {
        if (resetWhenPlayerLeavesArea && !IsPlayerOnTriggerPlatform())
        {
            ResetToStart();
            return;
        }

        if (autoResetDelay <= 0f)
            return;

        resetTimer += Time.fixedDeltaTime;
        if (resetTimer >= autoResetDelay)
            ResetToStart();
    }

    private void ResetToStart()
    {
        // Возвращаемся на исходную позицию: и Transform, и сетку, и райдеров.
        Vector3 oldPos = transform.position;
        transform.position = startWorldPosition;
        transform.rotation = startWorldRotation;
        if (ownBody != null)
        {
            ownBody.position = startWorldPosition;
            ownBody.linearVelocity = Vector2.zero;
            ownBody.angularVelocity = 0f;
        }

        if (board != null && placedBlock != null)
        {
            // Снимаем со всех текущих клеток и регистрируем заново в стартовых.
            board.UnregisterBlock(placedBlock);

            Vector2Int[] offsets = placedBlock.CellOffsets;
            Vector3 pivotWorld = board.CellToWorld(startPivotCell);

            // Через MoveToCell — для согласованности с board.RegisterBlock.
            placedBlock.MoveToCell(startPivotCell, pivotWorld);
            // SetPivot переписал transform на pivotWorld — вернём визуальный сдвиг.
            transform.position = pivotWorld + visualOffset;
            if (ownBody != null) ownBody.position = transform.position;

            board.RegisterBlock(placedBlock);
        }

        // Райдеров возвращаем по сохранённому offset'у (и явные, и авто-найденные).
        for (int i = 0; i < riders.Count && i < riderOffsets.Count; i++)
        {
            Transform t = riders[i];
            if (t == null) continue;
            t.position = transform.position + riderOffsets[i];
        }

        state = State.Idle;
        resetTimer = 0f;
        cellAccumulator = 0f;

        if (verboseLogs)
            Debug.Log($"{nameof(KillBlock)} '{name}': сброс на исходную позицию (с {oldPos}).", this);
    }

    /// <summary>Программный сброс блока на исходную позицию (например, при респавне игрока).</summary>
    public void ForceReset()
    {
        ResetToStart();
    }

    // ───── Cell resolution (как в TetrisGridStaticPlatform/MovingPlatform) ─────

    private List<Vector2Int> ResolveCells()
    {
        switch (cellSource)
        {
            case CellSource.ExplicitCells:
                return explicitCells != null ? new List<Vector2Int>(explicitCells) : null;

            case CellSource.ManualSize:
                return CellsFromManualSize();

            case CellSource.AutoFromRenderer:
                {
                    Renderer renderer = GetComponentInChildren<Renderer>();
                    if (renderer == null)
                    {
                        Debug.LogWarning($"{nameof(KillBlock)}: на '{name}' нет Renderer для AutoFromRenderer.", this);
                        return null;
                    }
                    return CellsInsideAABB(renderer.bounds);
                }

            case CellSource.AutoFromCollider:
            default:
                {
                    Collider2D collider = GetComponentInChildren<Collider2D>();
                    if (collider == null)
                    {
                        Debug.LogWarning($"{nameof(KillBlock)}: на '{name}' нет Collider2D для AutoFromCollider.", this);
                        return null;
                    }
                    return CellsInsideAABB(collider.bounds);
                }
        }
    }

    private List<Vector2Int> CellsFromManualSize()
    {
        int w = Mathf.Max(1, manualSizeInCells.x);
        int h = Mathf.Max(1, manualSizeInCells.y);

        Vector2Int pivot = board.WorldToCell(transform.position);
        List<Vector2Int> cells = new List<Vector2Int>(w * h);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                Vector2Int cell = new Vector2Int(pivot.x + x, pivot.y + y);
                if (board.IsInside(cell))
                    cells.Add(cell);
            }
        return cells;
    }

    private List<Vector2Int> CellsInsideAABB(Bounds bounds)
    {
        float pad = overlapTolerance * board.CellSize;
        Vector3 min = bounds.min + new Vector3(pad, pad, 0f);
        Vector3 max = bounds.max - new Vector3(pad, pad, 0f);

        Vector2Int minCell = board.WorldToCell(min);
        Vector2Int maxCell = board.WorldToCell(max);

        List<Vector2Int> cells = new List<Vector2Int>();
        for (int x = minCell.x; x <= maxCell.x; x++)
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (board.IsInside(cell))
                    cells.Add(cell);
            }
        return cells;
    }

    private void OnDrawGizmosSelected()
    {
        if (triggerPlatformCollider == null)
            return;

        Bounds b = triggerPlatformCollider.bounds;
        Vector3 center = new Vector3(b.center.x, b.max.y + detectionHeight * 0.5f, 0f);
        Vector3 size = new Vector3(b.size.x, detectionHeight, 0.01f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireCube(center, size);
    }
}
