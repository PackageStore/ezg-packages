#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Input mode Decor: đặt/kéo/xoá từng chấm (snap 1/2 ô, R xoay) và brush vùng chữ nhật
    /// (trái = rải random, phải = xoá trong vùng).</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void HandleDecorInput(Rect canvas)
        {
            if (_decorAreaMode)
            {
                HandleDecorAreaInput(canvas);
                return;
            }

            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseMove:
                    if (TryPixelToHalfPoint(canvas, e.mousePosition, out Vector2Int hover)
                        && canvas.Contains(e.mousePosition))
                    {
                        _decorHover = true;
                        _decorHoverP2 = hover;
                        Repaint();
                    }
                    else if (_decorHover)
                    {
                        _decorHover = false;
                        Repaint();
                    }
                    break;

                case EventType.MouseDown when e.button == 0 && canvas.Contains(e.mousePosition):
                {
                    int hit = FindDecorAt(MouseToGridF(canvas, e.mousePosition));
                    if (hit >= 0)
                    {
                        _draggingDecor = hit; // kéo di chuyển item có sẵn
                    }
                    else if (TryPixelToHalfPoint(canvas, e.mousePosition, out Vector2Int p2))
                    {
                        PlaceDecorAt(p2);
                        _paintingDecor = true; // kéo tiếp = rải liên tục
                    }
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseDown when e.button == 1 && canvas.Contains(e.mousePosition):
                {
                    int hit = FindDecorAt(MouseToGridF(canvas, e.mousePosition));
                    if (hit >= 0) _decors.RemoveAt(hit);
                    _erasingDecor = true;
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseDrag when _draggingDecor >= 0:
                    if (TryPixelToHalfPoint(canvas, e.mousePosition, out Vector2Int move))
                    {
                        DecorItem item = _decors[_draggingDecor];
                        item.x2 = move.x;
                        item.y2 = move.y;
                        _decors[_draggingDecor] = item;
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when _paintingDecor:
                    if (TryPixelToHalfPoint(canvas, e.mousePosition, out Vector2Int paint))
                        PlaceDecorAt(paint);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when _erasingDecor:
                {
                    int hit = FindDecorAt(MouseToGridF(canvas, e.mousePosition));
                    if (hit >= 0) _decors.RemoveAt(hit);
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseUp:
                    if (_draggingDecor >= 0 || _paintingDecor || _erasingDecor)
                    {
                        _draggingDecor = -1;
                        _paintingDecor = false;
                        _erasingDecor = false;
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.R:
                {
                    int idx = _draggingDecor >= 0
                        ? _draggingDecor
                        : FindDecorAt(MouseToGridF(canvas, e.mousePosition));
                    if (idx >= 0)
                    {
                        DecorItem item = _decors[idx];
                        item.rot = (item.rot + 1) & 3;
                        _decors[idx] = item;
                        e.Use();
                        Repaint();
                    }
                    break;
                }
            }
        }

        /// <summary>Brush vùng: trái khoanh chữ nhật → rải random theo mật độ; phải khoanh → xoá
        /// mọi decor trong vùng.</summary>
        private void HandleDecorAreaInput(Rect canvas)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when (e.button == 0 || e.button == 1)
                                              && canvas.Contains(e.mousePosition):
                    _areaDragging = true;
                    _areaErasing = e.button == 1;
                    _areaStart = MouseToGridF(canvas, e.mousePosition);
                    _areaEnd = _areaStart;
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when _areaDragging:
                    _areaEnd = MouseToGridF(canvas, e.mousePosition);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when _areaDragging:
                {
                    _areaDragging = false;
                    float minX = Mathf.Max(0f, Mathf.Min(_areaStart.x, _areaEnd.x));
                    float maxX = Mathf.Min(_gridWidth - 1, Mathf.Max(_areaStart.x, _areaEnd.x));
                    float minY = Mathf.Max(0f, Mathf.Min(_areaStart.y, _areaEnd.y));
                    float maxY = Mathf.Min(_gridHeight - 1, Mathf.Max(_areaStart.y, _areaEnd.y));

                    if (_areaErasing) EraseDecorsInRect(minX, minY, maxX, maxY);
                    else ScatterDecorsInRect(minX, minY, maxX, maxY);

                    _areaErasing = false;
                    e.Use();
                    Repaint();
                    break;
                }
            }
        }
    }
}
#endif
