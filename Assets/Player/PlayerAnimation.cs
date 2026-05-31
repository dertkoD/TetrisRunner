using UnityEngine;

/// <summary>
/// Кормит <see cref="Animator"/> игрока тремя параметрами, которые ожидает
/// контроллер <c>PlayerAnimationController</c>:
///
/// <list type="bullet">
///   <item><c>xVelocity</c> (float, 0..1) — нормализованная скорость по горизонтали
///         для blend tree <c>Moving</c> (0 = Idle, 1 = Run).</item>
///   <item><c>yVelocity</c> (float, -1..1) — нормализованная скорость по вертикали
///         для blend tree <c>Jumping</c> (-1 = Landing/падение, 1 = WindUp/подъём).</item>
///   <item><c>IsGround</c> (bool) — стоит ли игрок на земле; переключает состояния
///         <c>Moving</c> ↔ <c>Jumping</c>.</item>
/// </list>
///
/// Скрипт не управляет flip'ом — этим занимается <see cref="PlayerMovement"/>
/// (он переворачивает <c>bodyTransform.localScale</c>). Аниматор только меняет
/// активный спрайт, а уже инвертированный transform-родителя зеркалит его в
/// нужную сторону.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimation : MonoBehaviour
{
    private static readonly int XVelocityHash = Animator.StringToHash("xVelocity");
    private static readonly int YVelocityHash = Animator.StringToHash("yVelocity");
    private static readonly int IsGroundHash = Animator.StringToHash("IsGround");

    [Header("References")]
    [Tooltip("Animator с контроллером PlayerAnimationController. Если не задан — " +
             "будет найден на этом же GameObject или в дочерних.")]
    [SerializeField] private Animator animator;

    [Tooltip("Фасад игрока, из которого берутся Rigidbody2D, конфиг и GroundChecker. " +
             "Если не задан — будет найден на этом же GameObject.")]
    [SerializeField] private PlayerFacade facade;

    [Header("Tuning")]
    [Tooltip("Минимальная |x-скорость|, при которой включается анимация бега. " +
             "Меньше этого — считаем игрока стоящим, чтобы Idle не мерцал, когда " +
             "трение по полу оставляет крошечную остаточную скорость.")]
    [SerializeField, Min(0f)] private float runThreshold = 0.05f;

    [Tooltip("Сглаживание (damp time) параметра xVelocity. 0 — мгновенно, больше — " +
             "плавнее переход между Idle и Run.")]
    [SerializeField, Min(0f)] private float xVelocityDampTime = 0.05f;

    [Tooltip("Сглаживание параметра yVelocity. 0 — мгновенно, больше — плавнее " +
             "переход между WindUp и Landing внутри blend tree Jumping.")]
    [SerializeField, Min(0f)] private float yVelocityDampTime = 0.02f;

    [Tooltip("По какой максимальной |y-скорости| нормализуется yVelocity в диапазон " +
             "-1..1. По умолчанию берётся из PlayerConfigSO.MaxFallSpeed, чтобы " +
             "Landing включался ровно на пределе падения. Если ты хочешь раньше " +
             "видеть «полную» позу Landing — уменьши значение здесь.")]
    [SerializeField, Min(0.01f)] private float yVelocityReference = 0f;

    private Rigidbody2D body;
    private PlayerConfigSO config;
    private PlayerGroundChecker groundChecker;
    private bool initialized;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        if (facade == null)
            facade = GetComponent<PlayerFacade>();

        if (animator == null)
        {
            Debug.LogError($"{nameof(PlayerAnimation)}: Animator не найден.", this);
            enabled = false;
            return;
        }

        if (facade == null)
        {
            Debug.LogError($"{nameof(PlayerAnimation)}: PlayerFacade не найден.", this);
            enabled = false;
            return;
        }

        body = facade.Body;
        config = facade.Config;
        groundChecker = facade.GroundChecker;

        if (body == null || config == null || groundChecker == null)
        {
            Debug.LogError(
                $"{nameof(PlayerAnimation)}: PlayerFacade недонастроен (нет Body/Config/GroundChecker).",
                this);
            enabled = false;
            return;
        }

        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        Vector2 velocity = body.linearVelocity;

        // ---- IsGround ----
        bool isGrounded = groundChecker.IsGrounded;
        animator.SetBool(IsGroundHash, isGrounded);

        // ---- xVelocity (0..1) ----
        float maxSpeed = Mathf.Max(0.01f, config.MaxMoveSpeed);
        float absX = Mathf.Abs(velocity.x);

        // На земле резкое замедление до 0 не должно мерцать в Run; в воздухе
        // вообще не имеет смысла включать беговой цикл — играет blend tree
        // Jumping.
        float xParam;
        if (!isGrounded)
        {
            xParam = 0f;
        }
        else if (absX < runThreshold)
        {
            xParam = 0f;
        }
        else
        {
            xParam = Mathf.Clamp01(absX / maxSpeed);
        }

        if (xVelocityDampTime > 0f)
            animator.SetFloat(XVelocityHash, xParam, xVelocityDampTime, Time.deltaTime);
        else
            animator.SetFloat(XVelocityHash, xParam);

        // ---- yVelocity (-1..1) ----
        float yReference = yVelocityReference > 0f
            ? yVelocityReference
            : Mathf.Max(0.01f, config.MaxFallSpeed);

        float yParam = Mathf.Clamp(velocity.y / yReference, -1f, 1f);

        if (yVelocityDampTime > 0f)
            animator.SetFloat(YVelocityHash, yParam, yVelocityDampTime, Time.deltaTime);
        else
            animator.SetFloat(YVelocityHash, yParam);
    }
}
