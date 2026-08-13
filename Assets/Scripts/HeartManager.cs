using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class HeartManager : MonoBehaviour
{
    [Header("Heart UI Images (3 Slots)")]
    [SerializeField] private Image[] heartSlots;

    [Header("Heart Sprites")]
    [SerializeField] private Sprite fullHeartSprite;
    [SerializeField] private Sprite halfHeartSprite;
    [SerializeField] private Sprite emptyHeartSprite;

    public const int MaxHalfHearts = 6;
    public int CurrentHalfHearts { get; private set; }

    public bool IsGameOver => CurrentHalfHearts <= 0;

    public System.Action OnGameOver;
    public System.Action<int> OnHalfHeartDeducted;

    private void Awake()
    {
        CurrentHalfHearts = MaxHalfHearts;
    }

    public void ResetHearts()
    {
        CurrentHalfHearts = MaxHalfHearts;
        UpdateUI();
    }

    public bool DeductHalfHeart()
    {
        if (CurrentHalfHearts <= 0) return false;

        CurrentHalfHearts--;
        int affectedHeartIndex = CurrentHalfHearts / 2;

        UpdateUI();

        // Punch/Shake effect on damaged heart slot
        if (heartSlots != null && affectedHeartIndex >= 0 && affectedHeartIndex < heartSlots.Length)
        {
            RectTransform heartRect = heartSlots[affectedHeartIndex].rectTransform;
            heartRect.DOKill(true);
            heartRect.transform.localScale = Vector3.one;
            heartRect.DOPunchScale(Vector3.one * 0.35f, 0.4f, 8, 1f);
        }

        OnHalfHeartDeducted?.Invoke(CurrentHalfHearts);

        if (CurrentHalfHearts <= 0)
        {
            OnGameOver?.Invoke();
            return true;
        }

        return false;
    }

    public void UpdateUI()
    {
        if (heartSlots == null || heartSlots.Length < 3) return;

        for (int i = 0; i < 3; i++)
        {
            int slotHalfHearts = CurrentHalfHearts - (i * 2);

            if (slotHalfHearts >= 2)
            {
                heartSlots[i].sprite = fullHeartSprite;
                heartSlots[i].enabled = (fullHeartSprite != null);
            }
            else if (slotHalfHearts == 1)
            {
                heartSlots[i].sprite = halfHeartSprite;
                heartSlots[i].enabled = (halfHeartSprite != null);
            }
            else
            {
                heartSlots[i].sprite = emptyHeartSprite;
                heartSlots[i].enabled = (emptyHeartSprite != null);
            }
        }
    }
}
