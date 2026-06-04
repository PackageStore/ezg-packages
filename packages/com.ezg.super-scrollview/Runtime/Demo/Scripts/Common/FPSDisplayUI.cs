using UnityEngine;
using UnityEngine.UI;

public class FPSDisplayUI : MonoBehaviour
{
    private Text fpsText;

    private int frameCount = 0;
    private float timer = 0f;
    private int fps = 0;

    void Start()
    {
        fpsText = GetComponent<Text>();

        if (fpsText == null)
        {
            Debug.LogError("Không tìm thấy Text component!");
        }
    }

    void Update()
    {
        frameCount++;
        timer += Time.unscaledDeltaTime;

        if (timer >= 1f)
        {
            fps = frameCount;
            frameCount = 0;
            timer = 0f;

            if (fpsText)
            {
                fpsText.text = "FPS: " + fps;
            }
            if (fps >= 50)
                fpsText.color = Color.green;
            else if (fps >= 30)
                fpsText.color = Color.yellow;
            else
                fpsText.color = Color.red;
        }
    }
}