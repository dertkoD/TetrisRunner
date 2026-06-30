using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[AddComponentMenu("Game/UI Button Click Audio Binder")]
public class UIButtonClickAudioBinder : MonoBehaviour
{
    [SerializeField] private bool bindOnEnable = true;
    [SerializeField] private bool includeInactiveChildren = true;
    [SerializeField] private Button[] explicitButtons;

    private readonly List<Button> boundButtons = new List<Button>();

    private void OnEnable()
    {
        if (bindOnEnable)
            RefreshBindings();
    }

    private void OnDisable()
    {
        ClearBindings();
    }

    [ContextMenu("Refresh Bindings")]
    public void RefreshBindings()
    {
        ClearBindings();

        if (explicitButtons != null)
        {
            for (int i = 0; i < explicitButtons.Length; i++)
                Bind(explicitButtons[i]);
        }

        Button[] childButtons = GetComponentsInChildren<Button>(includeInactiveChildren);
        for (int i = 0; i < childButtons.Length; i++)
            Bind(childButtons[i]);
    }

    private void Bind(Button button)
    {
        if (button == null || boundButtons.Contains(button))
            return;

        button.onClick.AddListener(HandleButtonClick);
        boundButtons.Add(button);
    }

    private void ClearBindings()
    {
        for (int i = 0; i < boundButtons.Count; i++)
        {
            Button button = boundButtons[i];
            if (button != null)
                button.onClick.RemoveListener(HandleButtonClick);
        }

        boundButtons.Clear();
    }

    private void HandleButtonClick()
    {
        GameAudioController.PlayUiClick();
    }
}
