#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Mode Station/Parking: hover ghost, đặt/kéo/xoá khối (snap 1/2 ô), phím R xoay 4 hướng.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void HandleStationInput(Rect canvas)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseMove:
                    if (canvas.Contains(e.mousePosition))
                    {
                        Vector2Int k = BlockKindSize;
                        _hasHover = true;
                        _hoverAnchor = ClampBlockAnchor(
                            AnchorFromMouseFor(canvas, e.mousePosition, k.x, k.y), k.x, k.y);
                        Repaint();
                    }
                    else if (_hasHover)
                    {
                        _hasHover = false;
                        Repaint();
                    }
                    break;

                case EventType.MouseDown when e.button == 0 && canvas.Contains(e.mousePosition):
                {
                    // Click trúng khối có sẵn (station trước, parking sau) → kéo nó;
                    // click chỗ trống → đặt khối mới theo loại đang chọn rồi kéo luôn.
                    Vector2 f = MouseToGridF(canvas, e.mousePosition);
                    int hitS = FindStationAt(f);
                    if (hitS >= 0)
                    {
                        _draggingStation = hitS;
                    }
                    else
                    {
                        int hitP = FindParkingAt(f);
                        if (hitP >= 0)
                        {
                            _draggingParking = hitP;
                        }
                        else
                        {
                            Vector2Int k = BlockKindSize;
                            Vector2Int anchor = ClampBlockAnchor(
                                AnchorFromMouseFor(canvas, e.mousePosition, k.x, k.y), k.x, k.y);
                            if (_blockKind == 0)
                            {
                                _stations.Add(EncodeStation(anchor, 0));
                                _draggingStation = _stations.Count - 1;
                            }
                            else
                            {
                                _parkings.Add(EncodeParking(anchor, _blockKind == 2 ? 1 : 0));
                                _draggingParking = _parkings.Count - 1;
                            }
                        }
                    }
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseDown when e.button == 1 && canvas.Contains(e.mousePosition):
                {
                    Vector2 f = MouseToGridF(canvas, e.mousePosition);
                    int hitS = FindStationAt(f);
                    if (hitS >= 0)
                    {
                        _stations.RemoveAt(hitS);
                    }
                    else
                    {
                        int hitP = FindParkingAt(f);
                        if (hitP >= 0) _parkings.RemoveAt(hitP);
                    }
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseDrag when _draggingStation >= 0:
                {
                    int s = StationSize;
                    DecodeStation(_stations[_draggingStation], out _, out _, out int rot);
                    Vector2Int a = ClampBlockAnchor(
                        AnchorFromMouseFor(canvas, e.mousePosition, s, s), s, s);
                    _stations[_draggingStation] = EncodeStation(a, rot);
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseDrag when _draggingParking >= 0:
                {
                    DecodeParking(_parkings[_draggingParking], out _, out _, out int orient);
                    Vector2Int k = ParkingCells(orient);
                    _parkings[_draggingParking] = EncodeParking(ClampBlockAnchor(
                        AnchorFromMouseFor(canvas, e.mousePosition, k.x, k.y), k.x, k.y), orient);
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseUp when _draggingStation >= 0 || _draggingParking >= 0:
                    _draggingStation = -1;
                    _draggingParking = -1;
                    e.Use();
                    Repaint();
                    break;

                // R: xoay khối dưới chuột (hoặc đang kéo) — station xoay 4 hướng mặt,
                // parking đảo ngang/dọc quanh anchor.
                case EventType.KeyDown when e.keyCode == KeyCode.R:
                {
                    Vector2 f = MouseToGridF(canvas, e.mousePosition);
                    int idxS = _draggingStation >= 0 ? _draggingStation : FindStationAt(f);
                    if (idxS >= 0)
                    {
                        DecodeStation(_stations[idxS], out int x2, out int y2, out int rot);
                        _stations[idxS] = EncodeStation(new Vector2Int(x2, y2), (rot + 1) & 3);
                        e.Use();
                        Repaint();
                        break;
                    }

                    int idxP = _draggingParking >= 0 ? _draggingParking : FindParkingAt(f);
                    if (idxP >= 0)
                    {
                        DecodeParking(_parkings[idxP], out int px2, out int py2, out int rotP);
                        int next = (rotP + 1) & 3; // 4 hướng mặt như station
                        Vector2Int k = ParkingCells(next);
                        _parkings[idxP] = EncodeParking(
                            ClampBlockAnchor(new Vector2Int(px2, py2), k.x, k.y), next);
                        e.Use();
                        Repaint();
                    }
                    break;
                }
            }
        }
    }
}
#endif
