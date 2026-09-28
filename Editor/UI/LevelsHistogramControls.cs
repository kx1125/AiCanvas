using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal enum HistogramChannel { RGB, Luminance }

    internal static class LevelsHandles
    {
        internal const float MinimumGap = 0.0001f;
        internal static float Position(LevelsSettings value, int handle) => handle switch
        {
            0 => value.inputBlack,
            1 => Mathf.Lerp(value.inputBlack, value.inputWhite, Mathf.Pow(0.5f, value.gamma)),
            2 => value.inputWhite,
            3 => value.outputBlack,
            _ => value.outputWhite
        };

        internal static float Parameter(LevelsSettings value, int handle, float position)
        {
            if (handle != 1) return Clamp(value, handle, position);
            float t = Mathf.InverseLerp(value.inputBlack, value.inputWhite, position);
            return Mathf.Clamp(Mathf.Log(Mathf.Clamp(t, Mathf.Pow(0.5f, 10f), Mathf.Pow(0.5f, 0.01f))) / Mathf.Log(0.5f), 0.01f, 10f);
        }

        internal static float Clamp(LevelsSettings value, int handle, float number)
        {
            if (!float.IsFinite(number)) return Read(value, handle);
            return handle switch
            {
                0 => Mathf.Clamp(number, 0, Mathf.Max(0, value.inputWhite - MinimumGap)),
                1 => Mathf.Clamp(number, 0.01f, 10f),
                2 => Mathf.Clamp(number, Mathf.Min(1, value.inputBlack + MinimumGap), 1),
                _ => Mathf.Clamp01(number)
            };
        }

        internal static float Read(LevelsSettings value, int handle) => handle switch
        {
            0 => value.inputBlack, 1 => value.gamma, 2 => value.inputWhite,
            3 => value.outputBlack, _ => value.outputWhite
        };
    }

    internal sealed class HistogramPlot : VisualElement
    {
        internal ImageHistogramData Data;
        internal HistogramChannel Channel;
        private static readonly Color[] Colors = { new(1, 0.3f, 0.28f), new(0.35f, 0.9f, 0.45f), new(0.3f, 0.55f, 1), new(0.86f, 0.88f, 0.9f) };
        internal HistogramPlot()
        {
            name = "levels-histogram-plot"; style.height = 112;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            HistogramPaint.Rectangle(painter, contentRect, new Color(0.065f, 0.075f, 0.085f));
            var rect = new Rect(8, 0, Mathf.Max(1, contentRect.width - 16), contentRect.height);
            painter.strokeColor = new Color(0.19f, 0.21f, 0.24f); painter.lineWidth = 1;
            for (int i = 1; i < 4; i++)
            {
                painter.BeginPath(); painter.MoveTo(new Vector2(rect.x + rect.width * i / 4, 0));
                painter.LineTo(new Vector2(rect.x + rect.width * i / 4, rect.height)); painter.Stroke();
            }
            if (Data == null) return;
            int first = Channel == HistogramChannel.Luminance ? 3 : 0;
            int end = Channel == HistogramChannel.Luminance ? 4 : 3;
            uint maximum = 1;
            for (int i = first * 256; i < end * 256; i++) maximum = Math.Max(maximum, Data.Bins[i]);
            for (int channel = first; channel < end; channel++)
            {
                var color = Colors[channel];
                painter.BeginPath(); painter.MoveTo(new Vector2(rect.x, rect.height));
                for (int i = 0; i < 256; i++)
                    painter.LineTo(new Vector2(rect.x + rect.width * i / 255, rect.height - (rect.height - 4) * Data.Bins[channel * 256 + i] / maximum));
                painter.LineTo(new Vector2(rect.xMax, rect.height)); painter.ClosePath();
                painter.fillColor = new Color(color.r, color.g, color.b, 0.25f); painter.Fill();
                painter.strokeColor = new Color(color.r, color.g, color.b, 0.85f); painter.lineWidth = 1; painter.Stroke();
            }
        }
    }

    internal sealed class LevelsHandleBar : VisualElement
    {
        private readonly bool input;
        private readonly Func<LevelsSettings> read;
        private readonly Action<int, float> change;
        private readonly Action begin, end;
        private int activePointer = -1, selected;
        internal LevelsHandleBar(bool input, Func<LevelsSettings> read, Action<int, float> change, Action begin, Action end)
        {
            this.input = input; this.read = read; this.change = change; this.begin = begin; this.end = end;
            selected = input ? 1 : 3;
            name = input ? "levels-input-handles" : "levels-output-handles";
            tooltip = input ? "拖动黑 / 灰 / 白手柄调整输入范围和 Gamma。方向键微调，Shift 更精细。" : "拖动输出黑白手柄；允许交叉以反转。";
            style.height = 26; focusable = true;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || activePointer >= 0) return;
                var position = (Vector2)evt.localPosition;
                float best = float.MaxValue;
                for (int i = input ? 0 : 3; i < (input ? 3 : 5); i++)
                {
                    float distance = (HandlePoint(i) - position).sqrMagnitude;
                    if (distance < best) { best = distance; selected = i; }
                }
                activePointer = evt.pointerId; Focus(); this.CapturePointer(activePointer); begin();
                Move(position.x); evt.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != activePointer) return;
                Move(evt.localPosition.x); evt.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != activePointer) return;
                Move(evt.localPosition.x); Finish(); evt.StopPropagation();
            });
            RegisterCallback<PointerCancelEvent>(evt => { if (evt.pointerId == activePointer) Finish(); });
            RegisterCallback<PointerCaptureOutEvent>(evt => { if (evt.pointerId == activePointer) Finish(); });
            RegisterCallback<DetachFromPanelEvent>(_ => Finish());
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow) return;
                float step = evt.shiftKey ? 0.0005f : 0.005f;
                float position = LevelsHandles.Position(read(), selected) + (evt.keyCode == KeyCode.LeftArrow ? -step : step);
                begin(); change(selected, LevelsHandles.Parameter(read(), selected, position)); end();
                evt.StopPropagation();
            });
        }
        internal Vector2 HandlePoint(int handle) => new(
            8 + Mathf.Clamp01(LevelsHandles.Position(read(), handle)) * Mathf.Max(1, contentRect.width - 16),
            input ? 10 : 17);
        private void Move(float x) => change(selected, LevelsHandles.Parameter(read(), selected,
            Mathf.Clamp01((x - 8) / Mathf.Max(1, contentRect.width - 16))));
        private void Finish()
        {
            if (activePointer < 0) return;
            int pointer = activePointer; activePointer = -1;
            if (this.HasPointerCapture(pointer)) this.ReleasePointer(pointer);
            end();
        }
        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            for (int i = 0; i < 64; i++)
                HistogramPaint.Rectangle(painter, new Rect(8 + (contentRect.width - 16) * i / 64, input ? 19 : 1,
                    (contentRect.width - 16) / 64 + 0.5f, 6), Color.Lerp(Color.black, Color.white, i / 63f));
            for (int i = input ? 0 : 3; i < (input ? 3 : 5); i++)
            {
                var point = HandlePoint(i);
                var color = i == 0 || i == 3 ? new Color(0.12f, 0.12f, 0.12f) : i == 1 ? Color.gray : Color.white;
                float direction = input ? 1 : -1;
                painter.BeginPath(); painter.MoveTo(new Vector2(point.x, point.y + direction * 7));
                painter.LineTo(new Vector2(point.x - 6, point.y - direction * 4));
                painter.LineTo(new Vector2(point.x + 6, point.y - direction * 4)); painter.ClosePath();
                painter.fillColor = color; painter.Fill();
                painter.strokeColor = i == selected ? new Color(0.35f, 0.75f, 1) : new Color(0.6f, 0.65f, 0.7f);
                painter.lineWidth = 1; painter.Stroke();
            }
        }
    }

    internal static class HistogramPaint
    {
        internal static void Rectangle(Painter2D painter, Rect rect, Color color)
        {
            painter.BeginPath(); painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin)); painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax)); painter.ClosePath();
            painter.fillColor = color; painter.Fill();
        }
    }
}
