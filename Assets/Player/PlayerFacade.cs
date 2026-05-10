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

    public PlayerConfigSO Config => config;
    public Rigidbody2D Body => body;
    public Transform BodyTransform => bodyTransform;
    public PlayerMovement Movement => movement;
    public PlayerJump Jump => jump;
    public PlayerGroundChecker GroundChecker => groundChecker;

    public PlayerWallChecker WallChecker => wallChecker;
    public PlayerWallJump WallJump => wallJump;
    public PlayerDoubleJump DoubleJump => doubleJump;
}
