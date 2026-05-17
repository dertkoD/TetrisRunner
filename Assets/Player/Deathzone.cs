using UnityEngine;

/// <summary>
/// Опасная зона. Пустой объект с триггер-коллайдером: когда в неё попадает
/// игрок — наносит ему урон и телепортирует обратно в точку последнего
/// прыжка (PlayerRespawnAnchor).
///
/// На корне объекта-коллайдера или в его родителях должен быть PlayerFacade.
/// Если PlayerHealth и/или PlayerRespawnAnchor не назначены в PlayerFacade,
/// они автоматически ищутся через GetComponent на том же объекте, что и
/// PlayerFacade.
///
/// Коллайдер сцены добавляется автоматически (BoxCollider2D), если на
/// объекте Deathzone его не было.
/// </summary>
[DisallowMultipleComponent]
public class Deathzone : MonoBehaviour
{
    [Header("Damage")]
    [Tooltip("Сколько HP отнимать у игрока за касание зоны.")]
    [SerializeField, Min(0)] private int damage = 1;

    [Header("Respawn")]
    [Tooltip("Если true — после получения урона игрок телепортируется в свою " +
             "последнюю точку прыжка (PlayerRespawnAnchor).")]
    [SerializeField] private bool teleportOnHit = true;

    [Header("Cooldown")]
    [Tooltip("Минимальный интервал между повторными срабатываниями зоны для одного игрока. " +
             "Защищает от мгновенного повторного срабатывания, пока игрок ещё стоит внутри.")]
    [SerializeField, Min(0f)] private float retriggerCooldown = 0.25f;

    [Header("Debug")]
    [Tooltip("Если включено — в консоль будут писаться сообщения о входе в зону, " +
             "поиске PlayerFacade и сработке урона/телепорта.")]
    [SerializeField] private bool verboseLogs = false;

    private Collider2D ownCollider;
    private float lastHitTime = -999f;

    private void Reset()
    {
        // Когда скрипт впервые добавляют через инспектор, гарантируем наличие
        // конкретного коллайдера, чтобы зона действительно ловила тела.
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();
    }

    private void Awake()
    {
        ownCollider = GetComponent<Collider2D>();

        if (ownCollider == null)
        {
            // В рантайме на пустых объектах Collider2D — абстрактный класс,
            // RequireComponent его не создаст. Добавим BoxCollider2D сами.
            ownCollider = gameObject.AddComponent<BoxCollider2D>();
            Debug.LogWarning(
                $"{nameof(Deathzone)}: на '{name}' не было Collider2D — автоматически добавлен BoxCollider2D. " +
                "Размер коллайдера лучше выставить вручную под форму зоны.",
                this);
        }

        if (!ownCollider.isTrigger)
            ownCollider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (verboseLogs)
            Debug.Log($"{nameof(Deathzone)}: OnTriggerEnter2D от '{other?.name}' (layer={other?.gameObject.layer}).", this);

        TryDamage(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (Time.time - lastHitTime < retriggerCooldown)
            return;

        TryDamage(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider != null)
            TryDamage(collision.collider);
    }

    private void TryDamage(Collider2D other)
    {
        if (other == null)
            return;

        PlayerFacade facade = other.GetComponent<PlayerFacade>()
                              ?? other.GetComponentInParent<PlayerFacade>();

        if (facade == null)
        {
            if (verboseLogs)
                Debug.Log($"{nameof(Deathzone)}: '{other.name}' не содержит PlayerFacade — игнорирую.", this);
            return;
        }

        if (Time.time - lastHitTime < retriggerCooldown)
            return;

        lastHitTime = Time.time;

        // Если поля в PlayerFacade не назначены — пытаемся найти компоненты
        // на том же GameObject, что и сам PlayerFacade. Так Deathzone работает
        // даже без ручной настройки в инспекторе фасада.
        PlayerHealth health = facade.Health != null
            ? facade.Health
            : facade.GetComponent<PlayerHealth>();

        PlayerRespawnAnchor anchor = facade.RespawnAnchor != null
            ? facade.RespawnAnchor
            : facade.GetComponent<PlayerRespawnAnchor>();

        if (verboseLogs)
            Debug.Log(
                $"{nameof(Deathzone)}: hit player. health={(health != null ? "OK" : "null")}, " +
                $"anchor={(anchor != null ? "OK" : "null")}, damage={damage}, teleport={teleportOnHit}",
                this);

        if (health != null && damage > 0)
            health.TakeDamage(damage);
        else if (health == null && verboseLogs)
            Debug.LogWarning($"{nameof(Deathzone)}: на игроке нет PlayerHealth — урон не нанесён.", this);

        if (!teleportOnHit)
            return;

        if (anchor != null)
            anchor.Respawn();
        else if (verboseLogs)
            Debug.LogWarning($"{nameof(Deathzone)}: на игроке нет PlayerRespawnAnchor — телепорта не будет.", this);
    }
}
