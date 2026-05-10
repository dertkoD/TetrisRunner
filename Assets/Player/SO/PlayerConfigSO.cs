using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "PlayerConfig", menuName = "Player/Player Config")]
public sealed class PlayerConfigSO : ScriptableObject
{
    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float maxMoveSpeed = 8f;
    [SerializeField, Min(0f)] private float acceleration = 80f;
    [SerializeField, Min(0f)] private float deceleration = 100f;
    [SerializeField, Range(0f, 1f)] private float airControlMultiplier = 0.7f;
    [SerializeField] private bool flipByScale = true;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpVelocity = 14f;
    [SerializeField, Min(0f)] private float gravityScale = 3f;
    [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.45f;
    [SerializeField, Min(0f)] private float maxFallSpeed = 24f;

    [Header("Ground")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField, Range(0f, 1f)] private float minGroundNormalY = 0.65f;

    public InputActionReference MoveAction => moveAction;
    public InputActionReference JumpAction => jumpAction;

    public float MaxMoveSpeed => maxMoveSpeed;
    public float Acceleration => acceleration;
    public float Deceleration => deceleration;
    public float AirControlMultiplier => airControlMultiplier;
    public bool FlipByScale => flipByScale;

    public float JumpVelocity => jumpVelocity;
    public float GravityScale => gravityScale;
    public float CoyoteTime => coyoteTime;
    public float JumpBufferTime => jumpBufferTime;
    public float JumpCutMultiplier => jumpCutMultiplier;
    public float MaxFallSpeed => maxFallSpeed;

    public LayerMask GroundLayers => groundLayers;
    public float MinGroundNormalY => minGroundNormalY;
}
