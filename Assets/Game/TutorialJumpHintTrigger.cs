using UnityEngine;

/// <summary>
/// Place this component on the Is Trigger collider that should introduce jumping.
/// The trigger stays armed until the movement tutorial has been completed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
[AddComponentMenu("Game/Tutorial/Jump Hint Trigger")]
public class TutorialJumpHintTrigger : MonoBehaviour
{
    [SerializeField] private TutorialSequenceController tutorial;
    [SerializeField] private bool disableAfterTrigger = true;

    private Collider2D triggerCollider;
    private bool triggered;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider2D>();

        if (tutorial == null)
            tutorial = FindAnyObjectByType<TutorialSequenceController>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTrigger(other);
    }

    private void TryTrigger(Collider2D other)
    {
        if (triggered || tutorial == null || other == null)
            return;

        if (other.GetComponentInParent<PlayerFacade>() == null)
            return;

        if (!tutorial.TryShowJumpHint())
            return;

        triggered = true;

        if (disableAfterTrigger && triggerCollider != null)
            triggerCollider.enabled = false;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        Collider2D collider = GetComponent<Collider2D>();
        if (collider != null)
            collider.isTrigger = true;
    }

    private void OnValidate()
    {
        Collider2D collider = GetComponent<Collider2D>();
        if (collider != null)
            collider.isTrigger = true;
    }
#endif
}
