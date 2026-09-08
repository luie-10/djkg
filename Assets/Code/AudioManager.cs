using UnityEngine;
using UnityEngine.Audio;

public enum TetrisSfx
{
    Move, Rotate, RotateFail, HardDrop, SoftLock, Hold,
    LineClear, Tetris, TSpin, PerfectClear, LevelUp, GameOver
}

/// <summary>
/// BGM + 효과음. 씬의 빈 GameObject(예: AudioController)에 붙인다.
/// TetrisManager의 이벤트를 구독하므로 매니저 코드를 건드릴 필요가 없다.
/// </summary>
public class TetrisAudio : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TetrisManager manager;

    [Header("Mixer (선택)")]
    [SerializeField] private AudioMixerGroup bgmGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;

    [Header("BGM")]
    [SerializeField] private AudioClip bgmClip;
    [SerializeField] private AudioClip gameOverBgmClip;
    [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.45f;
    [SerializeField] private bool playBgmOnStart = true;
    [Tooltip("레벨이 오르면 BGM 피치를 올림")]
    [SerializeField] private bool speedUpWithLevel = true;
    [SerializeField] private float maxBgmPitch = 1.2f;

    [Header("SFX — 조작")]
    [SerializeField] private AudioClip moveClip;
    [SerializeField] private AudioClip rotateClip;
    [SerializeField] private AudioClip rotateFailClip;
    [SerializeField] private AudioClip hardDropClip;
    [SerializeField] private AudioClip lockClip;
    [SerializeField] private AudioClip holdClip;

    [Header("SFX — 라인 클리어 (터지는 소리)")]
    [SerializeField] private AudioClip lineClearClip;
    [SerializeField] private AudioClip tetrisClip;
    [SerializeField] private AudioClip tSpinClip;
    [SerializeField] private AudioClip perfectClearClip;
    [SerializeField] private AudioClip levelUpClip;
    [SerializeField] private AudioClip gameOverSfxClip;

    [Header("SFX 설정")]
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 0.85f;
    [Tooltip("동시 재생 가능한 효과음 개수")]
    [SerializeField] private int voiceCount = 8;
    [Tooltip("콤보가 쌓이면 터지는 소리 피치가 올라감")]
    [SerializeField] private bool pitchUpWithCombo = true;
    [SerializeField] private float comboPitchStep = 0.06f;
    [SerializeField] private float maxComboPitch = 1.8f;
    [Tooltip("같은 효과음이 이 시간 안에 다시 오면 무시 (연타 노이즈 방지)")]
    [SerializeField] private float minRepeatInterval = 0.02f;

    private AudioSource bgmSource;
    private AudioSource[] voices;
    private int voiceIndex;
    private float lastMoveTime, lastRotateTime;

    // ================= 초기화 =================

    private void Awake()
    {
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = bgmVolume;
        bgmSource.spatialBlend = 0f;          // 2D
        bgmSource.outputAudioMixerGroup = bgmGroup;

        voiceCount = Mathf.Max(1, voiceCount);
        voices = new AudioSource[voiceCount];
        for (int i = 0; i < voiceCount; i++)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;
            s.outputAudioMixerGroup = sfxGroup;
            voices[i] = s;
        }

        if (manager == null) manager = FindFirstObjectByType<TetrisManager>();
    }

    private void OnEnable()
    {
        if (manager == null) return;

        manager.GameStarted += HandleGameStarted;
        manager.GameEnded += HandleGameEnded;
        manager.LineCleared += HandleLineCleared;
        manager.LevelChanged += HandleLevelChanged;
    }

    private void OnDisable()
    {
        if (manager == null) return;

        manager.GameStarted -= HandleGameStarted;
        manager.GameEnded -= HandleGameEnded;
        manager.LineCleared -= HandleLineCleared;
        manager.LevelChanged -= HandleLevelChanged;
    }

    // ================= 이벤트 핸들러 =================

    private void HandleGameStarted()
    {
        if (playBgmOnStart) PlayBgm(bgmClip, 1f);
    }

    private void HandleGameEnded(int finalScore)
    {
        PlaySfx(gameOverSfxClip);

        if (gameOverBgmClip != null) PlayBgm(gameOverBgmClip, 1f, loop: false);
        else StopBgm();
    }

    /// <summary>라인이 지워질 때 터지는 소리. 콤보에 따라 피치가 올라간다.</summary>
    private void HandleLineCleared(int lines, TSpinResult tspin, bool perfectClear)
    {
        float pitch = 1f;
        if (pitchUpWithCombo)
            pitch = Mathf.Min(maxComboPitch, 1f + manager.ComboCount * comboPitchStep);

        if (perfectClear && perfectClearClip != null)
        {
            PlaySfx(perfectClearClip, 1f);
            return;
        }

        if (tspin != TSpinResult.None && tSpinClip != null)
        {
            PlaySfx(tSpinClip, pitch);
            return;
        }

        if (lines >= 4 && tetrisClip != null)
        {
            PlaySfx(tetrisClip, pitch);
            return;
        }

        PlaySfx(lineClearClip, pitch);
    }

    private void HandleLevelChanged(int level)
    {
        PlaySfx(levelUpClip);

        if (speedUpWithLevel && bgmSource.isPlaying)
            bgmSource.pitch = Mathf.Min(maxBgmPitch, 1f + (level - 1) * 0.02f);
    }

    // ================= 외부 호출 (Block/Manager) =================

    public void Play(TetrisSfx sfx)
    {
        switch (sfx)
        {
            case TetrisSfx.Move:
                if (Time.unscaledTime - lastMoveTime < minRepeatInterval) return;
                lastMoveTime = Time.unscaledTime;
                PlaySfx(moveClip);
                break;

            case TetrisSfx.Rotate:
                if (Time.unscaledTime - lastRotateTime < minRepeatInterval) return;
                lastRotateTime = Time.unscaledTime;
                PlaySfx(rotateClip);
                break;

            case TetrisSfx.RotateFail: PlaySfx(rotateFailClip, 1f, 0.5f); break;
            case TetrisSfx.HardDrop: PlaySfx(hardDropClip); break;
            case TetrisSfx.SoftLock: PlaySfx(lockClip); break;
            case TetrisSfx.Hold: PlaySfx(holdClip); break;
            case TetrisSfx.LineClear: PlaySfx(lineClearClip); break;
            case TetrisSfx.Tetris: PlaySfx(tetrisClip); break;
            case TetrisSfx.TSpin: PlaySfx(tSpinClip); break;
            case TetrisSfx.PerfectClear: PlaySfx(perfectClearClip); break;
            case TetrisSfx.LevelUp: PlaySfx(levelUpClip); break;
            case TetrisSfx.GameOver: PlaySfx(gameOverSfxClip); break;
        }
    }

    // ================= 재생 유틸 =================

    private void PlaySfx(AudioClip clip, float pitch = 1f, float volumeScale = 1f)
    {
        if (clip == null) return;

        AudioSource s = voices[voiceIndex];
        voiceIndex = (voiceIndex + 1) % voices.Length;   // 라운드로빈

        s.pitch = pitch;
        s.PlayOneShot(clip, sfxVolume * volumeScale);     // 재생 중인 클립을 끊지 않음
    }

    public void PlayBgm(AudioClip clip, float pitch = 1f, bool loop = true)
    {
        if (clip == null) return;

        bgmSource.Stop();
        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.pitch = pitch;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    public void StopBgm() => bgmSource.Stop();

    public void SetBgmVolume(float v)   // 옵션 슬라이더용
    {
        bgmVolume = Mathf.Clamp01(v);
        bgmSource.volume = bgmVolume;
    }

    public void SetSfxVolume(float v) => sfxVolume = Mathf.Clamp01(v);
}