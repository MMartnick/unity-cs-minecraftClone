using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CanvasNav : MonoBehaviour
{
    public List<Button> buttons;
    int buttonIndex = 0;

    void Start()
    {
        if (buttons != null && buttons.Count > 0)
        {
            buttonIndex = 0;
            buttons[buttonIndex].Select();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab) && buttons != null && buttons.Count > 0)
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                buttonIndex = (buttonIndex - 1 + buttons.Count) % buttons.Count;
            }
            else
            {
                buttonIndex = (buttonIndex + 1) % buttons.Count;
            }
            buttons[buttonIndex].Select();
        }
    }
}
