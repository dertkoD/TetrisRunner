using UnityEngine;

public class PlayerFacade : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private PlayerConfigSO config;

    [Header("Core References")]
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Transform bodyTransform;

    [Header("Modules")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerJump jump;
    [SerializeField] private PlayerGroundChecker groundChecker;

    [Header("Optional Abilities")]
    [Tooltip("Заполняй для персонажа, который умеет прыгать от стен (Hollow Knight style).")]
    [SerializeField] private PlayerWallChecker wallChecker;
    [SerializeField] private PlayerWallJump wallJump;

    [Tooltip("Заполняй для персонажа, который умеет двойной прыжок.")]
    [SerializeField] private PlayerDoubleJump doubleJump;

    [Header("Optional Health / Respawn")]
    [Tooltip("HP игрока. Используется Deathzone для нанесения урона.")]
    [SerializeField] private PlayerHealth health;
    [Tooltip("Чекпоинт по последнему прыжку. Используется Deathzone для возврата игрока.")]
    [SerializeField] private PlayerRespawnAnchor respawnAnchor;

    public PlayerConfigSO Config => config;
    public Rigidbody2D Body => body;
    public Transform BodyTransform => bodyTransform;
    public PlayerMovement Movement => movement;
    public PlayerJump Jump => jump;
    public PlayerGroundChecker GroundChecker => groundChecker;

    public PlayerWallChecker WallChecker => wallChecker;
    public PlayerWallJump WallJump => wallJump;
    public PlayerDoubleJump DoubleJump => doubleJump;

    public PlayerHealth Health
    {
        get
        {
            // Если поле не назначили в инспекторе — попробуем найти компонент
            // на этом же объекте. Так Deathzone и т.п. работают и без явной
            // привязки в фасаде.
            if (health == null)
                health = GetComponent<PlayerHealth>();
            return health;
        }
    }

    public PlayerRespawnAnchor RespawnAnchor
    {
        get
        {
            if (respawnAnchor == null)
                respawnAnchor = GetComponent<PlayerRespawnAnchor>();
            return respawnAnchor;
        }
    }
}
