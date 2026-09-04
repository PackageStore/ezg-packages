#!/usr/bin/env python3
"""Fill/layer opacity shared between the manifest and the PNG exporter.

The manifest predicts the export path and the exporter takes it, so both must
read opacity through the same predicate. `composite()` bakes fill opacity into
the pixels; `topil()` bakes neither fill nor layer opacity.
"""

from psd_tools.constants import Tag

FILL_KINDS = ("solidcolorfill", "gradientfill", "patternfill")


def has_enabled_effects(layer):
    if not layer.effects:
        return False
    return any(e.enabled for e in layer.effects)


def bakes_fill_opacity(layer):
    """True when the export composites (baking fill opacity), False for topil()."""
    return (has_enabled_effects(layer) or layer.has_clip_layers()
            or layer.kind in FILL_KINDS)


def fill_opacity(layer):
    """Photoshop fill opacity, 0-255; absent block means 255."""
    blk = layer.tagged_blocks.get(Tag.BLEND_FILL_OPACITY)
    if blk is None:
        return 255
    return int(blk.data)


def opacity_fields(layer, is_text=False):
    """Return (node_opacity, fillOpacity, layerOpacity) rounded to 4 dp.

    fillOpacity/layerOpacity are None (not emitted) when the layer's fill
    opacity is 255, so such layers stay byte-identical. When fill opacity is
    below 255 the node opacity a builder sets is:
      - text: layer opacity (fill opacity goes to the glyph fill alpha)
      - baked path: layer opacity (fill already in the PNG pixels)
      - topil path: layer x fill (the builder carries both)
    """
    layer_op = int(layer.opacity)
    fill_op = fill_opacity(layer)
    if fill_op >= 255:
        return round(layer_op / 255.0, 4), None, None
    lo = round(layer_op / 255.0, 4)
    fo = round(fill_op / 255.0, 4)
    if is_text or bakes_fill_opacity(layer):
        node = lo
    else:
        node = round((layer_op / 255.0) * (fill_op / 255.0), 4)
    return node, fo, lo


def _selftest():
    class Effect:
        def __init__(self, enabled):
            self.enabled = enabled

    class Block:
        def __init__(self, value):
            self.data = value

    class Blocks:
        def __init__(self, mapping):
            self._m = mapping

        def get(self, key):
            return self._m.get(key)

    class Layer:
        def __init__(self, opacity=255, fill=255, kind="pixel",
                     effects=None, clip=False):
            self.opacity = opacity
            self.kind = kind
            self.effects = effects or []
            self._clip = clip
            self.tagged_blocks = Blocks(
                {} if fill >= 255 else {Tag.BLEND_FILL_OPACITY: Block(fill)})

        def has_clip_layers(self):
            return self._clip

    assert fill_opacity(Layer()) == 255
    assert fill_opacity(Layer(fill=128)) == 128

    assert not bakes_fill_opacity(Layer())
    assert bakes_fill_opacity(Layer(kind="solidcolorfill"))
    assert bakes_fill_opacity(Layer(effects=[Effect(True)]))
    assert not bakes_fill_opacity(Layer(effects=[Effect(False)]))
    assert bakes_fill_opacity(Layer(clip=True))

    assert opacity_fields(Layer()) == (1.0, None, None)
    assert opacity_fields(Layer(opacity=115)) == (0.451, None, None)

    assert opacity_fields(Layer(fill=38)) == (0.149, 0.149, 1.0)
    assert opacity_fields(Layer(fill=107)) == (0.4196, 0.4196, 1.0)

    # fill 50% + drop shadow -> composite path bakes fill,
    # node opacity is layer opacity alone.
    assert opacity_fields(Layer(fill=128, effects=[Effect(True)])) == (
        1.0, 0.502, 1.0)

    assert opacity_fields(Layer(fill=204, kind="solidcolorfill")) == (
        1.0, 0.8, 1.0)

    assert opacity_fields(Layer(fill=128), is_text=True) == (1.0, 0.502, 1.0)

    print("psd_opacity self-test OK")


if __name__ == "__main__":
    _selftest()
