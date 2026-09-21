using System;
using UnityEngine;
using GoogleMobileAds.Api;

public class RewardedAdManager : MonoBehaviour
{
    public static RewardedAdManager Instance { get; private set; }

    [Header("AdMob Configuration")]
    [SerializeField] private string androidAdUnitId = "ca-app-pub-1440083213541314/2796049641";
    [SerializeField] private string iosAdUnitId = "ca-app-pub-1440083213541314/2796049641";

    private RewardedAd rewardedAd;
    private bool isLoadingAd = false;
    private bool isSdkInitialized = false;

    public string AdUnitId
    {
        get
        {
#if UNITY_IOS
            return iosAdUnitId;
#else
            return androidAdUnitId;
#endif
        }
    }

#if UNITY_EDITOR
    [Header("Editor Simulation / Testing")]
    [Tooltip("If true, simulates ad display without requiring manual clicks on the Unity Editor mock ad GUI.")]
    [SerializeField] private bool useEditorSimulation = false;
    [SerializeField] private bool simulateAdAvailable = true;
    [SerializeField] private bool simulateRewardEarned = true;

    public bool UseEditorSimulation
    {
        get => useEditorSimulation;
        set => useEditorSimulation = value;
    }

    public bool SimulateAdAvailable
    {
        get => simulateAdAvailable;
        set => simulateAdAvailable = value;
    }

    public bool SimulateRewardEarned
    {
        get => simulateRewardEarned;
        set => simulateRewardEarned = value;
    }

    public Action OnAdShowingMiddleCallback { get; set; }
#endif

    public static void EnsureInstance()
    {
        if (Instance == null)
        {
            Instance = FindAnyObjectByType<RewardedAdManager>();
            if (Instance == null)
            {
                GameObject go = new GameObject("RewardedAdManager");
                Instance = go.AddComponent<RewardedAdManager>();
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        InitializeAdSdk();
    }

    public void InitializeAdSdk()
    {
        if (isSdkInitialized) return;

        Debug.Log("[RewardedAdManager] Initializing Google Mobile Ads SDK...");
        // MobileAdsEventExecutor handles dispatching to Unity main thread in recent GMA versions
#pragma warning disable 0618
        MobileAds.RaiseAdEventsOnUnityMainThread = true;
#pragma warning restore 0618
        MobileAds.Initialize(initStatus =>
        {
            isSdkInitialized = true;
            Debug.Log("[RewardedAdManager] Google Mobile Ads SDK initialized successfully.");
            LoadRewardedAd();
        });
    }

    public void LoadRewardedAd()
    {
        if (isLoadingAd)
        {
            Debug.Log("[RewardedAdManager] Rewarded ad is already loading.");
            return;
        }

        if (rewardedAd != null)
        {
            rewardedAd.Destroy();
            rewardedAd = null;
        }

        isLoadingAd = true;
        Debug.Log($"[RewardedAdManager] Loading rewarded ad with Unit ID: {AdUnitId}");

        AdRequest adRequest = new AdRequest();
        RewardedAd.Load(AdUnitId, adRequest, (RewardedAd ad, LoadAdError error) =>
        {
            isLoadingAd = false;
            if (error != null || ad == null)
            {
                Debug.LogWarning($"[RewardedAdManager] Failed to load rewarded ad: {error}");
                return;
            }

            rewardedAd = ad;
            Debug.Log("[RewardedAdManager] Rewarded ad loaded successfully.");
        });
    }

    public bool IsAdAvailable()
    {
#if UNITY_EDITOR
        if (useEditorSimulation)
        {
            return simulateAdAvailable;
        }
#endif
        return rewardedAd != null && rewardedAd.CanShowAd();
    }

    public void ShowRewardedAd(Action onUserEarnedReward, Action onAdClosed, Action onAdFailedToShow)
    {
#if UNITY_EDITOR
        if (useEditorSimulation)
        {
            if (!simulateAdAvailable)
            {
                Debug.LogWarning("[RewardedAdManager] (Simulation) Ad not available.");
                onAdFailedToShow?.Invoke();
                return;
            }

            Debug.Log($"[RewardedAdManager] (Simulation) Showing rewarded ad. RewardEarned: {simulateRewardEarned}");
            OnAdShowingMiddleCallback?.Invoke();
            if (simulateRewardEarned)
            {
                onUserEarnedReward?.Invoke();
            }
            onAdClosed?.Invoke();
            return;
        }
#endif

        if (!IsAdAvailable())
        {
            Debug.LogWarning("[RewardedAdManager] Rewarded ad is not available to show.");
            onAdFailedToShow?.Invoke();
            LoadRewardedAd();
            return;
        }

        rewardedAd.OnAdFullScreenContentClosed += () =>
        {
            Debug.Log("[RewardedAdManager] Rewarded ad content closed.");
            onAdClosed?.Invoke();
            LoadRewardedAd();
        };

        rewardedAd.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogError($"[RewardedAdManager] Rewarded ad full screen content failed: {error}");
            onAdFailedToShow?.Invoke();
            LoadRewardedAd();
        };

        rewardedAd.Show((Reward reward) =>
        {
            Debug.Log($"[RewardedAdManager] User earned reward: {reward.Type} - {reward.Amount}");
            onUserEarnedReward?.Invoke();
        });
    }

    private void OnDestroy()
    {
        if (rewardedAd != null)
        {
            rewardedAd.Destroy();
            rewardedAd = null;
        }
    }
}
