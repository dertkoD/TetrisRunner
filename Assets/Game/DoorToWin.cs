using UnityEngine;

public class DoorToWin : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private WinMenuController winMenuController;

    [Header("Player")]
    [SerializeField] private string playerTag = "Player";

    private bool wasTriggered;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (wasTriggered)
            return;

        if (!other.CompareTag(playerTag))
            return;

        wasTriggered = true;

        if (winMenuController == null)
        {
            Debug.LogError("WinMenuController is not assigned on DoorToWin.");
            return;
        }

        winMenuController.ShowWinMenu();
    }
}
