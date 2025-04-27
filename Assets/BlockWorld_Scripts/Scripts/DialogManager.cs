using UnityEngine;
using UnityEngine.UI;
using TMPro;                                // <- swap for UnityEngine.UI.Text if you don’t use TMP

/// <summary>
/// Drop this on an empty GameObject in the scene.  
/// It will create its own Canvas the first time it runs.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    // Singleton --------------------------------------------------------------
    public static DialogueManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();                                 // make a tiny UI on the fly
    }

    // UI ---------------------------------------------------------------------
    CanvasGroup cg;
    TMP_Text textBox;
    const float uiWidth = 600f;

    void BuildUI()
    {
        // Canvas
        GameObject cGO = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler),
                                        typeof(GraphicRaycaster), typeof(CanvasGroup));
        cGO.transform.SetParent(transform);
        Canvas canvas = cGO.GetComponent<Canvas>();
        cg = cGO.GetComponent<CanvasGroup>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;                // always on top
        cGO.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        // Panel
        GameObject panel = new GameObject("DlgPanel", typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(uiWidth, 140);
        rt.anchorMin = new Vector2(.5f, 0);
        rt.anchorMax = new Vector2(.5f, 0);
        rt.pivot = new Vector2(.5f, 0);
        rt.anchoredPosition = new Vector2(0, 20);
        panel.GetComponent<Image>().color = new Color(0, 0, 0, .75f);

        // Text
        GameObject t = new GameObject("DlgText", typeof(TMP_Text));
        t.AddComponent<TextMeshPro>();
        t.transform.SetParent(panel.transform, false);
        textBox = t.GetComponent<TMP_Text>();
        textBox.fontSize = 24;
        textBox.alignment = TextAlignmentOptions.TopLeft;
        textBox.enableWordWrapping = true;
        textBox.raycastTarget = false;
        RectTransform tr = textBox.rectTransform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(15, 15);
        tr.offsetMax = new Vector2(-15, -15);

        cg.alpha = 0;                              // hidden
        cg.interactable = cg.blocksRaycasts = false;
    }

    // Dialogue state ---------------------------------------------------------
    string[] currentLines;
    int lineIndex;
    bool isRunning;
    MonoBehaviour fpcMovementScript;               // whatever controls movement

    /// <summary>Start a conversation – call from NPC.</summary>
    public void Begin(string[] lines, MonoBehaviour movementScriptToDisable = null)
    {
        if (isRunning || lines == null || lines.Length == 0) return;

        currentLines = lines;
        lineIndex = 0;
        isRunning = true;
        fpcMovementScript = movementScriptToDisable;
        if (fpcMovementScript) fpcMovementScript.enabled = false;

        cg.alpha = 1;
        ShowLine();
    }

    void ShowLine() => textBox.text = currentLines[lineIndex];

    void Update()
    {
        if (!isRunning) return;

        // advance dialogue on Space / LeftClick / Return
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
            Input.GetMouseButtonDown(0))
        {
            lineIndex++;
            if (lineIndex >= currentLines.Length)
                End();
            else
                ShowLine();
        }
    }

    void End()
    {
        isRunning = false;
        cg.alpha = 0;
        if (fpcMovementScript) fpcMovementScript.enabled = true;
        currentLines = null;
    }
}
