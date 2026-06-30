using UnityEngine;
using UnityEngine.EventSystems;

public class MenuButtonAnimation : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Object To Animate")]
    public RectTransform target;

    [Header("Scale Settings")]
    public float normalScale = 1f;
    public float hoverScale = 1.12f;
    public float pressedScaleX = 0.92f;
    public float pressedScaleY = 0.82f;

    [Header("Speed")]
    public float animationSpeed = 12f;

    private Vector3 targetScale;
    private bool isPointerInside;
    private bool isPointerDown;

    private void Start()
    {
        if (target == null)
        {
            target = GetComponent<RectTransform>();
        }

        targetScale = new Vector3(normalScale, normalScale, normalScale);
        target.localScale = targetScale;
    }

    private void Update()
    {
        if (target == null)
        {
            return;
        }

        target.localScale = Vector3.Lerp(
            target.localScale,
            targetScale,
            Time.unscaledDeltaTime * animationSpeed
        );
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;

        if (isPointerDown == false)
        {
            targetScale = new Vector3(hoverScale, hoverScale, hoverScale);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;

        if (isPointerDown == false)
        {
            targetScale = new Vector3(normalScale, normalScale, normalScale);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPointerDown = true;

        targetScale = new Vector3(
            pressedScaleX,
            pressedScaleY,
            normalScale
        );
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerDown = false;

        if (isPointerInside)
        {
            targetScale = new Vector3(hoverScale, hoverScale, hoverScale);
        }
        else
        {
            targetScale = new Vector3(normalScale, normalScale, normalScale);
        }
    }
}
