using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace FigCraftBridge.Editor.Source
{
    /// <summary>
    ///     Turns FigCraft's document JSON (<c>inspect_document</c> with every page at full depth) into the file
    ///     shape the importer reads: REST-style nodes, ids, paints, layout and component maps.
    /// </summary>
    public static class FigCraftToRest
    {
        public sealed class Result
        {
            public JObject File;
            /// <summary>Importer node id -> FigCraft layer id, for renders.</summary>
            public Dictionary<string, long> LayerIds = new Dictionary<string, long>();
        }

        static readonly Dictionary<string, string> Types = new Dictionary<string, string>
        {
            ["FRAME"] = "FRAME", ["GROUP"] = "GROUP", ["RECTANGLE"] = "RECTANGLE", ["ELLIPSE"] = "ELLIPSE",
            ["TEXT"] = "TEXT", ["VECTOR"] = "VECTOR", ["LINE"] = "LINE", ["ARROW"] = "LINE", ["STAR"] = "STAR",
            ["POLYGON"] = "REGULAR_POLYGON", ["REGULAR_POLYGON"] = "REGULAR_POLYGON",
            ["BOOLEAN_OPERATION"] = "BOOLEAN_OPERATION", ["BOOLEAN"] = "BOOLEAN_OPERATION", ["SLICE"] = "SLICE",
            ["SECTION"] = "SECTION", ["COMPONENT"] = "COMPONENT", ["COMPONENT_SET"] = "COMPONENT_SET",
            ["INSTANCE"] = "INSTANCE",
        };

        static readonly Dictionary<string, string> Constraint = new Dictionary<string, string>
        {
            ["left"] = "LEFT", ["right"] = "RIGHT", ["center"] = "CENTER", ["leftRight"] = "LEFT_RIGHT", ["scale"] = "SCALE",
            ["top"] = "TOP", ["bottom"] = "BOTTOM", ["topBottom"] = "TOP_BOTTOM",
        };

        static readonly Dictionary<string, string> ScaleModes = new Dictionary<string, string>
        {
            ["fill"] = "FILL", ["fit"] = "FIT", ["tile"] = "TILE", ["crop"] = "STRETCH", ["stretch"] = "STRETCH",
        };

        static readonly Dictionary<string, string> Effects = new Dictionary<string, string>
        {
            ["dropShadow"] = "DROP_SHADOW", ["innerShadow"] = "INNER_SHADOW", ["layerBlur"] = "LAYER_BLUR",
            ["backgroundBlur"] = "BACKGROUND_BLUR", ["glass"] = "GLASS", ["noise"] = "NOISE", ["texture"] = "TEXTURE",
        };

        static readonly Dictionary<string, int> Weights = new Dictionary<string, int>
        {
            ["thin"] = 100, ["hairline"] = 100, ["extralight"] = 200, ["ultralight"] = 200, ["light"] = 300,
            ["regular"] = 400, ["normal"] = 400, ["book"] = 400, ["medium"] = 500, ["semibold"] = 600,
            ["demibold"] = 600, ["bold"] = 700, ["extrabold"] = 800, ["ultrabold"] = 800, ["black"] = 900, ["heavy"] = 900,
        };

        sealed class Context
        {
            public readonly Dictionary<long, JObject> Layers = new Dictionary<long, JObject>();
            public readonly Dictionary<long, string> RestIds = new Dictionary<long, string>();
            public readonly HashSet<long> Assigning = new HashSet<long>();
            public readonly JObject Components = new JObject();
            public readonly JObject ComponentSets = new JObject();
            public readonly Result Result = new Result();
        }

        public static Result Convert(JObject document, string name)
        {
            var ctx = new Context();
            var pages = document["pages"] as JArray ?? new JArray();
            foreach (var page in pages.OfType<JObject>())
                foreach (var child in Children(page))
                    Index(child, ctx);
            foreach (var page in pages.OfType<JObject>())
                foreach (var child in Children(page))
                    AssignIds(child, Id(child).ToString(CultureInfo.InvariantCulture), ctx);

            var restPages = new JArray();
            foreach (var page in pages.OfType<JObject>())
            {
                var children = new JArray();
                foreach (var child in Children(page)) children.Add(Node(child, null, ctx));
                restPages.Add(new JObject
                {
                    ["id"] = "0:" + page.Value<string>("id"),
                    ["name"] = page.Value<string>("name") ?? "Page",
                    ["type"] = "CANVAS",
                    ["visible"] = true,
                    ["backgroundColor"] = Color(page["background"] ?? "#F5F5F5"),
                    ["children"] = children,
                });
            }

            ctx.Result.File = new JObject
            {
                ["name"] = string.IsNullOrEmpty(name) ? document.Value<string>("name") ?? "Document" : name,
                ["lastModified"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["version"] = "0",
                ["schemaVersion"] = 0,
                ["document"] = new JObject
                {
                    ["id"] = "0:0", ["name"] = "Document", ["type"] = "DOCUMENT", ["visible"] = true, ["children"] = restPages,
                },
                ["components"] = ctx.Components,
                ["componentSets"] = ctx.ComponentSets,
                ["styles"] = new JObject(),
            };
            return ctx.Result;
        }

        /// <summary>
        ///     FigCraft's layer JSON leaves out a text box's vertical alignment; the saved archive keeps it as
        ///     <c>align_v</c> in <c>document.json</c>. Copies it onto every TEXT layer that lacks
        ///     <c>textAlignVertical</c>, so a fixed box centred in FigCraft is not imported top-aligned.
        ///     Returns the number of layers filled.
        /// </summary>
        public static int FillTextAlignVertical(JObject document, IReadOnlyDictionary<long, string> alignV)
        {
            if (alignV == null || alignV.Count == 0) return 0;
            var filled = 0;
            void Walk(JObject node)
            {
                if (node.Value<string>("type") == "TEXT" && node["textAlignVertical"] == null &&
                    alignV.TryGetValue(Id(node), out var align))
                {
                    node["textAlignVertical"] = align;
                    filled++;
                }
                foreach (var child in Children(node)) Walk(child);
            }
            foreach (var page in (document["pages"] as JArray ?? new JArray()).OfType<JObject>())
                foreach (var child in Children(page)) Walk(child);
            return filled;
        }

        /// <summary>Layer id -> <c>align_v</c> of every text layer in an archive's <c>document.json</c>.</summary>
        public static Dictionary<long, string> ReadTextAlignVertical(JObject archiveDocument)
        {
            var map = new Dictionary<long, string>();
            if (!(archiveDocument?["nodes"] is JObject nodes)) return map;
            foreach (var property in nodes.Properties())
                if (property.Value is JObject layer && layer.Value<string>("type") == "text" &&
                    layer.Value<string>("align_v") is string align && long.TryParse(property.Name, out var id))
                    map[id] = align;
            return map;
        }

        static IEnumerable<JObject> Children(JObject node) => (node["children"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        static long Id(JObject node) => node.Value<long>("id");

        static void Index(JObject node, Context ctx)
        {
            ctx.Layers[Id(node)] = node;
            foreach (var child in Children(node)) Index(child, ctx);
        }

        /// <summary>
        ///     Gives each layer the id the importer expects. A layer inside an instance is
        ///     <c>I&lt;instance&gt;;&lt;master layer&gt;</c>, paired with the master's layer at the same place, so
        ///     the importer's override and swap logic finds it; slot content with no counterpart keeps its own id.
        /// </summary>
        static void AssignIds(JObject node, string restId, Context ctx)
        {
            var id = Id(node);
            ctx.RestIds[id] = restId;
            if (node.Value<string>("type") == "INSTANCE" && node["mainComponent"] != null)
            {
                var master = MasterOf(node, ctx);
                if (master != null) EnsureAssigned(master, ctx);
                var prefix = restId.StartsWith("I", StringComparison.Ordinal) ? restId : "I" + restId;
                AssignPaired(Children(node).ToList(), master != null ? Children(master).ToList() : null, prefix, ctx);
                return;
            }
            foreach (var child in Children(node))
                if (!ctx.RestIds.ContainsKey(Id(child)))
                    AssignIds(child, Id(child).ToString(CultureInfo.InvariantCulture), ctx);
        }

        static void AssignPaired(List<JObject> children, List<JObject> masters, string prefix, Context ctx)
        {
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var counterpart = masters != null && i < masters.Count ? masters[i] : null;
                if (counterpart == null || counterpart.Value<string>("type") != child.Value<string>("type"))
                {
                    AssignIds(child, Id(child).ToString(CultureInfo.InvariantCulture), ctx);
                    continue;
                }
                var masterRestId = ctx.RestIds.TryGetValue(Id(counterpart), out var assigned)
                    ? assigned
                    : Id(counterpart).ToString(CultureInfo.InvariantCulture);
                var restId = prefix + ";" + (masterRestId.StartsWith("I", StringComparison.Ordinal) ? masterRestId.Substring(1) : masterRestId);
                ctx.RestIds[Id(child)] = restId;
                if (child.Value<string>("type") == "INSTANCE")
                    AssignIds(child, restId, ctx);
                else
                    AssignPaired(Children(child).ToList(), Children(counterpart).ToList(), prefix, ctx);
            }
        }

        static JObject MasterOf(JObject instance, Context ctx) =>
            ctx.Layers.TryGetValue(instance.Value<long>("mainComponent"), out var master) ? master : null;

        static void EnsureAssigned(JObject master, Context ctx)
        {
            var id = Id(master);
            if (ctx.RestIds.ContainsKey(id) && Children(master).All(c => ctx.RestIds.ContainsKey(Id(c)))) return;
            if (!ctx.Assigning.Add(id)) return;
            AssignIds(master, ctx.RestIds.TryGetValue(id, out var restId) ? restId : id.ToString(CultureInfo.InvariantCulture), ctx);
            ctx.Assigning.Remove(id);
        }

        static JObject Node(JObject raw, JObject parent, Context ctx)
        {
            var id = Id(raw);
            var restId = ctx.RestIds.TryGetValue(id, out var assigned) ? assigned : id.ToString(CultureInfo.InvariantCulture);
            ctx.Result.LayerIds[restId] = id;

            var rawType = raw.Value<string>("type") ?? "VECTOR";
            var type = Types.TryGetValue(rawType, out var mapped) ? mapped : "VECTOR";
            var bounds = raw["absoluteBounds"] as JObject;
            var width = raw.Value<float?>("width") ?? bounds?.Value<float?>("width") ?? 0f;
            var height = raw.Value<float?>("height") ?? bounds?.Value<float?>("height") ?? 0f;
            var children = Children(raw).ToList();

            var node = new JObject
            {
                ["id"] = restId,
                ["name"] = raw.Value<string>("name") ?? string.Empty,
                ["type"] = type,
                ["visible"] = raw.Value<bool?>("visible") ?? true,
                ["opacity"] = raw.Value<float?>("opacity") ?? 1f,
                ["blendMode"] = children.Count > 0 ? "PASS_THROUGH" : Blend(raw.Value<string>("blend")),
                ["absoluteBoundingBox"] = new JObject
                {
                    ["x"] = bounds?.Value<float?>("x") ?? raw.Value<float?>("x") ?? 0f,
                    ["y"] = bounds?.Value<float?>("y") ?? raw.Value<float?>("y") ?? 0f,
                    ["width"] = bounds?.Value<float?>("width") ?? width,
                    ["height"] = bounds?.Value<float?>("height") ?? height,
                },
                ["size"] = new JObject { ["x"] = width, ["y"] = height },
                ["relativeTransform"] = Transform(raw),
                ["fills"] = Paints(raw["fills"]),
                ["strokes"] = Paints(raw["strokes"]),
                ["strokeWeight"] = raw.Value<float?>("strokeWeight") ?? 1f,
                ["strokeAlign"] = (raw.Value<string>("strokeAlign") ?? "inside").ToUpperInvariant(),
                ["effects"] = new JArray((raw["effects"] as JArray ?? new JArray()).OfType<JObject>().Select(Effect)),
                ["clipsContent"] = raw.Value<bool?>("clip") ?? false,
                ["isMask"] = raw.Value<bool?>("isMask") ?? false,
            };

            if (raw["strokeSides"] is JArray sides && sides.Count == 4)
                node["individualStrokeWeights"] = new JObject
                {
                    ["top"] = sides[0].Value<float>(), ["right"] = sides[1].Value<float>(),
                    ["bottom"] = sides[2].Value<float>(), ["left"] = sides[3].Value<float>(),
                };
            if (raw["constraints"] is JObject constraints)
                node["constraints"] = new JObject
                {
                    ["horizontal"] = Map(Constraint, constraints.Value<string>("horizontal"), "LEFT"),
                    ["vertical"] = Map(Constraint, constraints.Value<string>("vertical"), "TOP"),
                };
            if (raw["cornerRadius"] is JArray radii)
            {
                node["rectangleCornerRadii"] = new JArray(radii.Take(4).Select(r => r.Value<float>()));
                node["cornerRadius"] = radii.Count > 0 ? radii.Max(r => r.Value<float>()) : 0f;
            }
            else if (raw["cornerRadius"] != null && raw["cornerRadius"].Type != JTokenType.Null)
                node["cornerRadius"] = raw.Value<float>("cornerRadius");

            Layout(raw, parent, node);

            if (raw["exports"] is JArray exports && exports.Count > 0)
                node["exportSettings"] = new JArray(new JObject
                {
                    ["suffix"] = string.Empty, ["format"] = "PNG",
                    ["constraint"] = new JObject { ["type"] = "SCALE", ["value"] = 1 },
                });
            if (type == "TEXT")
            {
                node["characters"] = raw.Value<string>("characters") ?? string.Empty;
                node["style"] = TextStyle(raw);
            }
            if (type == "COMPONENT")
                ctx.Components[restId] = new JObject
                {
                    ["key"] = restId, ["name"] = node["name"], ["description"] = raw.Value<string>("description") ?? string.Empty,
                };
            if (type == "COMPONENT_SET")
                ctx.ComponentSets[restId] = new JObject
                {
                    ["key"] = restId, ["name"] = node["name"], ["description"] = raw.Value<string>("description") ?? string.Empty,
                };
            if (type == "INSTANCE" && raw["mainComponent"] != null)
            {
                var masterId = raw.Value<long>("mainComponent");
                node["componentId"] = ctx.RestIds.TryGetValue(masterId, out var masterRestId)
                    ? masterRestId
                    : masterId.ToString(CultureInfo.InvariantCulture);
            }
            if (children.Count > 0) node["children"] = new JArray(children.Select(c => Node(c, raw, ctx)));
            return node;
        }

        static void Layout(JObject raw, JObject parent, JObject node)
        {
            var sizing = raw["sizing"] as JObject;
            var horizontal = (sizing?.Value<string>("horizontal") ?? "fixed").ToUpperInvariant();
            var vertical = (sizing?.Value<string>("vertical") ?? "fixed").ToUpperInvariant();
            if (sizing != null)
            {
                node["layoutSizingHorizontal"] = horizontal;
                node["layoutSizingVertical"] = vertical;
            }

            if (raw["layout"] is JObject layout && !string.IsNullOrEmpty(layout.Value<string>("mode")))
            {
                var mode = layout.Value<string>("mode").ToUpperInvariant();
                node["layoutMode"] = mode;
                node["itemSpacing"] = layout.Value<float?>("gap") ?? 0f;
                var padding = layout["padding"] as JArray;
                node["paddingTop"] = padding?.Count == 4 ? padding[0].Value<float>() : 0f;
                node["paddingRight"] = padding?.Count == 4 ? padding[1].Value<float>() : 0f;
                node["paddingBottom"] = padding?.Count == 4 ? padding[2].Value<float>() : 0f;
                node["paddingLeft"] = padding?.Count == 4 ? padding[3].Value<float>() : 0f;
                node["primaryAxisAlignItems"] = (layout.Value<string>("mainAlign") ?? "min") switch
                {
                    "center" => "CENTER", "max" => "MAX", "spaceBetween" => "SPACE_BETWEEN", _ => "MIN",
                };
                node["counterAxisAlignItems"] = (layout.Value<string>("crossAlign") ?? "min") switch
                {
                    "center" => "CENTER", "max" => "MAX", _ => "MIN",
                };
                node["itemReverseZIndex"] = layout.Value<bool?>("reverseZ") ?? false;
                node["strokesIncludedInLayout"] = layout.Value<bool?>("strokesIncluded") ?? false;
                var main = mode == "VERTICAL" ? vertical : horizontal;
                var cross = mode == "VERTICAL" ? horizontal : vertical;
                node["primaryAxisSizingMode"] = main == "HUG" ? "AUTO" : "FIXED";
                node["counterAxisSizingMode"] = cross == "HUG" ? "AUTO" : "FIXED";
            }

            var parentMode = (parent?["layout"] as JObject)?.Value<string>("mode");
            if (string.IsNullOrEmpty(parentMode)) return;
            if (raw.Value<bool?>("absolute") == true)
            {
                node["layoutPositioning"] = "ABSOLUTE";
                return;
            }
            var crossSizing = parentMode == "vertical" ? horizontal : vertical;
            node["layoutAlign"] = crossSizing == "FILL" ? "STRETCH" : "INHERIT";
        }

        static JArray Transform(JObject raw)
        {
            var radians = (raw.Value<double?>("rotation") ?? 0d) * Math.PI / 180d;
            var c = (float)Math.Cos(radians);
            var s = (float)Math.Sin(radians);
            return new JArray(
                new JArray(c, s, raw.Value<float?>("x") ?? 0f),
                new JArray(-s, c, raw.Value<float?>("y") ?? 0f));
        }

        static JArray Paints(JToken paints)
        {
            var result = new JArray();
            foreach (var paint in paints as JArray ?? new JArray())
            {
                var converted = Paint(paint);
                if (converted != null) result.Add(converted);
            }
            return result;
        }

        static JObject Paint(JToken token)
        {
            var paint = token as JObject ?? new JObject { ["type"] = "solid", ["color"] = token };
            var type = paint.Value<string>("type") ?? "solid";
            var result = new JObject
            {
                ["visible"] = paint.Value<bool?>("visible") ?? true,
                ["opacity"] = paint.Value<float?>("opacity") ?? 1f,
                ["blendMode"] = Blend(paint.Value<string>("blend")),
            };
            switch (type)
            {
                case "solid":
                    result["type"] = "SOLID";
                    result["color"] = Color(paint["color"]);
                    return result;
                case "image":
                    result["type"] = "IMAGE";
                    result["imageRef"] = paint.Value<string>("image");
                    result["scaleMode"] = Map(ScaleModes, paint.Value<string>("scale_mode") ?? paint.Value<string>("scaleMode"), "FILL");
                    result["scalingFactor"] = paint.Value<float?>("scale") ?? 1f;
                    result["rotation"] = paint.Value<float?>("rotation") ?? 0f;
                    // FigCraft's crop is the image transform as (a, b, c, d, e, f): x' = a x + c y + e, y' = b x + d y + f.
                    // Figma REST keeps it as [[a, c, e], [b, d, f]]; the 9-slice pass reads each cell's crop from it.
                    if (paint["crop"] is JArray crop && crop.Count == 6)
                        result["imageTransform"] = new JArray(
                            new JArray(crop[0].Value<float>(), crop[2].Value<float>(), crop[4].Value<float>()),
                            new JArray(crop[1].Value<float>(), crop[3].Value<float>(), crop[5].Value<float>()));
                    return result;
                default:
                    var kind = type == "gradient" ? paint.Value<string>("kind") ?? "linear" : Regex.Replace(type, "Gradient$", string.Empty);
                    result["type"] = kind switch
                    {
                        "linear" => "GRADIENT_LINEAR", "radial" => "GRADIENT_RADIAL", "angular" => "GRADIENT_ANGULAR",
                        "diamond" => "GRADIENT_DIAMOND", _ => "UNKNOWN",
                    };
                    if (result.Value<string>("type") == "UNKNOWN") return result;
                    var stops = paint["stops"] as JArray ?? new JArray();
                    result["gradientStops"] = new JArray(stops.Select((stop, i) => stop is JObject s
                        ? new JObject { ["position"] = s.Value<float?>("position") ?? 0f, ["color"] = Color(s["color"]) }
                        : new JObject { ["position"] = stops.Count > 1 ? (float)i / (stops.Count - 1) : 0f, ["color"] = Color(stop) }));
                    var from = Point(paint["from"], 0f, 0.5f);
                    var to = Point(paint["to"], 1f, 0.5f);
                    var width = Point(paint["width"], from.x, from.y + 1f);
                    result["gradientHandlePositions"] = new JArray(Vector(from), Vector(to), Vector(width));
                    return result;
            }
        }

        static (float x, float y) Point(JToken token, float x, float y) =>
            token is JArray a && a.Count == 2 ? (a[0].Value<float>(), a[1].Value<float>()) : (x, y);

        static JObject Vector((float x, float y) p) => new JObject { ["x"] = p.x, ["y"] = p.y };

        static JObject Effect(JObject effect)
        {
            float dx = 0f, dy = 0f;
            if (effect["offset"] is JArray offset && offset.Count == 2)
            {
                dx = offset[0].Value<float>();
                dy = offset[1].Value<float>();
            }
            else if (effect["offset"] is JObject point)
            {
                dx = point.Value<float?>("x") ?? 0f;
                dy = point.Value<float?>("y") ?? 0f;
            }
            return new JObject
            {
                ["type"] = Map(Effects, effect.Value<string>("type"), "UNKNOWN"),
                ["visible"] = effect.Value<bool?>("visible") ?? true,
                ["radius"] = effect.Value<float?>("radius") ?? 0f,
                ["spread"] = effect.Value<float?>("spread") ?? 0f,
                ["color"] = Color(effect["color"]),
                ["offset"] = new JObject { ["x"] = dx, ["y"] = dy },
                ["blendMode"] = Blend(effect.Value<string>("blend")),
                ["showShadowBehindNode"] = effect.Value<bool?>("showBehind") ?? false,
            };
        }

        static JObject TextStyle(JObject raw)
        {
            var size = raw.Value<float?>("fontSize") ?? 12f;
            var styleName = raw.Value<string>("fontStyle") ?? "Regular";
            var key = Regex.Replace(Regex.Replace(styleName.ToLowerInvariant(), "italic|oblique", string.Empty), @"[\s_-]", string.Empty);
            if (key.Length == 0) key = "regular";
            var style = new JObject
            {
                ["fontFamily"] = raw.Value<string>("fontFamily") ?? "Inter",
                ["fontWeight"] = Weights.TryGetValue(key, out var weight) ? weight : 400,
                ["italic"] = Regex.IsMatch(styleName, "italic|oblique", RegexOptions.IgnoreCase),
                ["fontSize"] = size,
                ["textAlignHorizontal"] = (raw.Value<string>("textAlign") ?? "left").ToLowerInvariant() switch
                {
                    "center" => "CENTER", "right" => "RIGHT", "justified" => "JUSTIFIED", "justify" => "JUSTIFIED", _ => "LEFT",
                },
                ["textAlignVertical"] = (raw.Value<string>("textAlignVertical") ?? "top").ToLowerInvariant() switch
                {
                    "center" => "CENTER", "middle" => "CENTER", "bottom" => "BOTTOM", _ => "TOP",
                },
                ["letterSpacing"] = Length(raw["letterSpacing"], size) ?? 0f,
                ["textAutoResize"] = raw.Value<string>("textAutoResize") switch
                {
                    "widthAndHeight" => "WIDTH_AND_HEIGHT", "height" => "HEIGHT", "truncate" => "TRUNCATE", _ => "NONE",
                },
                ["textCase"] = raw.Value<string>("textCase") switch
                {
                    "upper" => "UPPER", "lower" => "LOWER", "title" => "TITLE", "smallCaps" => "SMALL_CAPS", _ => "ORIGINAL",
                },
            };
            if (raw.Value<string>("leadingTrim") == "capHeight") style["leadingTrim"] = "CAP_HEIGHT";
            var lineHeight = raw["lineHeight"];
            var text = lineHeight?.Type == JTokenType.String ? lineHeight.Value<string>() : null;
            if (text != null && text.EndsWith("%", StringComparison.Ordinal) &&
                float.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                style["lineHeightPx"] = size * percent / 100f;
                style["lineHeightPercentFontSize"] = percent;
                style["lineHeightUnitString"] = "FONT_SIZE_%";
            }
            else if (Length(lineHeight, size) is float pixels)
            {
                style["lineHeightPx"] = pixels;
                style["lineHeightPercentFontSize"] = size > 0 ? pixels / size * 100f : 100f;
                style["lineHeightUnitString"] = "PIXELS";
            }
            else
            {
                style["lineHeightPx"] = size * 1.2f;
                style["lineHeightUnitString"] = "INTRINSIC_%";
            }
            return style;
        }

        /// <summary>A number, or "12", "12px" or "5%" of <paramref name="size"/>; null for "auto" or nothing.</summary>
        static float? Length(JToken token, float size)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) return token.Value<float>();
            var match = Regex.Match(token.Value<string>() ?? string.Empty, @"^\s*(-?[\d.]+)\s*(%|px)?\s*$");
            if (!match.Success) return null;
            var value = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return match.Groups[2].Value == "%" ? value / 100f * size : value;
        }

        static JObject Color(JToken token)
        {
            if (token is JObject c)
                return new JObject
                {
                    ["r"] = c.Value<float?>("r") ?? 0f, ["g"] = c.Value<float?>("g") ?? 0f,
                    ["b"] = c.Value<float?>("b") ?? 0f, ["a"] = c.Value<float?>("a") ?? 1f,
                };
            var match = Regex.Match(token?.ToString() ?? string.Empty, "^#?([0-9a-fA-F]{6})([0-9a-fA-F]{2})?$");
            if (!match.Success) return new JObject { ["r"] = 0f, ["g"] = 0f, ["b"] = 0f, ["a"] = 1f };
            var rgb = int.Parse(match.Groups[1].Value, NumberStyles.HexNumber);
            return new JObject
            {
                ["r"] = ((rgb >> 16) & 255) / 255f,
                ["g"] = ((rgb >> 8) & 255) / 255f,
                ["b"] = (rgb & 255) / 255f,
                ["a"] = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, NumberStyles.HexNumber) / 255f : 1f,
            };
        }

        static string Blend(string blend) =>
            string.IsNullOrEmpty(blend) ? "NORMAL" : Regex.Replace(blend, "([a-z])([A-Z])", "$1_$2").ToUpperInvariant();

        static string Map(Dictionary<string, string> map, string key, string fallback) =>
            key != null && map.TryGetValue(key, out var value) ? value : fallback;
    }
}
