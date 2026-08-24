#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Scatter, erase, place, roll-scale, hit-test for decor items.
    /// Remains partial during migration — depends on decor panel state not yet extracted.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        /// <summary>Rải random trong vùng (ô): số item = diện tích × mật độ, vị trí snap nửa ô,
        /// không trùng điểm cùng loại; xoay random 4 hướng nếu bật.</summary>
        private void ScatterDecorsInRect(float minX, float minY, float maxX, float maxY)
        {
            if (_decorLibrary == null || _decorLibrary.entries.Count == 0) return;
            float w = maxX - minX, h = maxY - minY;
            if (w <= 0.01f || h <= 0.01f) return;

            int entry = Mathf.Clamp(_decorEntryIndex, 0, _decorLibrary.entries.Count - 1);
            int count = Mathf.Max(1, Mathf.RoundToInt(w * h * _decorDensity));
            int placed = 0;

            // Thử tối đa count*4 lần để bù các lần trúng điểm đã có.
            for (int attempt = 0; attempt < count * 4 && placed < count; attempt++)
            {
                int x2 = Mathf.Clamp(Mathf.RoundToInt(UnityEngine.Random.Range(minX, maxX) * 2f),
                    0, (_gridWidth - 1) * 2);
                int y2 = Mathf.Clamp(Mathf.RoundToInt(UnityEngine.Random.Range(minY, maxY) * 2f),
                    0, (_gridHeight - 1) * 2);

                bool exists = false;
                for (int i = 0; i < _decors.Count && !exists; i++)
                    exists = _decors[i].x2 == x2 && _decors[i].y2 == y2 && _decors[i].entry == entry;
                if (exists) continue;

                _decors.Add(new DecorItem
                {
                    entry = entry,
                    x2 = x2,
                    y2 = y2,
                    rot = _decorRandomRot ? UnityEngine.Random.Range(0, 4) : 0,
                    scale = RollScale(entry),
                });
                placed++;
            }
        }

        private void EraseDecorsInRect(float minX, float minY, float maxX, float maxY)
        {
            _decors.RemoveAll(d =>
            {
                float x = d.x2 * 0.5f, y = d.y2 * 0.5f;
                return x >= minX - 0.001f && x <= maxX + 0.001f
                       && y >= minY - 0.001f && y <= maxY + 0.001f;
            });
        }

        private void PlaceDecorAt(Vector2Int p2)
        {
            if (_decorLibrary == null || _decorLibrary.entries.Count == 0) return;
            int entry = Mathf.Clamp(_decorEntryIndex, 0, _decorLibrary.entries.Count - 1);

            // Không đặt trùng item cùng loại tại cùng điểm (kéo rải đi qua nhiều lần).
            for (int i = 0; i < _decors.Count; i++)
            {
                if (_decors[i].x2 == p2.x && _decors[i].y2 == p2.y && _decors[i].entry == entry)
                    return;
            }

            _decors.Add(new DecorItem
            {
                entry = entry, x2 = p2.x, y2 = p2.y, rot = 0, scale = RollScale(entry),
            });
        }

        /// <summary>Roll scale ngẫu nhiên theo config [scaleMin, scaleMax] của entry.</summary>
        private float RollScale(int entry)
        {
            if (_decorLibrary == null || entry < 0 || entry >= _decorLibrary.entries.Count) return 1f;
            DecorLibrary.DecorEntry e = _decorLibrary.entries[entry];
            float min = Mathf.Max(0.01f, Mathf.Min(e.scaleMin, e.scaleMax));
            float max = Mathf.Max(0.01f, Mathf.Max(e.scaleMin, e.scaleMax));
            return UnityEngine.Random.Range(min, max);
        }

        /// <summary>Index decor gần toạ độ lưới f nhất trong bán kính 0.35 ô, -1 nếu không có.</summary>
        private int FindDecorAt(Vector2 f)
        {
            const float radius = 0.35f;
            int best = -1;
            float bestD = radius;
            for (int i = _decors.Count - 1; i >= 0; i--)
            {
                float d = Vector2.Distance(f, new Vector2(_decors[i].x2 * 0.5f, _decors[i].y2 * 0.5f));
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
#endif
