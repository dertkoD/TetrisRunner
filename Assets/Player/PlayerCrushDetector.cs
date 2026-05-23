using UnityEngine;

/// <summary>
/// Висит на игроке: ловит контакт с тетрисным блоком и перезагружает сцену.
/// Сейчас «опасным» считается контакт с активным (управляемым) тетрис-блоком —
/// то есть тем, у которого <see cref="TetrisBlockController"/> ещё не залочен
/// и компонент включён. Стоячие/залоченные блоки игрок может спокойно
/// использовать как платформу.
///
/// При желании поведение расширяется: достаточно убрать фильтр <c>!locked</c>,
/// и тогда любой контакт с любым тетрис-блоком будет считаться смертью.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCrushDetector : MonoBehaviour
{
    [Tooltip("Если true — гибель будет срабатывать на ЛЮБОЙ контакт с тетрис-блоком, " +
             "включая статичные/залоченные. Полезно для уровней, где даже стоящий блок " +
             "считается смертельным препятствием.")]
    [SerializeField] private bool dieOnAnyTetrisBlock = false;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null)
            return;

        HandleContact(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Активный тетрис-блок — НЕ trigger, но на случай если кто-то поменяет
        // настройку, обрабатываем и так. Trigger без TetrisBlockController
        // (например, Deathzone, DeathWater) игнорируется здесь — у них своя логика.
        HandleContact(other);
    }

    private void HandleContact(Collider2D other)
    {
        if (other == null)
            return;

        TetrisBlockController controller = other.GetComponent<TetrisBlockController>()
                                           ?? other.GetComponentInParent<TetrisBlockController>();

        TetrisPlacedBlock placed = other.GetComponent<TetrisPlacedBlock>()
                                   ?? other.GetComponentInParent<TetrisPlacedBlock>();

        if (controller == null && placed == null)
            return;

        if (!dieOnAnyTetrisBlock)
        {
            // По умолчанию считаем смертельным только АКТИВНЫЙ блок:
            // controller включён и ещё не залочен. На статичные/залоченные
            // блоки игрок может смело наступать.
            if (controller == null)
                return;

            if (!controller.enabled || controller.IsLocked)
                return;
        }

        LevelReloader.RequestReload();
    }
}
