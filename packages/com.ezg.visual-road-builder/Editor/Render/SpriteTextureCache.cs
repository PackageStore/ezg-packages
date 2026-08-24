#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Rotation/extraction cache: piece đã xoay sẵn thành Texture2D → vẽ axis-aligned
    /// (không GUI.matrix) để được scroll-view clip.</summary>
    internal sealed class SpriteTextureCache
    {
        // Key: (sprite, turns CW, mirrorY) — mirrorY dùng cho ramp hway_to_road bị lật gương (phím F).
        private readonly Dictionary<(Sprite, int, bool), Texture2D> _roadPieceTex = new();
        private readonly Dictionary<Texture, Texture2D> _roadReadable = new();

        /// <summary>Texture piece đã xoay sẵn turns×90° CW, cache theo (sprite, turns). Vẽ bằng GUI.DrawTexture
        /// axis-aligned nên được scroll-view clip đúng mép — thay cho GUI.matrix (nét xoay không bị clip).</summary>
        internal Texture2D GetRoadPieceTex(Sprite sprite, int turns, bool mirrorY = false)
        {
            turns &= 3;
            var key = (sprite, turns, mirrorY);
            if (_roadPieceTex.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            Texture2D tex = ExtractPiece(sprite);
            if (mirrorY) // lật gương ramp: mirror trục cao tốc TRONG frame gốc (trước khi xoay turns)
            {
                Texture2D flipped = FlipY(tex);
                Object.DestroyImmediate(tex);
                tex = flipped;
            }
            for (int k = 0; k < turns; k++)
            {
                Texture2D rotated = RotateCW90(tex);
                Object.DestroyImmediate(tex);
                tex = rotated;
            }
            _roadPieceTex[key] = tex;
            return tex;
        }

        /// <summary>Lật Texture2D theo trục dọc (trên↔dưới). Ramp base có cao tốc DỌC nên đây = mirror
        /// trục cao tốc → đổi bên thân ramp loe, khớp mesh bake scaleMul.x = -1.</summary>
        internal static Texture2D FlipY(Texture2D src)
        {
            int w = src.width, h = src.height;
            Color32[] s = src.GetPixels32();
            var d = new Color32[w * h];
            for (int y = 0; y < h; y++)
                System.Array.Copy(s, y * w, d, (h - 1 - y) * w, w);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = src.filterMode, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(d);
            t.Apply();
            return t;
        }

        /// <summary>Cắt region của sprite (trong atlas PSD) ra Texture2D upright. Đọc qua bản readable đầy đủ
        /// (Blit + ReadPixels) để không phụ thuộc cờ Read/Write Enabled của asset.
        /// Dùng <c>rect</c> (ô slice gốc) chứ KHÔNG dùng <c>textureRect</c>: mesh Tight cắt sát nét nên
        /// textureRect nhỏ hơn slice (Road_T 161×192, Road_turn 161×161) → cắt theo nó rồi kéo vừa khung
        /// vuông sẽ bóp méo + lệch tâm.</summary>
        internal Texture2D ExtractPiece(Sprite sprite)
        {
            Rect tr = sprite.rect;
            int w = Mathf.RoundToInt(tr.width), h = Mathf.RoundToInt(tr.height);
            Texture2D full = GetReadable(sprite.texture);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = sprite.texture.filterMode, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels(full.GetPixels(Mathf.RoundToInt(tr.x), Mathf.RoundToInt(tr.y), w, h));
            t.Apply();
            return t;
        }

        /// <summary>Bản Texture2D readable (giữ màu sRGB) của atlas, cache theo texture nguồn.</summary>
        internal Texture2D GetReadable(Texture src)
        {
            if (_roadReadable.TryGetValue(src, out Texture2D r) && r != null) return r;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave };
            tex.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            _roadReadable[src] = tex;
            return tex;
        }

        /// <summary>Xoay Texture2D 90° theo chiều kim đồng hồ (khớp GUIUtility.RotateAroundPivot dương).</summary>
        internal static Texture2D RotateCW90(Texture2D src)
        {
            int w = src.width, h = src.height;
            Color32[] s = src.GetPixels32();
            var d = new Color32[w * h];
            for (int dy = 0; dy < w; dy++)
                for (int dx = 0; dx < h; dx++)
                    d[dy * h + dx] = s[dx * w + (w - 1 - dy)];
            var t = new Texture2D(h, w, TextureFormat.RGBA32, false)
                { filterMode = src.filterMode, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(d);
            t.Apply();
            return t;
        }

        /// <summary>Số nấc RotateMask90 (CW) để baseMask trùng targetMask.</summary>
        internal static int TurnsFromBase(int baseMask, int targetMask)
        {
            int m = baseMask;
            for (int k = 0; k < 4; k++)
            {
                if (m == targetMask) return k;
                m = DirBits.RotateMask90(m);
            }
            return 0;
        }

        internal void ClearPieceCache()
        {
            foreach (Texture2D tex in _roadPieceTex.Values)
                if (tex != null) Object.DestroyImmediate(tex);
            _roadPieceTex.Clear();
        }

        /// <summary>Huỷ + xoá bản readable của atlas. Thiếu bước này thì dù cắt lại piece vẫn ra art CŨ.</summary>
        internal void ClearReadableCache()
        {
            foreach (Texture2D tex in _roadReadable.Values)
                if (tex != null) Object.DestroyImmediate(tex);
            _roadReadable.Clear();
        }

        internal void Dispose()
        {
            ClearPieceCache();
            ClearReadableCache();
        }
    }
}
#endif
