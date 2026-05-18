using UnityEngine;

/// <summary>
/// "Kill block" — гильотина: висит над опорной платформой и срывается вниз,
/// когда игрок встаёт на эту платформу. Если по пути есть препятствие
/// (например, тетрисный блок, который игрок заранее уложил на платформу),
/// блок "заедает" на этом препятствии и не доходит до игрока.
///
/// При прямом попадании в игрока:
///   * вызывает <see cref="PlayerHealth.TakeDamage"/>;
///   * опционально — телепортирует игрока в чекпоинт через <see cref="PlayerRespawnAnchor.Respawn"/>.
///
/// Сам объект — обычный спрайт с Collider2D. Скрипт автоматически добавит
/// Kinematic Rigidbody2D и Collider2D, если их нет.
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

    [Header("Refs")]
    [Tooltip("Коллайдер платформы, на которую игрок встаёт. Когда игрок появляется в " +
             "детекторной зоне над этой платформой — Kill Block срывается вниз.")]
    [SerializeField] private Collider2D triggerPlatformCollider;

    [Header("Detection")]
    [Tooltip("Высота зоны над платформой, в которой ищется игрок (мировые единицы).")]
    [SerializeField, Min(0.05f)] private float detectionHeight = 1.5f;

    [Tooltip("Слои, которые считаются игроком. Если оставить Nothing — " +
             "детектор ищет компонент PlayerFacade на любом объекте сверху от платформы.")]
    [SerializeField] private LayerMask playerLayers = 0;

    [Header("Fall")]
    [Tooltip("Скорость падения, мировых единиц в секунду. 'Резко' = 25..50.")]
    [SerializeField, Min(1f)] private float fallSpeed = 30f;

    [Tooltip("Если по дороге вниз нашли коллайдер на этих слоях — Kill Block " +
             "'заедает' (останавливается) на нём. По умолчанию — всё. Триггер-коллайдеры " +
             "игнорируются автоматически.")]
    [SerializeField] private LayerMask jamLayers = ~0;

    [Header("Damage")]
    [Tooltip("Сколько HP отнимать у игрока при прямом попадании.")]
    [SerializeField, Min(0)] private int damage = 1;

    [Tooltip("После прямого попадания также телепортировать игрока в его " +
             "последнюю точку прыжка (PlayerRespawnAnchor).")]
    [SerializeField] private bool teleportPlayerOnHit = true;

    [Tooltip("Если true — после остановки игрок всё равно может пройти под Kill Block, " +
             "так как коллайдер блока переключается на trigger. По умолчанию false — " +
             "блок остаётся физическим препятствием там, где упал.")]
    [SerializeField] private bool becomeNonBlockingAfterStop = false;

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
    private Vector3 startPosition;
    private Quaternion startRotation;
    private State state = State.Idle;
    private float resetTimer;
    private bool originalIsTrigger;

    // Буфер для OverlapBox/BoxCast, чтобы не аллоцировать каждый кадр.
    private readonly RaycastHit2D[] castBuffer = new RaycastHit2D[16];
    private readonly Collider2D[] overlapBuffer = new Collider2D[16];

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
        startPosition = transform.position;
        startRotation = transform.rotation;
        originalIsTrigger = ownCollider != null && ownCollider.isTrigger;
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

    private void FixedUpdate()
    {
        switch (state)
        {
            case State.Idle:
                if (IsPlayerOnTriggerPlatform())
                    TransitionToFalling();
                break;

            case State.Falling:
                FallStep();
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

        // Если задан конкретный layer-mask — используем его.
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

        // Иначе — ищем PlayerFacade на любом коллайдере в зоне.
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
        if (verboseLogs)
            Debug.Log($"{nameof(KillBlock)} '{name}': игрок над платформой — падаю.", this);
    }

    private void FallStep()
    {
        if (ownCollider == null)
        {
            state = State.Jammed;
            return;
        }

        float distance = fallSpeed * Time.fixedDeltaTime;
        Vector2 origin = transform.position;
        Vector2 size = ownCollider.bounds.size;

        int hitCount = Physics2D.BoxCastNonAlloc(origin, size, 0f, Vector2.down, castBuffer, distance);

        RaycastHit2D bestHit = default;
        bool hasBest = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D h = castBuffer[i];
            if (h.collider == null) continue;
            if (h.collider == ownCollider) continue;
            // Свои дочерние коллайдеры тоже пропускаем.
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.isTrigger) continue;

            if (!hasBest || h.distance < bestHit.distance)
            {
                bestHit = h;
                hasBest = true;
            }
        }

        if (!hasBest)
        {
            transform.position = origin + Vector2.down * distance;
            return;
        }

        // Останавливаемся вплотную к препятствию.
        float clamped = Mathf.Max(0f, bestHit.distance - 0.001f);
        transform.position = origin + Vector2.down * clamped;

        // Проверим: попали в игрока?
        PlayerFacade pf = bestHit.collider.GetComponent<PlayerFacade>()
                         ?? bestHit.collider.GetComponentInParent<PlayerFacade>();

        if (pf != null)
        {
            ApplyHitToPlayer(pf, bestHit.collider);
        }
        else if (verboseLogs)
        {
            Debug.Log($"{nameof(KillBlock)} '{name}': заело на '{bestHit.collider.name}' (layer={bestHit.collider.gameObject.layer}).", this);
        }

        EnterJammed();
    }

    private void ApplyHitToPlayer(PlayerFacade pf, Collider2D hitCollider)
    {
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
            Debug.Log(
                $"{nameof(KillBlock)} '{name}': попал в игрока '{hitCollider.name}'. damage={damage}, teleport={teleportPlayerOnHit}.",
                this);
    }

    private void EnterJammed()
    {
        state = State.Jammed;
        resetTimer = 0f;

        if (becomeNonBlockingAfterStop && ownCollider != null)
            ownCollider.isTrigger = true;
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
        transform.position = startPosition;
        transform.rotation = startRotation;

        if (ownBody != null)
        {
            ownBody.position = startPosition;
            ownBody.linearVelocity = Vector2.zero;
            ownBody.angularVelocity = 0f;
        }

        if (ownCollider != null)
            ownCollider.isTrigger = originalIsTrigger;

        state = State.Idle;
        resetTimer = 0f;

        if (verboseLogs)
            Debug.Log($"{nameof(KillBlock)} '{name}': вернулся на исходную позицию.", this);
    }

    /// <summary>Программный сброс блока на исходную позицию (например, при респавне игрока).</summary>
    public void ForceReset()
    {
        ResetToStart();
    }

    private void OnDrawGizmosSelected()
    {
        // Визуализация детекторной зоны над платформой.
        if (triggerPlatformCollider == null)
            return;

        Bounds b = triggerPlatformCollider.bounds;
        Vector3 center = new Vector3(b.center.x, b.max.y + detectionHeight * 0.5f, 0f);
        Vector3 size = new Vector3(b.size.x, detectionHeight, 0.01f);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireCube(center, size);
    }
}
