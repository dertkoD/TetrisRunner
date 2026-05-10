using UnityEngine;

public class TetrisBlockFacade : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Transform blockTransform;

    [Header("Modules")]
    [SerializeField] private TetrisBlockController controller;
    [SerializeField] private TetrisBlockMovement movement;
    [SerializeField] private TetrisBlockRotator rotator;
    [SerializeField] private TetrisBlockContactReporter contactReporter;
    [SerializeField] private TetrisBlockCells blockCells;

    public Rigidbody2D Body => body;
    public Transform BlockTransform => blockTransform;

    public TetrisBlockController Controller => controller;
    public TetrisBlockMovement Movement => movement;
    public TetrisBlockRotator Rotator => rotator;
    public TetrisBlockContactReporter ContactReporter => contactReporter;
    public TetrisBlockCells BlockCells => blockCells;
}
