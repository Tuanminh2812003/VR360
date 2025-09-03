using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;

public class IntroManager : MonoBehaviour
{
    [Header("UI")]
    public GameObject introPanel;     // PanelIntro (che toàn bộ)
    public VideoPlayer introPlayer;   // VideoPlayer trên IntroRaw
    public GameObject menuPanel;      // Panel (menu chính)
    public Button skipButton;         // (tuỳ chọn) nút Skip

    [Header("Files")]
    public string introFileName = "intro.mp4"; // đặt trong Assets/StreamingAssets

    [Header("Behavior")]
    public bool showMenuAfterIntro = true; // hết video thì bật menu
    public bool hideIntroAudioIfNoDevice = false; // giữ mặc định là false

    void Start()
    {
        // 1) Ẩn menu, bật intro
        if (menuPanel) menuPanel.SetActive(false);
        if (introPanel) introPanel.SetActive(true);

        // 2) Gán URL cho VideoPlayer (StreamingAssets)
        if (introPlayer)
        {
            string url = System.IO.Path.Combine(Application.streamingAssetsPath, introFileName);
            introPlayer.url = url;

            // Bắt sự kiện hết video
            introPlayer.loopPointReached += OnIntroFinished;

            // Play
            introPlayer.Play();
        }
        else
        {
            // Không có player => hiện menu luôn
            FinishIntro();
        }

        // 3) Gán sự kiện cho nút Skip (nếu có)
        if (skipButton) skipButton.onClick.AddListener(FinishIntro);
    }

    void OnDestroy()
    {
        if (introPlayer) introPlayer.loopPointReached -= OnIntroFinished;
        if (skipButton)  skipButton.onClick.RemoveListener(FinishIntro);
    }

    void OnIntroFinished(VideoPlayer _)
    {
        FinishIntro();
    }

    void FinishIntro()
    {
        if (introPanel) introPanel.SetActive(false);

        if (showMenuAfterIntro && menuPanel)
        {
            menuPanel.SetActive(true);

            // Nếu bạn dùng script VRMenuSummoner để “snap” menu trước mặt:
            var summoner = menuPanel.GetComponent<VRMenuSummoner>();
            if (summoner)
            {
                summoner.ShowMenu(); // sẽ tự snap đúng hướng HMD
            }
        }
    }
}
