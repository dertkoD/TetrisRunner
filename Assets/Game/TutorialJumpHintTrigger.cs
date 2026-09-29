using UnityEngine;

/// <summary>
/// Optional bridge when the sequence controller is on a different scene object.
/// Its collider is disabled by the controller only after the full sequence.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
[AddComponentMenu("Game/Tutorial/Tutorial Sequence Trigger")]
public class TutorialJumpHintTrigger : MonoBehaviour
{
    [SerializeField] private TutorialSequenceController tutorial;

    private Collider2D triggerCollider;

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

    private void OnTriggerStay2D(Collider2D other)
    {
        TryTrigger(other);
    }

    private void TryTrigger(Collider2D other)
    {
        if (tutorial == null)
            return;
        tutorial.TryBeginTutorial(other, triggerCollider);
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
