using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerStateMachine : MonoBehaviour
{
    [Header("Facade")]
    [SerializeField] private PlayerFacade facade;

    public PlayerState CurrentState { get; private set; }

    private PlayerConfigSO config;
    private Rigidbody2D body;
    private Transform bodyTransform;
    private PlayerMovement movement;
    private PlayerJump jump;
    private PlayerGroundChecker groundChecker;

    private InputAction moveAction;
    private InputAction jumpAction;

    private float moveInputX;
    private float coyoteTimer;
    private float jumpBufferTimer;

    private bool jumpReleased;
    private bool initialized;

    private void Awake()
    {
        InitializeFromFacade();
        ApplyInitialSettings();
    }

    private void OnEnable()
    {
        EnableInput();
    }

    private void OnDisable()
    {
        DisableInput();
    }

    private void Update()
    {
        if (!initialized)
            return;

        ReadState();
        ReadTimers();
        DecideJump();
        DecideJumpCut();
        DecideCurrentState();
    }

    private void FixedUpdate()
    {
        if (!initialized)
            return;

        DecideMovement();
        DecideFallLimit();
    }

    private void InitializeFromFacade()
    {
        if (facade == null)
        {
            Debug.LogError($"{nameof(PlayerStateMachine)}: Facade is not assigned.", this);
            enabled = false;
            return;
        }

        config = facade.Config;
        body = facade.Body;
        bodyTransform = facade.BodyTransform;
        movement = facade.Movement;
        jump = facade.Jump;
        groundChecker = facade.GroundChecker;

        if (config == null || body == null || bodyTransform == null || movement == null || jump == null || groundChecker == null)
        {
            Debug.LogError($"{nameof(PlayerStateMachine)}: One or more references are missing in PlayerFacade.", this);
            enabled = false;
            return;
        }

        moveAction = config.MoveAction != null ? config.MoveAction.action : null;
        jumpAction = config.JumpAction != null ? config.JumpAction.action : null;

        if (moveAction == null || jumpAction == null)
        {
            Debug.LogError($"{nameof(PlayerStateMachine)}: Input actions are not assigned in PlayerConfigSO.", this);
            enabled = false;
            return;
        }

        initialized = true;
    }

    private void ApplyInitialSettings()
    {
        if (!initialized)
            return;

        body.gravityScale = config.GravityScale;
        CurrentState = PlayerState.Falling;
    }

    private void EnableInput()
    {
        if (!initialized)
            return;

        moveAction.performed += OnMovePerformed;
        moveAction.canceled += OnMoveCanceled;

        jumpAction.performed += OnJumpPerformed;
        jumpAction.canceled += OnJumpCanceled;

        moveAction.Enable();
        jumpAction.Enable();
    }

    private void DisableInput()
    {
        if (!initialized)
            return;

        moveAction.performed -= OnMovePerformed;
        moveAction.canceled -= OnMoveCanceled;

        jumpAction.performed -= OnJumpPerformed;
        jumpAction.canceled -= OnJumpCanceled;

        moveAction.Disable();
        jumpAction.Disable();
    }

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();
        moveInputX = Mathf.Clamp(value.x, -1f, 1f);
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        moveInputX = 0f;
    }

    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        jumpBufferTimer = config.JumpBufferTime;
    }

    private void OnJumpCanceled(InputAction.CallbackContext context)
    {
        jumpReleased = true;
    }

    private void ReadState()
    {
        if (groundChecker.IsGrounded && body.linearVelocity.y <= 0.01f)
            coyoteTimer = config.CoyoteTime;
    }

    private void ReadTimers()
    {
        float deltaTime = Time.deltaTime;

        if (!groundChecker.IsGrounded)
            coyoteTimer -= deltaTime;

        if (jumpBufferTimer > 0f)
            jumpBufferTimer -= deltaTime;
    }

    private void DecideJump()
    {
        bool hasBufferedJump = jumpBufferTimer > 0f;
        bool canJump = coyoteTimer > 0f;

        if (!hasBufferedJump || !canJump)
            return;

        jump.PerformJump(body, config);

        jumpBufferTimer = 0f;
        coyoteTimer = 0f;

        CurrentState = PlayerState.Rising;
    }

    private void DecideJumpCut()
    {
        if (!jumpReleased)
            return;

        jumpReleased = false;
        jump.CutJump(body, config);
    }

    private void DecideMovement()
    {
        movement.Move(
            body,
            bodyTransform,
            config,
            moveInputX,
            groundChecker.IsGrounded
        );
    }

    private void DecideFallLimit()
    {
        jump.LimitFallSpeed(body, config);
    }

    private void DecideCurrentState()
    {
        if (groundChecker.IsGrounded && body.linearVelocity.y <= 0.01f)
        {
            CurrentState = PlayerState.Grounded;
            return;
        }

        CurrentState = body.linearVelocity.y > 0f
            ? PlayerState.Rising
            : PlayerState.Falling;
    }
}
