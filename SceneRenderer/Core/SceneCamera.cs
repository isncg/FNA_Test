using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace SceneRenderer;

/// <summary>Orbit camera with frustum culling support.</summary>
public class SceneCamera
{
    public float Yaw;
    public float Pitch = 0.3f;
    public float Distance = 8f;
    public Vector3 Target = Vector3.Zero;
    public float MinDist = 2f, MaxDist = 40f;

    public float FovY = MathHelper.PiOver4;
    public float NearPlane = 0.1f;
    public float FarPlane = 200f;

    private Point _lastMouse;
    private int _lastScroll;
    private bool _dragging;

    public float AspectRatio { get; set; } = 16f / 9f;

    public Matrix ViewMatrix
    {
        get
        {
            var eye = GetEyePosition();
            return Matrix.CreateLookAt(eye, Target, Vector3.Up);
        }
    }

    public Matrix ProjectionMatrix
        => Matrix.CreatePerspectiveFieldOfView(FovY, AspectRatio, NearPlane, FarPlane);

    public Matrix ViewProjectionMatrix => ViewMatrix * ProjectionMatrix;

    public Matrix InvViewProjection => Matrix.Invert(ViewProjectionMatrix);

    public BoundingFrustum Frustum
        => new BoundingFrustum(ViewProjectionMatrix);

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

    public Vector3 Forward => Vector3.Normalize(Target - GetEyePosition());

    /// <summary>Update camera from mouse input. Returns true if camera moved.</summary>
    public bool Update(bool allowInput)
    {
        var ms = Mouse.GetState();
        var scrollDelta = ms.ScrollWheelValue - _lastScroll;
        _lastScroll = ms.ScrollWheelValue;

        if (allowInput)
        {
            Distance -= scrollDelta * 0.001f * Distance * 0.1f;
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
        else
        {
            /* Input is gated (e.g. ImGui owns the mouse). Drop any in-flight
             * drag so the stale _lastMouse anchor cannot cause a camera jump
             * when input is handed back; the next press starts a fresh drag.
             */
            _dragging = false;
        }

        return _dragging || allowInput;
    }
}
