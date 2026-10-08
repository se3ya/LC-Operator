using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Operator.Cassette;

internal enum DeckMode
{
    NoTape,
    Loading,
    Stop,
    Play,
    Pause,
    Off,
}

internal sealed class DeckDisplay : IDisposable
{
    private static readonly Color32 Hot = new(255, 255, 255, 255);
    private static readonly Color32 Dim = new(120, 120, 120, 255);
    private static readonly Color32 Dark = new(0, 0, 0, 255);

    private static readonly Dictionary<char, string> Segments = new()
    {
        ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abged", ['3'] = "abgcd", ['4'] = "fgbc",
        ['5'] = "afgcd", ['6'] = "afgedc", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcfgd",
        ['-'] = "g", [' '] = "", ['A'] = "abcefg", ['P'] = "abefg", ['L'] = "def",
        ['d'] = "bcdeg", ['I'] = "ef", ['n'] = "ceg", ['G'] = "acdef", ['O'] = "abcdef",
    };

    private readonly Texture2D _texture;
    private readonly Material _material;
    private readonly Color32[] _pixels;
    private readonly int _width;
    private readonly int _height;
    private DeckMode _mode = (DeckMode)(-1);
    private int _speedTenths = -1;
    private int _elapsed = -1;
    private int _total = -1;
    private int _bar = -1;
    private string _clock = string.Empty;
    private float _nextDraw;

    private DeckDisplay(Material material, int width, int height)
    {
        _material = material;
        _width = width;
        _height = height;
        _pixels = new Color32[width * height];
        _texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
        {
            name = "DeckDisplay",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
        };
        _material.SetTexture(Constants.Television.DisplayEmissiveMap, _texture);
    }

    private int BarInner => _width - 25 - 12;

    internal static DeckDisplay? Create(Transform deck)
    {
        Transform? quad = deck.Find(Constants.Television.DisplayObjectName);
        var renderer = quad != null ? quad.GetComponent<MeshRenderer>() : null;
        if (renderer == null || renderer.sharedMaterial == null) return null;
        if (!renderer.sharedMaterial.HasProperty(Constants.Television.DisplayEmissiveMap)) return null;
        return new DeckDisplay(renderer.material, Constants.Television.DisplayWidth, Constants.Television.DisplayHeight);
    }

    internal void Show(DeckMode mode, float speed, int elapsed, int total, float fraction, string clock)
    {
        int tenths = Mathf.RoundToInt(speed * 10f);
        int bar = Mathf.RoundToInt(Mathf.Clamp01(fraction) * BarInner);
        bool layout = mode != _mode || tenths != _speedTenths || total != _total || clock != _clock;
        if (!layout && elapsed == _elapsed && bar == _bar) return;
        if (!layout && Time.unscaledTime < _nextDraw) return;

        _mode = mode;
        _speedTenths = tenths;
        _elapsed = elapsed;
        _total = total;
        _bar = bar;
        _clock = clock;
        _nextDraw = Time.unscaledTime + Constants.Television.DisplayRedrawInterval;
        Draw();
        _texture.SetPixels32(_pixels);
        _texture.Apply(true);
    }

    private void Draw()
    {
        Array.Fill(_pixels, Dark);
        if (_mode == DeckMode.Off) return;
        float h = _height;
        float w = _width;
        float by0 = (int)(h * 0.75f);
        float by1 = (int)(h * 0.96f);
        float gh = h * 0.525f;
        float gy = h * 0.055f;

        Outline(12f, by0, w - 13f, by1, 3f, Dim);
        if (_mode == DeckMode.NoTape)
        {
            DrawClock(gy, gh);
            return;
        }

        if (_mode == DeckMode.Loading)
        {
            SegText("LOAdInG", (w - SegWidth("LOAdInG", gh)) / 2f, gy, gh, Hot);
            return;
        }

        if (_mode == DeckMode.Play)
        {
            float half = gh / 2f;
            for (int y = Mathf.FloorToInt(gy); y <= Mathf.CeilToInt(gy + gh); y++)
            {
                float span = 1f - (Mathf.Abs(y - (gy + half)) / half);
                if (span > 0f) Rect(14f, y, 14f + (gh * 0.84f * span), y + 1f, Hot);
            }
        }
        else if (_mode == DeckMode.Pause)
        {
            float bw = gh * 0.28f;
            Rect(14f, gy, 14f + bw, gy + gh, Hot);
            Rect(14f + (gh * 0.54f), gy, 14f + (gh * 0.54f) + bw, gy + gh, Hot);
        }
        else
        {
            Rect(14f, gy, 14f + (gh * 0.82f), gy + gh, Hot);
        }

        float x = 14f + (gh * 0.84f) + 22f;
        if (_mode != DeckMode.Stop)
        {
            x = SegText($"{_speedTenths / 10}.{_speedTenths % 10}", x, gy, gh, Hot) + 6f;
            float size = gh * 0.30f;
            float cx = x + (size / 2f);
            float cy = gy + (gh * 0.70f);
            float stroke = Mathf.Max(3f, gh * 0.13f);
            Line(cx - (size / 2f), cy - (size / 2f), cx + (size / 2f), cy + (size / 2f), stroke, Hot);
            Line(cx - (size / 2f), cy + (size / 2f), cx + (size / 2f), cy - (size / 2f), stroke, Hot);
            x = cx + (size / 2f) + 22f;
        }

        int shown = _mode == DeckMode.Stop ? 0 : _elapsed;
        SegText($"{shown / 3600}:{(shown / 60) % 60:00}:{shown % 60:00}", x, gy, gh, Hot);

        float th = gh * 0.52f;
        string total = $"{_total / 60}:{_total % 60:00}";
        float tx = w - 14f - SegWidth(total, th);
        SegText(total, tx, gy + gh - th, th, Hot);
        Line(tx - 26f, gy + gh, tx - 12f, gy + gh - th, 4f, Hot);

        if (_mode != DeckMode.Stop && _bar > 0) Rect(16f, by0 + 4f, 16f + _bar, by1 - 4f, Hot);
    }

