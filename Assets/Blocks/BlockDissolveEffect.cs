using System.Collections;
using UnityEngine;

/// <summary>
/// Проигрывает на блоке эффект растворения (шейдер DissolveShaderGraph /
/// материал DisMat) и по его завершении уничтожает GameObject блока.
///
/// Используется при схлопывании блоков одного цвета: блок логически уже снят
/// с сетки (см. <see cref="TetrisGridBoard.ResolveMatches"/>), а визуально
/// плавно растворяется через анимацию <c>_DisolveAmount</c> и контур
/// <c>_OutlineThickness</c>. Значение <c>_DisolveAmount</c> анимируется
/// per-renderer через <see cref="MaterialPropertyBlock"/>, поэтому общий
/// материал DisMat на других блоках не затрагивается.
///
/// Если juice/материал не сконфигурированы — блок удаляется мгновенно
/// (старое поведение), чтобы логика сетки не зависла.
/// </summary>
[DisallowMultipleComponent]
public class BlockDissolveEffect : MonoBehaviour
{
    private static readonly int DisolveAmountId = Shader.PropertyToID("_DisolveAmount");
    private static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

    /// <summary>
    /// Запускает растворение блока, если доступна нужная конфигурация juice;
    /// иначе сразу уничтожает блок.
    /// </summary>
    public static void PlayOrDestroy(TetrisPlacedBlock block)
    {
        if (block == null)
            return;

        BlockJuiceController juice = BlockJuiceController.Instance;
        TetrisBlockConfigSO config = juice != null ? juice.Config : null;

        if (config == null || config.BlockDissolveMaterial == null)
        {
            Destroy(block.gameObject);
            return;
        }

        BlockDissolveEffect effect = block.gameObject.GetComponent<BlockDissolveEffect>();
        if (effect == null)
            effect = block.gameObject.AddComponent<BlockDissolveEffect>();

        effect.Begin(config);
    }

    private bool started;

    private void Begin(TetrisBlockConfigSO config)
    {
        if (started)
            return;

        started = true;

        // Растворяющийся блок больше не должен быть препятствием для игрока.
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        StartCoroutine(Run(config));
    }

    private IEnumerator Run(TetrisBlockConfigSO config)
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        float duration = Mathf.Max(0.05f, config.DissolveDuration);
        float startAmount = config.DissolveStartAmount;
        float endAmount = config.DissolveEndAmount;
        float maxOutline = config.DissolveOutlineThickness;

        Color outlineColor = config.DissolveOutlineColor * Mathf.Max(0f, config.DissolveOutlineIntensity);
        outlineColor.a = 1f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / duration);

            float amount = Mathf.Lerp(startAmount, endAmount, n);
            float outline = Mathf.Lerp(0f, maxOutline, n);

            ApplyToRenderers(renderers, mpb, amount, outline, outlineColor);
            yield return null;
        }

        Destroy(gameObject);
    }

    private static void ApplyToRenderers(
        SpriteRenderer[] renderers,
        MaterialPropertyBlock mpb,
        float amount,
        float outline,
        Color outlineColor)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(mpb);
            mpb.SetFloat(DisolveAmountId, amount);
            mpb.SetFloat(OutlineThicknessId, outline);
            mpb.SetColor(OutlineColorId, outlineColor);
            renderer.SetPropertyBlock(mpb);
        }
    }
}
