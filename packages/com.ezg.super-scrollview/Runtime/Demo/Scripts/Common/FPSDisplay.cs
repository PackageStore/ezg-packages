using System.Collections.Generic;
using UnityEngine;

namespace SuperScrollView
{
    /// <summary>
    /// Show thông số
    /// AnhNT custom thêm show nhiều thứ hơn FPS
    /// </summary>
    public class FPSDisplay : MonoBehaviour
    {
        float deltaTime = 0.0f;
        GUIStyle mStyle;
        Texture2D backgroundTexture;

        public bool showFPS = true;
        public bool showBatches = true;
        public bool showSavedByBatching = true;
        public bool showTris = true;
        public bool showVerts = true;

        int batches = 0;
        int tris = 0;
        int verts = 0;

        void Awake()
        {
            mStyle = new GUIStyle();
            mStyle.alignment = TextAnchor.UpperLeft;
            mStyle.fontSize = 25;
            mStyle.normal.textColor = new Color(0f, 1f, 0f, 1.0f);

            backgroundTexture = new Texture2D(1, 1);
            backgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.5f));
            backgroundTexture.Apply();
            mStyle.normal.background = backgroundTexture;
            mStyle.padding = new RectOffset(10, 10, 10, 10);
        }

        void Update()
        {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
            CalculateRenderStats();
        }

        void CalculateRenderStats()
        {
            batches = 0;
            tris = 0;
            verts = 0;

            //Camera cam = Camera.main;
            //if (cam == null) return;

            //List<Renderer> renderers = new List<Renderer>(FindObjectsOfType<Renderer>());
            //foreach (Renderer renderer in renderers)
            //{
            //    if (!renderer.isVisible) continue;

            //    MeshFilter mf = renderer.GetComponent<MeshFilter>();
            //    if (mf && mf.sharedMesh)
            //    {
            //        try
            //        {
            //            if (mf.sharedMesh.isReadable)
            //            {
            //                tris += mf.sharedMesh.triangles.Length / 3;
            //                verts += mf.sharedMesh.vertexCount;
            //            }
            //        }
            //        catch
            //        {
            //            // Nếu mesh không thể đọc, bỏ qua lỗi để tránh crash
            //        }
            //    }
            //    batches++;
            //}
        }

        void OnGUI()
        {
            int w = Screen.width;
            int h = Screen.height;
            float fps = 1.0f / deltaTime;

            string text = "";
            if (showFPS) text += string.Format("FPS: {0:0.} \n", fps);
            if (showBatches) text += "Batches: " + batches + "\n";
            if (showTris) text += "Tris: " + tris + "\n";
            if (showVerts) text += "Verts: " + verts + "\n";

            Vector2 textSize = mStyle.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(10, 10, textSize.x + 20, textSize.y + 20), "", mStyle);
            GUI.Label(new Rect(20, 20, textSize.x, textSize.y), text, mStyle);
        }
    }
}