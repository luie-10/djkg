using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 점수·레벨·라인·콤보 텍스트와 게임오버 패널. Canvas 아래 빈 GameObject에 붙인다.
/// TMP_Text를 쓰므로 TextMeshPro 패키지가 필요하다(Legacy Text를 쓰려면 아래 주석 참고).
/// </summary>
public class TetrisUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TetrisManager manager;

    [Header("Score Texts")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text highScoreText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text linesText;

    [Header("Popup Texts")]
    [SerializeField] private TMP_Text comboText;
    [SerializeField] private TMP_Text backToBackText;
    [SerializeField] private TMP_Text actionText;      // "TETRIS!", "T-SPIN DOUBLE" 등
    [SerializeField] private float popupDuration = 1.2f;

    [Header("Game Over")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text finalScoreText;
    [SerializeField] private Button restartButton;

    [Header("Options")]
    [Tooltip("점수가 굴러 올라가는 연출")]
    [SerializeField] private bool animateScore = true;
    [SerializeField] private float scoreRollSpeed = 4000f;   // 초당 증가량
    [SerializeField] private string scoreFormat = "N0";      // 1,234,567
    [SerializeField] private string highScoreKey = "TetrisHighScore";

    private int targetScore;
    private float displayedScore;
    private int highScore;
    private float actionTimer;

    // ================= 초기화 =================

    private void Awake()
    {
        if (manager == null) manager = FindFirstObjectByType<TetrisManager>();

        highScore = PlayerPrefs.GetInt(highScoreKey, 0);

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
    }

    private void OnEnable()
    {
        if (manager == null) return;

        manager.ScoreChanged += HandleScoreChanged;
        manager.LevelChanged += HandleLevelChanged;
        manager.LinesChanged += HandleLinesChanged;
        manager.ComboChanged += HandleComboChanged;
        manager.BackToBackChanged += HandleBackToBackChanged;
        manager.LineCleared += HandleLineCleared;
        manager.GameStarted += HandleGameStarted;
        manager.GameEnded += HandleGameEnded;
    }

    private void OnDisable()
    {
        if (manager == null) return;

        manager.ScoreChanged -= HandleScoreChanged;
        manager.LevelChanged -= HandleLevelChanged;
        manager.LinesChanged -= HandleLinesChanged;
        manager.ComboChanged -= HandleComboChanged;
        manager.BackToBackChanged -= HandleBackToBackChanged;
        manager.LineCleared -= HandleLineCleared;
        manager.GameStarted -= HandleGameStarted;
        manager.GameEnded -= HandleGameEnded;
    }

    private void OnDestroy()
    {
        if (restartButton != null) restartButton.onClick.RemoveListener(OnRestartClicked);
    }

    // ================= 갱신 =================

    private void Update()
    {
        if (animateScore && !Mathf.Approximately(displayedScore, targetScore))
        {
            displayedScore = Mathf.MoveTowards(displayedScore, targetScore,
                                               scoreRollSpeed * Time.unscaledDeltaTime);
            SetText(scoreText, Mathf.RoundToInt(displayedScore).ToString(scoreFormat));
        }

        if (actionTimer > 0f)
        {
            actionTimer -= Time.unscaledDeltaTime;
            if (actionTimer <= 0f && actionText != null) actionText.text = "";
        }
    }

    // ================= 이벤트 핸들러 =================

    private void HandleGameStarted()
    {
        targetScore = 0;
        displayedScore = 0f;
        actionTimer = 0f;

        SetText(scoreText, 0.ToString(scoreFormat));
        SetText(highScoreText, highScore.ToString(scoreFormat));
        SetText(levelText, "1");
        SetText(linesText, "0");
        SetText(comboText, "");
        SetText(backToBackText, "");
        SetText(actionText, "");

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    private void HandleScoreChanged(int score)
    {
        targetScore = score;

        if (!animateScore)
        {
            displayedScore = score;
            SetText(scoreText, score.ToString(scoreFormat));
        }

        if (score > highScore)
        {
            highScore = score;
            SetText(highScoreText, highScore.ToString(scoreFormat));
        }
    }

    private void HandleLevelChanged(int level) => SetText(levelText, level.ToString());

    private void HandleLinesChanged(int lines) => SetText(linesText, lines.ToString());

    private void HandleComboChanged(int combo) =>
        SetText(comboText, combo >= 1 ? $"{combo} COMBO" : "");

    private void HandleBackToBackChanged(bool active) =>
        SetText(backToBackText, active ? "BACK-TO-BACK" : "");

    private void HandleLineCleared(int lines, TSpinResult tspin, bool perfectClear)
    {
        string label = BuildActionLabel(lines, tspin);
        if (perfectClear) label = $"PERFECT CLEAR\n{label}";

        SetText(actionText, label);
        actionTimer = popupDuration;
    }

    private void HandleGameEnded(int finalScore)
    {
        displayedScore = finalScore;
        targetScore = finalScore;
        SetText(scoreText, finalScore.ToString(scoreFormat));

        if (finalScore >= highScore)
        {
            highScore = finalScore;
            PlayerPrefs.SetInt(highScoreKey, highScore);
            PlayerPrefs.Save();
            SetText(highScoreText, highScore.ToString(scoreFormat));
        }

        SetText(finalScoreText, finalScore.ToString(scoreFormat));
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
    }

    private void OnRestartClicked()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        manager?.NewGame();
    }

    // ================= 유틸 =================

    private static string BuildActionLabel(int lines, TSpinResult tspin)
    {
        string count = lines switch
        {
            1 => "SINGLE",
            2 => "DOUBLE",
            3 => "TRIPLE",
            _ => "TETRIS",
        };

        return tspin switch
        {
            TSpinResult.Full => lines >= 4 ? count : $"T-SPIN {count}",
            TSpinResult.Mini => $"MINI T-SPIN {count}",
            _ => count,
        };
    }

    // Legacy Text(UnityEngine.UI.Text)를 쓰려면 TMP_Text를 Text로 바꾸면
    // 아래 메서드 시그니처만 고쳐도 나머지 코드는 그대로 동작한다.
    private static void SetText(TMP_Text t, string value)
    {
        if (t != null) t.text = value;
    }
}
