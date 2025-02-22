﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    public static partial class ToolBox
    {
        // Convert an RGB value into an HLS value.
        public static Vector3 RgbToHLS(this Color color)
        {
            return RgbToHLS(color.ToVector3());
        }

        // Convert an HLS value into an RGB value.
        public static Color HLSToRGB(Vector3 hls)
        {
            double h = hls.X, l = hls.Y, s = hls.Z;

            double p2;
            if (l <= 0.5) p2 = l * (1 + s);
            else p2 = l + s - l * s;

            double p1 = 2 * l - p2;
            double double_r, double_g, double_b;
            if (s == 0)
            {
                double_r = l;
                double_g = l;
                double_b = l;
            }
            else
            {
                double_r = QqhToRgb(p1, p2, h + 120);
                double_g = QqhToRgb(p1, p2, h);
                double_b = QqhToRgb(p1, p2, h - 120);
            }

            // Convert RGB to the 0 to 255 range.
            return new Color((byte)(double_r * 255.0), (byte)(double_g * 255.0), (byte)(double_b * 255.0));
        }

        private static double QqhToRgb(double q1, double q2, double hue)
        {
            if (hue > 360) hue -= 360;
            else if (hue < 0) hue += 360;

            if (hue < 60) return q1 + (q2 - q1) * hue / 60;
            if (hue < 180) return q2;
            if (hue < 240) return q1 + (q2 - q1) * (240 - hue) / 60;
            return q1;
        }

        public static Color Add(this Color sourceColor, Color color)
        {
            return new Color(
                sourceColor.R + color.R,
                sourceColor.G + color.G,
                sourceColor.B + color.B,
                sourceColor.A + color.A);
        }

        public static Color Subtract(this Color sourceColor, Color color)
        {
            return new Color(
                sourceColor.R - color.R,
                sourceColor.G - color.G,
                sourceColor.B - color.B,
                sourceColor.A - color.A);
        }

        public static string LimitString(string str, ScalableFont font, int maxWidth)
        {
            if (maxWidth <= 0 || string.IsNullOrWhiteSpace(str)) return "";

            float currWidth = font.MeasureString("...").X;
            for (int i = 0; i < str.Length; i++)
            {
                currWidth += font.MeasureString(str[i].ToString()).X;

                if (currWidth > maxWidth)
                {
                    return str.Substring(0, Math.Max(i - 2, 1)) + "...";
                }
            }

            return str;
        }
        
        public static Color GradientLerp(float t, params Color[] gradient)
        {
            if (t <= 0.0f) return gradient[0];
            if (t >= 1.0f) return gradient[gradient.Length - 1];

            float scaledT = t * (gradient.Length - 1);

            return Color.Lerp(gradient[(int)scaledT], gradient[(int)Math.Min(scaledT + 1, gradient.Length - 1)], (scaledT - (int)scaledT));
        }

        public static string WrapText(string text, float lineLength, ScalableFont font, float textScale = 1.0f) //TODO: could integrate this into the ScalableFont class directly
        {
            Vector2 textSize = font.MeasureString(text);
            if (textSize.X < lineLength) { return text; }

            text = text.Replace("\n", " \n ");

            List<string> words = new List<string>();
            string currWord = "";
            for (int i = 0; i < text.Length; i++)
            {
                if (TextManager.IsCJK(text[i].ToString()))
                {
                    if (currWord.Length > 0)
                    {
                        words.Add(currWord);
                        currWord = "";
                    }
                    words.Add(text[i].ToString());
                }
                else if (text[i] == ' ')
                {
                    if (currWord.Length > 0)
                    {
                        words.Add(currWord);
                        currWord = "";
                    }
                }
                else
                {
                    currWord += text[i];
                }
            }
            if (currWord.Length > 0)
            {
                words.Add(currWord);
                currWord = "";
            }

            StringBuilder wrappedText = new StringBuilder();
            float linePos = 0f;
            Vector2 spaceSize = font.MeasureString(" ") * textScale;
            for (int i = 0; i < words.Count; ++i)
            {
                if (words[i].Length == 0)
                {
                    //space
                }
                else if (string.IsNullOrWhiteSpace(words[i]) && words[i] != "\n")
                {
                    continue;
                }

                Vector2 size = words[i].Length == 0 ? spaceSize : font.MeasureString(words[i]) * textScale;
                if (size.X > lineLength)
                {
                    if (linePos == 0.0f)
                    {
                        wrappedText.AppendLine(words[i]);
                    }
                    else
                    {
                        do
                        {
                            if (words[i].Length == 0) break;

                            wrappedText.Append(words[i][0]);
                            words[i] = words[i].Remove(0, 1);

                            linePos += size.X;
                        } while (words[i].Length > 0 && (size = font.MeasureString((words[i][0]).ToString()) * textScale).X + linePos < lineLength);

                        wrappedText.Append("\n");
                        linePos = 0.0f;
                        i--;
                    }

                    continue;
                }

                if (linePos + size.X < lineLength)
                {
                    wrappedText.Append(words[i]);
                    if (words[i] == "\n")
                    {
                        linePos = 0.0f;
                    }
                    else
                    {
                        linePos += size.X + spaceSize.X;
                    }
                }
                else
                {
                    wrappedText.Append("\n");
                    wrappedText.Append(words[i]);

                    linePos = size.X + spaceSize.X;
                }

                if (i < words.Count - 1 && !TextManager.IsCJK(words[i]) && !TextManager.IsCJK(words[i + 1]))
                {
                    wrappedText.Append(" ");
                }
            }

            return wrappedText.ToString().Replace(" \n ", "\n");
        }     
    }
}
