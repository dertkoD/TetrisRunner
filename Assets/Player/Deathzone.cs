using UnityEngine;

/// <summary>
/// Опасная зона. Пустой объект с коллайдером (как правило isTrigger=true):
/// когда в неё попадает игрок — наносит ему урон и телепортирует обратно
/// в точку последнего прыжка (PlayerRespawnAnchor).
///
/// Ищет на коллайдере, который зашёл в зону, PlayerFacade. Сам PlayerFacade
/// можно держать как на корне, так и в родителе объекта-коллайдера.
/// </summary>
[RequireComponent(typeof(Collider2D))]
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

    private Collider2D ownCollider;
    private float lastHitTime = -999f;

    private void Awake()
    {
        ownCollider = GetComponent<Collider2D>();

        if (ownCollider != null && !ownCollider.isTrigger)
            ownCollider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
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
            return;

        if (Time.time - lastHitTime < retriggerCooldown)
            return;

        lastHitTime = Time.time;

        PlayerHealth health = facade.Health;
        if (health != null && damage > 0)
            health.TakeDamage(damage);

        if (!teleportOnHit)
            return;

        PlayerRespawnAnchor anchor = facade.RespawnAnchor;
        if (anchor != null)
            anchor.Respawn();
    }
}
