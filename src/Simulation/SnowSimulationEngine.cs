using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Frosty;

public enum SnowColorTheme
{
    ClassicWhite = 0,
    CyberpunkNeon = 1,
    RoyalGold = 2,
    FestiveConfetti = 3
}

public enum ParticleStyle
{
    Snow = 0,
    AutumnLeaves = 1,
    SakuraPetals = 2,
    Embers = 3
}

public struct Particle
{
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public float BaseRadius;
    public float Radius;
    public float Phase;
    public float SwingAmplitude;
    public float SwingFrequency;
    public byte SpriteIndex;
    public byte ColorIndex;
}

public struct SplashParticle
{
    public bool Active;
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public float Life;
    public float MaxLife;
    public byte SpriteIndex;
    public byte ColorIndex;
}

public struct AkBullet
{
    public bool Active;
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public float Life;
    public Snowman? Owner;
}

public struct ShellCasing
{
    public bool Active;
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public float Angle;
    public float AngularVel;
    public float Life;
}

public sealed class Snowball
{
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public float Radius;
    public bool IsGrounded;
    public bool IsHiddenWithWindow;
    public bool IsSnowmanProjectile;
    public Snowman? OwnerSnowman;
    public IntPtr GroundedWindowHandle;
    public float OffsetFromWindowLeft;
}

public sealed class Snowman
{
    public float X;
    public float BottomY;
    public float Vx;
    public float Vy;
    public float TiltAngle;
    public float TiltVelocity;
    public bool IsGrounded;
    public bool IsHiddenWithWindow;
    public IntPtr GroundedWindowHandle;
    public float OffsetFromWindowLeft;
    public bool IsDressed = true;
    public byte ScarfColorIndex;
    public sbyte FacingDir = 1;
    public float ThrowCooldown = 3.5f;
    public bool HasAk47;
    public int AkBurstShotsLeft;
    public float AkShotTimer;
    public float AkMuzzleFlashTimer;
    public float AkAimAngle;
    public float AkRecoilOffset;
    public readonly List<float> Radii = new(3);

    public float TotalHeight
    {
        get
        {
            float h = 0f;
            for (int i = 0; i < Radii.Count; i++)
            {
                h += i == 0 ? Radii[i] * 2f : Radii[i] * 1.65f;
            }
            return h;
        }
    }

    public float MaxRadius => Radii.Count > 0 ? Radii[0] : 15f;
}

public struct SplatterBlob
{
    public float OffsetX;
    public float OffsetY;
    public float Radius;
    public float DripFactor;
}

public sealed class ScreenSplatter
{
    public float X;
    public float Y;
    public float BaseRadius;
    public float Life;
    public float MaxLife;
    public float SlideDistance;
    public float SlideSpeed;
    public readonly SplatterBlob[] Lumps;
    public readonly SplatterBlob[] Droplets;

    public ScreenSplatter(int lumpCount, int dropletCount)
    {
        Lumps = new SplatterBlob[lumpCount];
        Droplets = new SplatterBlob[dropletCount];
    }
}

public sealed class SurfaceSnowBank
{
    public IntPtr Handle;
    public int LastLeft;
    public int LastRight;
    public int LastTopY;
    public int LastBottomY;
    public int LastMoveSignX;
    public float ShakeEnergy;
    public float SmoothVelX;
    public float SmoothVelY;
    public float ShakeCooldown;
    public bool IsTaskbar;
    public bool SeenInCurrentFrame;
    public bool IsDirty;
    public bool HasAnySnow;
    public float[] Heights;

    public SurfaceSnowBank(int bucketCount)
    {
        Heights = new float[bucketCount];
    }

    public void EnsureCapacity(int bucketCount)
    {
        if (Heights.Length < bucketCount)
        {
            Array.Resize(ref Heights, bucketCount);
        }
    }

    public void ClearHeights()
    {
        Array.Clear(Heights, 0, Heights.Length);
        IsDirty = false;
        HasAnySnow = false;
    }
}

public sealed class SnowSimulationEngine : IDisposable
{
    public const int MaxParticlesPool = 2500;
    private const int MaxSplashParticles = 360;
    private const int MaxAkBullets = 64;
    private const int MaxShellCasings = 48;
    private const int BucketSize = 8;
    private const float DefaultMaxSnowHeight = 38f;
    private const float BaseGravity = 980f;
    private const int SpriteCount = 4;
    private const int ColorVariantCount = 8;
    private const float SurfaceContactOffset = 2.0f;
    private const float BaseWallShatterSpeed = 420f;

    private static readonly int[] SpriteSizes = { 12, 18, 24, 32 };
    private static readonly float[] SpriteHalfSizes = { 6f, 9f, 12f, 16f };

    private struct LandingSurfaceEntry
    {
        public int SnapshotIndex;
        public int Left;
        public int Right;
        public int TopY;
        public int WinBuckets;
        public bool IsTaskbar;
        public SurfaceSnowBank Bank;
    }

    private int _originX;
    private int _originY;
    private int _width;
    private int _height;
    private int _bucketCount;
    private readonly IWindowTracker _tracker;

    private readonly Particle[] _particles = new Particle[MaxParticlesPool];
    private readonly SplashParticle[] _splashParticles = new SplashParticle[MaxSplashParticles];
    private readonly AkBullet[] _akBullets = new AkBullet[MaxAkBullets];
    private readonly ShellCasing[] _shellCasings = new ShellCasing[MaxShellCasings];
    private int _nextSplashIndex;
    private int _nextBulletIndex;
    private int _nextShellIndex;

    private readonly SKBitmap[,] _bakedBitmaps = new SKBitmap[ColorVariantCount, SpriteCount];

    private int _activeParticles = 1200;
    private float _windMultiplier = 1.0f;
    private float _windChangeInterval = 5.0f;
    private float _swayMultiplier = 1.0f;
    private float _speedMultiplier = 1.0f;
    private float _sizeMultiplier = 1.0f;
    private float _maxSnowHeight = DefaultMaxSnowHeight;
    private float _accumulationRateMultiplier = 1.0f;

    private bool _isBlizzardMode;
    private bool _enablePowerSaving = true;
    private float _powerSaveTimeoutSeconds = 60.0f;
    private bool _enableCursorScatter = true;
    private float _cursorScatterPower = 0.5f;
    private bool _enableSnowballCrafting = true;
    private float _objectGravityMultiplier = 1.0f;
    private float _snowballToughnessMultiplier = 1.0f;
    private bool _enableWindowOcclusion = true;
    private bool _keepSnowOnHiddenWindows = true;
    private bool _accumulateOnTaskbar = true;

    private bool _enableLivingSnowmen = true;
    private bool _forceAk47EasterEgg;
    private bool _enableWindowShakeShed = true;
    private float _windowShakeThreshold = 1.0f;
    private bool _enableScreenSplatters = true;

    private SnowColorTheme _colorTheme = SnowColorTheme.ClassicWhite;
    private ParticleStyle _particleStyle = ParticleStyle.Snow;
    private SKBitmap? _bankTextureBitmap;
    private SKShader? _bankTextureShader;

    private SKRect _settingsExclusionRect;
    private bool _hasSettingsExclusion;
    private SKRect _trayExclusionRect;
    private bool _hasTrayExclusion;
    private SKRect _welcomeExclusionRect;
    private bool _hasWelcomeExclusion;

    private readonly List<TrackedSurface> _surfacesSnapshot = new(64);
    private LandingSurfaceEntry[] _landingCache = new LandingSurfaceEntry[64];
    private int _landingCacheCount;

    private readonly Dictionary<IntPtr, SurfaceSnowBank> _snowBanks = new(64);
    private readonly List<IntPtr> _cleanupKeys = new(32);

    private readonly List<Snowball> _snowballs = new(32);
    private readonly List<Snowman> _snowmen = new(16);
    private readonly List<ScreenSplatter> _splatters = new(24);

    private Snowball? _activeSnowball;
    private Snowman? _activeSnowman;
    private float _throwVx;
    private float _throwVy;

    private readonly SKPaint _snowdriftPaint;
    private readonly SKPaint _ballShadowPaint;
    private readonly SKPaint _activeHighlightPaint;
    private readonly SKPaint _detailPaint;
    private readonly SKPaint _branchPaint;
    private readonly SKPaint _carrotPaint;
    private readonly SKPaint _hatPaint;
    private readonly SKPaint _scarfPaint;
    private readonly SKPaint _splatterShadowPaint;
    private readonly SKPaint _splatterBasePaint;
    private readonly SKPaint _splatterCorePaint;
    private readonly SKPaint _akMetalPaint;
    private readonly SKPaint _akWoodPaint;
    private readonly SKPaint _akFlashOuterPaint;
    private readonly SKPaint _akFlashInnerPaint;
    private readonly SKPaint _akTracerPaint;
    private readonly SKPaint _akShellPaint;
    private readonly SKPath _reusablePath = new();
    private readonly SKPath _occluderPath = new();
    private readonly Random _rng = new();

    private static readonly SKColor[] NeonPalette =
    {
        new(0, 245, 255),
        new(255, 42, 212),
        new(168, 85, 247),
        new(56, 189, 248),
        new(52, 211, 153),
        new(244, 114, 182),
        new(129, 140, 248),
        new(34, 211, 238)
    };

    private static readonly SKColor[] GoldPalette =
    {
        new(255, 215, 0),
        new(255, 236, 139),
        new(251, 191, 36),
        new(245, 158, 11),
        new(254, 240, 138),
        new(253, 224, 71),
        new(255, 250, 205),
        new(217, 119, 6)
    };

    private static readonly SKColor[] ConfettiPalette =
    {
        new(255, 99, 132),
        new(54, 162, 235),
        new(255, 206, 86),
        new(75, 192, 192),
        new(153, 102, 255),
        new(255, 159, 64),
        new(74, 222, 128),
        new(244, 114, 182)
    };

    private static readonly SKColor[] SakuraPalette =
    {
        new(255, 183, 197),
        new(255, 209, 220),
        new(244, 143, 177),
        new(255, 228, 235),
        new(251, 160, 184),
        new(255, 192, 203),
        new(249, 168, 212),
        new(255, 240, 245)
    };

    private static readonly SKColor[] AutumnPalette =
    {
        new(234, 88, 12),
        new(217, 119, 6),
        new(220, 38, 38),
        new(245, 158, 11),
        new(180, 83, 9),
        new(251, 146, 60),
        new(194, 65, 12),
        new(239, 68, 68)
    };

    private static readonly SKColor[] EmberPalette =
    {
        new(255, 107, 53),
        new(255, 159, 28),
        new(255, 214, 10),
        new(247, 127, 0),
        new(255, 84, 0),
        new(255, 190, 11),
        new(251, 86, 7),
        new(255, 230, 150)
    };

    private static readonly SKColor[] ScarfColors =
    {
        new(220, 45, 55),
        new(35, 135, 230),
        new(40, 175, 95),
        new(230, 140, 25)
    };

    private float _globalWind;
    private float _targetWind;
    private float _windTimer;
    private float _blizzardGustPhase;

    private float _prevMouseX;
    private float _prevMouseY;
    private bool _resetMouseDelta = true;
    private float _idleSeconds;

    public bool IsInPowerSaveSleep => _enablePowerSaving && _idleSeconds >= _powerSaveTimeoutSeconds && !HasActiveInteraction;
    public bool HasActiveInteraction => _activeSnowball != null || _activeSnowman != null;

    public SnowSimulationEngine(int originX, int originY, int width, int height, IWindowTracker tracker)
    {
        _originX = originX;
        _originY = originY;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _bucketCount = (_width / BucketSize) + 4;
        _tracker = tracker;

        _snowdriftPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(246, 250, 255, 248)
        };

