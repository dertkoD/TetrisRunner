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
    [Tooltip("Кнопка ускоренного падения активного блока (soft-drop). Удерживать.")]
    [SerializeField] private InputActionReference softDropAction;

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
    [Tooltip("Как часто блок может смещаться вбок при удержании клавиши (секунд между шагами).")]
    [SerializeField, Min(0.01f)] private float horizontalStepRepeatTime = 0.12f;
    [SerializeField, Min(0.01f)] private float fallStepDistance = 1f;
    [Tooltip("Скорость падения блоков: сколько секунд проходит между шагами вниз на одну клетку. " +
             "Меньше = быстрее. Например 0.5 — один шаг в полсекунды, 0.1 — очень быстро.")]
    [SerializeField, Min(0.01f)] private float fallStepInterval = 0.5f;
    [Tooltip("Во сколько раз ускоряется падение при soft-drop (S / стрелка вниз).")]
    [SerializeField, Min(1f)] private float softDropMultiplier = 8f;

    [Header("Spawn")]
    [Tooltip("Пауза в секундах между тем, как один блок залочился, и появлением следующего. " +
             "0 — без паузы (как в классическом тетрисе), >0 — даёт игроку немного отдышаться.")]
    [SerializeField, Min(0f)] private float spawnDelay = 0.25f;

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

    [Header("Cell Colors")]
    [Tooltip("Палитра цветов, из которой случайно выбирается цвет каждой ячейки спавнящегося блока. " +
             "Если массив пуст, будут использованы дефолтные цвета.")]
    [SerializeField]
    private Color[] cellColorPalette =
    {
        new Color(0.95f, 0.30f, 0.30f, 1f), // красный
        new Color(0.30f, 0.75f, 0.95f, 1f), // голубой
        new Color(0.95f, 0.85f, 0.30f, 1f), // жёлтый
        new Color(0.40f, 0.85f, 0.40f, 1f), // зелёный
        new Color(0.75f, 0.45f, 0.95f, 1f), // фиолетовый
        new Color(0.95f, 0.65f, 0.30f, 1f), // оранжевый
    };

    [Header("Death Water")]
    [Tooltip("На сколько клеток DeathWater поднимается вверх, когда падающий блок " +
             "встал на блок ДРУГОГО цвета (т.е. не произошёл матчинг).")]
    [SerializeField, Min(0)] private int deathWaterGrowOnDifferentColorLanding = 1;

    [Tooltip("На сколько клеток DeathWater опускается вниз, когда падающий блок " +
             "встал на блок ТАКОГО ЖЕ цвета (т.е. произошёл матчинг).")]
    [SerializeField, Min(0)] private int deathWaterShrinkOnSameColorLanding = 1;

    [Tooltip("На сколько клеток DeathWater поднимается вверх, когда падающий блок " +
             "проваливается в саму DeathWater.")]
    [SerializeField, Min(0)] private int deathWaterGrowOnBlockEnteringWater = 1;

    [Tooltip("Сколько секунд занимает плавный подъём DeathWater на ОДНУ клетку. " +
             "0 — мгновенно (как старое поведение). Например 1.5 = одна клетка " +
             "поднимается полторы секунды. Если событий несколько подряд, целевая " +
             "высота просто становится больше, а вода едет к ней без остановок.")]
    [SerializeField, Min(0f)] private float deathWaterGrowSecondsPerCell = 1.5f;

    [Tooltip("Сколько секунд занимает плавное опускание DeathWater на ОДНУ клетку " +
             "при матчинге. 0 — мгновенно.")]
    [SerializeField, Min(0f)] private float deathWaterShrinkSecondsPerCell = 0.6f;

    [Tooltip("Максимальная скорость движения уровня воды в клетках в секунду. " +
             "0 — без верхнего предела (полностью определяется *SecondsPerCell). " +
             "Полезно, чтобы при большом скоплении событий вода не уезжала слишком " +
             "быстро, даже если рассчитанная скорость велика.")]
    [SerializeField, Min(0f)] private float deathWaterMaxCellsPerSecond = 0f;

    [Header("Juice — Dissolve (схлопывание одинаковых блоков)")]
    [Tooltip("Материал DisMat (DissolveShaderGraph). Ставится на все ячейки блоков. " +
             "Когда блоки одного цвета схлопываются — на них проигрывается dissolve и " +
             "только после этого они исчезают. Если пусто — блоки исчезают мгновенно.")]
    [SerializeField] private Material blockDissolveMaterial;

    [Tooltip("Сколько секунд длится эффект растворения перед удалением блока.")]
    [SerializeField, Min(0.05f)] private float dissolveDuration = 0.55f;

    [Tooltip("Значение _DisolveAmount, при котором блок полностью виден (старт анимации).")]
    [SerializeField] private float dissolveStartAmount = 1.1f;

    [Tooltip("Значение _DisolveAmount, при котором блок полностью растворён (конец анимации).")]
    [SerializeField] private float dissolveEndAmount = 0f;

    [Tooltip("Максимальная толщина контура (_OutlineThickness) во время растворения.")]
    [SerializeField, Min(0f)] private float dissolveOutlineThickness = 0.15f;

    [Tooltip("Цвет контура растворения (_OutlineColor).")]
    [SerializeField] private Color dissolveOutlineColor = new Color(0f, 1f, 0.95f, 1f);

    [Tooltip("Множитель яркости контура (эмуляция HDR-интенсивности из примера).")]
    [SerializeField, Min(0f)] private float dissolveOutlineIntensity = 4f;

    [Header("Juice — Shock Wave (разные цвета / потеря блока)")]
    [Tooltip("Префаб ShockWaveRender (полноэкранный спрайт с материалом ShockMat / " +
             "ShockWaveSprite shader). Когда блок встал на блок ДРУГОГО цвета или " +
             "пропал за нижним краем сетки — из этого места запускается ударная волна, " +
             "и только после неё поднимается вода. Если пусто — вода поднимается сразу.")]
    [SerializeField] private GameObject shockWaveRenderPrefab;

    [Tooltip("Длительность ударной волны (секунды).")]
    [SerializeField, Min(0.05f)] private float shockWaveDuration = 0.7f;

    [Tooltip("До какого значения доезжает _WaveDistanceFromCenter (радиус волны).")]
    [SerializeField, Min(0f)] private float shockWaveMaxDistance = 1f;

    [Tooltip("Толщина кольца волны (_Size).")]
    [SerializeField, Min(0f)] private float shockWaveSize = 0.1f;

    [Tooltip("Сила искажения волны (_ShockWaveStrength).")]
    [SerializeField] private float shockWaveStrength = -0.08f;

    [Header("Juice — Stack Particles")]
    [Tooltip("Сколько частиц вылетает при стакинге блока (цвет совпадает с цветом блока).")]
    [SerializeField, Min(0)] private int stackParticleCount = 16;

    [Tooltip("Стартовая скорость частиц стакинга.")]
    [SerializeField, Min(0f)] private float stackParticleSpeed = 4f;

    [Tooltip("Время жизни частиц стакинга (секунды).")]
    [SerializeField, Min(0.05f)] private float stackParticleLifetime = 0.6f;

    [Tooltip("Размер частиц стакинга.")]
    [SerializeField, Min(0.005f)] private float stackParticleSize = 0.18f;

    public InputActionReference ToggleSpawnAction => toggleSpawnAction;
    public InputActionReference MoveAction => moveAction;
    public InputActionReference RotateLeftAction => rotateLeftAction;
    public InputActionReference RotateRightAction => rotateRightAction;
    public InputActionReference SoftDropAction => softDropAction;

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
    public float SoftDropMultiplier => softDropMultiplier;

    public float SpawnDelay => spawnDelay;

    public float RotationStepDegrees => rotationStepDegrees;

    public bool FreezeRotationWhileControlled => freezeRotationWhileControlled;

    public float ReleasedGravityScale => releasedGravityScale;

    public bool SnapPositionWhenStacking => snapPositionWhenStacking;
    public bool SnapRotationWhenStacking => snapRotationWhenStacking;
    public float GridCellSize => gridCellSize;

    public int DeathWaterGrowOnDifferentColorLanding => deathWaterGrowOnDifferentColorLanding;
    public int DeathWaterShrinkOnSameColorLanding => deathWaterShrinkOnSameColorLanding;
    public int DeathWaterGrowOnBlockEnteringWater => deathWaterGrowOnBlockEnteringWater;
    public float DeathWaterGrowSecondsPerCell => deathWaterGrowSecondsPerCell;
    public float DeathWaterShrinkSecondsPerCell => deathWaterShrinkSecondsPerCell;
    public float DeathWaterMaxCellsPerSecond => deathWaterMaxCellsPerSecond;

    public Material BlockDissolveMaterial => blockDissolveMaterial;
    public float DissolveDuration => dissolveDuration;
    public float DissolveStartAmount => dissolveStartAmount;
    public float DissolveEndAmount => dissolveEndAmount;
    public float DissolveOutlineThickness => dissolveOutlineThickness;
    public Color DissolveOutlineColor => dissolveOutlineColor;
    public float DissolveOutlineIntensity => dissolveOutlineIntensity;

    public GameObject ShockWaveRenderPrefab => shockWaveRenderPrefab;
    public bool ShockWaveOnDifferentColor => shockWaveOnDifferentColor;
    public bool ShockWaveOnSameColor => shockWaveOnSameColor;
    public bool ShockWaveOnBlockFellToBottom => shockWaveOnBlockFellToBottom;
    public float ShockWaveDuration => shockWaveDuration;
    public float ShockWaveMaxDistance => shockWaveMaxDistance;
    public float ShockWaveSize => shockWaveSize;
    public float ShockWaveStrength => shockWaveStrength;

    public int StackParticleCount => stackParticleCount;
    public float StackParticleSpeed => stackParticleSpeed;
    public float StackParticleLifetime => stackParticleLifetime;
    public float StackParticleSize => stackParticleSize;

    public Color[] CellColorPalette => cellColorPalette;
}
