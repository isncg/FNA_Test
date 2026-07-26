using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MaterialLib;

/// <summary>Simple orbit camera with mouse drag and scroll zoom.</summary>
public class OrbitCamera
{
    public float Yaw;
    public float Pitch = 0.3f;
    public float Distance = 8f;
    public Vector3 Target = Vector3.Zero;
    public float MinDist = 2f, MaxDist = 40f;

    private Point _lastMouse;
    private int _lastScroll;
    private bool _dragging;

    /// <summary>Call once per frame. Returns false if mouse is over ImGui.</summary>
    public bool Update(bool allowInput)
    {
        var ms = Mouse.GetState();
        var scrollDelta = ms.ScrollWheelValue - _lastScroll;
        _lastScroll = ms.ScrollWheelValue;

        if (allowInput)
        {
            Distance -= scrollDelta * 0.005f;
            Distance = MathHelper.Clamp(Distance, MinDist, MaxDist);

            if (ms.LeftButton == ButtonState.Pressed)
            {
                if (!_dragging)
                {
                    _dragging = true;
                    _lastMouse = new Point(ms.X, ms.Y);
                }
                else
                {
                    float dx = ms.X - _lastMouse.X;
                    float dy = ms.Y - _lastMouse.Y;
                    Yaw -= dx * 0.005f;
                    Pitch -= dy * 0.005f;
                    Pitch = MathHelper.Clamp(Pitch, -MathHelper.PiOver2 + 0.01f, MathHelper.PiOver2 - 0.01f);
                    _lastMouse = new Point(ms.X, ms.Y);
                }
            }
            else
            {
                _dragging = false;
            }
        }

        return _dragging || allowInput;
    }

    public Matrix ViewMatrix
    {
        get
        {
            var eye = GetEyePosition();
            return Matrix.CreateLookAt(eye, Target, Vector3.Up);
        }
    }

    public Vector3 GetEyePosition()
    {
        float cosP = MathF.Cos(Pitch);
        float sinP = MathF.Sin(Pitch);
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);
        return new Vector3(
            Target.X + cosP * sinY * Distance,
            Target.Y + sinP * Distance,
            Target.Z + cosP * cosY * Distance
        );
    }
}
