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

    public PlayerConfigSO Config => config;
    public Rigidbody2D Body => body;
    public Transform BodyTransform => bodyTransform;
    public PlayerMovement Movement => movement;
    public PlayerJump Jump => jump;
    public PlayerGroundChecker GroundChecker => groundChecker;
}
