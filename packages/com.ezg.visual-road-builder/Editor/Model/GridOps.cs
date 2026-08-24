#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Migrate data version, expand/shrink grid, prune out-of-range content.</summary>
    internal sealed class GridOps
    {
        private readonly RoadCanvasDoc _doc;
        private readonly System.Action _repaint;

        internal GridOps(RoadCanvasDoc doc, System.Action repaint)
        {
            _doc = doc;
            _repaint = repaint;
        }

        /// <summary>Layout đang vẽ dở trong window được serialize qua domain reload — khi format
        /// đổi (edge chuyển sang nửa ô) thì nâng cấp tại chỗ để GIỮ NGUYÊN hình đang vẽ.</summary>
        internal void MigrateWindowData()
        {
            if (_doc.DataVersion < 3)
            {
                EdgeCodec.DoubleEdgeCoords(_doc.Edges);
                EdgeCodec.DoubleEdgeCoords(_doc.HighwayEdges);
                EdgeCodec.DoubleEdgeCoords(_doc.HwDecorEdges);
                _doc.DataVersion = 3;
            }
            if (_doc.DataVersion < 4)
            {
                EdgeCodec.SplitEdgeSpan(_doc.Edges);
                EdgeCodec.SplitEdgeSpan(_doc.HighwayEdges);
                EdgeCodec.SplitEdgeSpan(_doc.HwDecorEdges);
                EdgeCodec.SplitEdgeSpan(_doc.Road2Edges);
                // _pathEdges sinh ra đã là span nửa ô (edgeSpanVersion 1) → KHÔNG bao giờ SplitEdgeSpan/DoubleEdgeCoords
                _doc.DataVersion = 4;
            }
        }

        /// <summary>Nới (dương) / cắt (âm) lưới N ô mỗi phía. Phía trái/dưới: dịch toàn bộ layout
        /// (left, down) ô và dời Root scene đi (-left,-down)*cell để mọi thứ GIỮ NGUYÊN vị trí
        /// world — sau đó cần Apply lại. Cắt (âm): nội dung rơi ra ngoài vùng mới bị xoá
        /// (xoá TRƯỚC khi dịch để encode toạ độ không bị âm).</summary>
        internal void ExpandGrid(int left, int down, int right, int up, System.Action<Vector2Int> offsetAll)
        {
            int newW = _doc.GridWidth + left + right;
            int newH = _doc.GridHeight + down + up;
            if (newW > GridConst.MaxGridSize || newH > GridConst.MaxGridSize)
            {
                EditorUtility.DisplayDialog("Road Grid",
                    $"Lưới mới {newW}x{newH} vượt giới hạn {GridConst.MaxGridSize}.", "OK");
                return;
            }
            if (newW < 2 || newH < 2)
            {
                EditorUtility.DisplayDialog("Road Grid", $"Lưới mới {newW}x{newH} quá nhỏ.", "OK");
                return;
            }

            // Cắt từ trái/dưới: xoá nội dung nằm trong vùng bị cắt TRƯỚC khi dịch.
            if (left < 0 || down < 0)
                RemoveContentBelow(Mathf.Max(0, -left) * 2, Mathf.Max(0, -down) * 2);

            _doc.GridWidth = newW;
            _doc.GridHeight = newH;

            if (left != 0 || down != 0)
            {
                offsetAll(new Vector2Int(left * 2, down * 2));
                // Layout dịch (+left,+down) ô; dời gốc toạ độ cùng chiều để số của cell cũ KHÔNG đổi.
                _doc.OriginCell += new Vector2Int(-left, -down);
            }

            PruneOutOfRangeEdges(); // cắt phải/trên: prune phần tràn biên mới
            _repaint();
        }

        /// <summary>Xoá mọi phần tử có toạ độ (nửa ô) nhỏ hơn ngưỡng — dùng khi cắt lưới từ trái/dưới.</summary>
        internal void RemoveContentBelow(int cutX2, int cutY2)
        {
            System.Predicate<int> edgeBelow = id =>
            {
                EdgeCodec.DecodeEdge(id, out int x2, out int y2, out _);
                return x2 < cutX2 || y2 < cutY2;
            };
            _doc.Edges.RemoveAll(edgeBelow);
            _doc.HighwayEdges.RemoveAll(edgeBelow);
            _doc.HwDecorEdges.RemoveAll(edgeBelow);
            _doc.Road2Edges.RemoveAll(edgeBelow);
            _doc.PathEdges.RemoveAll(edgeBelow);

            _doc.Stations.RemoveAll(id =>
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out _);
                return x2 < cutX2 || y2 < cutY2;
            });
            _doc.Parkings.RemoveAll(id =>
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out _);
                return x2 < cutX2 || y2 < cutY2;
            });
            _doc.Decors.RemoveAll(d => d.x2 < cutX2 || d.y2 < cutY2);
            _doc.RampFlips.RemoveAll(key =>
            {
                EdgeCodec.DecodeRampAnchor(key, out int x2, out int y2);
                return x2 < cutX2 || y2 < cutY2;
            });
        }

        internal void PruneOutOfRangeEdges()
        {
            int gx2Max = (_doc.GridWidth - 1) * 2, gy2Max = (_doc.GridHeight - 1) * 2;
            System.Predicate<int> outOfRange = id =>
            {
                EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
                int xe2 = orient == 0 ? x2 + 1 : x2;
                int ye2 = orient == 1 ? y2 + 1 : y2;
                return xe2 > gx2Max || ye2 > gy2Max;
            };
            _doc.Edges.RemoveAll(outOfRange);
            _doc.HighwayEdges.RemoveAll(outOfRange);
            _doc.HwDecorEdges.RemoveAll(outOfRange);
            _doc.Road2Edges.RemoveAll(outOfRange);
            _doc.PathEdges.RemoveAll(outOfRange);

            // Station: lưới nhỏ hơn khối thì bỏ, còn lại kẹp anchor (nửa ô) vào biên mới.
            int size = GridConst.StationSize;
            for (int i = _doc.Stations.Count - 1; i >= 0; i--)
            {
                if (_doc.GridWidth - 1 < size || _doc.GridHeight - 1 < size)
                {
                    _doc.Stations.RemoveAt(i);
                    continue;
                }
                BlockCodec.DecodeStation(_doc.Stations[i], out int x2, out int y2, out int rot);
                _doc.Stations[i] = BlockCodec.EncodeStation(
                    BlockCodec.ClampBlockAnchor(new Vector2Int(x2, y2), size, size,
                        _doc.GridWidth, _doc.GridHeight), rot);
            }

            for (int i = _doc.Parkings.Count - 1; i >= 0; i--)
            {
                BlockCodec.DecodeParking(_doc.Parkings[i], out int x2, out int y2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                if (_doc.GridWidth - 1 < k.x || _doc.GridHeight - 1 < k.y)
                {
                    _doc.Parkings.RemoveAt(i);
                    continue;
                }
                _doc.Parkings[i] = BlockCodec.EncodeParking(
                    BlockCodec.ClampBlockAnchor(new Vector2Int(x2, y2), k.x, k.y,
                        _doc.GridWidth, _doc.GridHeight), orient);
            }

            int dMaxX2 = (_doc.GridWidth - 1) * 2, dMaxY2 = (_doc.GridHeight - 1) * 2;
            for (int i = 0; i < _doc.Decors.Count; i++)
            {
                DecorItem item = _doc.Decors[i];
                item.x2 = Mathf.Clamp(item.x2, 0, dMaxX2);
                item.y2 = Mathf.Clamp(item.y2, 0, dMaxY2);
                _doc.Decors[i] = item;
            }

            // Anchor ramp-flip là điểm lattice: rơi ra ngoài lưới mới thì bỏ cờ lật (ramp đó không còn).
            _doc.RampFlips.RemoveAll(key =>
            {
                EdgeCodec.DecodeRampAnchor(key, out int x2, out int y2);
                return x2 < 0 || y2 < 0 || x2 > dMaxX2 || y2 > dMaxY2;
            });
        }
    }
}
#endif
