using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Manages transient render targets, recreating them on window resize.</summary>
public class ResourcePool : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<string, RenderTarget2D> _targets = new();
    private int _cachedWidth, _cachedHeight;

    public ResourcePool(GraphicsDevice device)
    {
        _device = device;
    }

    public int Width => _cachedWidth;
    public int Height => _cachedHeight;

    /// <summary>Get or create a render target. Recreates if dimensions changed.</summary>
    public RenderTarget2D GetOrCreate(string key, int w, int h,
        SurfaceFormat format, DepthFormat depthFormat)
    {
        if (_targets.TryGetValue(key, out var existing))
        {
            if (existing.Width == w && existing.Height == h)
                return existing;
            existing.Dispose();
            _targets.Remove(key);
        }

        var rt = new RenderTarget2D(_device, w, h, false, format, depthFormat);
        _targets[key] = rt;
        _cachedWidth = w;
        _cachedHeight = h;
        return rt;
    }

    /// <summary>Release a specific target by key.</summary>
    public void Release(string key)
    {
        if (_targets.TryGetValue(key, out var rt))
        {
            rt.Dispose();
            _targets.Remove(key);
        }
    }

    /// <summary>Get a previously created target (returns null if not found).</summary>
    public RenderTarget2D? Get(string key)
        => _targets.TryGetValue(key, out var rt) ? rt : null;

    /// <summary>Dispose all targets.</summary>
    public void Dispose()
    {
        foreach (var rt in _targets.Values)
            rt.Dispose();
        _targets.Clear();
    }
}
