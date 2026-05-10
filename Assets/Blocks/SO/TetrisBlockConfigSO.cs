using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "TetrisBlockConfig", menuName = "Tetris Blocks/Block Config")]
public sealed class TetrisBlockConfigSO : ScriptableObject
{
    [Header("Input")]
    [SerializeField] private InputActionReference toggleSpawnAction;
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference rotateLeftAction;
    [SerializeField] private InputActionReference rotateRightAction;

    [Header("Prefabs")]
    [SerializeField] private TetrisBlockFacade[] blockPrefabs;

    [Header("Main Rules")]
    [SerializeField] private bool freeFall = true;
    [SerializeField] private bool stackBlocks = true;

    [Header("Layers")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private LayerMask blockLayers;

    [Header("Contact Lock")]
    [SerializeField] private bool requireBottomContactToLock = true;
    [SerializeField, Range(0f, 1f)] private float minLockNormalY = 0.45f;

    [Header("Free Fall Movement")]
    [SerializeField, Min(0f)] private float freeFallGravityScale = 1f;
    [SerializeField, Min(0f)] private float freeFallHorizontalSpeed = 5f;
    [SerializeField, Min(0f)] private float freeFallHorizontalAcceleration = 40f;
    [SerializeField, Min(0f)] private float maxFallSpeed = 18f;

    [Header("Classic Step Movement")]
    [SerializeField, Min(0.01f)] private float horizontalStepDistance = 1f;
    [SerializeField, Min(0.01f)] private float horizontalStepRepeatTime = 0.12f;
    [SerializeField, Min(0.01f)] private float fallStepDistance = 1f;
    [SerializeField, Min(0.01f)] private float fallStepInterval = 0.5f;

    [Header("Rotation")]
    [SerializeField] private float rotationStepDegrees = 90f;

    [Header("Controlled Block Physics")]
    [SerializeField] private bool freezeRotationWhileControlled = true;

    [Header("Released Block Physics")]
    [SerializeField, Min(0f)] private float releasedGravityScale = 1f;

    [Header("Stack Snap")]
    [SerializeField] private bool snapPositionWhenStacking = true;
    [SerializeField] private bool snapRotationWhenStacking = true;
    [SerializeField, Min(0.01f)] private float gridCellSize = 1f;

    public InputActionReference ToggleSpawnAction => toggleSpawnAction;
    public InputActionReference MoveAction => moveAction;
    public InputActionReference RotateLeftAction => rotateLeftAction;
    public InputActionReference RotateRightAction => rotateRightAction;

    public TetrisBlockFacade[] BlockPrefabs => blockPrefabs;

    public bool FreeFall => freeFall;
    public bool StackBlocks => stackBlocks;

    public LayerMask GroundLayers => groundLayers;
    public LayerMask BlockLayers => blockLayers;

    public bool RequireBottomContactToLock => requireBottomContactToLock;
    public float MinLockNormalY => minLockNormalY;

    public float FreeFallGravityScale => freeFallGravityScale;
    public float FreeFallHorizontalSpeed => freeFallHorizontalSpeed;
    public float FreeFallHorizontalAcceleration => freeFallHorizontalAcceleration;
    public float MaxFallSpeed => maxFallSpeed;

    public float HorizontalStepDistance => horizontalStepDistance;
    public float HorizontalStepRepeatTime => horizontalStepRepeatTime;
    public float FallStepDistance => fallStepDistance;
    public float FallStepInterval => fallStepInterval;

    public float RotationStepDegrees => rotationStepDegrees;

    public bool FreezeRotationWhileControlled => freezeRotationWhileControlled;

    public float ReleasedGravityScale => releasedGravityScale;

    public bool SnapPositionWhenStacking => snapPositionWhenStacking;
    public bool SnapRotationWhenStacking => snapRotationWhenStacking;
    public float GridCellSize => gridCellSize;
}