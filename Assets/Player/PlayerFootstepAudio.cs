using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Player/Player Footstep Audio")]
public class PlayerFootstepAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerFacade facade;
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private PlayerGroundChecker groundChecker;

    [Header("Run Detection")]
    [SerializeField, Min(0f)] private float minHorizontalSpeed = 0.15f;
    [SerializeField, Min(0.01f)] private float speedForFastestInterval = 5f;

    [Header("Timing")]
    [SerializeField, Min(0.02f)] private float slowStepInterval = 0.32f;
    [SerializeField, Min(0.02f)] private float fastStepInterval = 0.16f;
    [SerializeField] private bool playImmediatelyWhenRunStarts = true;

    private bool wasRunning;
    private float nextStepTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        wasRunning = false;
        nextStepTime = 0f;
    }

    private void Update()
    {
        ResolveReferences();

        if (body == null || groundChecker == null)
            return;

        bool running = IsRunningOnGround(out float speed01);
        float now = Time.time;

        if (!running)
        {
            wasRunning = false;
            nextStepTime = now;
            return;
        }

        if (!wasRunning && playImmediatelyWhenRunStarts)
            nextStepTime = now;

        wasRunning = true;

        if (now < nextStepTime)
            return;

        GameAudioController.PlayFootstep();
        nextStepTime = now + Mathf.Lerp(slowStepInterval, fastStepInterval, speed01);
    }

    private bool IsRunningOnGround(out float speed01)
    {
        float speed = Mathf.Abs(body.linearVelocity.x);
        speed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, speedForFastestInterval));

        return groundChecker.IsGrounded && speed >= minHorizontalSpeed;
    }

    private void ResolveReferences()
    {
        if (facade == null)
            facade = GetComponent<PlayerFacade>();
        if (facade == null)
            facade = GetComponentInParent<PlayerFacade>();

        if (facade != null)
        {
            if (body == null)
                body = facade.Body;
            if (groundChecker == null)
                groundChecker = facade.GroundChecker;

            PlayerConfigSO config = facade.Config;
            if (config != null && speedForFastestInterval <= 0.01f)
                speedForFastestInterval = Mathf.Max(0.01f, config.MaxMoveSpeed);
        }

        if (body == null)
            body = GetComponent<Rigidbody2D>();
        if (body == null)
            body = GetComponentInParent<Rigidbody2D>();

        if (groundChecker == null)
            groundChecker = GetComponent<PlayerGroundChecker>();
        if (groundChecker == null)
            groundChecker = GetComponentInParent<PlayerGroundChecker>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (fastStepInterval > slowStepInterval)
            fastStepInterval = slowStepInterval;
    }
#endif
}
