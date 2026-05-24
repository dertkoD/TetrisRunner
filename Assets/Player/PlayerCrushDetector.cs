using UnityEngine;

/// <summary>
/// Висит на игроке: ловит контакт с активным (управляемым) тетрис-блоком.
/// Раньше любой контакт с активным блоком считался смертельным, поэтому если
/// игрок прыгал на падающий блок сбоку или приземлялся на него сверху — он
/// мгновенно умирал. Теперь смерть наступает только если активный блок
/// действительно «падает на игрока сверху»: контактная нормаль направлена
/// вниз (блок выше игрока) и/или AABB блока находится над AABB игрока. Сбоку
/// и снизу контакт уже не убивает — игрок может, например, оттолкнуться от
/// активного блока стенопрыжком или быстро на него запрыгнуть.
///
/// При желании поведение можно вернуть к старому: включить
/// <see cref="dieOnAnyTetrisBlock"/> и любой контакт с тетрис-блоком (даже
/// сбоку или с уже залоченным) будет смертельным.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCrushDetector : MonoBehaviour
{
    [Tooltip("Если true — гибель будет срабатывать на ЛЮБОЙ контакт с тетрис-блоком, " +
             "включая статичные/залоченные и контакт сбоку/снизу. Полезно для уровней, " +
             "где даже стоящий блок считается смертельным препятствием.")]
    [SerializeField] private bool dieOnAnyTetrisBlock = false;

    [Tooltip("Насколько строго смотрим на направление контакта: 0 — любой контакт " +
             "сверху считается смертельным, 1 — только идеально вертикальный «давит сверху». " +
             "Значения ~0.5 дают хорошее эмпирическое поведение: блок реально над игроком, " +
             "а не просто рядом по диагонали.")]
    [SerializeField, Range(0f, 1f)] private float crushNormalThreshold = 0.5f;

    [Tooltip("Доп. проверка по AABB: если bottom-Y блока выше top-Y игрока (с поправкой " +
             "на этот допуск) — считаем, что блок действительно ВЫШЕ. Помогает в случаях, " +
             "когда контактные нормали ещё не подтянулись, но геометрически блок очевидно " +
             "сверху.")]
    [SerializeField, Min(0f)] private float aboveAabbTolerance = 0.05f;

    private Collider2D ownCollider;

    private void Awake()
    {
        ResolveOwnCollider();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        HandleCollision(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision == null)
            return;

        HandleCollision(collision);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Активный тетрис-блок обычно НЕ trigger, но на случай если кто-то поменяет
        // настройку — обрабатываем и так. Контактных нормалей у trigger'а нет,
        // поэтому фильтр «сверху» делаем по AABB.
        HandleTrigger(other);
    }

    private void HandleCollision(Collision2D collision)
    {
        Collider2D other = collision.collider;

        if (!IsLethalTetrisBlock(other, out _))
            return;

        if (dieOnAnyTetrisBlock || IsContactFromAbove(collision, other))
            LevelReloader.RequestReload();
    }

    private void HandleTrigger(Collider2D other)
    {
        if (!IsLethalTetrisBlock(other, out _))
            return;

        if (dieOnAnyTetrisBlock || IsOtherAboveByAabb(other))
            LevelReloader.RequestReload();
    }

    /// <summary>
    /// Проверяет, что контакт идёт с активным управляемым тетрис-блоком
    /// (не залоченным и не статичным). Если <see cref="dieOnAnyTetrisBlock"/>
    /// = true, фильтр снимается и любой тетрис-блок считается смертельным.
    /// </summary>
    private bool IsLethalTetrisBlock(Collider2D other, out TetrisBlockController controller)
    {
        controller = null;

        if (other == null)
            return false;

        controller = other.GetComponent<TetrisBlockController>()
                     ?? other.GetComponentInParent<TetrisBlockController>();

        TetrisPlacedBlock placed = other.GetComponent<TetrisPlacedBlock>()
                                   ?? other.GetComponentInParent<TetrisPlacedBlock>();

        if (controller == null && placed == null)
            return false;

        if (dieOnAnyTetrisBlock)
            return true;

        // По умолчанию опасен только АКТИВНЫЙ блок: контроллер включён и ещё
        // не залочен. На уже залоченные или статичные блоки игрок может
        // спокойно наступать — они служат платформой.
        if (controller == null)
            return false;

        if (!controller.enabled || controller.IsLocked)
            return false;

        return true;
    }

    /// <summary>
    /// True, если хотя бы один контакт указывает на то, что блок находится
    /// СВЕРХУ игрока. Идём по контактным нормалям: нормаль ContactPoint2D
    /// указывает от другого коллайдера к нашему, поэтому при «давит сверху»
    /// она направлена ВНИЗ (contact.normal.y отрицателен).
    /// Если у коллизии вообще нет контактных точек (редкий случай в 2D
    /// физике, но бывает на грани разделимости коллайдеров) — откатываемся
    /// на проверку взаимного расположения AABB. Когда контакты есть, но все
    /// они сбоку — это валидный сигнал «блок не сверху», и мы НЕ убиваем
    /// игрока.
    /// </summary>
    private bool IsContactFromAbove(Collision2D collision, Collider2D other)
    {
        int contactCount = collision.contactCount;

        if (contactCount == 0)
            return IsOtherAboveByAabb(other);

        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);

            if (contact.normal.y <= -crushNormalThreshold)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True, если AABB блока находится ВЫШЕ AABB игрока: bottom-Y блока выше
    /// верхней грани игрока (с допуском). Это страховка на случай, когда у
    /// контакта нет нормалей или нормали неинформативны.
    /// </summary>
    private bool IsOtherAboveByAabb(Collider2D other)
    {
        ResolveOwnCollider();

        if (ownCollider == null || other == null)
            return false;

        Bounds playerBounds = ownCollider.bounds;
        Bounds blockBounds = other.bounds;

        return blockBounds.min.y >= playerBounds.max.y - aboveAabbTolerance;
    }

    private void ResolveOwnCollider()
    {
        if (ownCollider != null && ownCollider)
            return;

        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D c = colliders[i];
            if (c == null) continue;
            if (c.isTrigger) continue;

            ownCollider = c;
            return;
        }

        if (ownCollider == null)
            ownCollider = GetComponentInChildren<Collider2D>();
    }
}
