using UnityEngine;

/// <summary>
/// Заставляет <c>WaterRW.SurfaceWaterCompute</c> рисовать волны от блоков
/// даже когда блок ПОЛНОСТЬЮ под водой.
///
/// Зачем это нужно: WaterRW сканирует «поверхность» горизонтальным
/// <see cref="Physics2D.Linecast"/> по самой верхней грани воды и берёт
/// скорость с <see cref="Rigidbody2D.linearVelocity"/> того, кого пересекли.
/// Если блок целиком ниже поверхности, линейкаст его не задевает —
/// и волна не появляется. Кроме того, наши блоки кинематические и двигаются
/// через <see cref="Rigidbody2D.MovePosition"/>, который сам velocity не
/// меняет (это поправлено в <c>TetrisBlockMovement</c>).
///
/// Решение: на каждый блок вешается этот скрипт. Пока блок под водой, он
/// держит в сцене отдельный «прокси»-объект:
/// <list type="bullet">
///   <item>отдельный GameObject в корне сцены (не дочка блока — иначе
///         у его коллайдера <c>attachedRigidbody</c> вернёт rigidbody
///         блока и WaterRW снова прочитает velocity с него, а не с
///         нашего провайдера);</item>
///   <item>тонкий триггер-<see cref="BoxCollider2D"/> у самой поверхности
///         воды по X-координате блока — его и ловит линейкаст WaterRW;</item>
///   <item>компонент <see cref="VelocityProvider"/>, реализующий
///         <c>Ruccho.IWaterRWInteractionProvider</c>, который возвращает
///         фактическую скорость блока (считается тут же по дельте позиции).</item>
/// </list>
///
/// Когда блок выходит из воды или уничтожается — прокси удаляется. Сам
/// слой и параметры прокси можно крутить через инспектор. По умолчанию
/// он живёт на слое Default, который входит в <c>layersToInteractWith</c>
/// у воды (385 = Default | Block | Player).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class BlockWaveProxy : MonoBehaviour
{
    [Tooltip("Слой прокси-коллайдера. ДОЛЖЕН входить в layersToInteractWith у Water " +
             "(по умолчанию 0 = Default, у Water это включено).")]
    [SerializeField] private int proxyLayer = 0;

    [Tooltip("Высота прокси-коллайдера в мировых единицах — тонкая полоска у поверхности.")]
    [SerializeField, Min(0.01f)] private float proxyHeight = 0.1f;

    [Tooltip("На сколько единиц прокси утоплен ниже поверхности воды. Чем глубже — " +
             "тем мягче он пересекает линейкаст WaterRW (но если уйти слишком глубоко, " +
             "линейкаст вообще промахнётся, потому что бьёт ровно по верхней грани).")]
    [SerializeField, Min(0f)] private float proxyDepthBelowSurface = 0.05f;

    [Tooltip("Если |скорость блока| меньше этого — прокси активен, но velocity = 0 " +
             "(волну не гоним, чтобы не было еле заметных вибраций от дрожащих блоков).")]
    [SerializeField, Min(0f)] private float minBroadcastSpeed = 0.05f;

    [Tooltip("Запас (мир-Y), на сколько верх блока должен уйти под поверхность, чтобы " +
             "включить прокси. 0 — как только пересёк поверхность; чуть больше — даём " +
             "поработать обычной wave-логике WaterRW и не дублируем волну в момент входа.")]
    [SerializeField, Min(0f)] private float submergedSlack = 0.02f;

    private Rigidbody2D body;
    private Collider2D mainCollider;
    private GameObject proxyGO;
    private BoxCollider2D proxyCollider;
    private VelocityProvider proxyProvider;
    private Vector2 prevPosition;
    private bool hasPrev;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        mainCollider = GetComponent<Collider2D>();
        if (mainCollider == null)
            mainCollider = GetComponentInChildren<Collider2D>();
    }

    private void OnDisable()
    {
        DestroyProxy();
        hasPrev = false;
    }

    private void OnDestroy()
    {
        DestroyProxy();
    }

    private void FixedUpdate()
    {
        DeathWaterController water = DeathWaterController.Instance;
        if (water == null || mainCollider == null)
        {
            DestroyProxy();
            hasPrev = false;
            return;
        }

        float surfaceY = water.CurrentTopY;
        Bounds b = mainCollider.bounds;

        // Прокси нужен только если блок целиком под водой — на самой
        // поверхности (или выше) WaterRW и так увидит сам коллайдер блока.
        bool fullySubmerged = b.max.y < surfaceY - submergedSlack;
        if (!fullySubmerged)
        {
            DestroyProxy();
            hasPrev = false;
            return;
        }

        EnsureProxy();

        // Считаем скорость от изменения позиции тела.
        Vector2 currentPos = body.position;
        Vector2 vel = Vector2.zero;
        if (hasPrev && Time.fixedDeltaTime > 0f)
            vel = (currentPos - prevPosition) / Time.fixedDeltaTime;
        prevPosition = currentPos;
        hasPrev = true;

        proxyProvider.Velocity = vel.sqrMagnitude < minBroadcastSpeed * minBroadcastSpeed
            ? Vector2.zero
            : vel;

        // Прокси висит у самой поверхности воды строго над блоком, по
        // ширине совпадает с блоком — так волна будет такой же ширины.
        proxyGO.transform.position = new Vector3(
            b.center.x,
            surfaceY - proxyDepthBelowSurface - proxyHeight * 0.5f,
            0f);

        proxyCollider.size = new Vector2(Mathf.Max(0.05f, b.size.x), proxyHeight);
    }

    private void EnsureProxy()
    {
        if (proxyGO != null)
            return;

        proxyGO = new GameObject($"BlockWaveProxy[{name}]");
        // Не сохраняем прокси в сцене: он живёт только в рантайме.
        proxyGO.hideFlags = HideFlags.DontSave;
        proxyGO.layer = proxyLayer;

        proxyCollider = proxyGO.AddComponent<BoxCollider2D>();
        proxyCollider.isTrigger = true;

        proxyProvider = proxyGO.AddComponent<VelocityProvider>();
    }

    private void DestroyProxy()
    {
        if (proxyGO == null)
            return;

        if (Application.isPlaying)
            Destroy(proxyGO);
        else
            DestroyImmediate(proxyGO);

        proxyGO = null;
        proxyCollider = null;
        proxyProvider = null;
    }

    /// <summary>
    /// Маленький компонент, который рассказывает WaterRW «текущую» скорость
    /// нашего блока через интерфейс <c>Ruccho.IWaterRWInteractionProvider</c>.
    /// Он живёт на отдельном root-GameObject (см. <see cref="EnsureProxy"/>),
    /// чтобы у его коллайдера <c>attachedRigidbody</c> был null — иначе
    /// WaterRW сначала возьмёт velocity с этого rigidbody и наш провайдер
    /// проигнорирует.
    /// </summary>
    private sealed class VelocityProvider : MonoBehaviour, Ruccho.IWaterRWInteractionProvider
    {
        public Vector2 Velocity { get; set; }
    }
}
