using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Управляет полноэкранным спрайтом с шейдером <c>ShockWaveSprite</c> (материал
/// ShockMat). Проигрывает расходящуюся ударную волну из заданной мировой точки.
///
/// Шейдер искажает картинку <c>_CameraSortingLayerTexture</c>, поэтому в
/// настройках URP-рендерера должна быть включена «Camera Sorting Layer Texture»,
/// а спрайт волны должен находиться на сортировочном слое ВЫШЕ тех, что
/// попадают в эту текстуру (как и настроено на префабе ShockWaveRender).
///
/// На время волны спрайт растягивается под текущую камеру (чтобы искажение
/// было видно на всём экране), центр кольца (<c>_RingSpawnPosition</c>)
/// выставляется в экранные UV точки события, а <c>_WaveDistanceFromCenter</c>
/// плавно доезжает от 0 до максимума. По окончании спрайт выключается и
/// вызывается колбэк (например, подъём воды).
/// </summary>
[DisallowMultipleComponent]
public class ShockWaveController : MonoBehaviour
{
    private static readonly int RingSpawnPositionId = Shader.PropertyToID("_RingSpawnPosition");
    private static readonly int WaveDistanceId = Shader.PropertyToID("_WaveDistanceFromCenter");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int StrengthId = Shader.PropertyToID("_ShockWaveStrength");
    private static readonly int XSizeRatioId = Shader.PropertyToID("_XSizeRatio");

    private TetrisBlockConfigSO config;
    private SpriteRenderer spriteRenderer;
    private Material material;

    private readonly Queue<Request> pending = new Queue<Request>();
    private bool processing;

    private struct Request
    {
        public Vector3 WorldPosition;
        public Action OnComplete;
    }

    /// <summary>
    /// Имя сортировочного слоя, на котором ОБЯЗАН находиться спрайт волны.
    /// Он должен быть ВЫШЕ слоя, до которого захватывается
    /// <c>_CameraSortingLayerTexture</c> (Foremost Sorting Layer в Renderer2D).
    /// Иначе полноэкранный непрозрачный спрайт волны перекроет всё, что не
    /// попало в эту текстуру (например, воду), и оно «пропадёт» на время волны.
    /// </summary>
    private const string ShockWaveSortingLayer = "ShockWave";

    public void Initialize(TetrisBlockConfigSO config)
    {
        this.config = config;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            // material (а не sharedMaterial) — создаёт инстанс, чтобы анимация
            // не писала в общий ассет ShockMat на диске.
            material = spriteRenderer.material;

            // Принудительно кладём спрайт на верхний слой ShockWave, чтобы он
            // рисовался ПОВЕРХ захваченной _CameraSortingLayerTexture, а не
            // перекрывал не попавший в неё контент (воду и т.п.).
            ForceTopSortingLayer();

            spriteRenderer.enabled = false;
        }
    }

    private void ForceTopSortingLayer()
    {
        if (spriteRenderer == null)
            return;

        SortingLayer[] layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name == ShockWaveSortingLayer)
            {
                spriteRenderer.sortingLayerID = layers[i].id;
                spriteRenderer.sortingOrder = 32000;
                return;
            }
        }
    }

    /// <summary>
    /// Ставит волну в очередь. Если в момент вызова уже играет другая волна —
    /// новая запустится сразу после неё (волны не накладываются визуально, но
    /// колбэк каждой гарантированно вызывается).
    /// </summary>
    public void Enqueue(Vector3 worldPosition, Action onComplete)
    {
        if (spriteRenderer == null || material == null)
        {
            onComplete?.Invoke();
            return;
        }

        pending.Enqueue(new Request { WorldPosition = worldPosition, OnComplete = onComplete });

        if (!processing)
            StartCoroutine(ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        processing = true;

        while (pending.Count > 0)
        {
            Request request = pending.Dequeue();
            yield return PlayOne(request.WorldPosition);
            request.OnComplete?.Invoke();
        }

        processing = false;
    }

    private IEnumerator PlayOne(Vector3 worldPosition)
    {
        Camera cam = ResolveCamera();
        FitToCamera(cam);

        Vector3 viewport = cam != null
            ? cam.WorldToViewportPoint(worldPosition)
            : new Vector3(0.5f, 0.5f, 0f);

        material.SetVector(RingSpawnPositionId, new Vector4(viewport.x, viewport.y, 0f, 0f));

        if (cam != null)
            material.SetFloat(XSizeRatioId, Mathf.Max(0.01f, cam.aspect));

        float duration = config != null ? Mathf.Max(0.05f, config.ShockWaveDuration) : 0.7f;
        float maxDistance = config != null ? config.ShockWaveMaxDistance : 1f;
        float size = config != null ? config.ShockWaveSize : 0.1f;
        float strength = config != null ? config.ShockWaveStrength : -0.08f;

        spriteRenderer.enabled = true;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / duration);

            // Кольцо расходится из центра, а сила искажения и толщина кольца
            // плавно гаснут к концу — чтобы волна не «обрубалась» резко.
            material.SetFloat(WaveDistanceId, Mathf.Lerp(0f, maxDistance, n));
            material.SetFloat(SizeId, size * (1f - n));
            material.SetFloat(StrengthId, strength * (1f - n));

            yield return null;
        }

        material.SetFloat(WaveDistanceId, maxDistance);
        material.SetFloat(SizeId, 0f);
        material.SetFloat(StrengthId, 0f);

        spriteRenderer.enabled = false;
    }

    private Camera ResolveCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            cam = FindFirstObjectByType<Camera>();
        return cam;
    }

    /// <summary>
    /// Растягивает спрайт волны так, чтобы он накрывал весь кадр текущей
    /// камеры (для ортографической камеры — точно по размеру вьюпорта).
    /// </summary>
    private void FitToCamera(Camera cam)
    {
        if (cam == null || spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        float worldHeight;
        float worldWidth;

        if (cam.orthographic)
        {
            worldHeight = cam.orthographicSize * 2f;
            worldWidth = worldHeight * cam.aspect;
        }
        else
        {
            float distance = Mathf.Abs(transform.position.z - cam.transform.position.z);
            worldHeight = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            worldWidth = worldHeight * cam.aspect;
        }

        Vector2 spriteSize = spriteRenderer.sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            return;

        // Небольшой запас (1.02), чтобы по краям не светились пиксели вне волны.
        transform.localScale = new Vector3(
            worldWidth / spriteSize.x * 1.02f,
            worldHeight / spriteSize.y * 1.02f,
            1f);

        Vector3 camPos = cam.transform.position;
        transform.position = new Vector3(camPos.x, camPos.y, transform.position.z);
    }
}
