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

    [Header("Wall Jump (Hollow Knight style)")]
    [Tooltip("Включает способность отталкиваться от стен.")]
    [SerializeField] private bool wallJumpEnabled = false;
    [Tooltip("Слои, которые считаются стенами. Если оставить пустым, будут использованы Ground Layers.")]
    [SerializeField] private LayerMask wallLayers;
    [SerializeField, Range(0f, 1f)] private float minWallNormalX = 0.7f;
    [SerializeField, Min(0f)] private float wallJumpVelocity = 14f;
    [SerializeField, Min(0f)] private float wallJumpHorizontalVelocity = 10f;
    [Tooltip("Сколько секунд игнорируется горизонтальный инпут после wall-jump, " +
             "чтобы нельзя было сразу прилипнуть обратно к той же стене.")]
    [SerializeField, Min(0f)] private float wallJumpLockoutTime = 0.18f;
    [Tooltip("Сколько wall-jump'ов доступно за один полёт. Восполняется при приземлении. " +
             "По умолчанию 1 — отпрыгнуть от стены можно только один раз, потом нужно коснуться земли.")]
    [SerializeField, Min(0)] private int wallJumpCount = 1;

    [Header("Health")]
    [Tooltip("Стартовый уровень ХП игрока (он же максимум по умолчанию).")]
    [SerializeField, Min(1)] private int maxHealth = 3;

    [Header("Block Freeze Ability")]
    [Tooltip("Input action для способности заморозки блоков. Используется в режиме 'удерживай для активации'.")]
    [SerializeField] private InputActionReference freezeAction;

    [Tooltip("Сколько секунд можно держать заморозку без перерыва (полный запас).")]
    [SerializeField, Min(0f)] private float freezeMaxDuration = 3f;

    [Tooltip("Сколько секунд заморозки восстанавливается за одну реальную секунду, когда " +
             "способность не используется. Например 1 = полная зарядка за freezeMaxDuration секунд, " +
             "0.5 = в два раза дольше.")]
    [SerializeField, Min(0f)] private float freezeRechargeSecondsPerSecond = 1f;

    [Tooltip("Минимальный запас (в секундах), при котором можно ЗАНОВО активировать заморозку. " +
             "Защита от мерцания: если запас иссяк, надо немного перезарядиться перед следующим включением.")]
    [SerializeField, Min(0f)] private float freezeMinToReactivate = 0.2f;

    [Header("Double Jump")]
    [Tooltip("Включает способность доп. прыжков в воздухе.")]
    [SerializeField] private bool doubleJumpEnabled = false;
    [Tooltip("Сколько прыжков доступно в воздухе после первого 'обычного'. 1 = классический double jump.")]
    [SerializeField, Min(0)] private int airJumpCount = 1;
    [SerializeField, Min(0f)] private float airJumpVelocity = 13f;

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

    public bool WallJumpEnabled => wallJumpEnabled;
    public LayerMask WallLayers => wallLayers;
    /// <summary>WallLayers если задан, иначе GroundLayers. Чтобы не дублировать настройку.</summary>
    public LayerMask EffectiveWallLayers => wallLayers.value != 0 ? wallLayers : groundLayers;
    public float MinWallNormalX => minWallNormalX;
    public float WallJumpVelocity => wallJumpVelocity;
    public float WallJumpHorizontalVelocity => wallJumpHorizontalVelocity;
    public float WallJumpLockoutTime => wallJumpLockoutTime;
    public int WallJumpCount => wallJumpCount;

    public int MaxHealth => maxHealth;

    public InputActionReference FreezeAction => freezeAction;
    public float FreezeMaxDuration => freezeMaxDuration;
    public float FreezeRechargeSecondsPerSecond => freezeRechargeSecondsPerSecond;
    public float FreezeMinToReactivate => freezeMinToReactivate;

    public bool DoubleJumpEnabled => doubleJumpEnabled;
    public int AirJumpCount => airJumpCount;
    public float AirJumpVelocity => airJumpVelocity;
}
