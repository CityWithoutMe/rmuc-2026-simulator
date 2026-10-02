using UnityEngine;
using UnityEngine.UI;

// 比赛倒计时：只负责倒数和屏幕显示，不做时间点事件
public class MatchTimer : MonoBehaviour
{
    [SerializeField] private float durationSeconds = 7f * 60f; // 默认 7 分钟
    [SerializeField] private bool autoStart = true;

    private float remainingSeconds;
    private bool isRunning;
    private bool isFinished;
    private Text timerText;

    public float RemainingSeconds => remainingSeconds;
    public float DurationSeconds => durationSeconds;
    // 比赛已经进行的秒数。能量机关用它区分小符（前 3 分钟）和大符。
    public float ElapsedSeconds => Mathf.Max(0f, durationSeconds - remainingSeconds);
    public bool IsFinished => isFinished;

    private void Awake()
    {
        remainingSeconds = durationSeconds;
        CreateDisplay();
        RefreshDisplay();
    }

    private void Start()
    {
        if (autoStart)
            isRunning = true;
    }

    private void Update()
    {
        if (isRunning && !isFinished)
        {
            remainingSeconds -= Time.deltaTime;
            if (remainingSeconds <= 0f)
            {
                remainingSeconds = 0f; // 到 0 停住，不再变负
                isRunning = false;
                isFinished = true;
            }
        }

        RefreshDisplay();
    }

    public void StartTimer()
    {
        if (isFinished)
            return;
        isRunning = true;
    }

    public void Pause()
    {
        isRunning = false;
    }

    // 胜负已出时停表。不把 IsFinished 改成 true，避免基地被提前击毁被当成时间耗尽。
    public void Halt()
    {
        isRunning = false;
    }

    public void ResetTimer()
    {
        remainingSeconds = durationSeconds;
        isFinished = false;
        isRunning = autoStart;
        RefreshDisplay();
    }

    private void CreateDisplay()
    {
        // 运行时自己搭 Canvas，避免改场景里其它内容
        GameObject canvasGo = new GameObject("MatchTimerCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textGo = new GameObject("TimerText");
        textGo.transform.SetParent(canvasGo.transform, false);

        timerText = textGo.AddComponent<Text>();
        timerText.font = LoadBuiltinFont();
        timerText.fontSize = 48;
        timerText.alignment = TextAnchor.UpperCenter;
        timerText.color = Color.white;
        timerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        timerText.verticalOverflow = VerticalWrapMode.Overflow;
        timerText.raycastTarget = false;

        RectTransform rt = timerText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        // 哨塔和基地血量贴在本行上方（OutpostHealthHud，y=-8，高 72）。这里再下移，避免叠住。
        rt.anchoredPosition = new Vector2(0f, -88f);
        rt.sizeDelta = new Vector2(400f, 80f);
    }

    private static Font LoadBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont("Arial", 48);
        return font;
    }

    private void RefreshDisplay()
    {
        if (timerText == null)
            return;

        int total = remainingSeconds <= 0f ? 0 : Mathf.CeilToInt(remainingSeconds);
        int minutes = total / 60;
        int seconds = total % 60;
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}