    private void DrawClock(float gy, float gh)
    {
        if (_clock.Length == 0) return;
        int space = _clock.IndexOf(' ');
        string time = space < 0 ? _clock : _clock.Substring(0, space);
        string half = space < 0 ? string.Empty : _clock.Substring(space + 1);
        float sh = gh * 0.52f;
        float width = SegWidth(time, gh) + (half.Length > 0 ? (gh * 0.25f) + SegWidth(half, sh) : 0f);
        float x = SegText(time, (_width - width) / 2f, gy, gh, Hot);
        if (half.Length > 0) SegText(half, x - (gh * 0.155f) + (gh * 0.25f), gy + gh - sh, sh, Hot);
    }

    private static float SegWidth(string text, float h)
    {
        float width = 0f;
        foreach (char c in text) width += (c == ':' || c == '.' ? 2f * h * 0.072f : h * 0.56f) + (h * 0.155f);
        return width - (h * 0.155f);
    }

    private float SegText(string text, float x, float y, float h, Color32 color)
    {
        float gap = h * 0.155f;
        float r = h * 0.072f;
        foreach (char c in text)
        {
            if (c == ':')
            {
                Circle(x + r, y + (h * 0.30f), r, color);
                Circle(x + r, y + (h * 0.72f), r, color);
                x += (2f * r) + gap;
            }
            else if (c == '.')
            {
                Circle(x + r, y + h - r, r, color);
                x += (2f * r) + gap;
            }
            else
            {
                Seven(c, x, y, h, color);
                x += (h * 0.56f) + gap;
            }
        }
        return x;
    }

    private void Seven(char c, float x, float y, float h, Color32 color)
    {
        if (!Segments.TryGetValue(c, out string? lit)) return;
        float w = h * 0.56f;
        float t = h * 0.17f;
        float m = h / 2f;
        float e = t * 0.55f;
        foreach (char s in lit)
        {
            switch (s)
            {
                case 'a': Rect(x + e, y, x + w - e, y + t, color); break;
                case 'g': Rect(x + e, y + m - (t / 2f), x + w - e, y + m + (t / 2f), color); break;
                case 'd': Rect(x + e, y + h - t, x + w - e, y + h, color); break;
                case 'f': Rect(x, y + e, x + t, y + m - (e * 0.4f), color); break;
                case 'b': Rect(x + w - t, y + e, x + w, y + m - (e * 0.4f), color); break;
                case 'e': Rect(x, y + m + (e * 0.4f), x + t, y + h - e, color); break;
                case 'c': Rect(x + w - t, y + m + (e * 0.4f), x + w, y + h - e, color); break;
            }
        }
    }

    private void Outline(float x0, float y0, float x1, float y1, float stroke, Color32 color)
    {
        Rect(x0, y0, x1, y0 + stroke, color);
        Rect(x0, y1 - stroke, x1, y1, color);
        Rect(x0, y0, x0 + stroke, y1, color);
        Rect(x1 - stroke, y0, x1, y1, color);
    }

    private void Rect(float x0, float y0, float x1, float y1, Color32 color)
    {
        int ax = Mathf.Max(0, Mathf.RoundToInt(x0));
        int bx = Mathf.Min(_width, Mathf.RoundToInt(x1));
        int ay = Mathf.Max(0, Mathf.RoundToInt(y0));
        int by = Mathf.Min(_height, Mathf.RoundToInt(y1));
        for (int y = ay; y < by; y++)
        {
            int row = (_height - 1 - y) * _width;
            for (int x = ax; x < bx; x++) _pixels[row + x] = color;
        }
    }

    private void Circle(float cx, float cy, float r, Color32 color)
    {
        for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
        {
            for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                if ((dx * dx) + (dy * dy) <= r * r) Plot(x, y, color);
            }
        }
    }

    private void Line(float x0, float y0, float x1, float y1, float stroke, Color32 color)
    {
        float half = stroke / 2f;
        var a = new Vector2(x0, y0);
        var b = new Vector2(x1, y1);
        Vector2 ab = b - a;
        float length = Mathf.Max(1e-4f, ab.sqrMagnitude);
        for (int y = Mathf.FloorToInt(Mathf.Min(y0, y1) - half); y <= Mathf.CeilToInt(Mathf.Max(y0, y1) + half); y++)
        {
            for (int x = Mathf.FloorToInt(Mathf.Min(x0, x1) - half); x <= Mathf.CeilToInt(Mathf.Max(x0, x1) + half); x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
                if ((a + (ab * t) - p).sqrMagnitude <= half * half) Plot(x, y, color);
            }
        }
    }

    private void Plot(int x, int y, Color32 color)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height) return;
        _pixels[((_height - 1 - y) * _width) + x] = color;
    }

    public void Dispose()
    {
        Object.Destroy(_texture);
        Object.Destroy(_material);
    }
}
