using System;
using System.Globalization;
using UnityEngine;
using Operator.Recording;

namespace Operator;

internal sealed partial class VideoCameraItem
{
    private const string OsdGlyphOrder = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:./-+% ";

    // 5x7 seven bytes glyph, low bit is right most column, same order as OsdGlyphOrder
    // yep, its three hundred magic numbers, its a font
    // add extra character and you add seven bytes in same place or whole thing shifts
    private static readonly byte[] OsdFontRows =
    {
        0x0E,0x11,0x13,0x15,0x19,0x11,0x0E,
        0x04,0x0C,0x04,0x04,0x04,0x04,0x0E,
        0x0E,0x11,0x01,0x02,0x04,0x08,0x1F,
        0x1F,0x02,0x04,0x02,0x01,0x11,0x0E,
        0x02,0x06,0x0A,0x12,0x1F,0x02,0x02,
        0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E,
        0x06,0x08,0x10,0x1E,0x11,0x11,0x0E,
        0x1F,0x01,0x02,0x04,0x08,0x08,0x08,
        0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E,
        0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C,
        0x0E,0x11,0x11,0x1F,0x11,0x11,0x11,
        0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E,
        0x0E,0x11,0x10,0x10,0x10,0x11,0x0E,
        0x1C,0x12,0x11,0x11,0x11,0x12,0x1C,
        0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F,
        0x1F,0x10,0x10,0x1E,0x10,0x10,0x10,
        0x0E,0x11,0x10,0x17,0x11,0x11,0x0F,
        0x11,0x11,0x11,0x1F,0x11,0x11,0x11,
        0x0E,0x04,0x04,0x04,0x04,0x04,0x0E,
        0x07,0x02,0x02,0x02,0x02,0x12,0x0C,
        0x11,0x12,0x14,0x18,0x14,0x12,0x11,
        0x10,0x10,0x10,0x10,0x10,0x10,0x1F,
        0x11,0x1B,0x15,0x15,0x11,0x11,0x11,
        0x11,0x11,0x19,0x15,0x13,0x11,0x11,
        0x0E,0x11,0x11,0x11,0x11,0x11,0x0E,
        0x1E,0x11,0x11,0x1E,0x10,0x10,0x10,
        0x0E,0x11,0x11,0x11,0x15,0x12,0x0D,
        0x1E,0x11,0x11,0x1E,0x14,0x12,0x11,
        0x0F,0x10,0x10,0x0E,0x01,0x01,0x1E,
        0x1F,0x04,0x04,0x04,0x04,0x04,0x04,
        0x11,0x11,0x11,0x11,0x11,0x11,0x0E,
        0x11,0x11,0x11,0x11,0x11,0x0A,0x04,
        0x11,0x11,0x11,0x15,0x15,0x15,0x0A,
        0x11,0x11,0x0A,0x04,0x0A,0x11,0x11,
        0x11,0x11,0x0A,0x04,0x04,0x04,0x04,
        0x1F,0x01,0x02,0x04,0x08,0x10,0x1F,
        0x00,0x0C,0x0C,0x00,0x0C,0x0C,0x00,
        0x00,0x00,0x00,0x00,0x00,0x0C,0x0C,
        0x01,0x02,0x02,0x04,0x08,0x08,0x10,
        0x00,0x00,0x00,0x0E,0x00,0x00,0x00,
        0x00,0x04,0x04,0x1F,0x04,0x04,0x00,
        0x19,0x1A,0x02,0x04,0x08,0x0B,0x13,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,
    };

    private static Texture2D? _osdAtlas;
    private static float _osdAtlasW;

    private double _osdLastDrawTime;
    private float _osdCh1;
    private float _osdCh2;
    private float _osdTargetH;
    private double _osdZoomShowUntil;
    private float _osdLastFov;

    private static void CreateOsdAtlas()
    {
        if (_osdAtlas != null) return;

        int glyphCount = OsdGlyphOrder.Length;
        int width = (glyphCount + 1) * 6;
        int height = 8;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "VC_OsdFont",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var px = new Color32[width * height];
        var white = new Color32(255, 255, 255, 255);
        for (int g = 0; g < glyphCount; g++)
        {
            for (int row = 0; row < 7; row++)
            {
                byte bits = OsdFontRows[g * 7 + row];
                int y = 6 - row;
                for (int col = 0; col < 5; col++)
                {
                    if ((bits & (0x10 >> col)) != 0)
                    {
                        px[y * width + g * 6 + col] = white;
                    }
                }
            }
        }
        int solidX = glyphCount * 6;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                px[y * width + solidX + x] = white;
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        _osdAtlas = tex;
        _osdAtlasW = width;
    }

