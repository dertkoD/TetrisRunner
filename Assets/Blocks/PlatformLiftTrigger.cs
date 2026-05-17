using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Триггер-зона: пустой объект с триггер-коллайдером. При входе в неё
/// объекта-«входящей платформы» (например, двигающейся платформы) — другая
/// «поднимаемая платформа» (просто спрайт с коллайдером) плавно едет в
/// заданную точку. Когда входящая платформа покидает зону — поднимаемая
/// возвращается на исходную позицию.
///
/// Кого считать «входящей платформой» можно настроить через слой
/// (<c>Triggering Layers</c>) и/или необходимость наличия
/// <see cref="TetrisGridMovingPlatform"/> на объекте.
///
/// Поднимаемая платформа двигается обычным Transform.position — она не
/// взаимодействует с сеткой тетриса (это просто препятствие для игрока).
/// </summary>
[DisallowMultipleComponent]
public class PlatformLiftTrigger : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Платформа, которую нужно поднимать. Просто спрайт + коллайдер.")]
    [SerializeField] private Transform platformToLift;

    [Tooltip("Точка (пустой объект), в которую платформа едет, пока в зоне " +
             "находится входящая платформа.")]
    [SerializeField] private Transform liftPoint;

    [Header("Filter — кто считается 'входящей платформой'")]
    [Tooltip("Слои, объекты которых будут активировать подъём. " +
             "Если оставить пустым (Nothing) — фильтр по слою не применяется.")]
    [SerializeField] private LayerMask triggeringLayers = ~0;

    [Tooltip("Если true — активация сработает только если на вошедшем объекте " +
             "(или в его родителях) есть TetrisGridMovingPlatform.")]
    [SerializeField] private bool requireMovingPlatformComponent = false;

    [Tooltip("Опционально: дополнительный конкретный Transform — только он считается " +
             "входящей платформой (если задано). Полезно, если рядом несколько разных " +
             "движущихся объектов и важно реагировать только на один.")]
    [SerializeField] private Transform specificTriggeringPlatform;

    [Header("Movement")]
    [Tooltip("Скорость движения поднимаемой платформы (единиц мира в секунду). " +
             "0 = мгновенный телепорт между исходной точкой и liftPoint.")]
    [SerializeField, Min(0f)] private float moveSpeed = 5f;

    [Header("Debug")]
    [Tooltip("Если true — в консоль пишется вход/выход триггера, причина срабатывания " +
             "и кто остался внутри. По умолчанию включено для удобства отладки.")]
    [SerializeField] private bool verboseLogs = true;

    private Collider2D ownCollider;
    private Vector3 originalPosition;
    private bool hasOrigin;

    private readonly HashSet<Collider2D> activeTriggers = new HashSet<Collider2D>();

    private void Reset()
    {
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();

        EnsureKinematicRigidbody();
    }

    private void Awake()
    {
        ownCollider = GetComponent<Collider2D>();

        if (ownCollider == null)
        {
            ownCollider = gameObject.AddComponent<BoxCollider2D>();
            Debug.LogWarning(
                $"{nameof(PlatformLiftTrigger)}: на '{name}' не было Collider2D — добавлен BoxCollider2D автоматически.",
                this);
        }

        if (!ownCollider.isTrigger)
            ownCollider.isTrigger = true;

        // Чтобы OnTrigger2D срабатывал, когда «входящая платформа» — статический
        // объект без Rigidbody2D, на самой зоне должен быть Rigidbody2D
        // (kinematic). Добавим автоматически.
        EnsureKinematicRigidbody();

        if (platformToLift != null)
        {
            originalPosition = platformToLift.position;
            hasOrigin = true;
        }
    }

    private void EnsureKinematicRigidbody()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();

        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            if (verboseLogs)
                Debug.Log(
                    $"{nameof(PlatformLiftTrigger)}: на '{name}' не было Rigidbody2D — добавлен Kinematic Rigidbody2D, чтобы " +
                    "OnTrigger2D корректно срабатывал.",
                    this);
        }

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        // Зона не должна двигаться от внешних сил.
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    private void Update()
    {
        if (platformToLift == null || !hasOrigin)
            return;

        // Защита от случая, когда триггерящий объект был уничтожен и OnTriggerExit2D
        // не сработал — иначе activeTriggers навсегда останется непустым, и поднятая
        // платформа никогда не вернётся.
        if (activeTriggers.Count > 0)
            activeTriggers.RemoveWhere(IsColliderInvalid);

        bool active = activeTriggers.Count > 0 && liftPoint != null;
        Vector3 target = active ? liftPoint.position : originalPosition;

        if (moveSpeed <= 0f)
        {
            platformToLift.position = target;
            return;
        }

        platformToLift.position = Vector3.MoveTowards(
            platformToLift.position,
            target,
            moveSpeed * Time.deltaTime
        );
    }

    private static bool IsColliderInvalid(Collider2D c)
    {
        // Уничтоженный или выключенный коллайдер — UnityEngine.Object == null проверка
        // (через оператор ==) корректно ловит destroyed objects.
        return c == null || !c.gameObject.activeInHierarchy || !c.enabled;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!MatchesAsTriggeringPlatform(other))
        {
            if (verboseLogs)
                Debug.Log($"{nameof(PlatformLiftTrigger)}: '{other?.name}' не подходит под фильтр входа.", this);
            return;
        }

        activeTriggers.Add(other);

        if (verboseLogs)
            Debug.Log($"{nameof(PlatformLiftTrigger)}: '{other.name}' зашёл в зону — поднимаю платформу.", this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other == null)
            return;

        bool removed = activeTriggers.Remove(other);

        if (verboseLogs)
        {
            string state = activeTriggers.Count > 0
                ? $"ещё кто-то внутри (осталось {activeTriggers.Count})"
                : "зона пуста, опускаю платформу обратно";
            Debug.Log(
                $"{nameof(PlatformLiftTrigger)}: '{other.name}' вышел из зоны (removed={removed}); {state}.",
                this);
        }
    }

    private bool MatchesAsTriggeringPlatform(Collider2D other)
    {
        if (other == null)
            return false;

        if (triggeringLayers.value != 0)
        {
            int otherLayerBit = 1 << other.gameObject.layer;
            if ((triggeringLayers.value & otherLayerBit) == 0)
                return false;
        }

        if (specificTriggeringPlatform != null)
        {
            // Сравниваем по корневому Transform: коллайдер может быть на дочернем объекте.
            Transform root = other.transform;
            bool matches = false;

            while (root != null)
            {
                if (root == specificTriggeringPlatform)
                {
                    matches = true;
                    break;
                }
                root = root.parent;
            }

            if (!matches)
                return false;
        }

        if (requireMovingPlatformComponent)
        {
            TetrisGridMovingPlatform platform = other.GetComponent<TetrisGridMovingPlatform>()
                                                ?? other.GetComponentInParent<TetrisGridMovingPlatform>();

            if (platform == null)
                return false;
        }

        return true;
    }
}