        _ballShadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(195, 215, 235, 200)
        };

        _activeHighlightPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.2f,
            Color = new SKColor(56, 189, 248, 220)
        };

        _detailPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(35, 40, 48, 255)
        };

        _branchPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.4f,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(105, 68, 42, 255)
        };

        _carrotPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(245, 115, 25, 255)
        };

        _hatPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(45, 52, 64, 255)
        };

        _scarfPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = ScarfColors[0]
        };

        _splatterShadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(165, 190, 215, 110)
        };

        _splatterBasePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(232, 242, 252, 225)
        };

        _splatterCorePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(250, 253, 255, 245)
        };

        _akMetalPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(36, 40, 46, 255)
        };

        _akWoodPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(145, 76, 32, 255)
        };

        _akFlashOuterPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 150, 20, 220)
        };

        _akFlashInnerPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 245, 175, 250)
        };

        _akTracerPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.2f,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(255, 220, 75, 245)
        };

        _akShellPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(230, 185, 55, 255)
        };

        RebuildBakedBitmaps();
        InitializeParticles();
    }

    public void WakeFromIdle()
    {
        _idleSeconds = 0f;
    }

    public void SetUiExclusionRects(NativeMethods.RECT? settingsRect, NativeMethods.RECT? trayRect, NativeMethods.RECT? welcomeRect = null)
    {
        if (settingsRect.HasValue)
        {
            var r = settingsRect.Value;
            _settingsExclusionRect = new SKRect(r.Left - _originX, r.Top - _originY, r.Right - _originX, r.Bottom - _originY);
            _hasSettingsExclusion = true;
        }
        else
        {
            _hasSettingsExclusion = false;
        }

        if (trayRect.HasValue)
        {
            var r = trayRect.Value;
            _trayExclusionRect = new SKRect(r.Left - _originX, r.Top - _originY, r.Right - _originX, r.Bottom - _originY);
            _hasTrayExclusion = true;
        }
        else
        {
            _hasTrayExclusion = false;
        }

        if (welcomeRect.HasValue)
        {
            var r = welcomeRect.Value;
            _welcomeExclusionRect = new SKRect(r.Left - _originX, r.Top - _originY, r.Right - _originX, r.Bottom - _originY);
            _hasWelcomeExclusion = true;
        }
        else
        {
            _hasWelcomeExclusion = false;
        }
    }

    public void NotifyResume()
    {
        _resetMouseDelta = true;
        _idleSeconds = 0f;
    }

    public void UpdateScreenBounds(int originX, int originY, int width, int height)
    {
        _originX = originX;
        _originY = originY;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _bucketCount = (_width / BucketSize) + 4;

        foreach (var bank in _snowBanks.Values)
        {
            bank.EnsureCapacity(_bucketCount);
        }
        _resetMouseDelta = true;
    }

    private void RebuildBakedBitmaps()
    {
        for (int colIdx = 0; colIdx < ColorVariantCount; colIdx++)
        {
            SKColor baseColor = _particleStyle switch
            {
                ParticleStyle.AutumnLeaves => AutumnPalette[colIdx % AutumnPalette.Length],
                ParticleStyle.SakuraPetals => SakuraPalette[colIdx % SakuraPalette.Length],
                ParticleStyle.Embers => EmberPalette[colIdx % EmberPalette.Length],
                _ => _colorTheme switch
                {
                    SnowColorTheme.CyberpunkNeon => NeonPalette[colIdx % NeonPalette.Length],
                    SnowColorTheme.RoyalGold => GoldPalette[colIdx % GoldPalette.Length],
                    SnowColorTheme.FestiveConfetti => ConfettiPalette[colIdx % ConfettiPalette.Length],
                    _ => new SKColor(255, 255, 255)
                }
            };

            byte alphaFactor = (byte)(185 + (colIdx % 4) * 20);

            for (int sIdx = 0; sIdx < SpriteCount; sIdx++)
            {
                _bakedBitmaps[colIdx, sIdx]?.Dispose();

                int sz = SpriteSizes[sIdx];
                float center = sz * 0.5f;
                float radius = center - 0.8f;

                var bmp = new SKBitmap(sz, sz, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(bmp);
                canvas.Clear(SKColors.Transparent);

                if (_particleStyle == ParticleStyle.AutumnLeaves)
                {
                    DrawMapleLeaf(canvas, center, center, radius * 0.96f, (colIdx * 43 + sIdx * 29) % 360, baseColor);
                }
                else if (_particleStyle == ParticleStyle.SakuraPetals)
                {
                    DrawSakuraPetal(canvas, center, center, radius * 0.94f, (colIdx * 47 + sIdx * 31) % 360, baseColor);
                }
                else if (_particleStyle == ParticleStyle.Embers)
                {
                    DrawEmberSpark(canvas, center, center, radius * 0.95f, (colIdx * 37 + sIdx * 19) % 360, baseColor);
                }
                else
                {
                    using var shader = SKShader.CreateRadialGradient(
                        new SKPoint(center, center),
                        radius,
                        new[]
                        {
                            baseColor.WithAlpha(alphaFactor),
                            baseColor.WithAlpha((byte)(alphaFactor * 0.92f)),
                            baseColor.WithAlpha((byte)(alphaFactor * 0.55f)),
                            baseColor.WithAlpha((byte)(alphaFactor * 0.18f)),
                            baseColor.WithAlpha(0)
                        },
                        new[] { 0.0f, 0.32f, 0.62f, 0.85f, 1.0f },
                        SKShaderTileMode.Clamp);

                    using var paint = new SKPaint { IsAntialias = true, Shader = shader };
                    canvas.DrawCircle(center, center, radius, paint);
                }

                canvas.Flush();
                bmp.SetImmutable();
                _bakedBitmaps[colIdx, sIdx] = bmp;
            }
        }

        RebuildBankTexture();
    }

    private void RebuildBankTexture()
    {
        _snowdriftPaint.Shader = null;
        _bankTextureShader?.Dispose();
        _bankTextureShader = null;
        _bankTextureBitmap?.Dispose();
        _bankTextureBitmap = null;

        if (_particleStyle == ParticleStyle.Snow)
        {
            _snowdriftPaint.Color = _colorTheme switch
            {
                SnowColorTheme.CyberpunkNeon => new SKColor(220, 248, 255, 245),
                SnowColorTheme.RoyalGold => new SKColor(255, 248, 220, 245),
                _ => new SKColor(246, 250, 255, 248)
            };
            return;
        }

        const int tile = 128;
        var bmp = new SKBitmap(tile, tile, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            var rng = new Random(4242);

            if (_particleStyle == ParticleStyle.AutumnLeaves)
            {
                canvas.Clear(new SKColor(115, 48, 16, 250));
                for (int i = 0; i < 110; i++)
                {
                    float x = (float)(rng.NextDouble() * tile);
                    float y = (float)(rng.NextDouble() * tile);
                    float r = 7.5f + (float)(rng.NextDouble() * 7.5);
                    float ang = (float)(rng.NextDouble() * 360.0);
                    SKColor col = AutumnPalette[i % AutumnPalette.Length];

                    for (int ox = -tile; ox <= tile; ox += tile)
                    {
                        for (int oy = -tile; oy <= tile; oy += tile)
                        {
                            float dx = x + ox;
                            float dy = y + oy;
                            if (dx < -r * 2 || dx > tile + r * 2 || dy < -r * 2 || dy > tile + r * 2) continue;
                            DrawMapleLeaf(canvas, dx, dy, r, ang, col, withShadow: true);
                        }
                    }
                }
            }
            else if (_particleStyle == ParticleStyle.SakuraPetals)
            {
                canvas.Clear(new SKColor(242, 186, 206, 245));
                for (int i = 0; i < 120; i++)
                {
                    float x = (float)(rng.NextDouble() * tile);
                    float y = (float)(rng.NextDouble() * tile);
                    float r = 6.5f + (float)(rng.NextDouble() * 6.5);
                    float ang = (float)(rng.NextDouble() * 360.0);
                    SKColor col = SakuraPalette[i % SakuraPalette.Length];

                    for (int ox = -tile; ox <= tile; ox += tile)
                    {
                        for (int oy = -tile; oy <= tile; oy += tile)
                        {
                            float dx = x + ox;
                            float dy = y + oy;
                            if (dx < -r * 2 || dx > tile + r * 2 || dy < -r * 2 || dy > tile + r * 2) continue;
                            DrawSakuraPetal(canvas, dx, dy, r, ang, col, withShadow: true);
                        }
                    }
                }
            }
            else if (_particleStyle == ParticleStyle.Embers)
            {
                canvas.Clear(new SKColor(32, 24, 22, 246));
                using var ashPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                for (int i = 0; i < 140; i++)
                {
                    float x = (float)(rng.NextDouble() * tile);
                    float y = (float)(rng.NextDouble() * tile);
                    float r = 3f + (float)(rng.NextDouble() * 6f);
                    byte v = (byte)(28 + rng.Next(36));
                    ashPaint.Color = new SKColor((byte)(v + 12), v, (byte)Math.Max(0, v - 4), 230);
                    canvas.DrawCircle(x, y, r, ashPaint);
                }
                for (int i = 0; i < 42; i++)
                {
                    float x = (float)(rng.NextDouble() * tile);
                    float y = (float)(rng.NextDouble() * tile);
                    float r = 3.5f + (float)(rng.NextDouble() * 4.5);
                    SKColor col = EmberPalette[i % EmberPalette.Length];
                    DrawEmberSpark(canvas, x, y, r, (float)(rng.NextDouble() * 360.0), col);
                }
            }

            canvas.Flush();
        }

        bmp.SetImmutable();
        _bankTextureBitmap = bmp;
        _bankTextureShader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        _snowdriftPaint.Color = SKColors.White;
        _snowdriftPaint.Shader = _bankTextureShader;
    }

    private static void DrawMapleLeaf(SKCanvas canvas, float cx, float cy, float r, float angleDeg, SKColor color, bool withShadow = false)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(angleDeg);

        using var path = new SKPath();
        // Stylized 5-lobe autumn leaf
        path.MoveTo(0f, -r);
        path.LineTo(r * 0.24f, -r * 0.36f);
        path.LineTo(r * 0.88f, -r * 0.32f);
        path.LineTo(r * 0.42f, r * 0.08f);
        path.LineTo(r * 0.66f, r * 0.56f);
        path.LineTo(r * 0.12f, r * 0.42f);
        path.LineTo(0f, r * 0.92f);
        path.LineTo(-r * 0.12f, r * 0.42f);
        path.LineTo(-r * 0.66f, r * 0.56f);
        path.LineTo(-r * 0.42f, r * 0.08f);
        path.LineTo(-r * 0.88f, -r * 0.32f);
        path.LineTo(-r * 0.24f, -r * 0.36f);
        path.Close();

        if (withShadow)
        {
            using var shadowPaint = new SKPaint { IsAntialias = true, Color = new SKColor(28, 12, 4, 110) };
            canvas.Save();
            canvas.Translate(1.2f, 1.4f);
            canvas.DrawPath(path, shadowPaint);
            canvas.Restore();
        }

        using var fillPaint = new SKPaint { IsAntialias = true, Color = color.WithAlpha(242), Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, fillPaint);

        var edgeColor = new SKColor(
            (byte)(color.Red * 0.62f),
            (byte)(color.Green * 0.55f),
            (byte)(color.Blue * 0.50f),
            210);
        using var veinPaint = new SKPaint { IsAntialias = true, Color = edgeColor, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.8f, r * 0.09f) };
        canvas.DrawPath(path, veinPaint);
        canvas.DrawLine(0f, -r * 0.72f, 0f, r * 0.78f, veinPaint);

        canvas.Restore();
    }

    private static void DrawSakuraPetal(SKCanvas canvas, float cx, float cy, float r, float angleDeg, SKColor color, bool withShadow = false)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(angleDeg);

        using var path = new SKPath();
        // Classic cherry blossom petal with V-notch at tip
        path.MoveTo(0f, r * 0.85f);
        path.CubicTo(-r * 0.78f, r * 0.30f, -r * 0.72f, -r * 0.58f, -r * 0.24f, -r * 0.88f);
        path.LineTo(0f, -r * 0.56f);
        path.LineTo(r * 0.24f, -r * 0.88f);
        path.CubicTo(r * 0.72f, -r * 0.58f, r * 0.78f, r * 0.30f, 0f, r * 0.85f);
        path.Close();

        if (withShadow)
        {
            using var shadowPaint = new SKPaint { IsAntialias = true, Color = new SKColor(150, 70, 100, 75) };
            canvas.Save();
            canvas.Translate(1.0f, 1.2f);
            canvas.DrawPath(path, shadowPaint);
            canvas.Restore();
        }

        using var grad = SKShader.CreateLinearGradient(
            new SKPoint(0, -r),
            new SKPoint(0, r),
            new[] { color.WithAlpha(245), new SKColor(255, 245, 250, 235) },
            SKShaderTileMode.Clamp);
        using var fillPaint = new SKPaint { IsAntialias = true, Shader = grad };
        canvas.DrawPath(path, fillPaint);

        canvas.Restore();
    }

    private static void DrawEmberSpark(SKCanvas canvas, float cx, float cy, float r, float angleDeg, SKColor color)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(angleDeg);

        using var glowShader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            r,
            new[] { new SKColor(255, 250, 210, 255), color.WithAlpha(220), color.WithAlpha(70), color.WithAlpha(0) },
            new[] { 0.0f, 0.35f, 0.72f, 1.0f },
            SKShaderTileMode.Clamp);
        using var glowPaint = new SKPaint { IsAntialias = true, Shader = glowShader };
        canvas.DrawOval(0, 0, r * 0.95f, r * 0.58f, glowPaint);

        canvas.Restore();
    }

    public void SetActiveParticleCount(int count) => _activeParticles = Math.Clamp(count, 100, MaxParticlesPool);
    public void SetPreviewParticleCount(int count) => _activeParticles = Math.Clamp(count, 15, MaxParticlesPool);
    public void SetWindMultiplier(float multiplier) => _windMultiplier = Math.Clamp(multiplier, 0f, 4f);
    public void SetWindChangeInterval(float seconds) => _windChangeInterval = Math.Clamp(seconds, 1f, 15f);
    public void SetSwayAmplitudeMultiplier(float multiplier) => _swayMultiplier = Math.Clamp(multiplier, 0f, 2.5f);
    public void SetSpeedMultiplier(float multiplier) => _speedMultiplier = Math.Clamp(multiplier, 0.3f, 3.0f);

    public void SetSizeMultiplier(float multiplier)
    {
        float clamped = Math.Clamp(multiplier, 0.5f, 2.5f);
        if (Math.Abs(_sizeMultiplier - clamped) < 0.005f) return;
        _sizeMultiplier = clamped;

        for (int i = 0; i < MaxParticlesPool; i++)
        {
            ref Particle p = ref _particles[i];
            p.Radius = p.BaseRadius * _sizeMultiplier;
            p.SpriteIndex = (byte)Math.Clamp((int)((p.Radius - 1.2f) * 0.75f), 0, SpriteCount - 1);
        }
    }

    public void SetMaxSnowHeight(float maxHeight) => _maxSnowHeight = Math.Clamp(maxHeight, 10f, 95f);
    public void SetAccumulationRateMultiplier(float mult) => _accumulationRateMultiplier = Math.Clamp(mult, 0.2f, 3.0f);
    public void SetBlizzardMode(bool enabled) => _isBlizzardMode = enabled;
    public void SetPowerSaving(bool enabled) => _enablePowerSaving = enabled;
    public void SetPowerSaveTimeoutSeconds(float seconds) => _powerSaveTimeoutSeconds = Math.Clamp(seconds, 15f, 180f);
    public void SetCursorScatter(bool enabled) => _enableCursorScatter = enabled;
    public void SetCursorScatterPower(float power) => _cursorScatterPower = Math.Clamp(power, 0.5f, 2.5f);
    public void SetSnowballCrafting(bool enabled) => _enableSnowballCrafting = enabled;
    public void SetObjectGravityMultiplier(float mult) => _objectGravityMultiplier = Math.Clamp(mult, 0.5f, 2.0f);
    public void SetSnowballToughnessMultiplier(float mult) => _snowballToughnessMultiplier = Math.Clamp(mult, 0.4f, 3.0f);
    public void SetWindowOcclusion(bool enabled) => _enableWindowOcclusion = enabled;
    public void SetKeepSnowOnHiddenWindows(bool enabled) => _keepSnowOnHiddenWindows = enabled;

    public void SetLivingSnowmen(bool enabled) => _enableLivingSnowmen = enabled;
    public void SetWindowShakeShed(bool enabled) => _enableWindowShakeShed = enabled;
    public void SetWindowShakeThreshold(float value) => _windowShakeThreshold = Math.Clamp(value, 0.3f, 3.0f);
    public void SetScreenSplatters(bool enabled)
    {
        _enableScreenSplatters = enabled;
        if (!enabled) _splatters.Clear();
    }

    public void SetAccumulateOnTaskbar(bool enabled)
    {
        if (_accumulateOnTaskbar == enabled) return;
        _accumulateOnTaskbar = enabled;
        if (!enabled)
        {
            foreach (var bank in _snowBanks.Values)
            {
                if (bank.IsTaskbar) bank.ClearHeights();
            }
        }
    }

    public void SetColorTheme(SnowColorTheme theme)
    {
        if (_colorTheme == theme) return;
        _colorTheme = theme;
        if (_particleStyle == ParticleStyle.Snow)
            RebuildBakedBitmaps();
    }

    private bool IsSnowMode => _particleStyle == ParticleStyle.Snow;

    public void SetParticleStyle(ParticleStyle style)
    {
        if (_particleStyle == style) return;
        _particleStyle = style;
        if (!IsSnowMode)
        {
            ClearSnowmenAndSnowballs();
        }
        RebuildBakedBitmaps();
    }

    public bool ToggleAk47EasterEgg()
    {
        _forceAk47EasterEgg = !_forceAk47EasterEgg;
        return _forceAk47EasterEgg;
    }

    public void ClearAllSnow()
    {
        foreach (var bank in _snowBanks.Values)
        {
            bank.ClearHeights();
        }
        Array.Clear(_splashParticles, 0, _splashParticles.Length);
        Array.Clear(_akBullets, 0, _akBullets.Length);
        Array.Clear(_shellCasings, 0, _shellCasings.Length);
        _snowballs.Clear();
        _snowmen.Clear();
        _splatters.Clear();
        _activeSnowball = null;
        _activeSnowman = null;
    }

    public void ClearSnowmenAndSnowballs()
    {
        for (int i = 0; i < _snowmen.Count; i++)
        {
            ShatterSnowman(_snowmen[i], spawnSplatters: false);
        }
        for (int i = 0; i < _snowballs.Count; i++)
        {
            ShatterSnowball(_snowballs[i]);
        }
        Array.Clear(_akBullets, 0, _akBullets.Length);
        Array.Clear(_shellCasings, 0, _shellCasings.Length);
        _snowballs.Clear();
        _snowmen.Clear();
        _splatters.Clear();
        _activeSnowball = null;
        _activeSnowman = null;
    }

    private void InitializeParticles()
    {
        for (int i = 0; i < MaxParticlesPool; i++)
        {
            ResetParticle(ref _particles[i], randomY: true);
        }
    }

    private void ResetParticle(ref Particle p, bool randomY)
    {
        p.X = (float)(_rng.NextDouble() * _width);
        p.Y = randomY ? (float)(_rng.NextDouble() * _height) : -12f - (float)(_rng.NextDouble() * 45f);
        p.BaseRadius = 1.8f + (float)(_rng.NextDouble() * 3.4f);
        p.Radius = p.BaseRadius * _sizeMultiplier;
        p.Vy = (38f + p.Radius * 16f + (float)(_rng.NextDouble() * 18f)) * _speedMultiplier;
        p.Vx = 0f;
        p.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
        p.SwingAmplitude = 12f + (float)(_rng.NextDouble() * 22f);
        p.SwingFrequency = 1.1f + (float)(_rng.NextDouble() * 1.6f);
        p.SpriteIndex = (byte)Math.Clamp((int)((p.Radius - 1.2f) * 0.75f), 0, SpriteCount - 1);
        p.ColorIndex = (byte)_rng.Next(0, ColorVariantCount);
    }

    private void SpawnSplashParticle(float x, float y, float vx, float vy)
    {
        ref SplashParticle sp = ref _splashParticles[_nextSplashIndex];
        _nextSplashIndex = (_nextSplashIndex + 1) % MaxSplashParticles;

        sp.Active = true;
        sp.X = x;
        sp.Y = y;
        sp.Vx = vx + ((float)_rng.NextDouble() - 0.5f) * 55f;
        sp.Vy = vy;
        sp.MaxLife = 0.55f + (float)_rng.NextDouble() * 0.65f;
        sp.Life = sp.MaxLife;
        sp.SpriteIndex = (byte)_rng.Next(0, 2);
        sp.ColorIndex = (byte)_rng.Next(0, ColorVariantCount);
    }

    private void SpawnAkBullet(Snowman owner, float x, float y, float vx, float vy)
    {
        ref AkBullet b = ref _akBullets[_nextBulletIndex];
        _nextBulletIndex = (_nextBulletIndex + 1) % MaxAkBullets;
        b.Active = true;
        b.X = x;
        b.Y = y;
        b.Vx = vx;
        b.Vy = vy;
        b.Life = 1.6f;
        b.Owner = owner;
    }

    private void SpawnShellCasing(float x, float y, sbyte facingDir)
    {
        ref ShellCasing sc = ref _shellCasings[_nextShellIndex];
        _nextShellIndex = (_nextShellIndex + 1) % MaxShellCasings;
        sc.Active = true;
        sc.X = x;
        sc.Y = y;
        sc.Vx = -facingDir * (55f + (float)_rng.NextDouble() * 85f);
        sc.Vy = -140f - (float)_rng.NextDouble() * 90f;
        sc.Angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
        sc.AngularVel = (facingDir >= 0 ? -1f : 1f) * (12f + (float)_rng.NextDouble() * 16f);
        sc.Life = 0.85f;
    }

    private static int GetLocalBucketIndex(float screenX, int windowLeft, int maxBuckets)
    {
        int localPx = (int)screenX - windowLeft;
        return Math.Clamp(localPx / BucketSize, 0, maxBuckets - 1);
    }

    private static int GetWindowBucketCount(in TrackedSurface surf, int maxBuckets)
    {
        int widthPx = Math.Max(0, surf.Right - surf.Left);
        return Math.Clamp((widthPx / BucketSize) + 1, 3, maxBuckets - 1);
    }

    private static float GetMaxBankHeightNearLocalBucket(SurfaceSnowBank bank, int localBucket, int windowBucketCount)
    {
        int start = Math.Max(0, localBucket - 2);
        int end = Math.Min(windowBucketCount - 1, localBucket + 2);
        float maxH = 0f;
        for (int b = start; b <= end; b++)
        {
            if (bank.Heights[b] > maxH)
                maxH = bank.Heights[b];
        }
        return maxH;
    }

    private bool CanAccumulateOnSurface(in TrackedSurface surf)
    {
        if (surf.IsTaskbar) return _accumulateOnTaskbar;
        if (surf.IsMaximizedOrTopDocked) return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsPointInsideExcludedUi(float x, float y)
    {
        if (_hasSettingsExclusion && _settingsExclusionRect.Contains(x, y))
            return true;
        if (_hasTrayExclusion && _trayExclusionRect.Contains(x, y))
            return true;
        if (_hasWelcomeExclusion && _welcomeExclusionRect.Contains(x, y))
            return true;
        return false;
    }

    private bool IsPointCoveredByHigherWindow(float x, float y, int surfaceIndex)
    {
        if (!_enableWindowOcclusion)
            return false;

        for (int j = 0; j < surfaceIndex; j++)
        {
            TrackedSurface higher = _surfacesSnapshot[j];
            if (higher.IsTaskbar) continue;
            if (higher.IsMaximizedOrTopDocked) return true;
            if (x >= higher.Left - 2 && x <= higher.Right + 2 && y >= higher.TopY - 2 && y <= higher.BottomY + 2)
                return true;
        }
        return false;
    }

    private bool IsPointHiddenBehindHigherWindow(float x, float y, int limit)
    {
        int n = Math.Min(limit, _surfacesSnapshot.Count);
        for (int j = 0; j < n; j++)
        {
            TrackedSurface higher = _surfacesSnapshot[j];
            if (higher.IsMaximizedOrTopDocked) return true;
            if (x >= higher.Left - 2 && x <= higher.Right + 2 && y >= higher.TopY - 2 && y <= higher.BottomY + 2)
                return true;
        }
        return false;
    }

    // true, если точка эффекта на сугробе этого окна закрыта окнами, лежащими выше него.
    private bool IsSpotHidden(in TrackedSurface surf, float x, float y)
    {
        int idx = FindSurfaceIndexByHandle(surf.Handle);
        return idx > 0 && IsPointHiddenBehindHigherWindow(x, y, idx);
    }

    public bool ShouldInterceptMouse(float x, float y)
    {
        if (!IsSnowMode || !_enableSnowballCrafting || IsPointInsideExcludedUi(x, y))
            return false;

        if (_activeSnowball != null || _activeSnowman != null)
            return true;

        bool isLeftMouseButtonHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
        if (isLeftMouseButtonHeld)
            return false;

        for (int i = 0; i < _snowmen.Count; i++)
        {
            Snowman sm = _snowmen[i];
            if (sm.IsHiddenWithWindow) continue;
            if (Math.Abs(x - sm.X) <= sm.MaxRadius + 14f && y >= sm.BottomY - sm.TotalHeight - 18f && y <= sm.BottomY + 8f)
            {
                int surfIdx = sm.IsGrounded ? FindSurfaceIndexByHandle(sm.GroundedWindowHandle) : -1;
                if (surfIdx <= 0 || !IsPointCoveredByHigherWindow(x, y, surfIdx))
                    return true;
            }
        }

        for (int i = 0; i < _snowballs.Count; i++)
        {
            Snowball sb = _snowballs[i];
            if (sb.IsHiddenWithWindow) continue;
            float dx = x - sb.X;
            float dy = y - sb.Y;
            float hitR = sb.Radius + 10f;
            if (dx * dx + dy * dy <= hitR * hitR)
            {
                int surfIdx = sb.IsGrounded ? FindSurfaceIndexByHandle(sb.GroundedWindowHandle) : -1;
                if (surfIdx <= 0 || !IsPointCoveredByHigherWindow(x, y, surfIdx))
                    return true;
            }
        }

        for (int i = 0; i < _surfacesSnapshot.Count; i++)
        {
            TrackedSurface surf = _surfacesSnapshot[i];
            if (!CanAccumulateOnSurface(in surf)) continue;
            if (x < surf.Left || x > surf.Right) continue;

            if (_snowBanks.TryGetValue(surf.Handle, out SurfaceSnowBank? bank) && bank.HasAnySnow)
            {
                int winBuckets = GetWindowBucketCount(in surf, _bucketCount);
                int localB = GetLocalBucketIndex(x, surf.Left, winBuckets);
                float maxH = GetMaxBankHeightNearLocalBucket(bank, localB, winBuckets);
                float baseY = surf.TopY + SurfaceContactOffset;

                if (maxH >= 2.5f && y >= baseY - maxH - 12f && y <= baseY - 2f)
                {
                    if (!IsPointCoveredByHigherWindow(x, y, i))
                        return true;
                }
            }
        }

        return false;
    }

    public bool OnMouseDown(float x, float y)
    {
        if (!IsSnowMode || !_enableSnowballCrafting || IsPointInsideExcludedUi(x, y)) return false;

        _idleSeconds = 0f;
        _throwVx = 0f;
        _throwVy = 0f;

        for (int i = _snowmen.Count - 1; i >= 0; i--)
        {
            Snowman sm = _snowmen[i];
            if (sm.IsHiddenWithWindow) continue;
            if (Math.Abs(x - sm.X) <= sm.MaxRadius + 14f && y >= sm.BottomY - sm.TotalHeight - 18f && y <= sm.BottomY + 8f)
            {
                int surfIdx = sm.IsGrounded ? FindSurfaceIndexByHandle(sm.GroundedWindowHandle) : -1;
                if (surfIdx > 0 && IsPointCoveredByHigherWindow(x, y, surfIdx))
                    continue;

                _activeSnowman = sm;
                sm.IsGrounded = false;
                sm.IsHiddenWithWindow = false;
                sm.GroundedWindowHandle = IntPtr.Zero;
                sm.Vx = 0f;
                sm.Vy = 0f;
                return true;
            }
        }

        for (int i = _snowballs.Count - 1; i >= 0; i--)
        {
            Snowball sb = _snowballs[i];
            if (sb.IsHiddenWithWindow) continue;
            float dx = x - sb.X;
            float dy = y - sb.Y;
            float hitR = sb.Radius + 10f;
            if (dx * dx + dy * dy <= hitR * hitR)
            {
                int surfIdx = sb.IsGrounded ? FindSurfaceIndexByHandle(sb.GroundedWindowHandle) : -1;
                if (surfIdx > 0 && IsPointCoveredByHigherWindow(x, y, surfIdx))
                    continue;

                _activeSnowball = sb;
                sb.IsGrounded = false;
                sb.IsHiddenWithWindow = false;
                sb.IsSnowmanProjectile = false;
                sb.GroundedWindowHandle = IntPtr.Zero;
                sb.Vx = 0f;
                sb.Vy = 0f;
                return true;
            }
        }

        for (int i = 0; i < _surfacesSnapshot.Count; i++)
        {
            TrackedSurface surf = _surfacesSnapshot[i];
            if (!CanAccumulateOnSurface(in surf)) continue;
            if (x < surf.Left || x > surf.Right) continue;

            if (_snowBanks.TryGetValue(surf.Handle, out SurfaceSnowBank? bank) && bank.HasAnySnow)
            {
                int winBuckets = GetWindowBucketCount(in surf, _bucketCount);
                int localB = GetLocalBucketIndex(x, surf.Left, winBuckets);
                float maxH = GetMaxBankHeightNearLocalBucket(bank, localB, winBuckets);
                float baseY = surf.TopY + SurfaceContactOffset;

                if (maxH >= 2.5f && y >= baseY - maxH - 12f && y <= baseY - 2f)
                {
                    if (IsPointCoveredByHigherWindow(x, y, i))
                        continue;

                    ScoopSnowFromBank(bank, localB, 2, 3.5f, winBuckets);
                    var newBall = new Snowball
                    {
                        X = x,
                        Y = baseY - 9f,
                        Radius = 9f,
                        IsGrounded = false
                    };
                    _snowballs.Add(newBall);
                    _activeSnowball = newBall;
                    return true;
                }
            }
        }

        return false;
    }

    public bool OnRightMouseDown(float x, float y)
    {
        if (!IsSnowMode) return false;
        if (IsPointInsideExcludedUi(x, y)) return false;

        _idleSeconds = 0f;

        for (int i = _snowmen.Count - 1; i >= 0; i--)
        {
            Snowman sm = _snowmen[i];
            if (sm.IsHiddenWithWindow) continue;
            if (Math.Abs(x - sm.X) <= sm.MaxRadius + 14f && y >= sm.BottomY - sm.TotalHeight - 18f && y <= sm.BottomY + 8f)
            {
                int surfIdx = sm.IsGrounded ? FindSurfaceIndexByHandle(sm.GroundedWindowHandle) : -1;
                if (surfIdx > 0 && IsPointCoveredByHigherWindow(x, y, surfIdx))
                    continue;

                ShatterSnowman(sm, spawnSplatters: false);
                if (ReferenceEquals(_activeSnowman, sm)) _activeSnowman = null;
                _snowmen.RemoveAt(i);
                return true;
            }
        }

        for (int i = _snowballs.Count - 1; i >= 0; i--)
        {
            Snowball sb = _snowballs[i];
            if (sb.IsHiddenWithWindow) continue;
            float dx = x - sb.X;
            float dy = y - sb.Y;
            float hitR = sb.Radius + 10f;
            if (dx * dx + dy * dy <= hitR * hitR)
            {
                int surfIdx = sb.IsGrounded ? FindSurfaceIndexByHandle(sb.GroundedWindowHandle) : -1;
                if (surfIdx > 0 && IsPointCoveredByHigherWindow(x, y, surfIdx))
                    continue;

                ShatterSnowball(sb);
                if (ReferenceEquals(_activeSnowball, sb)) _activeSnowball = null;
                _snowballs.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public void OnMouseUp(float x, float y)
    {
        if (_activeSnowman != null)
        {
            Snowman sm = _activeSnowman;
            _activeSnowman = null;

            sm.Vx = Math.Clamp(_throwVx * 0.9f, -1550f, 1550f);
            sm.Vy = Math.Clamp(_throwVy * 0.9f, -1550f, 1550f);
            return;
        }

        if (_activeSnowball != null)
        {
            Snowball ball = _activeSnowball;
            _activeSnowball = null;

            if (TryStackSnowball(ball))
            {
                return;
            }

            float speed = MathF.Sqrt(_throwVx * _throwVx + _throwVy * _throwVy);
            if (_enableScreenSplatters && speed > 1300f && ball.Radius >= 11f)
            {
                SpawnRealisticSplatter(ball.X, ball.Y, ball.Radius * 1.4f);
                ShatterSnowball(ball);
                _snowballs.Remove(ball);
                return;
            }

            ball.Vx = Math.Clamp(_throwVx, -1650f, 1650f);
            ball.Vy = Math.Clamp(_throwVy, -1650f, 1650f);
        }
    }

    private void SpawnRealisticSplatter(float x, float y, float baseRadius)
    {
        if (!_enableScreenSplatters) return;

        if (_splatters.Count >= 18)
        {
            _splatters.RemoveAt(0);
        }

        float clampedR = Math.Clamp(baseRadius, 14f, 46f);
        int lumpCount = 15;
        int dropletCount = 16;

        var splatter = new ScreenSplatter(lumpCount, dropletCount)
        {
            X = Math.Clamp(x, clampedR * 0.5f, _width - clampedR * 0.5f),
            Y = Math.Clamp(y, clampedR * 0.5f, _height - clampedR * 0.5f),
            BaseRadius = clampedR,
            MaxLife = 6.5f,
            Life = 6.5f,
            SlideSpeed = 5f + (float)_rng.NextDouble() * 7f
        };

        for (int i = 0; i < lumpCount; i++)
        {
            float baseAngle = i * (MathF.PI * 2f / lumpCount);
            float angle = baseAngle + ((float)_rng.NextDouble() - 0.5f) * 0.35f;
            float dist = clampedR * (0.38f + (float)_rng.NextDouble() * 0.45f);
            float r = clampedR * (0.28f + (float)_rng.NextDouble() * 0.32f);
            float drip = MathF.Sin(angle) > 0.25f ? (0.8f + (float)_rng.NextDouble() * 1.4f) : 0.2f;

            splatter.Lumps[i] = new SplatterBlob
            {
                OffsetX = MathF.Cos(angle) * dist,
                OffsetY = MathF.Sin(angle) * dist,
                Radius = r,
                DripFactor = drip
            };
        }

        for (int i = 0; i < dropletCount; i++)
        {
            float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
            float dist = clampedR * (0.95f + (float)_rng.NextDouble() * 1.15f);
            float r = 2.0f + (float)_rng.NextDouble() * (clampedR * 0.16f);

            splatter.Droplets[i] = new SplatterBlob
            {
                OffsetX = MathF.Cos(angle) * dist,
                OffsetY = MathF.Sin(angle) * dist,
                Radius = r,
                DripFactor = 0.4f + (float)_rng.NextDouble() * 1.1f
            };
        }

        _splatters.Add(splatter);
    }

    private static float ScoopSnowFromBank(SurfaceSnowBank bank, int centerLocalBucket, int radiusBuckets, float amount, int maxWinBuckets)
    {
        float totalScooped = 0f;
        for (int b = centerLocalBucket - radiusBuckets; b <= centerLocalBucket + radiusBuckets; b++)
        {
            if (b < 0 || b >= maxWinBuckets) continue;
            float take = Math.Min(bank.Heights[b], amount);
            if (take > 0f)
            {
                bank.Heights[b] -= take;
                totalScooped += take;
                bank.IsDirty = true;
            }
        }
        return totalScooped;
    }

    private bool TryStackSnowball(Snowball topBall)
    {
        for (int i = 0; i < _snowmen.Count; i++)
        {
            Snowman sm = _snowmen[i];
            if (sm.IsHiddenWithWindow || sm.Radii.Count >= 3) continue;

            float headY = sm.BottomY - sm.TotalHeight;
            float topRadius = sm.Radii[^1];
            float dx = Math.Abs(topBall.X - sm.X);
            float dy = Math.Abs((topBall.Y + topBall.Radius) - headY);

            if (dx <= topRadius + topBall.Radius * 0.9f && dy <= topRadius + topBall.Radius + 22f && topBall.Radius <= topRadius * 1.2f)
            {
                sm.Radii.Add(Math.Min(topBall.Radius, topRadius * 0.92f));
                sm.IsDressed = true;
                sm.ScarfColorIndex = (byte)_rng.Next(0, ScarfColors.Length);
                sm.TiltVelocity += (topBall.X - sm.X) * 0.25f;
                _snowballs.Remove(topBall);
                return true;
            }
        }

        for (int i = 0; i < _snowballs.Count; i++)
        {
            Snowball baseBall = _snowballs[i];
            if (baseBall.IsHiddenWithWindow || baseBall.IsSnowmanProjectile || ReferenceEquals(baseBall, topBall)) continue;

            float dx = Math.Abs(topBall.X - baseBall.X);
            float dy = baseBall.Y - topBall.Y;

            if (dx <= baseBall.Radius + topBall.Radius * 0.85f && dy > -6f && dy <= baseBall.Radius + topBall.Radius + 26f)
            {
                float bottomR = Math.Max(baseBall.Radius, topBall.Radius);
                float upperR = Math.Min(baseBall.Radius, topBall.Radius);
                if (upperR > bottomR * 0.9f) upperR = bottomR * 0.82f;

                var snowman = new Snowman
                {
                    X = baseBall.X,
                    BottomY = baseBall.Y + baseBall.Radius,
                    IsGrounded = baseBall.IsGrounded,
                    GroundedWindowHandle = baseBall.GroundedWindowHandle,
                    OffsetFromWindowLeft = baseBall.OffsetFromWindowLeft,
                    ScarfColorIndex = (byte)_rng.Next(0, ScarfColors.Length),
                    ThrowCooldown = 2.5f + (float)_rng.NextDouble() * 3.0f
                };
                snowman.Radii.Add(bottomR);
                snowman.Radii.Add(upperR);

                _snowmen.Add(snowman);
                _snowballs.Remove(baseBall);
                _snowballs.Remove(topBall);
                return true;
            }
        }

        return false;
    }

    public void Update(float dt)
    {
        _blizzardGustPhase += dt * 2.2f;

        _windTimer -= dt;
        if (_windTimer <= 0f)
        {
            _windTimer = _isBlizzardMode
                ? Math.Max(0.8f, _windChangeInterval * 0.4f)
                : _windChangeInterval * (0.75f + (float)_rng.NextDouble() * 0.5f);

            float baseTarget = ((float)_rng.NextDouble() - 0.5f) * 65f;
            if (_isBlizzardMode)
            {
                baseTarget = 165f + (float)_rng.NextDouble() * 95f;
            }
            _targetWind = baseTarget;
        }

        float effectiveMultiplier = _isBlizzardMode ? Math.Max(1.8f, _windMultiplier * 1.9f) : _windMultiplier;
        float gust = _isBlizzardMode ? MathF.Sin(_blizzardGustPhase) * 55f : 0f;
        _globalWind += ((_targetWind * effectiveMultiplier + gust) - _globalWind) * dt * 1.2f;

        NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
        float mouseX = pt.X - _originX;
        float mouseY = pt.Y - _originY;

        if (_resetMouseDelta)
        {
            _prevMouseX = mouseX;
            _prevMouseY = mouseY;
            _resetMouseDelta = false;
        }

        float moveDx = mouseX - _prevMouseX;
        float moveDy = mouseY - _prevMouseY;
        float moveDist = MathF.Sqrt(moveDx * moveDx + moveDy * moveDy);
        float mouseVx = moveDx / dt;
        float mouseVy = moveDy / dt;
        float mouseSpeed = moveDist / dt;

        if (moveDist > 1.5f)
        {
            _idleSeconds = 0f;
        }
        else
        {
            _idleSeconds += dt;
        }

        _prevMouseX = mouseX;
        _prevMouseY = mouseY;

        _tracker.SyncSurfacesForFrame(_surfacesSnapshot);

        UpdateDraggedObjects(dt, mouseX, mouseY, moveDist, mouseVx, mouseVy);
        SyncSnowBanksWithWindows(dt, mouseX, mouseY, mouseVx, mouseVy, mouseSpeed);
        BuildLandingCache();
        UpdateSnowballs(dt);
        UpdateSnowmen(dt);
        UpdateAkBulletsAndShells(dt);
        UpdateScreenSplatters(dt);
        UpdateParticles(dt, mouseX, mouseY, mouseVx, mouseVy);
        UpdateSplashParticles(dt);
    }

    private void BuildLandingCache()
    {
        int count = _surfacesSnapshot.Count;
        if (_landingCache.Length < count)
        {
            Array.Resize(ref _landingCache, count + 16);
        }

        int writeIdx = 0;
        for (int i = 0; i < count; i++)
        {
            TrackedSurface surf = _surfacesSnapshot[i];
            if (!CanAccumulateOnSurface(in surf)) continue;

            if (_snowBanks.TryGetValue(surf.Handle, out SurfaceSnowBank? bank))
            {
                _landingCache[writeIdx++] = new LandingSurfaceEntry
                {
                    SnapshotIndex = i,
                    Left = surf.Left,
                    Right = surf.Right,
                    TopY = surf.TopY,
                    WinBuckets = GetWindowBucketCount(in surf, _bucketCount),
                    IsTaskbar = surf.IsTaskbar,
                    Bank = bank
                };
            }
        }
        _landingCacheCount = writeIdx;
    }

    private void UpdateDraggedObjects(float dt, float mouseX, float mouseY, float moveDist, float mouseVx, float mouseVy)
    {
        if (_activeSnowman != null)
        {
            _throwVx = _throwVx * 0.45f + mouseVx * 0.55f;
            _throwVy = _throwVy * 0.45f + mouseVy * 0.55f;

            _activeSnowman.X = Math.Clamp(mouseX, _activeSnowman.MaxRadius, _width - _activeSnowman.MaxRadius);
            _activeSnowman.BottomY = Math.Clamp(mouseY + _activeSnowman.TotalHeight * 0.45f, _activeSnowman.TotalHeight, _height);

            float targetTilt = Math.Clamp(-mouseVx * 0.00055f, -0.50f, 0.50f);
            float springForce = (targetTilt - _activeSnowman.TiltAngle) * 42f;
            _activeSnowman.TiltVelocity += springForce * dt;
            float tiltDamping = Math.Max(0f, 1f - 2.8f * dt);
            _activeSnowman.TiltVelocity *= tiltDamping;
            _activeSnowman.TiltAngle = Math.Clamp(_activeSnowman.TiltAngle + _activeSnowman.TiltVelocity * dt, -0.58f, 0.58f);
            return;
        }

        if (_activeSnowball != null)
        {
            _throwVx = _throwVx * 0.45f + mouseVx * 0.55f;
            _throwVy = _throwVy * 0.45f + mouseVy * 0.55f;

            _activeSnowball.X = Math.Clamp(mouseX, _activeSnowball.Radius, _width - _activeSnowball.Radius);
            _activeSnowball.Y = Math.Clamp(mouseY, _activeSnowball.Radius, _height - _activeSnowball.Radius);

            if (moveDist > 0.5f && _activeSnowball.Radius < 35f)
            {
                float growth = moveDist * 0.025f;

                for (int i = 0; i < _landingCacheCount; i++)
                {
                    ref LandingSurfaceEntry entry = ref _landingCache[i];
                    if (mouseX < entry.Left || mouseX > entry.Right) continue;

                    if (!entry.IsTaskbar &&
                        IsTopEdgeCovered((int)mouseX, entry.TopY, Math.Min(entry.SnapshotIndex, _surfacesSnapshot.Count)))
                    {
                        continue;
                    }

                    float baseY = entry.TopY + SurfaceContactOffset;
                    if (Math.Abs(mouseY - baseY) <= _activeSnowball.Radius + 24f)
                    {
                        int localB = GetLocalBucketIndex(mouseX, entry.Left, entry.WinBuckets);
                        float scooped = ScoopSnowFromBank(entry.Bank, localB, 1, 2.2f, entry.WinBuckets);
                        growth += scooped * 0.16f;
                    }
                }

                _activeSnowball.Radius = Math.Min(35f, _activeSnowball.Radius + growth);
            }
        }
    }

    private int FindSurfaceIndexByHandle(IntPtr handle)
    {
        for (int i = 0; i < _surfacesSnapshot.Count; i++)
        {
            if (_surfacesSnapshot[i].Handle == handle)
                return i;
        }
        return -1;
    }

    private bool IsWindowStillAliveAndHidden(IntPtr hwnd)
    {
        return _tracker.IsWindowMinimizingOrHidden(hwnd);
    }

    private void UpdateSnowballs(float dt)
    {
        float gravity = BaseGravity * _objectGravityMultiplier;
        float wallThreshold = BaseWallShatterSpeed * _snowballToughnessMultiplier;
        float surfaceShatterThreshold = 950f * _snowballToughnessMultiplier;
        float airDrag = Math.Max(0f, 1f - 0.28f * dt);

        for (int i = _snowballs.Count - 1; i >= 0; i--)
        {
            Snowball sb = _snowballs[i];
            if (ReferenceEquals(sb, _activeSnowball)) continue;

            if (sb.IsGrounded && sb.GroundedWindowHandle != IntPtr.Zero)
            {
                int surfIdx = FindSurfaceIndexByHandle(sb.GroundedWindowHandle);
                if (surfIdx >= 0)
                {
                    sb.IsHiddenWithWindow = false;
                    TrackedSurface surf = _surfacesSnapshot[surfIdx];
                    sb.X = surf.Left + sb.OffsetFromWindowLeft;

                    bool canStay = CanAccumulateOnSurface(in surf);
                    bool outOfBounds = sb.X < surf.Left + 2 || sb.X > surf.Right - 2 || sb.X < 0 || sb.X > _width;

                    if (canStay && !outOfBounds)
                    {
                        sb.Y = surf.TopY + SurfaceContactOffset - sb.Radius;
                        continue;
                    }
                }
                else if (_keepSnowOnHiddenWindows && IsWindowStillAliveAndHidden(sb.GroundedWindowHandle))
                {
                    sb.IsHiddenWithWindow = true;
                    continue;
                }

                sb.IsHiddenWithWindow = false;
                sb.IsGrounded = false;
                sb.GroundedWindowHandle = IntPtr.Zero;
            }

            if (sb.IsGrounded) continue;

            float prevY = sb.Y;
            sb.Vy += gravity * dt;
            sb.Vx *= airDrag;
            sb.X += sb.Vx * dt;
            sb.Y += sb.Vy * dt;

            bool hitOtherSnowman = false;
            for (int m = _snowmen.Count - 1; m >= 0; m--)
            {
                Snowman targetSm = _snowmen[m];
                if (targetSm.IsHiddenWithWindow || ReferenceEquals(targetSm, sb.OwnerSnowman)) continue;

                float smTop = targetSm.BottomY - targetSm.TotalHeight;
                if (Math.Abs(sb.X - targetSm.X) <= targetSm.MaxRadius + sb.Radius &&
                    sb.Y >= smTop - sb.Radius && sb.Y <= targetSm.BottomY)
                {
                    targetSm.TiltVelocity += Math.Clamp(sb.Vx * 0.015f, -4.5f, 4.5f);
                    ShatterSnowball(sb);
                    _snowballs.RemoveAt(i);
                    hitOtherSnowman = true;
                    break;
                }
            }
            if (hitOtherSnowman) continue;

            if (sb.X - sb.Radius <= 0f)
            {
                if (_snowballToughnessMultiplier < 2.4f && Math.Abs(sb.Vx) > wallThreshold)
                {
                    SpawnRealisticSplatter(sb.Radius + 10f, sb.Y, sb.Radius * 1.45f);
                    sb.X = sb.Radius;
                    ShatterSnowballAgainstWall(sb, hitLeftWall: true);
                    _snowballs.RemoveAt(i);
                    continue;
                }
                sb.X = sb.Radius;
                sb.Vx = -sb.Vx * 0.55f;
            }
            else if (sb.X + sb.Radius >= _width)
            {
                if (_snowballToughnessMultiplier < 2.4f && Math.Abs(sb.Vx) > wallThreshold)
                {
                    SpawnRealisticSplatter(_width - sb.Radius - 10f, sb.Y, sb.Radius * 1.45f);
                    sb.X = _width - sb.Radius;
                    ShatterSnowballAgainstWall(sb, hitLeftWall: false);
                    _snowballs.RemoveAt(i);
                    continue;
                }
                sb.X = _width - sb.Radius;
                sb.Vx = -sb.Vx * 0.55f;
            }

            bool hitSurface = false;
            if (sb.Vy > 0f)
            {
                for (int s = 0; s < _surfacesSnapshot.Count; s++)
                {
                    TrackedSurface surf = _surfacesSnapshot[s];
                    if (!CanAccumulateOnSurface(in surf)) continue;
                    if (sb.X < surf.Left || sb.X > surf.Right) continue;

                    float targetY = surf.TopY + SurfaceContactOffset - sb.Radius;
                    if (prevY <= targetY + 12f && sb.Y >= targetY)
                    {
                        if (!surf.IsTaskbar && IsTopEdgeOccludedByHigherWindow((int)sb.X, surf.TopY, s))
                            continue;

                        float impactSpeed = sb.Vy;
                        sb.Y = targetY;

                        if (sb.IsSnowmanProjectile || (_snowballToughnessMultiplier < 2.4f && impactSpeed > surfaceShatterThreshold))
                        {
                            if (!sb.IsSnowmanProjectile)
                            {
                                SpawnRealisticSplatter(sb.X, sb.Y, sb.Radius * 1.3f);
                            }
                            ShatterSnowball(sb);
                            _snowballs.RemoveAt(i);
                            hitSurface = true;
                            break;
                        }

                        if (impactSpeed > 180f)
                        {
                            sb.Vy = -impactSpeed * 0.35f;
                            sb.Vx *= 0.65f;
                        }
                        else
                        {
                            sb.Vy = 0f;
                            sb.Vx = 0f;
                            sb.IsGrounded = true;
                            sb.IsHiddenWithWindow = false;
                            sb.GroundedWindowHandle = surf.Handle;
                            sb.OffsetFromWindowLeft = sb.X - surf.Left;
                        }
                        hitSurface = true;
                        break;
                    }
                }
            }

            if (hitSurface) continue;

            if (sb.Y - sb.Radius > _height + 30f)
            {
                _snowballs.RemoveAt(i);
            }
        }
    }

    private void ShatterSnowball(Snowball sb)
    {
        int count = Math.Clamp((int)(sb.Radius * 1.6f), 12, 38);
        for (int c = 0; c < count; c++)
        {
            float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
            float speed = 70f + (float)_rng.NextDouble() * 240f;
            SpawnSplashParticle(
                sb.X + MathF.Cos(angle) * sb.Radius * 0.4f,
                sb.Y + MathF.Sin(angle) * sb.Radius * 0.4f,
                MathF.Cos(angle) * speed,
                MathF.Sin(angle) * speed - 85f);
        }
    }

    private void ShatterSnowballAgainstWall(Snowball sb, bool hitLeftWall)
    {
        int count = Math.Clamp((int)(sb.Radius * 1.8f), 14, 42);
        float dirX = hitLeftWall ? 1f : -1f;

        for (int c = 0; c < count; c++)
        {
            float vx = dirX * (60f + (float)_rng.NextDouble() * 260f);
            float vy = ((float)_rng.NextDouble() - 0.65f) * 280f;
            SpawnSplashParticle(
                sb.X,
                sb.Y + ((float)_rng.NextDouble() - 0.5f) * sb.Radius,
                vx,
                vy);
        }
    }

    private void ShatterSnowman(Snowman sm, bool spawnSplatters)
    {
        float currentY = sm.BottomY;
        for (int i = 0; i < sm.Radii.Count; i++)
        {
            float r = sm.Radii[i];
            float centerY = i == 0 ? currentY - r : currentY - r * 0.85f;
            int count = Math.Clamp((int)(r * 1.3f), 10, 28);

            if (spawnSplatters)
            {
                SpawnRealisticSplatter(sm.X, centerY, r * 1.38f);
            }

            for (int c = 0; c < count; c++)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                float speed = 65f + (float)_rng.NextDouble() * 220f;
                SpawnSplashParticle(
                    sm.X + MathF.Cos(angle) * r * 0.4f,
                    centerY + MathF.Sin(angle) * r * 0.4f,
                    MathF.Cos(angle) * speed,
                    MathF.Sin(angle) * speed - 90f);
            }
            currentY = centerY - r * 0.8f;
        }
    }

    private bool IsAk47EasterEggActive()
    {
        return _enableLivingSnowmen && _forceAk47EasterEgg && _snowmen.Count >= 2;
    }

    private void UpdateSnowmen(float dt)
    {
        float gravity = BaseGravity * _objectGravityMultiplier;
        float wallThreshold = 580f * _snowballToughnessMultiplier;
        float landingShatterThreshold = 980f * _snowballToughnessMultiplier;
        float tiltDamping = Math.Max(0f, 1f - 2.5f * dt);
        float airDrag = Math.Max(0f, 1f - 0.28f * dt);
        bool akMode = IsAk47EasterEggActive();

        for (int i = _snowmen.Count - 1; i >= 0; i--)
        {
            Snowman sm = _snowmen[i];
            sm.HasAk47 = akMode && sm.Radii.Count >= 2;
            if (sm.AkMuzzleFlashTimer > 0f) sm.AkMuzzleFlashTimer -= dt;
            if (sm.AkRecoilOffset > 0f) sm.AkRecoilOffset = Math.Max(0f, sm.AkRecoilOffset - dt * 28f);

            if (ReferenceEquals(sm, _activeSnowman)) continue;

            if (sm.IsGrounded && sm.GroundedWindowHandle != IntPtr.Zero)
            {
                int surfIdx = FindSurfaceIndexByHandle(sm.GroundedWindowHandle);
                if (surfIdx >= 0)
                {
                    sm.IsHiddenWithWindow = false;
                    TrackedSurface surf = _surfacesSnapshot[surfIdx];

                    float prevX = sm.X;
                    sm.X = surf.Left + sbOffsetSafe(sm.OffsetFromWindowLeft);
                    float windowMoveVx = (sm.X - prevX) / dt;
                    if (Math.Abs(windowMoveVx) > 5f)
                    {
                        sm.TiltVelocity += Math.Clamp(-windowMoveVx * 0.0012f, -0.8f, 0.8f);
                    }

                    float windLean = Math.Clamp(_globalWind * 0.0004f, -0.08f, 0.08f);
                    float restoring = (windLean - sm.TiltAngle) * 32f;
                    sm.TiltVelocity += restoring * dt;
                    sm.TiltVelocity *= tiltDamping;
                    sm.TiltAngle = Math.Clamp(sm.TiltAngle + sm.TiltVelocity * dt, -0.55f, 0.55f);

                    bool canStay = CanAccumulateOnSurface(in surf);
                    bool outOfBounds = sm.X < surf.Left + 4 || sm.X > surf.Right - 4 || sm.X < 0 || sm.X > _width;

                    if (canStay && !outOfBounds)
                    {
                        sm.BottomY = surf.TopY + SurfaceContactOffset;
                        if (surfIdx == 0 || !IsPointCoveredByHigherWindow(sm.X, sm.BottomY - sm.TotalHeight * 0.5f, surfIdx))
                        {
                            UpdateLivingSnowmanAi(sm, dt);
                        }
                        continue;
                    }
                }
                else if (_keepSnowOnHiddenWindows && IsWindowStillAliveAndHidden(sm.GroundedWindowHandle))
                {
                    sm.IsHiddenWithWindow = true;
                    continue;
                }

                sm.IsHiddenWithWindow = false;
                sm.IsGrounded = false;
                sm.GroundedWindowHandle = IntPtr.Zero;
            }

            if (sm.IsGrounded) continue;

            float windLeanFree = Math.Clamp(_globalWind * 0.0004f, -0.08f, 0.08f);
            sm.TiltVelocity += ((windLeanFree - sm.TiltAngle) * 32f) * dt;
            sm.TiltVelocity *= tiltDamping;
            sm.TiltAngle = Math.Clamp(sm.TiltAngle + sm.TiltVelocity * dt, -0.55f, 0.55f);

            float prevBottomY = sm.BottomY;
            sm.Vy += gravity * dt;
            sm.Vx *= airDrag;
            sm.X += sm.Vx * dt;
            sm.BottomY += sm.Vy * dt;

            sm.TiltVelocity += Math.Clamp(-sm.Vx * 0.002f, -1.5f, 1.5f) * dt;

            float maxR = sm.MaxRadius;
            if (sm.X - maxR <= 0f)
            {
                if (_snowballToughnessMultiplier < 2.4f && Math.Abs(sm.Vx) > wallThreshold)
                {
                    sm.X = maxR + 6f;
                    ShatterSnowman(sm, spawnSplatters: true);
                    _snowmen.RemoveAt(i);
                    continue;
                }
                sm.X = maxR;
                sm.Vx = -sm.Vx * 0.45f;
                sm.TiltVelocity += 2.5f;
            }
            else if (sm.X + maxR >= _width)
            {
                if (_snowballToughnessMultiplier < 2.4f && Math.Abs(sm.Vx) > wallThreshold)
                {
                    sm.X = _width - maxR - 6f;
                    ShatterSnowman(sm, spawnSplatters: true);
                    _snowmen.RemoveAt(i);
                    continue;
                }
                sm.X = _width - maxR;
                sm.Vx = -sm.Vx * 0.45f;
                sm.TiltVelocity -= 2.5f;
            }

            if (sm.Vy > 0f)
            {
                bool landed = false;
                for (int s = 0; s < _surfacesSnapshot.Count; s++)
                {
                    TrackedSurface surf = _surfacesSnapshot[s];
                    if (!CanAccumulateOnSurface(in surf)) continue;
                    if (sm.X < surf.Left || sm.X > surf.Right) continue;

                    float targetY = surf.TopY + SurfaceContactOffset;
                    if (prevBottomY <= targetY + 14f && sm.BottomY >= targetY)
                    {
                        if (!surf.IsTaskbar && IsTopEdgeOccludedByHigherWindow((int)sm.X, surf.TopY, s))
                            continue;

                        if (_snowballToughnessMultiplier < 2.4f && sm.Vy > landingShatterThreshold)
                        {
                            ShatterSnowman(sm, spawnSplatters: true);
                            _snowmen.RemoveAt(i);
                            landed = true;
                            break;
                        }

                        sm.BottomY = targetY;
                        sm.TiltVelocity += Math.Clamp(sm.Vx * 0.008f, -3.5f, 3.5f);
                        sm.Vx = 0f;
                        sm.Vy = 0f;
                        sm.IsGrounded = true;
                        sm.IsHiddenWithWindow = false;
                        sm.GroundedWindowHandle = surf.Handle;
                        sm.OffsetFromWindowLeft = sm.X - surf.Left;
                        landed = true;
                        break;
                    }
                }
                if (landed) continue;
            }

            if (sm.BottomY - sm.TotalHeight > _height + 50f)
            {
                _snowmen.RemoveAt(i);
            }
        }
    }

    private Snowman? FindBestTargetSnowman(Snowman sm)
    {
        Snowman? target = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _snowmen.Count; i++)
        {
            Snowman other = _snowmen[i];
            if (ReferenceEquals(other, sm) || other.IsHiddenWithWindow) continue;
            float dist = Math.Abs(other.X - sm.X);
            if (dist > 35f && dist < bestDist)
            {
                bestDist = dist;
                target = other;
            }
        }
        return target;
    }

    private void UpdateLivingSnowmanAi(Snowman sm, float dt)
    {
        if (!_enableLivingSnowmen || _snowmen.Count < 2)
            return;

        Snowman? target = FindBestTargetSnowman(sm);
        if (target == null) return;

        float dx = target.X - sm.X;
        float dy = (target.BottomY - target.TotalHeight * 0.55f) - (sm.BottomY - sm.TotalHeight * 0.55f);
        sm.FacingDir = dx >= 0f ? (sbyte)1 : (sbyte)-1;

        if (sm.HasAk47)
        {
            float rawAim = MathF.Atan2(dy, Math.Abs(dx));
            sm.AkAimAngle = Math.Clamp(rawAim, -0.55f, 0.55f);

            if (sm.AkBurstShotsLeft > 0)
            {
                sm.AkShotTimer -= dt;
                if (sm.AkShotTimer <= 0f)
                {
                    sm.AkShotTimer = 0.095f;
                    sm.AkBurstShotsLeft--;
                    FireAk47Shot(sm, dx, dy);
                }
                return;
            }

            sm.ThrowCooldown -= dt;
            if (sm.ThrowCooldown <= 0f)
            {
                sm.ThrowCooldown = 1.4f + (float)_rng.NextDouble() * 1.8f;
                sm.AkBurstShotsLeft = _rng.Next(4, 9);
                sm.AkShotTimer = 0f;
            }
            return;
        }

        if (_snowballs.Count >= 24)
            return;

        sm.ThrowCooldown -= dt;
        if (sm.ThrowCooldown > 0f)
            return;

        sm.ThrowCooldown = 3.5f + (float)_rng.NextDouble() * 4.5f;

        float flightTime = Math.Clamp(Math.Abs(dx) / 520f, 0.35f, 1.25f);
        float gravity = BaseGravity * _objectGravityMultiplier;
        float vx = (dx / flightTime) + ((float)_rng.NextDouble() - 0.5f) * 45f;
        float vy = (dy - 0.5f * gravity * flightTime * flightTime) / flightTime;

        sm.TiltVelocity -= Math.Sign(dx) * 1.4f;

        _snowballs.Add(new Snowball
        {
            X = sm.X + sm.FacingDir * (sm.MaxRadius + 6f),
            Y = sm.BottomY - sm.TotalHeight * 0.65f,
            Vx = Math.Clamp(vx, -950f, 950f),
            Vy = Math.Clamp(vy, -950f, 350f),
            Radius = 7.5f,
            IsGrounded = false,
            IsSnowmanProjectile = true,
            OwnerSnowman = sm
        });
    }

    private void FireAk47Shot(Snowman sm, float dx, float dy)
    {
        float gunY = sm.BottomY - sm.TotalHeight * 0.52f;
        float gunX = sm.X + sm.FacingDir * (sm.MaxRadius * 1.55f);

        float baseAngle = MathF.Atan2(dy, dx);
        float spread = ((float)_rng.NextDouble() - 0.5f) * 0.085f;
        float shotAngle = baseAngle + spread;
        const float bulletSpeed = 1480f;

        SpawnAkBullet(
            sm,
            gunX,
            gunY,
            MathF.Cos(shotAngle) * bulletSpeed,
            MathF.Sin(shotAngle) * bulletSpeed);

        SpawnShellCasing(sm.X + sm.FacingDir * (sm.MaxRadius * 0.45f), gunY - 3f, sm.FacingDir);

        sm.AkMuzzleFlashTimer = 0.055f;
        sm.AkRecoilOffset = 4.2f;
        sm.TiltVelocity -= sm.FacingDir * 0.95f;
    }

    private void UpdateAkBulletsAndShells(float dt)
    {
        float gravity = BaseGravity * _objectGravityMultiplier;

        for (int i = 0; i < MaxAkBullets; i++)
        {
            ref AkBullet b = ref _akBullets[i];
            if (!b.Active) continue;

            b.Life -= dt;
            if (b.Life <= 0f || b.X < -30f || b.X > _width + 30f || b.Y < -30f || b.Y > _height + 30f)
            {
                b.Active = false;
                continue;
            }

            b.Vy += gravity * 0.08f * dt;
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;

            for (int m = _snowmen.Count - 1; m >= 0; m--)
            {
                Snowman target = _snowmen[m];
                if (target.IsHiddenWithWindow || ReferenceEquals(target, b.Owner)) continue;

                float smTop = target.BottomY - target.TotalHeight;
                if (Math.Abs(b.X - target.X) <= target.MaxRadius * 0.95f &&
                    b.Y >= smTop && b.Y <= target.BottomY)
                {
                    target.TiltVelocity += Math.Clamp(b.Vx * 0.0022f, -3.5f, 3.5f);
                    for (int s = 0; s < 5; s++)
                    {
                        SpawnSplashParticle(
                            b.X,
                            b.Y,
                            -b.Vx * 0.12f + ((float)_rng.NextDouble() - 0.5f) * 140f,
                            -60f - (float)_rng.NextDouble() * 110f);
                    }
                    b.Active = false;
                    break;
                }
            }
        }

        for (int i = 0; i < MaxShellCasings; i++)
        {
            ref ShellCasing sc = ref _shellCasings[i];
            if (!sc.Active) continue;

            sc.Life -= dt;
            if (sc.Life <= 0f || sc.Y > _height + 20f)
            {
                sc.Active = false;
                continue;
            }

            sc.Vy += gravity * 0.65f * dt;
            sc.X += sc.Vx * dt;
            sc.Y += sc.Vy * dt;
            sc.Angle += sc.AngularVel * dt;
        }
    }

    private void UpdateScreenSplatters(float dt)
    {
        for (int i = _splatters.Count - 1; i >= 0; i--)
        {
            ScreenSplatter sp = _splatters[i];
            sp.Life -= dt;
            if (sp.Life <= 0f)
            {
                _splatters.RemoveAt(i);
                continue;
            }
            sp.SlideDistance += sp.SlideSpeed * dt;
            sp.Y += sp.SlideSpeed * 0.35f * dt;
        }
    }

    private static float sbOffsetSafe(float val) => float.IsNaN(val) ? 0f : val;

    private void UpdateParticles(float dt, float mouseX, float mouseY, float mouseVx, float mouseVy)
    {
        float cursorRadius = 65f * _cursorScatterPower;
        float cursorRadiusSq = cursorRadius * cursorRadius;
        int count = _activeParticles;
        float speedFactor = _isBlizzardMode ? _speedMultiplier * 1.45f : _speedMultiplier;

        float particleDamping = Math.Max(0f, 1f - 2.1f * dt);

        for (int i = 0; i < count; i++)
        {
            ref Particle p = ref _particles[i];
            p.Phase += p.SwingFrequency * dt;

            p.Vx *= particleDamping;
            float targetVy = (38f + p.Radius * 16f) * speedFactor;
            p.Vy += (targetVy - p.Vy) * dt * 2.5f;

            if (_enableCursorScatter && _activeSnowball == null && _activeSnowman == null)
            {
                float dx = p.X - mouseX;
                float dy = p.Y - mouseY;
                float distSq = dx * dx + dy * dy;
                if (distSq < cursorRadiusSq && distSq > 1f)
                {
                    float dist = MathF.Sqrt(distSq);
                    float factor = (1f - dist / cursorRadius) * _cursorScatterPower;
                    p.Vx += (dx / dist) * 180f * factor * dt + mouseVx * 0.25f * factor;
                    p.Vy += (dy / dist) * 140f * factor * dt + mouseVy * 0.20f * factor;
                }
            }

            float sway = MathF.Sin(p.Phase) * p.SwingAmplitude * _swayMultiplier;
            float prevY = p.Y;

            p.X += (_globalWind + sway + p.Vx) * dt;
            p.Y += p.Vy * dt;

            if (p.X < 0) p.X += _width;
            else if (p.X >= _width) p.X -= _width;

            if (p.Vy > 0f && TryLandParticleOnSurface(ref p, prevY))
            {
                ResetParticle(ref p, randomY: false);
                continue;
            }

            if (p.Y > _height + 15f)
            {
                ResetParticle(ref p, randomY: false);
            }
        }
    }

    private void UpdateSplashParticles(float dt)
    {
        float gravity = BaseGravity * _objectGravityMultiplier;
        float splashDamping = Math.Max(0f, 1f - 1.05f * dt);

        for (int i = 0; i < MaxSplashParticles; i++)
        {
            ref SplashParticle sp = ref _splashParticles[i];
            if (!sp.Active) continue;

            sp.Life -= dt;
            if (sp.Life <= 0f || sp.Y > _height + 15f)
            {
                sp.Active = false;
                continue;
            }

            sp.Vy += gravity * 0.42f * dt;
            sp.Vx *= splashDamping;
            sp.X += (sp.Vx + _globalWind * 0.4f) * dt;
            sp.Y += sp.Vy * dt;
        }
    }

    private void SyncSnowBanksWithWindows(float dt, float mouseX, float mouseY, float mouseVx, float mouseVy, float mouseSpeed)
    {
        foreach (var bank in _snowBanks.Values)
        {
            bank.SeenInCurrentFrame = false;
        }

        bool isLeftMouseButtonHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

        for (int i = 0; i < _surfacesSnapshot.Count; i++)
        {
            TrackedSurface surf = _surfacesSnapshot[i];
            if (!CanAccumulateOnSurface(in surf))
            {
                if (_snowBanks.TryGetValue(surf.Handle, out SurfaceSnowBank? maxBank) && maxBank.HasAnySnow)
                {
                    if (surf.IsMaximizedOrTopDocked)
                    {
                        ScatterBankIntoParticles(maxBank);
                    }
                    else
                    {
                        maxBank.SeenInCurrentFrame = true;
                    }
                }
                continue;
            }

            if (!_snowBanks.TryGetValue(surf.Handle, out SurfaceSnowBank? bank))
            {
                bank = new SurfaceSnowBank(_bucketCount)
                {
                    Handle = surf.Handle,
                    LastLeft = surf.Left,
                    LastRight = surf.Right,
                    LastTopY = surf.TopY,
                    LastBottomY = surf.BottomY,
                    IsTaskbar = surf.IsTaskbar,
                    SeenInCurrentFrame = true
                };
                _snowBanks[surf.Handle] = bank;
            }
            else
            {
                bank.SeenInCurrentFrame = true;

                int prevWidth = bank.LastRight - bank.LastLeft;
                int currWidth = surf.Right - surf.Left;

                if (Math.Abs(currWidth - prevWidth) > 45)
                {
                    ScatterBankIntoParticles(bank);
                }
                else
                {
                    int deltaX = surf.Left - bank.LastLeft;
                    int deltaY = surf.TopY - bank.LastTopY;
                    float moveStep = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);

                    if (moveStep > 0.5f && bank.HasAnySnow)
                    {
                        ApplyWindowMotionErosion(in surf, bank, deltaX, deltaY, moveStep);
                    }

                    if (_enableWindowShakeShed)
                    {
                        UpdateWindowShake(in surf, bank, dt, deltaX, deltaY);
                    }
                }

                bank.LastLeft = surf.Left;
                bank.LastRight = surf.Right;
                bank.LastTopY = surf.TopY;
                bank.LastBottomY = surf.BottomY;
            }

            if (!bank.HasAnySnow) continue;

            int winBuckets = GetWindowBucketCount(in surf, _bucketCount);

            if (!isLeftMouseButtonHeld && _enableCursorScatter && _activeSnowball == null && _activeSnowman == null &&
                mouseSpeed > (200f / _cursorScatterPower) && Math.Abs(mouseY - surf.TopY) < 42f * _cursorScatterPower &&
                mouseX >= surf.Left && mouseX <= surf.Right)
            {
                int centerLocalB = GetLocalBucketIndex(mouseX, surf.Left, winBuckets);
                int radiusBuckets = Math.Max(2, (int)(4 * _cursorScatterPower));

                for (int b = centerLocalB - radiusBuckets; b <= centerLocalB + radiusBuckets; b++)
                {
                    if (b < 0 || b >= winBuckets) continue;

                    if (!surf.IsTaskbar &&
                        IsTopEdgeCovered(surf.Left + b * BucketSize, surf.TopY, i))
                    {
                        continue;
                    }

                    float h = bank.Heights[b];
                    if (h > 1.0f)
                    {
                        float removed = h * Math.Min(0.85f, 0.50f * _cursorScatterPower);
                        bank.Heights[b] -= removed;
                        bank.IsDirty = true;

                        SpawnSplashParticle(
                            surf.Left + b * BucketSize + (float)_rng.NextDouble() * BucketSize,
                            surf.TopY + SurfaceContactOffset - h * 0.5f,
                            Math.Clamp(mouseVx * 0.32f * _cursorScatterPower, -280f, 280f),
                            -55f - (float)_rng.NextDouble() * 80f);
                    }
                }
            }

            if (bank.IsDirty)
            {
                SimulateSnowAvalancheAndEdges(in surf, bank, winBuckets);
            }
        }

        _cleanupKeys.Clear();
        foreach (var kvp in _snowBanks)
        {
            if (!kvp.Value.SeenInCurrentFrame)
            {
                if (_keepSnowOnHiddenWindows && NativeMethods.IsWindow(kvp.Key) && !NativeMethods.IsZoomed(kvp.Key))
                {
                    continue;
                }

                ScatterBankIntoParticles(kvp.Value);
                _cleanupKeys.Add(kvp.Key);
            }
        }

        for (int i = 0; i < _cleanupKeys.Count; i++)
        {
            _snowBanks.Remove(_cleanupKeys[i]);
        }
    }

    private void ApplyWindowMotionErosion(in TrackedSurface surf, SurfaceSnowBank bank, int deltaX, int deltaY, float moveStep)
    {
        int winBuckets = GetWindowBucketCount(in surf, _bucketCount);
        if (winBuckets <= 3) return;

        if (Math.Abs(deltaX) >= 2)
        {
            float shiftFactor = Math.Clamp(Math.Abs(deltaX) * 0.015f, 0.02f, 0.22f);
            if (deltaX > 0)
            {
                for (int b = 1; b < winBuckets - 1; b++)
                {
                    float transfer = bank.Heights[b] * shiftFactor;
                    bank.Heights[b] -= transfer;
                    bank.Heights[b - 1] = Math.Min(_maxSnowHeight * 1.15f, bank.Heights[b - 1] + transfer);
                }
            }
            else
            {
                for (int b = winBuckets - 2; b >= 1; b--)
                {
                    float transfer = bank.Heights[b] * shiftFactor;
                    bank.Heights[b] -= transfer;
                    bank.Heights[b + 1] = Math.Min(_maxSnowHeight * 1.15f, bank.Heights[b + 1] + transfer);
                }
            }
            bank.IsDirty = true;
        }

        if (moveStep > 18f)
        {
            float shedRatio = Math.Clamp((moveStep - 18f) * 0.0008f, 0.0f, 0.015f);
            for (int b = 1; b < winBuckets - 1; b++)
            {
                float h = bank.Heights[b];
                if (h > 4.0f)
                {
                    bank.Heights[b] -= h * shedRatio;
                    bank.IsDirty = true;

                    if ((b & 7) == 0 && _rng.NextDouble() < 0.25 &&
                        !IsSpotHidden(in surf, surf.Left + b * BucketSize, surf.TopY + SurfaceContactOffset - h * 0.7f))
                    {
                        SpawnSplashParticle(
                            surf.Left + b * BucketSize,
                            surf.TopY + SurfaceContactOffset - h * 0.7f,
                            -deltaX * 2.2f + ((float)_rng.NextDouble() - 0.5f) * 45f,
                            -Math.Abs(deltaY) * 1.2f - 15f - (float)_rng.NextDouble() * 35f);
                    }
                }
            }
        }
    }

    private void SimulateSnowAvalancheAndEdges(in TrackedSurface surf, SurfaceSnowBank bank, int winBuckets)
    {
        const float maxSlopeDiff = 4.2f;
        bool stillDirty = false;

        for (int b = 0; b < winBuckets - 1; b++)
        {
            float diff = bank.Heights[b] - bank.Heights[b + 1];
            if (Math.Abs(diff) > maxSlopeDiff)
            {
                float transfer = (Math.Abs(diff) - maxSlopeDiff) * 0.38f;
                if (diff > 0f)
                {
                    bank.Heights[b] -= transfer;
                    bank.Heights[b + 1] += transfer;
                }
                else
                {
                    bank.Heights[b] += transfer;
                    bank.Heights[b + 1] -= transfer;
                }
                stillDirty = true;
            }
        }

        if (!surf.IsTaskbar)
        {
            if (bank.Heights[0] > 3.2f)
            {
                float spill = bank.Heights[0] - 2.0f;
                bank.Heights[0] = 2.0f;
                stillDirty = true;
                if (spill > 0.9f && !IsSpotHidden(in surf, surf.Left - 2f, surf.TopY + SurfaceContactOffset))
                {
                    SpawnSplashParticle(
                        surf.Left - 2f,
                        surf.TopY + SurfaceContactOffset,
                        -18f - (float)_rng.NextDouble() * 35f,
                        15f + (float)_rng.NextDouble() * 35f);
                }
            }

            int lastIdx = winBuckets - 1;
            if (bank.Heights[lastIdx] > 3.2f)
            {
                float spill = bank.Heights[lastIdx] - 2.0f;
                bank.Heights[lastIdx] = 2.0f;
                stillDirty = true;
                if (spill > 0.9f && !IsSpotHidden(in surf, surf.Right + 2f, surf.TopY + SurfaceContactOffset))
                {
                    SpawnSplashParticle(
                        surf.Right + 2f,
                        surf.TopY + SurfaceContactOffset,
                        18f + (float)_rng.NextDouble() * 35f,
                        15f + (float)_rng.NextDouble() * 35f);
                }
            }
        }

        bool hasAny = false;
        for (int b = 1; b < winBuckets - 1; b++)
        {
            float currentH = bank.Heights[b];
            if (currentH <= 0.05f)
            {
                bank.Heights[b] = 0f;
                continue;
            }

            float avg = (bank.Heights[b - 1] + currentH * 2f + bank.Heights[b + 1]) * 0.25f;
            float newH = currentH * 0.82f + avg * 0.18f;
            bank.Heights[b] = newH;
            if (newH > 0.35f) hasAny = true;
        }

        bank.HasAnySnow = hasAny;
        bank.IsDirty = stillDirty;
    }

    private const float ShakeAccelFloor = 12000f;
    private const float ShakeEnergyDecaySeconds = 0.45f;
    private const float ShakeBaseThreshold = 2800f;
    private const float ShakeStickyLayer = 1.4f;

    private void UpdateWindowShake(in TrackedSurface surf, SurfaceSnowBank bank, float dt, int deltaX, int deltaY)
    {
        if (dt < 0.004f) return;

        float rawVx = deltaX / dt;
        float rawVy = deltaY / dt;

        float prevVx = bank.SmoothVelX;
        float prevVy = bank.SmoothVelY;

        bank.SmoothVelX = prevVx * 0.45f + rawVx * 0.55f;
        bank.SmoothVelY = prevVy * 0.45f + rawVy * 0.55f;

        float ax = (bank.SmoothVelX - prevVx) / dt;
        float ay = (bank.SmoothVelY - prevVy) / dt;
        float accel = MathF.Sqrt(ax * ax + ay * ay);

        float threshold = ShakeBaseThreshold * _windowShakeThreshold;

        bank.ShakeEnergy *= MathF.Exp(-dt / ShakeEnergyDecaySeconds);
        if (accel > ShakeAccelFloor)
        {
            float gain = (accel - ShakeAccelFloor) * dt;
            bank.ShakeEnergy = Math.Min(bank.ShakeEnergy + Math.Min(gain, threshold * 3f), threshold * 6f);
        }

        if (bank.ShakeCooldown > 0f)
        {
            bank.ShakeCooldown -= dt;
        }

        if (bank.ShakeEnergy <= threshold) return;

        float energy = bank.ShakeEnergy;
        float excess = energy - threshold;

        if (bank.ShakeCooldown <= 0f && energy > threshold * 1.6f)
        {
            if (DislodgeObjectsFromWindow(surf.Handle, bank.SmoothVelX, energy > threshold * 2.4f))
            {
                bank.ShakeCooldown = 0.35f;
            }
        }

        bank.ShakeEnergy -= excess * 0.45f;

        if (!bank.HasAnySnow) return;

        float shedFrac = Math.Clamp(excess / (threshold * 2.5f), 0.04f, 0.6f);
        int winBuckets = GetWindowBucketCount(in surf, _bucketCount);

        float carryVx = Math.Clamp(bank.SmoothVelX * 0.55f, -380f, 380f);
        float carryVy = Math.Clamp(bank.SmoothVelY * 0.30f, -260f, 260f);
        float jolt = Math.Clamp(accel * 0.004f, 0f, 140f);

        int spawned = 0;
        for (int b = 0; b < winBuckets; b++)
        {
            float h = bank.Heights[b];
            float loose = h - ShakeStickyLayer;
            if (loose <= 0.2f) continue;

            bool nearEdge = b < 5 || b >= winBuckets - 5;
            float weight = (nearEdge ? 1.45f : 1.0f) * (0.55f + 0.45f * (float)_rng.NextDouble());
            float removed = Math.Min(loose, loose * shedFrac * weight);
            if (removed <= 0.05f) continue;

            bank.Heights[b] -= removed;

            if (spawned < 45 && removed > 0.6f && _rng.NextDouble() < 0.75 &&
                !IsSpotHidden(in surf, surf.Left + b * BucketSize, surf.TopY + SurfaceContactOffset - h * 0.6f))
            {
                spawned++;
                SpawnSplashParticle(
                    surf.Left + b * BucketSize + (float)_rng.NextDouble() * BucketSize,
                    surf.TopY + SurfaceContactOffset - h * 0.6f,
                    carryVx + ((float)_rng.NextDouble() - 0.5f) * 140f,
                    carryVy - 30f - jolt * (float)_rng.NextDouble() - (float)_rng.NextDouble() * 60f);
            }
        }

        bank.IsDirty = true;
    }

    private bool DislodgeObjectsFromWindow(IntPtr windowHandle, float windowVelX, bool includeSnowmen)
    {
        bool any = false;
        float carry = Math.Clamp(windowVelX * 0.35f, -260f, 260f);

        for (int i = 0; i < _snowballs.Count; i++)
        {
            Snowball sb = _snowballs[i];
            if (sb.IsGrounded && sb.GroundedWindowHandle == windowHandle && !ReferenceEquals(sb, _activeSnowball))
            {
                sb.IsGrounded = false;
                sb.GroundedWindowHandle = IntPtr.Zero;
                sb.Vx = carry + ((float)_rng.NextDouble() - 0.5f) * 220f;
                sb.Vy = -150f - (float)_rng.NextDouble() * 120f;
                any = true;
            }
        }

        if (includeSnowmen)
        {
            for (int i = 0; i < _snowmen.Count; i++)
            {
                Snowman sm = _snowmen[i];
                if (sm.IsGrounded && sm.GroundedWindowHandle == windowHandle && !ReferenceEquals(sm, _activeSnowman))
                {
                    sm.IsGrounded = false;
                    sm.GroundedWindowHandle = IntPtr.Zero;
                    sm.Vx = carry * 0.8f + ((float)_rng.NextDouble() - 0.5f) * 160f;
                    sm.Vy = -190f;
                    any = true;
                }
            }
        }

        return any;
    }

    private void ScatterBankIntoParticles(SurfaceSnowBank bank)
{
    if (!bank.HasAnySnow) return;

    int winBuckets = Math.Clamp(((bank.LastRight - bank.LastLeft) / BucketSize) + 1, 3, _bucketCount - 1);
    for (int b = 0; b < winBuckets; b++)
    {
        float h = bank.Heights[b];
        if (h > 2.0f && (b & 1) == 0)
        {
            float screenX = bank.LastLeft + b * BucketSize;
            float spawnY = bank.LastTopY + SurfaceContactOffset - h * 0.5f;

            int ownIdx = FindSurfaceIndexByHandle(bank.Handle);
            if (IsPointHiddenBehindHigherWindow(screenX, spawnY, ownIdx >= 0 ? ownIdx : _surfacesSnapshot.Count))
                continue;

            SpawnSplashParticle(
                screenX + (float)_rng.NextDouble() * BucketSize,
                spawnY,
                0f,
                15f + (float)_rng.NextDouble() * 40f);
        }
    }
    bank.ClearHeights();
}

    private bool TryLandParticleOnSurface(ref Particle p, float prevY)
    {
        int px = (int)p.X;

        if (IsPointInsideExcludedUi(p.X, p.Y))
            return false;

        for (int i = 0; i < _landingCacheCount; i++)
        {
            ref LandingSurfaceEntry entry = ref _landingCache[i];

            if (px < entry.Left || px > entry.Right)
                continue;

            int localB = GetLocalBucketIndex(p.X, entry.Left, entry.WinBuckets);

            SurfaceSnowBank bank = entry.Bank;
            float driftHeight = bank.Heights[localB];
            float baseContactY = entry.TopY + SurfaceContactOffset;
            float surfaceLineY = baseContactY - driftHeight;

            if (prevY <= surfaceLineY + 7f && p.Y >= surfaceLineY - 2f)
            {
                if (!entry.IsTaskbar && _enableWindowOcclusion && IsTopEdgeOccludedByHigherWindow(px, entry.TopY, entry.SnapshotIndex))
                    continue;

                if (!entry.IsTaskbar && (localB <= 1 || localB >= entry.WinBuckets - 2) && driftHeight > 3.5f)
                {
                    bool isLeftEdge = localB <= 1;
                    if (!IsTopEdgeCovered(px, entry.TopY, entry.SnapshotIndex))
                    {
                        SpawnSplashParticle(
                            isLeftEdge ? entry.Left - 2f : entry.Right + 2f,
                            entry.TopY + SurfaceContactOffset,
                            isLeftEdge ? (-22f - (float)_rng.NextDouble() * 35f) : (22f + (float)_rng.NextDouble() * 35f),
                            20f + (float)_rng.NextDouble() * 40f);
                    }
                    if (_enableWindowOcclusion || entry.IsTaskbar)
                        return true;
                    continue;
                }

                float add = p.Radius * 0.72f * _accumulationRateMultiplier;
                if (driftHeight < _maxSnowHeight)
                {
                    bank.Heights[localB] = Math.Min(_maxSnowHeight, bank.Heights[localB] + add);
                    if (localB > 0)
                        bank.Heights[localB - 1] = Math.Min(_maxSnowHeight, bank.Heights[localB - 1] + add * 0.50f);
                    if (localB < entry.WinBuckets - 1)
                        bank.Heights[localB + 1] = Math.Min(_maxSnowHeight, bank.Heights[localB + 1] + add * 0.50f);

                    bank.HasAnySnow = true;
                    bank.IsDirty = true;
                }
                else
                {
                    bank.Heights[localB] += add * 0.6f;
                    bank.IsDirty = true;
                }

                if (!_enableWindowOcclusion && !entry.IsTaskbar)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private bool IsTopEdgeOccludedByHigherWindow(int x, int surfaceTopY, int currentSurfaceIndex)
    {
        if (!_enableWindowOcclusion)
            return false;

        return IsTopEdgeCovered(x, surfaceTopY, currentSurfaceIndex);
    }

    // Не зависит от тумблера «крыши»: используется для эффектов (лавины, разлёт сугроба),
    // которые не должны происходить в местах, закрытых верхними окнами.
    private bool IsTopEdgeCovered(int x, int surfaceTopY, int currentSurfaceIndex)
    {
        int snowTopLimit = (int)_maxSnowHeight + 12;

        for (int j = 0; j < currentSurfaceIndex; j++)
        {
            TrackedSurface higher = _surfacesSnapshot[j];
            if (higher.IsMaximizedOrTopDocked) return true;

            if (x >= higher.Left - 2 && x <= higher.Right + 2 &&
                surfaceTopY >= higher.TopY - 2 && (surfaceTopY - snowTopLimit) <= higher.BottomY)
            {
                return true;
            }
        }

        // Окна, которые лежат ниже по z-порядку, но целиком выше этой кромки на экране:
        // сугроб не должен залезать на них (их z-порядок для непересекающихся окон ничего не значит).
        for (int j = currentSurfaceIndex + 1; j < _surfacesSnapshot.Count; j++)
        {
            TrackedSurface other = _surfacesSnapshot[j];
            if (other.IsTaskbar) continue;
            if (other.BottomY > surfaceTopY + 2) continue;

            if (x >= other.Left - 2 && x <= other.Right + 2 && (surfaceTopY - snowTopLimit) <= other.BottomY)
            {
                return true;
            }
        }
        return false;
    }

    private void SyncGroundedObjectsToLatestSurfaces()
    {
        for (int i = 0; i < _snowballs.Count; i++)
        {
            Snowball sb = _snowballs[i];
            if (!sb.IsGrounded || sb.GroundedWindowHandle == IntPtr.Zero || ReferenceEquals(sb, _activeSnowball))
                continue;

            int surfIdx = FindSurfaceIndexByHandle(sb.GroundedWindowHandle);
            if (surfIdx >= 0)
            {
                sb.IsHiddenWithWindow = false;
                TrackedSurface surf = _surfacesSnapshot[surfIdx];
                sb.X = surf.Left + sb.OffsetFromWindowLeft;
                sb.Y = surf.TopY + SurfaceContactOffset - sb.Radius;
            }
            else if (_keepSnowOnHiddenWindows && IsWindowStillAliveAndHidden(sb.GroundedWindowHandle))
            {
                sb.IsHiddenWithWindow = true;
            }
        }

        for (int i = 0; i < _snowmen.Count; i++)
        {
            Snowman sm = _snowmen[i];
            if (!sm.IsGrounded || sm.GroundedWindowHandle == IntPtr.Zero || ReferenceEquals(sm, _activeSnowman))
                continue;

            int surfIdx = FindSurfaceIndexByHandle(sm.GroundedWindowHandle);
            if (surfIdx >= 0)
            {
                sm.IsHiddenWithWindow = false;
                TrackedSurface surf = _surfacesSnapshot[surfIdx];
                sm.X = surf.Left + sm.OffsetFromWindowLeft;
                sm.BottomY = surf.TopY + SurfaceContactOffset;
            }
            else if (_keepSnowOnHiddenWindows && IsWindowStillAliveAndHidden(sm.GroundedWindowHandle))
            {
                sm.IsHiddenWithWindow = true;
            }
        }
    }

    private bool TryBuildOccluderClipPath(int surfaceIndex, SKRect targetBounds)
    {
        // Обрезка при рисовании не зависит от тумблера «крыши»: он управляет только тем,
        // садится ли снег под верхними окнами, но уже лежащий снег не должен рисоваться поверх окон.

        _occluderPath.Reset();
        bool anyOverlap = false;

        if (surfaceIndex < 0 || surfaceIndex >= _surfacesSnapshot.Count)
            return false;

        TrackedSurface target = _surfacesSnapshot[surfaceIndex];

        for (int j = 0; j < _surfacesSnapshot.Count; j++)
        {
            if (j == surfaceIndex) continue;

            TrackedSurface other = _surfacesSnapshot[j];
            // Панель задач закрывает только окна, которые под ней; снег на самой панели она не режет.
            if (other.IsTaskbar && (target.IsTaskbar || j > surfaceIndex)) continue;

            // Окна выше по z-порядку закрывают снег всегда. Окна ниже по z-порядку закрывают его
            // только если целиком лежат выше кромки (не пересекаются с окном, на котором лежит снег).
            // Для самого таскбара второе правило не применяется: снег на нём должен быть виден.
            if (j > surfaceIndex && (target.IsTaskbar || other.BottomY > target.TopY + 2)) continue;

            var rect = new SKRect(other.Left - 2, other.TopY - 2, other.Right + 2, other.BottomY + 2);
            if (rect.IntersectsWith(targetBounds))
            {
                _occluderPath.AddRect(rect);
                anyOverlap = true;
            }
        }

        return anyOverlap;
    }

    public void Render(SKCanvas canvas, int canvasWidth, int canvasHeight)
    {
        _tracker.RefreshMovedWindowImmediate(_surfacesSnapshot);
        SyncGroundedObjectsToLatestSurfaces();

        IntPtr draggedHwnd = _tracker.ActivelyDraggedWindow;

        canvas.Clear(SKColors.Transparent);

        canvas.Save();
        if (_hasSettingsExclusion)
        {
            canvas.ClipRect(_settingsExclusionRect, SKClipOperation.Difference);
        }
        if (_hasTrayExclusion)
        {
            canvas.ClipRect(_trayExclusionRect, SKClipOperation.Difference);
        }
        if (_hasWelcomeExclusion)
        {
            canvas.ClipRect(_welcomeExclusionRect, SKClipOperation.Difference);
        }

                                                                     
        for (int i = _surfacesSnapshot.Count - 1; i >= 0; i--)
        {
            TrackedSurface surf = _surfacesSnapshot[i];
            if (draggedHwnd != IntPtr.Zero && surf.Handle == draggedHwnd) continue;
            if (!CanAccumulateOnSurface(in surf)) continue;
            if (!_snowBanks.TryGetValue(surf.Handle, out SurfaceSnowBank? bank) || !bank.HasAnySnow)
                continue;

            _reusablePath.Reset();
            if (AppendSmoothSnowBankToPath(_reusablePath, in surf, bank))
            {
                var bankBounds = new SKRect(surf.Left, surf.TopY - _maxSnowHeight * 1.25f - 8f, surf.Right, surf.TopY + 6f);
                if (TryBuildOccluderClipPath(i, bankBounds))
                {
                    canvas.Save();
                    canvas.ClipPath(_occluderPath, SKClipOperation.Difference);
                    canvas.DrawPath(_reusablePath, _snowdriftPaint);
                    canvas.Restore();
                }
                else
                {
                    canvas.DrawPath(_reusablePath, _snowdriftPaint);
                }
            }
        }

                                            
        for (int i = 0; i < _snowmen.Count; i++)
        {
            Snowman sm = _snowmen[i];
            if (sm.IsHiddenWithWindow) continue;
            if (draggedHwnd != IntPtr.Zero && sm.IsGrounded && sm.GroundedWindowHandle == draggedHwnd) continue;

            int surfIdx = sm.IsGrounded ? FindSurfaceIndexByHandle(sm.GroundedWindowHandle) : -1;
            if (sm.IsGrounded && surfIdx < 0) continue;

            if (surfIdx >= 0)
            {
                float r = sm.MaxRadius * 2f;
                var smBounds = new SKRect(sm.X - r, sm.BottomY - sm.TotalHeight - 15f, sm.X + r, sm.BottomY + 5f);
                if (TryBuildOccluderClipPath(surfIdx, smBounds))
                {
                    canvas.Save();
                    canvas.ClipPath(_occluderPath, SKClipOperation.Difference);
                    DrawSnowman(canvas, sm);
                    canvas.Restore();
                }
                else
                {
                    DrawSnowman(canvas, sm);
                }
            }
            else
            {
                DrawSnowman(canvas, sm);
            }
        }

                                         
        for (int i = 0; i < _snowballs.Count; i++)
        {
            Snowball sb = _snowballs[i];
            if (sb.IsHiddenWithWindow) continue;
            if (draggedHwnd != IntPtr.Zero && sb.IsGrounded && sb.GroundedWindowHandle == draggedHwnd) continue;

            int surfIdx = sb.IsGrounded ? FindSurfaceIndexByHandle(sb.GroundedWindowHandle) : -1;
            if (smOrBallHidden(sb.IsGrounded, surfIdx)) continue;

            if (surfIdx >= 0)
            {
                var sbBounds = new SKRect(sb.X - sb.Radius - 2f, sb.Y - sb.Radius - 2f, sb.X + sb.Radius + 2f, sb.Y + sb.Radius + 2f);
                if (TryBuildOccluderClipPath(surfIdx, sbBounds))
                {
                    canvas.Save();
                    canvas.ClipPath(_occluderPath, SKClipOperation.Difference);
                    DrawSingleSnowball(canvas, sb);
                    canvas.Restore();
                }
                else
                {
                    DrawSingleSnowball(canvas, sb);
                }
            }
            else
            {
                DrawSingleSnowball(canvas, sb);
            }
        }

                                            
        DrawAkBulletsAndShells(canvas);

                                        
        int count = _activeParticles;
        for (int i = 0; i < count; i++)
        {
            ref Particle p = ref _particles[i];
            float half = SpriteHalfSizes[p.SpriteIndex];
            canvas.DrawBitmap(_bakedBitmaps[p.ColorIndex, p.SpriteIndex], p.X - half, p.Y - half);
        }

        for (int i = 0; i < MaxSplashParticles; i++)
        {
            ref SplashParticle sp = ref _splashParticles[i];
            if (!sp.Active) continue;

            float half = SpriteHalfSizes[sp.SpriteIndex];
            canvas.DrawBitmap(_bakedBitmaps[sp.ColorIndex, sp.SpriteIndex], sp.X - half, sp.Y - half);
        }

        if (_enableScreenSplatters && _splatters.Count > 0)
        {
            DrawScreenSplatters(canvas);
        }

        canvas.Restore();
    }

                                                                                                                    
    public void RenderSingleWindowCompanion(SKCanvas canvas, IntPtr targetHwnd, int width, int baseOffsetY)
    {
        canvas.Clear(SKColors.Transparent);

        bool clipped = false;
        int targetIdx = FindSurfaceIndexByHandle(targetHwnd);
        if (targetIdx >= 0)
        {
            TrackedSurface ts = _surfacesSnapshot[targetIdx];
            float originY = ts.TopY - baseOffsetY;
            var companionBounds = new SKRect(ts.Left, originY, ts.Left + width, originY + DragCompanionWindow.CompanionHeight);
            if (TryBuildOccluderClipPath(targetIdx, companionBounds))
            {
                _occluderPath.Transform(SKMatrix.CreateTranslation(-ts.Left, -originY));
                canvas.Save();
                canvas.ClipPath(_occluderPath, SKClipOperation.Difference);
                clipped = true;
            }
        }

        if (_snowBanks.TryGetValue(targetHwnd, out SurfaceSnowBank? bank) && bank.HasAnySnow)
        {
            var localSurf = new TrackedSurface
            {
                Handle = targetHwnd,
                Left = 0,
                Right = width,
                TopY = baseOffsetY,
                BottomY = baseOffsetY + 100,
                IsTaskbar = false
            };

            _reusablePath.Reset();
            if (AppendSmoothSnowBankToPath(_reusablePath, in localSurf, bank))
            {
                canvas.DrawPath(_reusablePath, _snowdriftPaint);
            }
        }

        for (int i = 0; i < _snowmen.Count; i++)
        {
            Snowman sm = _snowmen[i];
            if (!sm.IsGrounded || sm.GroundedWindowHandle != targetHwnd || sm.IsHiddenWithWindow) continue;

            float savedX = sm.X;
            float savedY = sm.BottomY;
            sm.X = sm.OffsetFromWindowLeft;
            sm.BottomY = baseOffsetY + SurfaceContactOffset;
            DrawSnowman(canvas, sm);
            sm.X = savedX;
            sm.BottomY = savedY;
        }

        for (int i = 0; i < _snowballs.Count; i++)
        {
            Snowball sb = _snowballs[i];
            if (!sb.IsGrounded || sb.GroundedWindowHandle != targetHwnd || sb.IsHiddenWithWindow) continue;

            float savedX = sb.X;
            float savedY = sb.Y;
            sb.X = sb.OffsetFromWindowLeft;
            sb.Y = baseOffsetY + SurfaceContactOffset - sb.Radius;
            DrawSingleSnowball(canvas, sb);
            sb.X = savedX;
            sb.Y = savedY;
        }

        if (clipped)
            canvas.Restore();
    }

    private void DrawAkBulletsAndShells(SKCanvas canvas)
    {
        for (int i = 0; i < MaxAkBullets; i++)
        {
            ref AkBullet b = ref _akBullets[i];
            if (!b.Active) continue;

            float tailX = b.X - b.Vx * 0.014f;
            float tailY = b.Y - b.Vy * 0.014f;
            canvas.DrawLine(tailX, tailY, b.X, b.Y, _akTracerPaint);
        }

        for (int i = 0; i < MaxShellCasings; i++)
        {
            ref ShellCasing sc = ref _shellCasings[i];
            if (!sc.Active) continue;

            canvas.Save();
            canvas.Translate(sc.X, sc.Y);
            canvas.RotateDegrees(sc.Angle * 57.2958f);
            canvas.DrawRect(-2.5f, -1.1f, 5.0f, 2.2f, _akShellPaint);
            canvas.Restore();
        }
    }

    private static bool smOrBallHidden(bool isGrounded, int surfIdx) => isGrounded && surfIdx < 0;

    private void DrawSingleSnowball(SKCanvas canvas, Snowball sb)
    {
        canvas.DrawCircle(sb.X, sb.Y, sb.Radius, _snowdriftPaint);
        canvas.DrawCircle(sb.X, sb.Y, sb.Radius - 0.7f, _ballShadowPaint);

        if (ReferenceEquals(sb, _activeSnowball))
        {
            canvas.DrawCircle(sb.X, sb.Y, sb.Radius + 1.2f, _activeHighlightPaint);
        }
    }

    private void DrawScreenSplatters(SKCanvas canvas)
    {
        for (int i = 0; i < _splatters.Count; i++)
        {
            ScreenSplatter sp = _splatters[i];
            float alphaFactor = Math.Clamp(sp.Life / sp.MaxLife, 0f, 1f);

            _splatterShadowPaint.Color = new SKColor(150, 178, 208, (byte)(95 * alphaFactor));
            _splatterBasePaint.Color = new SKColor(226, 238, 250, (byte)(220 * alphaFactor));
            _splatterCorePaint.Color = new SKColor(250, 253, 255, (byte)(248 * alphaFactor));

            canvas.DrawCircle(sp.X, sp.Y + 2.5f, sp.BaseRadius * 0.78f, _splatterShadowPaint);
            for (int L = 0; L < sp.Lumps.Length; L++)
            {
                ref SplatterBlob lump = ref sp.Lumps[L];
                float dripY = sp.SlideDistance * lump.DripFactor;
                canvas.DrawCircle(sp.X + lump.OffsetX, sp.Y + lump.OffsetY + dripY + 2.0f, lump.Radius, _splatterShadowPaint);
            }

            canvas.DrawCircle(sp.X, sp.Y, sp.BaseRadius * 0.74f, _splatterBasePaint);
            for (int L = 0; L < sp.Lumps.Length; L++)
            {
                ref SplatterBlob lump = ref sp.Lumps[L];
                float dripY = sp.SlideDistance * lump.DripFactor;
                canvas.DrawCircle(sp.X + lump.OffsetX, sp.Y + lump.OffsetY + dripY, lump.Radius * (0.65f + 0.35f * alphaFactor), _splatterBasePaint);
            }

            for (int d = 0; d < sp.Droplets.Length; d++)
            {
                ref SplatterBlob drop = ref sp.Droplets[d];
                float dripY = sp.SlideDistance * drop.DripFactor * 1.25f;
                canvas.DrawCircle(sp.X + drop.OffsetX, sp.Y + drop.OffsetY + dripY, drop.Radius * alphaFactor, _splatterBasePaint);
            }

            canvas.DrawCircle(sp.X, sp.Y - 1.0f, sp.BaseRadius * 0.52f * alphaFactor, _splatterCorePaint);
            for (int L = 0; L < sp.Lumps.Length; L += 2)
            {
                ref SplatterBlob lump = ref sp.Lumps[L];
                canvas.DrawCircle(
                    sp.X + lump.OffsetX * 0.65f,
                    sp.Y + lump.OffsetY * 0.65f + sp.SlideDistance * lump.DripFactor * 0.4f,
                    lump.Radius * 0.65f * alphaFactor,
                    _splatterCorePaint);
            }
        }
    }

    private bool AppendSmoothSnowBankToPath(SKPath path, in TrackedSurface surf, SurfaceSnowBank bank)
    {
        int winBuckets = GetWindowBucketCount(in surf, _bucketCount);
        if (winBuckets <= 2) return false;

        float baseY = surf.TopY + SurfaceContactOffset;
        float firstX = surf.Left;
        float prevX = firstX;
        float prevY = baseY;

        path.MoveTo(firstX, baseY);

        for (int b = 0; b < winBuckets; b++)
        {
            float currX = Math.Min((float)surf.Right, surf.Left + b * BucketSize);
            float h = (b == 0 || b == winBuckets - 1) ? Math.Min(1.5f, bank.Heights[b] * 0.4f) : bank.Heights[b];
            float currY = baseY - h;

            if (b == 0)
            {
                path.LineTo(currX, currY);
            }
            else
            {
                float midX = (prevX + currX) * 0.5f;
                float midY = (prevY + currY) * 0.5f;
                path.QuadTo(prevX, prevY, midX, midY);
            }

            prevX = currX;
            prevY = currY;
        }

        path.LineTo(surf.Right, baseY);
        path.Close();
        return true;
    }

    private void DrawSnowman(SKCanvas canvas, Snowman sm)
    {
        float currentY = sm.BottomY;
        float currentX = sm.X;
        float midBallX = currentX;
        float midBallY = currentY;
        float midBallR = 12f;

        for (int i = 0; i < sm.Radii.Count; i++)
        {
            float r = sm.Radii[i];
            float centerY = i == 0 ? currentY - r : currentY - r * 0.85f;

            float tiltShift = i == 0 ? 0f : MathF.Sin(sm.TiltAngle) * (r * 0.85f * i);
            float ballX = currentX + tiltShift;

            if (i == 1)
            {
                midBallX = ballX;
                midBallY = centerY;
                midBallR = r;
            }

            if (sm.Radii.Count == 3 && i == 1 && sm.IsDressed && !sm.HasAk47)
            {
                float armY = centerY - r * 0.15f;
                canvas.DrawLine(ballX - r * 0.85f, armY, ballX - r * 1.85f, armY - r * 0.55f + tiltShift * 0.3f, _branchPaint);
                canvas.DrawLine(ballX - r * 1.45f, armY - r * 0.33f, ballX - r * 1.65f, armY - r * 0.72f + tiltShift * 0.3f, _branchPaint);
                canvas.DrawLine(ballX + r * 0.85f, armY, ballX + r * 1.85f, armY - r * 0.45f - tiltShift * 0.3f, _branchPaint);
                canvas.DrawLine(ballX + r * 1.45f, armY - r * 0.25f, ballX + r * 1.70f, armY - r * 0.65f - tiltShift * 0.3f, _branchPaint);
            }

            canvas.DrawCircle(ballX, centerY, r, _snowdriftPaint);
            canvas.DrawCircle(ballX, centerY, r - 0.7f, _ballShadowPaint);

            if (ReferenceEquals(sm, _activeSnowman))
            {
                canvas.DrawCircle(ballX, centerY, r + 1.0f, _activeHighlightPaint);
            }

            if (sm.Radii.Count == 3 && i == 1)
            {
                canvas.DrawCircle(ballX, centerY - r * 0.22f, 2.2f, _detailPaint);
                canvas.DrawCircle(ballX, centerY + r * 0.25f, 2.2f, _detailPaint);
            }

            if (i == sm.Radii.Count - 1)
            {
                if (sm.Radii.Count == 3 && sm.IsDressed)
                {
                    _scarfPaint.Color = ScarfColors[sm.ScarfColorIndex % ScarfColors.Length];
                    float neckY = centerY + r * 0.75f;
                    canvas.DrawRoundRect(ballX - r * 0.85f, neckY - 3.5f, r * 1.7f, 7.5f, 3.5f, 3.5f, _scarfPaint);

                    float windSway = Math.Clamp(_globalWind * 0.04f - sm.TiltAngle * 14f, -8f, 8f);
                    _reusablePath.Reset();
                    _reusablePath.MoveTo(ballX + r * 0.35f, neckY + 2f);
                    _reusablePath.LineTo(ballX + r * 0.68f, neckY + 2f);
                    _reusablePath.LineTo(ballX + r * 0.75f + windSway, neckY + r * 0.95f);
                    _reusablePath.LineTo(ballX + r * 0.30f + windSway, neckY + r * 0.95f);
                    _reusablePath.Close();
                    canvas.DrawPath(_reusablePath, _scarfPaint);
                }

                float eyeOffsetX = r * 0.34f;
                float eyeY = centerY - r * 0.22f;
                canvas.DrawCircle(ballX - eyeOffsetX, eyeY, 2.1f, _detailPaint);
                canvas.DrawCircle(ballX + eyeOffsetX, eyeY, 2.1f, _detailPaint);

                _reusablePath.Reset();
                _reusablePath.MoveTo(ballX, centerY);
                _reusablePath.LineTo(ballX + sm.FacingDir * r * 0.75f, centerY + r * 0.12f);
                _reusablePath.LineTo(ballX, centerY + r * 0.22f);
                _reusablePath.Close();
                canvas.DrawPath(_reusablePath, _carrotPaint);

                if (sm.Radii.Count == 3)
                {
                    float brimY = centerY - r * 0.82f;
                    canvas.DrawRect(ballX - r * 0.85f, brimY - 3f, r * 1.7f, 3.5f, _hatPaint);
                    canvas.DrawRect(ballX - r * 0.55f, brimY - r * 0.85f, r * 1.1f, r * 0.85f, _hatPaint);
                }
            }

            currentY = centerY - r * 0.8f;
        }

        if (sm.HasAk47 && sm.Radii.Count >= 2)
        {
            DrawSnowmanAk47(canvas, sm, midBallX, midBallY, midBallR);
        }
    }

    private void DrawSnowmanAk47(SKCanvas canvas, Snowman sm, float bodyX, float bodyY, float bodyR)
    {
        float scale = Math.Clamp(bodyR / 13f, 0.75f, 1.45f);
        canvas.Save();
        canvas.Translate(bodyX, bodyY + 1f);
        canvas.Scale(sm.FacingDir * scale, scale);
        canvas.RotateRadians(sm.AkAimAngle);
        canvas.Translate(-sm.AkRecoilOffset, 0f);

        canvas.DrawLine(-bodyR * 0.35f, -2f, 2f, 4f, _branchPaint);
        canvas.DrawLine(bodyR * 0.25f, 2f, 13f, 1f, _branchPaint);

        _reusablePath.Reset();
        _reusablePath.MoveTo(-13f, 0f);
        _reusablePath.LineTo(-3f, -2.2f);
        _reusablePath.LineTo(-3f, 2.5f);
        _reusablePath.LineTo(-13f, 5.2f);
        _reusablePath.Close();
        canvas.DrawPath(_reusablePath, _akWoodPaint);

        canvas.DrawRoundRect(-3.5f, -3.2f, 13.5f, 5.2f, 1.2f, 1.2f, _akMetalPaint);

        _reusablePath.Reset();
        _reusablePath.MoveTo(-1.5f, 1.8f);
        _reusablePath.LineTo(2.0f, 1.8f);
        _reusablePath.LineTo(0.2f, 7.5f);
        _reusablePath.LineTo(-3.0f, 7.2f);
        _reusablePath.Close();
        canvas.DrawPath(_reusablePath, _akWoodPaint);

        _reusablePath.Reset();
        _reusablePath.MoveTo(4.5f, 1.8f);
        _reusablePath.LineTo(8.5f, 1.8f);
        _reusablePath.QuadTo(10.5f, 7.5f, 13.0f, 11.2f);
        _reusablePath.LineTo(9.2f, 12.2f);
        _reusablePath.QuadTo(6.5f, 7.5f, 4.5f, 1.8f);
        _reusablePath.Close();
        canvas.DrawPath(_reusablePath, _akMetalPaint);

        canvas.DrawRoundRect(9.5f, -2.6f, 7.5f, 4.2f, 1.0f, 1.0f, _akWoodPaint);

        canvas.DrawRect(16.5f, -1.6f, 11.5f, 2.0f, _akMetalPaint);
        canvas.DrawRect(10.0f, -3.5f, 9.5f, 1.3f, _akMetalPaint);

        _reusablePath.Reset();
        _reusablePath.MoveTo(25.0f, -1.6f);
        _reusablePath.LineTo(26.8f, -4.5f);
        _reusablePath.LineTo(27.8f, -1.6f);
        _reusablePath.Close();
        canvas.DrawPath(_reusablePath, _akMetalPaint);

        if (sm.AkMuzzleFlashTimer > 0f)
        {
            _reusablePath.Reset();
            _reusablePath.MoveTo(28.2f, -0.6f);
            _reusablePath.LineTo(35.5f, -5.2f);
            _reusablePath.LineTo(33.2f, -1.2f);
            _reusablePath.LineTo(40.5f, -0.6f);
            _reusablePath.LineTo(33.2f, 0.2f);
            _reusablePath.LineTo(35.5f, 4.2f);
            _reusablePath.Close();
            canvas.DrawPath(_reusablePath, _akFlashOuterPaint);
            canvas.DrawCircle(31.2f, -0.6f, 2.4f, _akFlashInnerPaint);
        }

        canvas.Restore();
    }

    public void Dispose()
    {
        for (int c = 0; c < ColorVariantCount; c++)
        {
            for (int s = 0; s < SpriteCount; s++)
            {
                _bakedBitmaps[c, s]?.Dispose();
            }
        }
        _snowdriftPaint.Shader = null;
        _bankTextureShader?.Dispose();
        _bankTextureBitmap?.Dispose();
        _snowdriftPaint.Dispose();
        _ballShadowPaint.Dispose();
        _activeHighlightPaint.Dispose();
        _detailPaint.Dispose();
        _branchPaint.Dispose();
        _carrotPaint.Dispose();
        _hatPaint.Dispose();
        _scarfPaint.Dispose();
        _splatterShadowPaint.Dispose();
        _splatterBasePaint.Dispose();
        _splatterCorePaint.Dispose();
        _akMetalPaint.Dispose();
        _akWoodPaint.Dispose();
        _akFlashOuterPaint.Dispose();
        _akFlashInnerPaint.Dispose();
        _akTracerPaint.Dispose();
        _akShellPaint.Dispose();
        _reusablePath.Dispose();
        _occluderPath.Dispose();
    }
}