    private static Color OsdTint(Color c)
    {
        return new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * Constants.Osd.Alpha * 0.5f);
    }

    private void OsdGlyph(char c, float x, float y, float s, Color tinted)
    {
        int idx = OsdGlyphOrder.IndexOf(char.ToUpperInvariant(c));
        if (idx < 0) idx = OsdGlyphOrder.Length - 1;

        float u = idx * 6f / _osdAtlasW;
        Rect src = new(u, 0f, 5f / _osdAtlasW, 7f / 8f);
        Rect dst = new(x, y, 5f * s, 7f * s);
        if (Constants.Osd.FlipVertical)
        {
            dst.y = _osdTargetH - y - dst.height;
            src.y = 7f / 8f;
            src.height = -7f / 8f;
        }
        Graphics.DrawTexture(dst, _osdAtlas, src, 0, 0, 0, 0, tinted);
    }

    private void OsdText(string text, float x, float y, float s, Color color)
    {
        Color tinted = OsdTint(color);
        for (int i = 0; i < text.Length; i++)
        {
            OsdGlyph(text[i], x + i * 6f * s, y, s, tinted);
        }
    }

    private static float OsdTextWidth(string text, float s) => text.Length > 0 ? (text.Length * 6f - 1f) * s : 0f;

    private void OsdRect(float x, float y, float w, float h, Color color)
    {
        int glyphCount = OsdGlyphOrder.Length;
        Rect src = new((glyphCount * 6f + 1.5f) / _osdAtlasW, 2f / 8f, 3f / _osdAtlasW, 4f / 8f);
        Rect dst = new(x, y, w, h);
        if (Constants.Osd.FlipVertical)
        {
            dst.y = _osdTargetH - y - h;
        }
        Graphics.DrawTexture(dst, _osdAtlas, src, 0, 0, 0, 0, OsdTint(color));
    }

    private void OsdDot(float x, float y, float d, Color color)
    {
        OsdRect(x + d * 0.25f, y, d * 0.5f, d, color);
        OsdRect(x, y + d * 0.25f, d, d * 0.5f, color);
        OsdRect(x + d * 0.125f, y + d * 0.125f, d * 0.75f, d * 0.75f, color);
    }

    private void OsdCornerBrackets(float w, float h, float inset, float len, float thick, Color color)
    {
        OsdRect(inset, inset, len, thick, color);
        OsdRect(inset, inset, thick, len, color);
        OsdRect(w - inset - len, inset, len, thick, color);
        OsdRect(w - inset - thick, inset, thick, len, color);
        OsdRect(inset, h - inset - thick, len, thick, color);
        OsdRect(inset, h - inset - len, thick, len, color);
        OsdRect(w - inset - len, h - inset - thick, len, thick, color);
        OsdRect(w - inset - thick, h - inset - len, thick, len, color);
    }

    private void OsdBattery(float x, float y, float s, float charge, Color outline)
    {
        float bw = 16f * s;
        float bh = 8f * s;
        float t = 1f * s;
        OsdRect(x, y, bw, t, outline);
        OsdRect(x, y + bh - t, bw, t, outline);
        OsdRect(x, y, t, bh, outline);
        OsdRect(x + bw - t, y, t, bh, outline);
        OsdRect(x + bw, y + bh * 0.3f, 1.5f * s, bh * 0.4f, outline);

        Color fill = charge > 0.2f ? Constants.Osd.BatteryFill : Constants.Osd.Red;
        float inner = bw - 4f * s;
        float fw = inner * Mathf.Clamp01(charge);
        if (fw > 0.5f)
        {
            OsdRect(x + 2f * s, y + 2f * s, fw, bh - 4f * s, fill);
        }
    }

    private static float MeterLevel(float peak)
    {
        if (peak <= 0.0001f) return 0f;
        float decibels = 20f * Mathf.Log10(peak);
        return Mathf.Clamp01(1f + decibels / Constants.Osd.MeterFloor);
    }

    private void OsdMeter(string label, float level, float x, float y, float s)
    {
        OsdText(label, x, y, s, Constants.Osd.White);
        float tx = x + OsdTextWidth(label, s) + 3f * s;
        float ty = y + 1.5f * s;
        float th = 4f * s;
        int blocks = 11;
        float bw = 2f * s;
        float gap = 1f * s;
        int lit = Mathf.RoundToInt(Mathf.Clamp01(level) * blocks);
        for (int i = 0; i < blocks; i++)
        {
            OsdRect(tx + i * (bw + gap), ty, bw, th, i < lit ? Constants.Osd.MeterFill : Constants.Osd.TrackBack);
        }
    }

    private void OsdCassette(float x, float y, float s, Color color)
    {
        float cw = 12f * s;
        float ch = 7f * s;
        float t = 1f * s;
        OsdRect(x, y, cw, t, color);
        OsdRect(x, y + ch - t, cw, t, color);
        OsdRect(x, y, t, ch, color);
        OsdRect(x + cw - t, y, t, ch, color);
        OsdRect(x + 3f * s, y + 2.5f * s, 2f * s, 2f * s, color);
        OsdRect(x + 7f * s, y + 2.5f * s, 2f * s, 2f * s, color);
    }

    private void OsdNightshot(float x, float y, float s)
    {
        if (_nvAmount <= 0.5f) return;
        OsdText("NIGHTSHOT", x, y, s, Constants.Osd.NightshotGreen);
    }

    private void OsdTapeInfo(float xRight, float y, float s, bool recOrPaused, double recSeconds, bool blinkOn)
    {
        string txt;
        Color color;
        bool icon = true;
        if (recOrPaused)
        {
            int maxMin = VideoCameraConfig.MaxMinutes.Value;
            int minsLeft = Mathf.Max(0, Mathf.CeilToInt((float)(maxMin * 60.0 - recSeconds) / 60f));
            txt = $"{minsLeft}MIN";
            color = minsLeft <= Constants.Osd.TapeLowMinutes ? Constants.Osd.Red : Constants.Osd.White;
        }
        else
        {
            int blanks = _blanksRemaining.Value;
            if (blanks <= 0)
            {
                if (!blinkOn) return;
                txt = "TAPE END";
                color = Constants.Osd.Red;
                icon = false;
            }
            else
            {
                txt = $"X{blanks}";
                color = Constants.Osd.White;
            }
        }

        float iconW = icon ? 12f * s + 3f * s : 0f;
        float x = xRight - OsdTextWidth(txt, s) - iconW;
        if (icon) OsdCassette(x, y, s, color);
        OsdText(txt, x + iconW, y, s, color);
    }

    private void OsdZoomBar(float w, float s, float y)
    {
        float maxFov = Mathf.Max(VideoCameraConfig.MaxFov.Value, VideoCameraConfig.MinFov.Value + 1f);
        float minFov = VideoCameraConfig.MinFov.Value;
        float z01 = Mathf.InverseLerp(maxFov, minFov, _currentFov);
        float mult = Mathf.Max(1f, maxFov / Mathf.Max(1f, _currentFov));
        string mtxt = $"X{mult:0.0}";

        float barW = 40f * s;
        float total = OsdTextWidth("W", s) + 4f * s + barW + 4f * s + OsdTextWidth("T", s) + 6f * s + OsdTextWidth(mtxt, s);
        float x = (w - total) * 0.5f;

        OsdText("W", x, y, s, Constants.Osd.White);
        x += OsdTextWidth("W", s) + 4f * s;
        OsdRect(x, y + 2.5f * s, barW, 2f * s, Constants.Osd.TrackBack);
        OsdRect(x + z01 * (barW - 2f * s), y, 2f * s, 7f * s, Constants.Osd.White);
        x += barW + 4f * s;
        OsdText("T", x, y, s, Constants.Osd.White);
        x += OsdTextWidth("T", s) + 6f * s;
        OsdText(mtxt, x, y, s, Constants.Osd.White);
    }

    private bool _afSoft;
    private float _afSharpSince = -1f;

    private void OsdAfReticle(float w, float h, float s)
    {
        CameraFocusMode focusMode = VideoCameraConfig.FocusMode.Value;
        if (focusMode == CameraFocusMode.Off) return;
        bool manual = focusMode == CameraFocusMode.Manual;
        float now = Time.unscaledTime;
        if (_afBlur > Constants.Osd.AfReticleThreshold)
        {
            _afSoft = true;
            _afSharpSince = -1f;
        }
        else if (_afBlur < Constants.Osd.AfReticleRelease)
        {
            if (_afSharpSince < 0f) _afSharpSince = now;
            if (now - _afSharpSince >= Constants.Osd.AfReticleHold) _afSoft = false;
        }
        else
        {
            _afSharpSince = -1f;
        }
        if (!manual && !_afSoft) return;

        bool sharp = manual && !_afSoft;
        float cx = w * 0.5f;
        float cy = h * 0.5f;
        float half = Constants.Osd.AfReticleHalf * s;
        float len = 5f * s;
        float t = 1f * s;
        Color c = sharp ? Constants.Osd.FocusGreen : Constants.Osd.White;
        OsdRect(cx - half, cy - half, len, t, c);
        OsdRect(cx - half, cy - half, t, len, c);
        OsdRect(cx + half - len, cy - half, len, t, c);
        OsdRect(cx + half - t, cy - half, t, len, c);
        OsdRect(cx - half, cy + half - t, len, t, c);
        OsdRect(cx - half, cy + half - len, t, len, c);
        OsdRect(cx + half - len, cy + half - t, len, t, c);
        OsdRect(cx + half - t, cy + half - len, t, len, c);
        if (!manual || sharp) OsdRect(cx - 1f * s, cy - 1f * s, 2f * s, 2f * s, c);
    }

    private void OsdLowBattery(float w, float h, float s, float charge, bool blinkOn)
    {
        if (charge > Constants.Osd.LowBatteryLevel) return;
        if (!blinkOn) return;

        float bs = 2f * s;
        float iconW = 16f * bs + 1.5f * bs;
        float x = (w - iconW) * 0.5f;
        float textOffset = 8f * bs + 3f * s;
        float blockH = textOffset + 7f * s;
        float y = (h - blockH) * 0.5f;
        OsdBattery(x, y, bs, charge, Constants.Osd.White);
        string txt = "LOW BATTERY";
        OsdText(txt, (w - OsdTextWidth(txt, s)) * 0.5f, y + textOffset, s, Constants.Osd.Red);
    }

    private static string OsdFormatTimecode(double seconds, int fps)
    {
        if (seconds < 0) seconds = 0;
        int total = (int)seconds;
        int ff = (int)((seconds - total) * fps);
        int hh = total / 3600;
        int mm = total / 60 % 60;
        int ss = total % 60;
        return $"{hh:00}:{mm:00}:{ss:00}:{ff:00}";
    }

    private void DrawFeedOsd()
    {
        CameraOverlayStyle style = VideoCameraConfig.Overlay.Value;
        if (style == CameraOverlayStyle.Off) return;
        RenderTexture? rt = _feedTexture;
        if (rt == null) return;
        CreateOsdAtlas();
        if (_osdAtlas == null) return;

        float w = rt.width;
        float h = rt.height;
        _osdTargetH = h;
        float s = Mathf.Max(1f, Mathf.Round(h / Constants.Osd.ReferenceHeight));

        bool rec = _recordingState.Value;
        double now = Time.unscaledTimeAsDouble;

        float dt = Mathf.Clamp((float)(now - _osdLastDrawTime), 0f, 0.25f);
        _osdLastDrawTime = now;

        if (style == CameraOverlayStyle.Pro)
        {
            float gamePeak = Operator.Recording.AudioCapture.RecordedPeak01;
            Operator.Recording.AudioCapture.RecordedPeak01 = 0f;
            float micPeak = Operator.Recording.Voice.MicCapture.RecordedPeak01;
            Operator.Recording.Voice.MicCapture.RecordedPeak01 = 0f;
            float fall = dt * Constants.Osd.MeterFallSpeed;
            _osdCh1 = Mathf.Max(MeterLevel(gamePeak), _osdCh1 - fall);
            _osdCh2 = Mathf.Max(MeterLevel(micPeak), _osdCh2 - fall);
        }

        bool paused = IsPaused();
        bool blinkOn = (int)(now * 2.0 * Constants.Osd.RecBlinkRate) % 2 == 0;
        float charge = insertedBattery?.charge ?? 0f;
        int fps = VideoCameraConfig.GetRecordingFps();
        double recSeconds = GetTapeTime();

        RenderTexture? prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, w, h, 0f);
        try
        {
            switch (style)
            {
                case CameraOverlayStyle.Minimal:
                    DrawOsdMinimal(w, h, s, rec, paused, blinkOn, charge);
                    break;
                case CameraOverlayStyle.Classic:
                    DrawOsdClassic(w, h, s, rec, paused, blinkOn, charge, recSeconds, fps);
                    break;
                case CameraOverlayStyle.Vhs:
                    DrawOsdVhs(w, h, s, rec, paused, blinkOn, recSeconds, fps);
                    break;
                case CameraOverlayStyle.Pro:
                    DrawOsdPro(w, h, s, rec, paused, blinkOn, charge, recSeconds, fps);
                    break;
            }

            if (Mathf.Abs(_currentFov - _osdLastFov) > 0.01f)
            {
                _osdZoomShowUntil = now + Constants.Osd.ZoomShowTime;
                _osdLastFov = _currentFov;
            }
            if (now < _osdZoomShowUntil)
            {
                float zoomY = style switch
                {
                    CameraOverlayStyle.Vhs => 12f * s,
                    CameraOverlayStyle.Classic => h - 34f * s,
                    _ => h - 30f * s,
                };
                OsdZoomBar(w, s, zoomY);
            }
            if (style == CameraOverlayStyle.Pro || style == CameraOverlayStyle.Classic
                || VideoCameraConfig.FocusMode.Value == CameraFocusMode.Manual)
            {
                OsdAfReticle(w, h, s);
            }
            OsdLowBattery(w, h, s, charge, blinkOn);
        }
        finally
        {
            GL.PopMatrix();
            RenderTexture.active = prev;
        }
    }

    private void DrawOsdMinimal(float w, float h, float s, bool rec, bool paused, bool blinkOn, float charge)
    {
        float m = 8f * s;
        OsdCornerBrackets(w, h, m, 12f * s, 1f * s, Constants.Osd.White);

        float x = m + 4f * s;
        float y = m + 4f * s;
        if (rec)
        {
            if (blinkOn) OsdDot(x, y + 1f * s, 5f * s, Constants.Osd.Red);
            OsdText("REC", x + 8f * s, y, s, Constants.Osd.White);
        }
        else
        {
            OsdText(paused ? "PAUSE" : "STBY", x, y, s, paused ? Constants.Osd.Amber : Constants.Osd.Grey);
        }
        OsdNightshot(x, y + 10f * s, s);

        OsdBattery(w - m - 22f * s, y, s * 0.8f, charge, Constants.Osd.White);
    }

    private void DrawOsdClassic(float w, float h, float s, bool rec, bool paused, bool blinkOn, float charge, double recSeconds, int fps)
    {
        float m = 6f * s;
        OsdCornerBrackets(w, h, m, 18f * s, 2f * s, Constants.Osd.White);

        float ix = m + 8f * s;
        float iy = m + 8f * s;
        if (rec)
        {
            OsdText("REC", ix, iy, s, Constants.Osd.White);
            if (blinkOn) OsdDot(ix + OsdTextWidth("REC", s) + 4f * s, iy + 1f * s, 5f * s, Constants.Osd.Red);
        }
        else
        {
            OsdText(paused ? "PAUSE" : "STBY", ix, iy, s, paused ? Constants.Osd.Amber : Constants.Osd.White);
        }
        OsdNightshot(ix, iy + 10f * s, s);

        OsdBattery(w - m - 28f * s, iy, s, charge, Constants.Osd.White);
        OsdTapeInfo(w - m - 8f * s, iy + 12f * s, s, rec || paused, recSeconds, blinkOn);

        float by = h - m - 8f * s - 7f * s;
        OsdText("ISO 200", ix, by, s, Constants.Osd.White);
        string tc = OsdFormatTimecode(recSeconds, fps);
        OsdText(tc, (w - OsdTextWidth(tc, s)) * 0.5f, by, s, Constants.Osd.White);
        OsdText("1/50", w - m - 8f * s - OsdTextWidth("1/50", s), by, s, Constants.Osd.White);
    }

    private void DrawOsdVhs(float w, float h, float s, bool rec, bool paused, bool blinkOn, double recSeconds, int fps)
    {
        float s2 = s * 2f;
        float m = 10f * s;
        if (rec)
        {
            OsdText("REC", m, m, s2, Constants.Osd.Red);
            if (blinkOn) OsdDot(m + OsdTextWidth("REC", s2) + 6f * s, m + 2f * s, 8f * s, Constants.Osd.Red);
        }
        else
        {
            OsdText(paused ? "PAUSE" : "STBY", m, m, s2, Constants.Osd.White);
        }
        OsdNightshot(m, m + 9f * s2, s);

        string day = OsdGameDay();
        if (!string.IsNullOrEmpty(day))
        {
            OsdText(day, w - m - OsdTextWidth(day, s), m, s, Constants.Osd.White);
        }

        if (rec || paused)
        {
            string tc = OsdFormatTimecode(recSeconds, fps);
            OsdText(tc, m, h - m - 7f * s, s, Constants.Osd.White);
        }

        string date = OsdVhsDate();
        string clock = OsdGameClock();
        float ly = h - m - 7f * s2;
        if (!string.IsNullOrEmpty(date))
        {
            OsdText(date, w - m - OsdTextWidth(date, s2), ly, s2, Constants.Osd.White);
            ly -= 9f * s2;
        }
        if (!string.IsNullOrEmpty(clock))
        {
            OsdText(clock, w - m - OsdTextWidth(clock, s2), ly, s2, Constants.Osd.White);
        }
    }

    private void DrawOsdPro(float w, float h, float s, bool rec, bool paused, bool blinkOn, float charge, double recSeconds, int fps)
    {
        float m = 8f * s;

        OsdText("P", m, m, s, Constants.Osd.White);
        string fmt = VideoCameraConfig.RecordingResolution.Value switch
        {
            RecordingResolutionPreset.High_860x520 => "SP",
            RecordingResolutionPreset.Medium_640x360 => "SP",
            _ => "LP",
        };
        OsdText(fmt, (w - OsdTextWidth(fmt, s)) * 0.5f, m, s, Constants.Osd.White);

        float battW = 16f * s + 1.5f * s;
        int remaining = BatteryMinutesRemaining();
        string mins = remaining < 0
            ? $"{Constants.Osd.BatteryMinutesUnknown}MIN"
            : $"{remaining}MIN";
        float px = w - m - OsdTextWidth(mins, s);
        OsdText(mins, px, m, s, Constants.Osd.White);
        OsdBattery(px - battW - 3f * s, m, s, charge, Constants.Osd.White);

        string? afLabel = VideoCameraConfig.FocusMode.Value switch
        {
            CameraFocusMode.Auto => "AF-C",
            CameraFocusMode.Manual => "MF",
            _ => null,
        };
        if (afLabel != null) OsdText(afLabel, m, h * 0.30f, s, Constants.Osd.White);
        OsdNightshot(m, h * 0.30f + 10f * s, s);
        OsdText("AWB", w - m - OsdTextWidth("AWB", s), h * 0.30f, s, Constants.Osd.White);

        float my = h * 0.44f;
        OsdMeter("CH1", _osdCh1, m, my, s);
        OsdMeter("CH2", _osdCh2, m, my + 9f * s, s);
        OsdTapeInfo(w - m, my, s, rec || paused, recSeconds, blinkOn);

        float ry = h * 0.62f;
        if (rec)
        {
            if (blinkOn) OsdDot(m, ry + 1f * s, 5f * s, Constants.Osd.Red);
            string line = "REC " + OsdFormatTimecode(recSeconds, fps);
            OsdText(line, m + 8f * s, ry, s, Constants.Osd.Red);
        }
        else if (paused)
        {
            OsdText("PAUSE " + OsdFormatTimecode(recSeconds, fps), m + 8f * s, ry, s, Constants.Osd.Amber);
        }
        else
        {
            OsdText("STBY", m + 8f * s, ry, s, Constants.Osd.Grey);
        }

        string bottom = "1/60  F2.2  +0.0  ISO AUTO";
        OsdText(bottom, (w - OsdTextWidth(bottom, s)) * 0.5f, h - m - 7f * s, s, Constants.Osd.White);
    }

    private static string OsdGameClock()
    {
        TimeOfDay? tod = TimeOfDay.Instance;
        if (tod == null) return string.Empty;
        int totalMin = (int)(tod.normalizedTimeOfDay * 60f * tod.numberOfHours) + 360;
        int hr24 = totalMin / 60;
        int min = totalMin % 60;
        string ap = hr24 < 12 || hr24 >= 24 ? "AM" : "PM";
        int hr = hr24 % 12;
        if (hr == 0) hr = 12;
        return $"{ap} {hr}:{min:00}";
    }

    private static string OsdGameDay()
    {
        var sor = StartOfRound.Instance;
        if (sor == null || sor.gameStats == null) return string.Empty;
        return $"DAY {sor.gameStats.daysSpent + 1}";
    }

    private static string OsdVhsDate()
    {
        var sor = StartOfRound.Instance;
        int days = sor != null && sor.gameStats != null ? sor.gameStats.daysSpent : 0;
        DateTime d = new DateTime(1986, 1, 1).AddDays(days);
        return d.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant() + $".{d.Day:00} {d.Year}";
    }

}
