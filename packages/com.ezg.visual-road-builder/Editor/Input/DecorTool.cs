#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Decor point input: place/drag/erase/rotate single items + area brush.</summary>
    internal sealed class DecorTool : IPaintTool
    {
        private readonly ToolContext _ctx;
        private readonly DecorState _decorState;

        internal DecorTool(ToolContext ctx, DecorState decorState)
        {
            _ctx = ctx;
            _decorState = decorState;
        }

        public bool HandleInput(Rect canvas, Event e)
        {
            if (_decorState.AreaMode)
                return HandleDecorAreaInput(canvas, e);
            return HandleDecorPointInput(canvas, e);
        }

        private bool HandleDecorPointInput(Rect canvas, Event e)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            switch (e.type)
            {
                case EventType.MouseMove:
                    if (CoordHelper.TryPixelToHalfPoint(canvas, e.mousePosition, doc, view, out Vector2Int hover)
                        && canvas.Contains(e.mousePosition))
                    {
                        _decorState.Hover = true;
                        _decorState.HoverP2 = hover;
                        _ctx.Host.Repaint();
                    }
                    else if (_decorState.Hover)
                    {
                        _decorState.Hover = false;
                        _ctx.Host.Repaint();
                    }
                    break;

                case EventType.MouseDown when e.button == 0 && canvas.Contains(e.mousePosition):
                {
                    int hit = DecorOpsStatic.FindDecorAt(CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view), doc.Decors);
                    if (hit >= 0)
                    {
                        _decorState.DraggingDecor = hit;
                    }
                    else if (CoordHelper.TryPixelToHalfPoint(canvas, e.mousePosition, doc, view, out Vector2Int p2))
                    {
                        DecorOpsStatic.PlaceDecorAt(p2, _decorState, doc);
                        _decorState.PaintingDecor = true;
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                case EventType.MouseDown when e.button == 1 && canvas.Contains(e.mousePosition):
                {
                    int hit = DecorOpsStatic.FindDecorAt(CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view), doc.Decors);
                    if (hit >= 0) doc.Decors.RemoveAt(hit);
                    _decorState.ErasingDecor = true;
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                case EventType.MouseDrag when _decorState.DraggingDecor >= 0:
                    if (CoordHelper.TryPixelToHalfPoint(canvas, e.mousePosition, doc, view, out Vector2Int move))
                    {
                        DecorItem item = doc.Decors[_decorState.DraggingDecor];
                        item.x2 = move.x;
                        item.y2 = move.y;
                        doc.Decors[_decorState.DraggingDecor] = item;
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                case EventType.MouseDrag when _decorState.PaintingDecor:
                    if (CoordHelper.TryPixelToHalfPoint(canvas, e.mousePosition, doc, view, out Vector2Int paint))
                        DecorOpsStatic.PlaceDecorAt(paint, _decorState, doc);
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                case EventType.MouseDrag when _decorState.ErasingDecor:
                {
                    int hit = DecorOpsStatic.FindDecorAt(CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view), doc.Decors);
                    if (hit >= 0) doc.Decors.RemoveAt(hit);
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                case EventType.MouseUp:
                    if (_decorState.DraggingDecor >= 0 || _decorState.PaintingDecor || _decorState.ErasingDecor)
                    {
                        _decorState.DraggingDecor = -1;
                        _decorState.PaintingDecor = false;
                        _decorState.ErasingDecor = false;
                        e.Use();
                        _ctx.Host.Repaint();
                    }
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.R:
                {
                    int idx = _decorState.DraggingDecor >= 0
                        ? _decorState.DraggingDecor
                        : DecorOpsStatic.FindDecorAt(CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view), doc.Decors);
                    if (idx >= 0)
                    {
                        DecorItem item = doc.Decors[idx];
                        item.rot = (item.rot + 1) & 3;
                        doc.Decors[idx] = item;
                        e.Use();
                        _ctx.Host.Repaint();
                        return true;
                    }
                    break;
                }

                default:
                    return false;
            }
            return true;
        }

        /// <summary>Brush vùng: trái khoanh chữ nhật → rải random theo mật độ; phải khoanh → xoá
        /// mọi decor trong vùng.</summary>
        private bool HandleDecorAreaInput(Rect canvas, Event e)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            switch (e.type)
            {
                case EventType.MouseDown when (e.button == 0 || e.button == 1)
                                              && canvas.Contains(e.mousePosition):
                    _decorState.AreaDragging = true;
                    _decorState.AreaErasing = e.button == 1;
                    _decorState.AreaStart = CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view);
                    _decorState.AreaEnd = _decorState.AreaStart;
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                case EventType.MouseDrag when _decorState.AreaDragging:
                    _decorState.AreaEnd = CoordHelper.MouseToGridF(canvas, e.mousePosition, doc, view);
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                case EventType.MouseUp when _decorState.AreaDragging:
                {
                    _decorState.AreaDragging = false;
                    float minX = Mathf.Max(0f, Mathf.Min(_decorState.AreaStart.x, _decorState.AreaEnd.x));
                    float maxX = Mathf.Min(doc.GridWidth - 1, Mathf.Max(_decorState.AreaStart.x, _decorState.AreaEnd.x));
                    float minY = Mathf.Max(0f, Mathf.Min(_decorState.AreaStart.y, _decorState.AreaEnd.y));
                    float maxY = Mathf.Min(doc.GridHeight - 1, Mathf.Max(_decorState.AreaStart.y, _decorState.AreaEnd.y));

                    if (_decorState.AreaErasing) DecorOpsStatic.EraseDecorsInRect(minX, minY, maxX, maxY, doc);
                    else DecorOpsStatic.ScatterDecorsInRect(minX, minY, maxX, maxY, _decorState, doc);

                    _decorState.AreaErasing = false;
                    e.Use();
                    _ctx.Host.Repaint();
                    break;
                }

                default:
                    return false;
            }
            return true;
        }

        public void DrawOverlay(Rect canvas) { }

        public void Cancel()
        {
            _decorState.ResetInteraction();
        }
    }
}
#endif
