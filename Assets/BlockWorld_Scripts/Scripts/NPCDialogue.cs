using UnityEngine;
using TMPro;

[RequireComponent(typeof(Collider))]
public class NPCDialogue : MonoBehaviour
{
    [TextArea(2, 4)]
    public string[] lines;

    [Tooltip("Optional: point to the object that controls player movement " +
             "(so it can be disabled while talking).")]
    public MonoBehaviour movementScript;

    // Simple “press E” prompt  ----------------------------------------------
    GameObject prompt;
    const string promptText = "<size=20><i>[E] Talk</i></size>";

    void Start()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;

        // small floating prompt
        prompt = new GameObject("TalkPrompt", typeof(Canvas), typeof(CanvasGroup), typeof(TMP_Text));
        Canvas canvas = prompt.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 500;
        TMP_Text txt = prompt.GetComponent<TMP_Text>();
        txt.text = promptText;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontSize = 1.5f;                       // because WorldSpace canvas units == metres
        prompt.transform.SetParent(transform);
        prompt.transform.localPosition = Vector3.up * 2f;
        prompt.transform.localRotation = Quaternion.identity;
        prompt.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
            prompt.SetActive(true);
    }
    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
            prompt.SetActive(false);
    }

    void OnTriggerStay(Collider other)
    {
        if (!other.gameObject.CompareTag("Player")) return;
        if (Input.GetKeyDown(KeyCode.E))
        {
            prompt.SetActive(false);
            DialogueManager.Instance.Begin(lines, movementScript);
        }
    }
}
