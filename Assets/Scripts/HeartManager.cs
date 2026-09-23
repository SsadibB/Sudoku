using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class HeartManager : MonoBehaviour
{
    // ---- NEW: 3-slot life model via Life/Lifeless child GameObjects ----
    [Header("Life Slots (Life1, Life2, Life3 - each has Life + Lifeless children)")]
    [Tooltip("Assign Life1, Life2, Life3 GameObjects here (in order, slot 0 = rightmost lost first).")]
    [SerializeField] private GameObject[] lifeSlots; // Life1, Life2, Life3

    // ---- Legacy: kept for backward-compat with heartSlots Image array usage ----
    [Header("Heart UI Images (3 Slots) - Legacy / fallback")]
    [SerializeField] private Image[] heartSlots;

    [Header("Heart Sprites")]
    [SerializeField] private Sprite fullHeartSprite;
    [SerializeField] private Sprite halfHeartSprite;
    [SerializeField] private Sprite emptyHeartSprite;

    public Sprite FullHeartSprite => fullHeartSprite;
    public Sprite HalfHeartSprite => halfHeartSprite;
    public Sprite EmptyHeartSprite => emptyHeartSprite;

    [Header("Straw Hat Fall Effect (2 hats per heart slot = 6 total)")]
    [Tooltip("6 straw hat RectTransforms, 2 per heart slot, in this exact order:\n[0] Heart 0 - 1st half lost, [1] Heart 0 - 2nd half lost,\n[2] Heart 1 - 1st half lost, [3] Heart 1 - 2nd half lost,\n[4] Heart 2 - 1st half lost, [5] Heart 2 - 2nd half lost.\nPosition each one in the Scene above its matching heart - that position becomes its reset/start pose. Left/right jump direction is auto-detected by comparing each pair's X position.")]
    [SerializeField] private RectTransform[] strawHatRects;
    [Tooltip("The Image component on each hat object above, same order/length as Straw Hat Rects (used to fade it out as it falls).")]
    [SerializeField] private Image[] strawHatImages;

    [Header("Phase 1 - Hop up and to the side")]
    [SerializeField] private float hatJumpSideDistance = 90f;
    [SerializeField] private float hatJumpArcHeight = 70f;
    [SerializeField] private float hatJumpNetRise = 40f;
    [SerializeField] private float hatJumpDuration = 0.3f;

    [Header("Phase 2 - Falls a little, then disappears")]
    [SerializeField] private float hatFallDistance = 160f;
    [SerializeField] private float hatFallDuration = 0.55f;
    [SerializeField] private float hatRotationAmount = 200f;

    private Vector2[] hatStartAnchoredPositions;
    private Quaternion[] hatStartRotations;
    private int[] hatJumpDirections; // +1 = jumps right, -1 = jumps left
    private bool hatStartPosCaptured;

    // ---- 3-life model ----
    public const int MaxLives = 3;
    public int CurrentLives { get; private set; }

    // Back-compat: multiplayer and external callers use half-heart values
    public const int MaxHalfHearts = MaxLives * 2;
    public int CurrentHalfHearts => CurrentLives * 2;

    // Only 1 life remaining. Plays once per game.
    private bool oneHeartWarningPlayed;

    public bool IsGameOver => CurrentLives <= 0;

    public System.Action OnGameOver;
    public System.Action<int> OnHalfHeartDeducted;

    private void Awake()
    {
        CurrentLives = MaxLives;
        CaptureHatStartPositions();
    }

    // Remembers each hat's designed pose - position AND the tilt/rotation
    // set on it in the Inspector - so it can be reset there before every
    // fall instead of snapping upright. Also figures out, per pair of hats
    // above the same heart, which one sits on the left vs the right, so
    // each hat jumps outward on the correct side.
    private void CaptureHatStartPositions()
    {
        if (strawHatRects == null || hatStartPosCaptured) return;

        hatStartAnchoredPositions = new Vector2[strawHatRects.Length];
        hatStartRotations = new Quaternion[strawHatRects.Length];
        hatJumpDirections = new int[strawHatRects.Length];

        for (int i = 0; i < strawHatRects.Length; i++)
        {
            if (strawHatRects[i] == null) continue;
            hatStartAnchoredPositions[i] = strawHatRects[i].anchoredPosition;
            hatStartRotations[i] = strawHatRects[i].localRotation;
        }

        for (int pairStart = 0; pairStart + 1 < strawHatRects.Length; pairStart += 2)
        {
            float xA = hatStartAnchoredPositions[pairStart].x;
            float xB = hatStartAnchoredPositions[pairStart + 1].x;

            if (xA >= xB)
            {
                hatJumpDirections[pairStart] = 1;      // right hat -> jumps right
                hatJumpDirections[pairStart + 1] = -1;  // left hat -> jumps left
            }
            else
            {
                hatJumpDirections[pairStart] = -1;
                hatJumpDirections[pairStart + 1] = 1;
            }
        }

        hatStartPosCaptured = true;
    }

    public void ResetHearts()
    {
        CurrentLives = MaxLives;
        oneHeartWarningPlayed = false;
        UpdateUI();
        ResetAllHats();
    }

    // Snaps every hat back to its start pose, fully visible, with no
    // animation - used at the start of a new game.
    private void ResetAllHats()
    {
        if (strawHatRects == null) return;
        if (hatStartAnchoredPositions == null) CaptureHatStartPositions();
        if (hatStartAnchoredPositions == null) return;

        for (int i = 0; i < strawHatRects.Length; i++)
        {
            RectTransform rect = strawHatRects[i];
            if (rect == null) continue;

            rect.DOKill();
            rect.gameObject.SetActive(true);
            rect.anchoredPosition = hatStartAnchoredPositions[i];
            rect.localRotation = hatStartRotations[i];
            rect.localScale = Vector3.one;

            if (strawHatImages != null && i < strawHatImages.Length && strawHatImages[i] != null)
            {
                strawHatImages[i].DOKill();
                Color c = strawHatImages[i].color;
                c.a = 1f;
                strawHatImages[i].color = c;
            }
        }
    }

    // Deducts one full life (called once per wrong number entry).
    // Returns true if lives just hit zero (game over).
    public bool DeductHalfHeart()
    {
        if (CurrentLives <= 0) return false;

        CurrentLives--;
        int lostSlotIndex = CurrentLives; // 0-based, the slot that just became empty

        UpdateUI();

        // Exactly 1 life remaining — warn once per game.
        if (CurrentLives == 1 && !oneHeartWarningPlayed)
        {
            oneHeartWarningPlayed = true;
            SoundManager.Instance?.PlaySFX("OneHeart");
        }

        // Punch/Shake effect on lost heart slot
        if (lifeSlots != null && lostSlotIndex >= 0 && lostSlotIndex < lifeSlots.Length)
        {
            var slotRT = lifeSlots[lostSlotIndex].GetComponent<RectTransform>();
            if (slotRT != null)
            {
                slotRT.DOKill(true);
                slotRT.localScale = Vector3.one;
                slotRT.DOPunchScale(Vector3.one * 0.35f, 0.4f, 8, 1f)
                    .OnComplete(() => slotRT.localScale = Vector3.one);
            }
        }
        else if (heartSlots != null && lostSlotIndex >= 0 && lostSlotIndex < heartSlots.Length)
        {
            // Legacy fallback
            RectTransform heartRect = heartSlots[lostSlotIndex].rectTransform;
            heartRect.DOKill(true);
            heartRect.transform.localScale = Vector3.one;
            heartRect.DOPunchScale(Vector3.one * 0.35f, 0.4f, 8, 1f)
                .OnComplete(() => heartRect.localScale = Vector3.one);
        }

        // Play straw hat fall for both hats of the lost slot (slots map to pairs)
        PlayStrawHatFallEffect(lostSlotIndex * 2);
        PlayStrawHatFallEffect(lostSlotIndex * 2 + 1);

        // Fire half-heart deducted with the back-compat value
        OnHalfHeartDeducted?.Invoke(CurrentHalfHearts);

        if (CurrentLives <= 0)
        {
            OnGameOver?.Invoke();
            return true;
        }

        return false;
    }

    // The hat matching the half-heart that was just lost hops up and out
    // to whichever side it sits on, falls a little further, then fades
    // away. Only that one hat plays - the other five stay put.
    private void PlayStrawHatFallEffect(int hatIndex)
    {
        if (strawHatRects == null || hatIndex < 0 || hatIndex >= strawHatRects.Length) return;

        RectTransform hatRect = strawHatRects[hatIndex];
        if (hatRect == null || !hatStartPosCaptured) return;

        Image hatImage = (strawHatImages != null && hatIndex < strawHatImages.Length) ? strawHatImages[hatIndex] : null;
        Vector2 startPos = hatStartAnchoredPositions[hatIndex];
        Quaternion startRot = hatStartRotations[hatIndex];
        int dir = hatJumpDirections[hatIndex];

        hatRect.DOKill();
        if (hatImage != null) hatImage.DOKill();

        hatRect.gameObject.SetActive(true);
        hatRect.anchoredPosition = startPos;
        hatRect.localRotation = startRot;
        hatRect.localScale = Vector3.one;

        if (hatImage != null)
        {
            Color c = hatImage.color;
            c.a = 1f;
            hatImage.color = c;
        }

        float peakY = startPos.y + hatJumpArcHeight;
        float landY = startPos.y + hatJumpNetRise;
        float landX = startPos.x + dir * hatJumpSideDistance;
        float dropY = landY - hatFallDistance;

        float jumpUpDuration = hatJumpDuration * 0.45f;
        float jumpDownDuration = hatJumpDuration * 0.55f;
        float totalDuration = hatJumpDuration + hatFallDuration;

        Sequence seq = DOTween.Sequence();

        // Phase 1: hop up and to the side - slides sideways the whole
        // hop, while Y rises to a peak then comes back down to "land".
        seq.Insert(0f, hatRect.DOAnchorPosX(landX, hatJumpDuration).SetEase(Ease.OutSine));
        seq.Insert(0f, hatRect.DOAnchorPosY(peakY, jumpUpDuration).SetEase(Ease.OutQuad));
        seq.Insert(jumpUpDuration, hatRect.DOAnchorPosY(landY, jumpDownDuration).SetEase(Ease.InQuad));

        // Phase 2: after landing, drops a little further, accelerating.
        seq.Insert(hatJumpDuration, hatRect.DOAnchorPosY(dropY, hatFallDuration).SetEase(Ease.InQuad));

        // Light continuous tumble across both phases, spinning toward the
        // jump direction, added on top of the hat's own starting tilt.
        float startZ = startRot.eulerAngles.z;
        Vector3 spinTarget = new Vector3(0f, 0f, startZ + dir * hatRotationAmount);
        seq.Insert(0f, hatRect.DORotate(spinTarget, totalDuration, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear));

        // Fade out over the back half of the fall.
        if (hatImage != null)
        {
            seq.Insert(hatJumpDuration + hatFallDuration * 0.3f, hatImage.DOFade(0f, hatFallDuration * 0.7f));
        }

        seq.OnComplete(() => hatRect.gameObject.SetActive(false));
        seq.SetLink(hatRect.gameObject);
    }

    public void UpdateUI()
    {
        // Primary: toggle Life / Lifeless children in each slot
        if (lifeSlots != null && lifeSlots.Length >= 3)
        {
            for (int i = 0; i < lifeSlots.Length; i++)
            {
                if (lifeSlots[i] == null) continue;

                bool alive = (i < CurrentLives);
                Transform lifeChild = lifeSlots[i].transform.Find("Life");
                Transform lifelessChild = lifeSlots[i].transform.Find("Lifeless");

                if (lifeChild != null) lifeChild.gameObject.SetActive(alive);
                if (lifelessChild != null) lifelessChild.gameObject.SetActive(!alive);
            }
            return;
        }

        // Legacy fallback: sprite-swap on heartSlots Images
        if (heartSlots == null || heartSlots.Length < 3) return;

        for (int i = 0; i < 3; i++)
        {
            bool alive = (i < CurrentLives);
            if (heartSlots[i] == null) continue;

            if (alive)
            {
                heartSlots[i].sprite = fullHeartSprite;
                heartSlots[i].enabled = (fullHeartSprite != null);
            }
            else
            {
                heartSlots[i].sprite = emptyHeartSprite;
                heartSlots[i].enabled = (emptyHeartSprite != null);
            }
        }
    }
}