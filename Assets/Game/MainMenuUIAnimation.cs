using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MainMenuUIAnimation : MonoBehaviour
{
    [System.Serializable]
    public class FlyItem
    {
        public RectTransform item;

        public Vector2 startPosition;
        public Vector2 endPosition;

        public float delay = 0f;
        public float duration = 1f;

        public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Eyes")]
        public bool eyesFollowThis = true;

        [HideInInspector] public bool isFlying;
    }

    [System.Serializable]
    public class Eye
    {
        [Header("Objects")]
        public RectTransform eyeSocket;
        public RectTransform pupil;

        [Header("Eye socket ellipse")]
        public Vector2 centerOffset;
        public float socketRadiusX = 20f;
        public float socketRadiusY = 10f;

        [Header("Pupil ellipse")]
        public float pupilRadiusX = 5f;
        public float pupilRadiusY = 5f;
    }

    [Header("Flying UI")]
    public List<FlyItem> flyItems = new List<FlyItem>();

    [Header("Eyes")]
    public List<Eye> eyes = new List<Eye>();

    [Header("Eye Settings")]
    public RectTransform targetWhileFlying;
    public float lookAtCenterTime = 2.5f;
    public float eyeSmoothSpeed = 10f;

    [Header("Gizmos")]
    public bool drawEyeGizmos = true;
    public int ellipseSegments = 32;

    [Header("Gizmo Colors")]
    public Color socketGizmoColor = Color.yellow;
    public Color pupilGizmoColor = Color.cyan;
    public Color movementAreaGizmoColor = Color.green;

    private bool followMouse;
    private bool lookAtEyeCenter;
    private int eyeTargetsLeft;

    private Vector2 currentLookScreenPosition;

    private Canvas canvas;
    private Camera uiCamera;

    private void Start()
    {
        canvas = GetComponentInParent<Canvas>();

        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCamera = canvas.worldCamera;
        }
        else
        {
            uiCamera = null;
        }

        followMouse = false;
        lookAtEyeCenter = false;

        currentLookScreenPosition = new Vector2(Screen.width / 2f, Screen.height / 2f);

        SetupStartPositions();
        SetupPupilsStartPosition();

        StartCoroutine(PlayMenuAnimation());
    }

    private void Update()
    {
        UpdateEyes();
    }

    private void SetupStartPositions()
    {
        for (int i = 0; i < flyItems.Count; i++)
        {
            if (flyItems[i].item != null)
            {
                flyItems[i].item.anchoredPosition = flyItems[i].startPosition;
            }
        }
    }

    private void SetupPupilsStartPosition()
    {
        for (int i = 0; i < eyes.Count; i++)
        {
            if (eyes[i].pupil != null)
            {
                eyes[i].pupil.anchoredPosition = eyes[i].centerOffset;
            }
        }
    }

    private IEnumerator PlayMenuAnimation()
    {
        eyeTargetsLeft = 0;

        for (int i = 0; i < flyItems.Count; i++)
        {
            if (flyItems[i].item != null)
            {
                if (flyItems[i].eyesFollowThis)
                {
                    eyeTargetsLeft++;
                }

                StartCoroutine(Fly(flyItems[i]));
            }
        }

        while (eyeTargetsLeft > 0)
        {
            yield return null;
        }

        followMouse = false;
        lookAtEyeCenter = true;

        yield return new WaitForSeconds(lookAtCenterTime);

        lookAtEyeCenter = false;
        followMouse = true;
    }

    private IEnumerator Fly(FlyItem flyItem)
    {
        yield return new WaitForSeconds(flyItem.delay);

        flyItem.isFlying = true;

        float timer = 0f;

        while (timer < flyItem.duration)
        {
            timer += Time.deltaTime;

            float t = timer / flyItem.duration;
            t = Mathf.Clamp01(t);

            float curveValue = flyItem.moveCurve.Evaluate(t);

            flyItem.item.anchoredPosition = Vector2.Lerp(
                flyItem.startPosition,
                flyItem.endPosition,
                curveValue
            );

            yield return null;
        }

        flyItem.item.anchoredPosition = flyItem.endPosition;
        flyItem.isFlying = false;

        if (flyItem.eyesFollowThis)
        {
            eyeTargetsLeft--;
        }
    }

    private void UpdateEyes()
    {
        if (lookAtEyeCenter)
        {
            MovePupilsToCenter();
            return;
        }

        Vector2 targetScreenPosition;

        if (followMouse)
        {
            targetScreenPosition = GetMouseScreenPosition();
        }
        else
        {
            RectTransform currentEyeTarget = GetCurrentFlyingEyeTarget();

            if (targetWhileFlying != null && HasFlyingEyeTarget())
            {
                targetScreenPosition = RectTransformUtility.WorldToScreenPoint(
                    uiCamera,
                    targetWhileFlying.position
                );

                currentLookScreenPosition = targetScreenPosition;
            }
            else if (currentEyeTarget != null)
            {
                targetScreenPosition = RectTransformUtility.WorldToScreenPoint(
                    uiCamera,
                    currentEyeTarget.position
                );

                currentLookScreenPosition = targetScreenPosition;
            }
            else
            {
                targetScreenPosition = currentLookScreenPosition;
            }
        }

        for (int i = 0; i < eyes.Count; i++)
        {
            MovePupil(eyes[i], targetScreenPosition);
        }
    }

    private void MovePupilsToCenter()
    {
        for (int i = 0; i < eyes.Count; i++)
        {
            if (eyes[i].pupil == null)
            {
                continue;
            }

            eyes[i].pupil.anchoredPosition = Vector2.Lerp(
                eyes[i].pupil.anchoredPosition,
                eyes[i].centerOffset,
                Time.deltaTime * eyeSmoothSpeed
            );
        }
    }

    private Vector2 GetMouseScreenPosition()
    {
        if (Mouse.current == null)
        {
            return new Vector2(Screen.width / 2f, Screen.height / 2f);
        }

        return Mouse.current.position.ReadValue();
    }

    private RectTransform GetCurrentFlyingEyeTarget()
    {
        for (int i = 0; i < flyItems.Count; i++)
        {
            if (flyItems[i].item != null &&
                flyItems[i].isFlying &&
                flyItems[i].eyesFollowThis)
            {
                return flyItems[i].item;
            }
        }

        return null;
    }

    private bool HasFlyingEyeTarget()
    {
        for (int i = 0; i < flyItems.Count; i++)
        {
            if (flyItems[i].isFlying && flyItems[i].eyesFollowThis)
            {
                return true;
            }
        }

        return false;
    }

    private void MovePupil(Eye eye, Vector2 targetScreenPosition)
    {
        if (eye.eyeSocket == null || eye.pupil == null)
        {
            return;
        }

        Vector2 targetLocalPosition;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            eye.eyeSocket,
            targetScreenPosition,
            uiCamera,
            out targetLocalPosition
        );

        Vector2 center = eye.centerOffset;
        Vector2 direction = targetLocalPosition - center;

        float moveRadiusX = eye.socketRadiusX - eye.pupilRadiusX;
        float moveRadiusY = eye.socketRadiusY - eye.pupilRadiusY;

        if (moveRadiusX < 0f)
        {
            moveRadiusX = 0f;
        }

        if (moveRadiusY < 0f)
        {
            moveRadiusY = 0f;
        }

        Vector2 clampedPosition = ClampPointToEllipse(
            center,
            direction,
            moveRadiusX,
            moveRadiusY
        );

        eye.pupil.anchoredPosition = Vector2.Lerp(
            eye.pupil.anchoredPosition,
            clampedPosition,
            Time.deltaTime * eyeSmoothSpeed
        );
    }

    private Vector2 ClampPointToEllipse(Vector2 center, Vector2 direction, float radiusX, float radiusY)
    {
        if (direction.magnitude < 0.01f)
        {
            return center;
        }

        if (radiusX <= 0f || radiusY <= 0f)
        {
            return center;
        }

        float x = direction.x;
        float y = direction.y;

        float value = (x * x) / (radiusX * radiusX) + (y * y) / (radiusY * radiusY);

        if (value <= 1f)
        {
            return center + direction;
        }

        float scale = 1f / Mathf.Sqrt(value);

        return center + direction * scale;
    }

    private void OnDrawGizmos()
    {
        if (drawEyeGizmos == false)
        {
            return;
        }

        if (ellipseSegments < 8)
        {
            ellipseSegments = 8;
        }

        for (int i = 0; i < eyes.Count; i++)
        {
            DrawEyeGizmo(eyes[i]);
        }
    }

    private void DrawEyeGizmo(Eye eye)
    {
        if (eye.eyeSocket == null)
        {
            return;
        }

        Vector2 center = eye.centerOffset;

        Gizmos.color = socketGizmoColor;
        DrawEllipse(
            eye.eyeSocket,
            center,
            eye.socketRadiusX,
            eye.socketRadiusY
        );

        float moveRadiusX = eye.socketRadiusX - eye.pupilRadiusX;
        float moveRadiusY = eye.socketRadiusY - eye.pupilRadiusY;

        if (moveRadiusX < 0f)
        {
            moveRadiusX = 0f;
        }

        if (moveRadiusY < 0f)
        {
            moveRadiusY = 0f;
        }

        Gizmos.color = movementAreaGizmoColor;
        DrawEllipse(
            eye.eyeSocket,
            center,
            moveRadiusX,
            moveRadiusY
        );

        Vector2 pupilPosition = center;

        if (eye.pupil != null)
        {
            pupilPosition = eye.pupil.anchoredPosition;
        }

        Gizmos.color = pupilGizmoColor;
        DrawEllipse(
            eye.eyeSocket,
            pupilPosition,
            eye.pupilRadiusX,
            eye.pupilRadiusY
        );

        Vector3 centerWorld = eye.eyeSocket.TransformPoint(
            new Vector3(center.x, center.y, 0f)
        );

        Gizmos.DrawSphere(centerWorld, 3f);
    }

    private void DrawEllipse(RectTransform parent, Vector2 center, float radiusX, float radiusY)
    {
        if (radiusX <= 0f || radiusY <= 0f)
        {
            return;
        }

        Vector3 previousPoint = Vector3.zero;

        for (int i = 0; i <= ellipseSegments; i++)
        {
            float angle = ((float)i / ellipseSegments) * Mathf.PI * 2f;

            Vector2 localPoint = new Vector2(
                Mathf.Cos(angle) * radiusX,
                Mathf.Sin(angle) * radiusY
            );

            Vector2 finalLocalPoint = center + localPoint;

            Vector3 worldPoint = parent.TransformPoint(
                new Vector3(finalLocalPoint.x, finalLocalPoint.y, 0f)
            );

            if (i > 0)
            {
                Gizmos.DrawLine(previousPoint, worldPoint);
            }

            previousPoint = worldPoint;
        }
    }
}