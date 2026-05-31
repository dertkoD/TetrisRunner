using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Дополняет шейдерные волны префаба <c>Water</c> (WaterRW.SurfaceWaterCompute)
/// эффектами капель/брызг как 2D-частиц:
///
/// <list type="bullet">
///   <item>Когда блок или игрок ВХОДИТ в триггер воды — бьётся «всплеск»:
///         бёрст частиц у самой поверхности воды по X-координате объекта.
///         Сила бёрста зависит от скорости и массы.</item>
///   <item>Пока объект быстро двигается ВНУТРИ воды — периодически
///         выпрыгивают мелкие капли (тоже у поверхности по его X).</item>
///   <item>Когда объект ВЫЛЕТАЕТ из воды вверх — стреляет ещё один всплеск
///         (например, прыжок игрока из воды).</item>
/// </list>
///
/// Сам <see cref="ParticleSystem"/> создаётся в рантайме, поэтому в префабе
/// ничего настраивать не нужно (но при желании в инспекторе можно подменить
/// его на свой через поле <see cref="splashParticlesOverride"/>). Все
/// ключевые параметры (цвет, размер, скорость, время жизни, сколько частиц
/// и от чего реагирует) вынесены в SerializeField — крути под игру в
/// инспекторе самого префаба <c>Water</c>.
///
/// Этот компонент НЕ создаёт волн сам — волны делает уже стоящий на префабе
/// <c>SurfaceWaterCompute</c> через своё <c>layersToInteractWith</c>. Здесь
/// мы добавляем именно визуальные брызги и капли.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class WaterDynamicEffects : MonoBehaviour
{
    [Header("Particles (можно оставить пустым — будет создан автоматически)")]
    [Tooltip("Если задано — этот ParticleSystem используется вместо авто-сгенерированного. " +
             "Удобно, если хочешь свои спрайты капель / свой материал.")]
    [SerializeField] private ParticleSystem splashParticlesOverride;

    [Header("Filters")]
    [Tooltip("Слои, при контакте с которыми эффекты включаются. По умолчанию — всё " +
             "что угодно. Имеет смысл оставить только Block и Player, чтобы не " +
             "брызгало от каждой штуки.")]
    [SerializeField] private LayerMask reactiveLayers = ~0;

    [Header("Splash (вход/выход)")]
    [Tooltip("Минимальная скорость удара (мир/сек), при которой запускаем всплеск.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 1.5f;

    [Tooltip("Сколько дополнительных частиц добавляется за единицу скорости×массы. " +
             "0 — берётся только базовый бёрст ниже.")]
    [SerializeField, Min(0f)] private float burstCountPerImpulse = 1.5f;

    [Tooltip("Базовое количество частиц в одном всплеске.")]
    [SerializeField, Range(0, 200)] private int baseBurstCount = 8;

    [Tooltip("Потолок частиц в одном всплеске (чтобы крупный блок не взрывал систему).")]
    [SerializeField, Range(1, 500)] private int maxBurstCount = 40;

    [Header("Movement droplets (внутри воды)")]
    [Tooltip("Если скорость движения объекта внутри воды больше этой — стреляем капли.")]
    [SerializeField, Min(0f)] private float dropletMinSpeed = 2f;

    [Tooltip("Сколько секунд между бёрстами капель для одного объекта (анти-спам).")]
    [SerializeField, Min(0.02f)] private float dropletInterval = 0.08f;

    [Tooltip("Сколько частиц в одном бёрсте капель от движения.")]
    [SerializeField, Range(0, 50)] private int dropletBurstCount = 3;

    [Header("Particle Look (используется только для авто-системы)")]
    [SerializeField] private Color startColor = new Color(0.7f, 0.9f, 1f, 0.85f);
    [SerializeField, Min(0.01f)] private float minLifetime = 0.35f;
    [SerializeField, Min(0.01f)] private float maxLifetime = 0.75f;
    [SerializeField, Min(0f)] private float minStartSpeed = 2.5f;
    [SerializeField, Min(0f)] private float maxStartSpeed = 5.5f;
    [SerializeField, Min(0.005f)] private float minStartSize = 0.06f;
    [SerializeField, Min(0.005f)] private float maxStartSize = 0.18f;
    [Tooltip("Гравитация частиц (мир-y/sec^2 с учётом Physics2D.gravity), чтобы капли " +
             "падали обратно вниз.")]
    [SerializeField, Min(0f)] private float gravityModifier = 1f;
    [Tooltip("Половина угла конуса разлёта (в градусах). 0 — все летят строго вверх, " +
             "90 — во все стороны над поверхностью.")]
    [SerializeField, Range(0f, 90f)] private float spreadHalfAngleDegrees = 35f;
    [Tooltip("Sorting Layer для частиц. Пусто/Default — пойдут в Default. Если у тебя " +
             "вода рисуется в Front — поставь то же имя, иначе капли уедут под воду.")]
    [SerializeField] private string sortingLayerName = "";
    [SerializeField] private int sortingOrder = 1;

    private Collider2D selfCollider;
    private ParticleSystem activeParticles;
    private bool autoCreatedParticles;

    private readonly Dictionary<Rigidbody2D, float> nextDropletTime
        = new Dictionary<Rigidbody2D, float>();

    private void Awake()
    {
        selfCollider = GetComponent<Collider2D>();

        if (selfCollider != null && !selfCollider.isTrigger)
        {
            Debug.LogWarning(
                $"{nameof(WaterDynamicEffects)}: Collider2D на '{name}' не помечен как " +
                $"Is Trigger — события OnTriggerEnter2D могут не приходить. Включи Is Trigger.",
                this);
        }

        EnsureParticleSystem();
    }

    private void OnDisable()
    {
        nextDropletTime.Clear();
    }

    private void EnsureParticleSystem()
    {
        if (splashParticlesOverride != null)
        {
            activeParticles = splashParticlesOverride;
            autoCreatedParticles = false;
            return;
        }

        // Создаём ParticleSystem-ребёнка в рантайме. Так не приходится тащить
        // огромный ParticleSystem-блок в YAML префаба, и любой, кто закинет
        // префаб Water в сцену, сразу получит работающие брызги.
        GameObject go = new GameObject("WaterSplashParticles (auto)");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ConfigureAutoParticleSystem(ps);

        activeParticles = ps;
        autoCreatedParticles = true;
    }

    private void ConfigureAutoParticleSystem(ParticleSystem ps)
    {
        // ВАЖНО: эти .main / .emission и т.д. — это структуры-обёртки, у них
        // ОТСУТСТВУЕТ передача по ссылке у нас в C#, но у ParticleSystem
        // через них всё равно идёт setter-проброс. Так что присваивание
        // полям прокидывается в систему.
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startColor = new ParticleSystem.MinMaxGradient(startColor);
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minStartSpeed, maxStartSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minStartSize, maxStartSize);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(gravityModifier);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1024;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = spreadHalfAngleDegrees;
        shape.radius = 0.05f;
        // Cone «смотрит» по оси +Z; поворачиваем его в +Y, чтобы капли летели
        // вверх над поверхностью воды.
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(startColor, 0f),
                new GradientColorKey(startColor, 1f),
            },
            new[] {
                new GradientAlphaKey(startColor.a, 0f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(g);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0.2f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = false;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;

            // Берём стандартный материал «Default-Particle» через шейдер,
            // чтобы не таскать ассеты. Если в проекте URP — этот шейдер тоже
            // существует и даёт мягкую круглую кляксу.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader != null)
                renderer.material = new Material(shader) { color = startColor };

            if (!string.IsNullOrEmpty(sortingLayerName))
                renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!ShouldReact(other))
            return;

        Rigidbody2D rb = other.attachedRigidbody;
        Vector2 velocity = rb != null ? rb.linearVelocity : Vector2.zero;
        float speed = velocity.magnitude;

        if (speed < minImpactSpeed)
            return;

        float mass = rb != null ? rb.mass : 1f;
        EmitSplash(other, speed, mass);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!ShouldReact(other))
            return;

        Rigidbody2D rb = other.attachedRigidbody;
        if (rb != null)
            nextDropletTime.Remove(rb);

        if (rb == null)
            return;

        Vector2 velocity = rb.linearVelocity;
        if (velocity.y > minImpactSpeed)
            EmitSplash(other, velocity.magnitude, rb.mass);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!ShouldReact(other))
            return;

        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null)
            return;

        float speed = rb.linearVelocity.magnitude;
        if (speed < dropletMinSpeed)
            return;

        float now = Time.time;
        if (nextDropletTime.TryGetValue(rb, out float t) && now < t)
            return;

        nextDropletTime[rb] = now + dropletInterval;
        EmitDroplets(other, speed);
    }

    private bool ShouldReact(Collider2D other)
    {
        if (other == null)
            return false;

        return ((1 << other.gameObject.layer) & reactiveLayers.value) != 0;
    }

    private void EmitSplash(Collider2D other, float speed, float mass)
    {
        if (activeParticles == null)
            return;

        int burst = baseBurstCount + Mathf.RoundToInt(burstCountPerImpulse * speed * mass);
        burst = Mathf.Clamp(burst, 1, maxBurstCount);

        EmitAtSurface(other, burst);
    }

    private void EmitDroplets(Collider2D other, float speed)
    {
        if (activeParticles == null || dropletBurstCount <= 0)
            return;

        EmitAtSurface(other, dropletBurstCount);
    }

    private void EmitAtSurface(Collider2D other, int burstCount)
    {
        Vector2 surfacePoint = ComputeSurfacePoint(other);

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            position = new Vector3(
                surfacePoint.x,
                surfacePoint.y,
                activeParticles.transform.position.z),
            applyShapeToPosition = true,
        };

        activeParticles.Emit(emitParams, burstCount);
    }

    /// <summary>
    /// Возвращает точку, где должен «жить» всплеск: X — центр объекта,
    /// Y — верхняя граница воды (верхняя грань текущего bounds коллайдера).
    /// Когда вода поднимается через <see cref="DeathWaterController"/> —
    /// bounds.max.y тоже растёт, так что брызги всегда у нужной поверхности.
    /// </summary>
    private Vector2 ComputeSurfacePoint(Collider2D other)
    {
        float surfaceY = selfCollider != null
            ? selfCollider.bounds.max.y
            : transform.position.y;

        return new Vector2(other.bounds.center.x, surfaceY);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (minLifetime > maxLifetime)
            maxLifetime = minLifetime;
        if (minStartSpeed > maxStartSpeed)
            maxStartSpeed = minStartSpeed;
        if (minStartSize > maxStartSize)
            maxStartSize = minStartSize;
    }
#endif
}
