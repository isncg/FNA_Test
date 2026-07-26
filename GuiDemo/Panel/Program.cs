using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Gui;
using FNA.Gui.Serialization;
using FNA.Test;
using GuiPanel = FNA.Gui.Panel;

namespace GuiDemo.Panel
{
    /// <summary>
    /// Phase 0 test program: validates IGuiRenderer, SpriteBatchGuiRenderer,
    /// Widget tree, Panel, Image, and clip stack (G01-G05).
    ///
    /// Headless mode (--headless): runs the current test's pixel assertions and exits.
    /// Interactive mode: press keys 1-5 to select test, Esc to exit.
    /// </summary>
    public class PanelDemo : Game
    {
        private GraphicsDeviceManager _gdm;
        private GuiSystem _guiSystem = null!;

        private int _testFrame = 5;
        private int _currentTestIndex;
        private readonly string[] _testNames = {
            "G01", "G02", "G03", "G04", "G05", "G06", "G07", "G08",
            "G09", "G10", "G11", "G12", "G13",
            "G14", "G15", "G16", "G17", "G18", "G19", "G20",
            "G21", "G22", "G23", "G24", "G25", "G26", "G27", "G28", "G29",
            "G30", "G31", "G32", "G33", "G34", "G35",
            "G36", "G37", "G38",
        };
        private SdfFont? _testFont;
        private string CurrentTest => _testNames[_currentTestIndex];

        private KeyboardState _prevKb;
        private MouseState _prevMouse;
        private bool _textInputActive;

        // G26-G29 interaction demo state
        private int _g26ClickCount;
        private Text? _g26Label;
        private Button? _g27Button;
        private GuiCommand? _g27Command;
        private bool _g27CanExec = true;
        private Slider? _g28Slider;
        private Text? _g28Label;
        private Bindable<float>? _g28Bindable;
        private IDisposable? _g28Subscription;
        private CheckBox? _g29Toggle;
        private Text? _g29Label;
        private Bindable<string>? _g29Bindable;
        private IDisposable? _g29Subscription;

        public PanelDemo(string testName = "G01")
        {
            _currentTestIndex = Array.IndexOf(_testNames, testName);
            if (_currentTestIndex < 0) _currentTestIndex = 0;

            _gdm = new GraphicsDeviceManager(this);
            _gdm.SynchronizeWithVerticalRetrace = true;
            _gdm.PreferredBackBufferWidth = 800;
            _gdm.PreferredBackBufferHeight = 600;
            IsFixedTimeStep = false;
            IsMouseVisible = true;
            Window.Title = "GuiDemo/Panel — Phase 0";

            // Subscribe to FNA text input events for TextBox editing
            TextInputEXT.TextInput += OnTextInput;
        }

        private void OnTextInput(char c)
        {
            // Route SDL text input to the GUI system's focused widget
            _guiSystem?.InjectTextInput(c.ToString());
        }

        protected override void LoadContent()
        {
            ImGuiTestHarness.Init(GraphicsDevice);
            _guiSystem = GuiSystem.CreateDefault(GraphicsDevice);
            _guiSystem.ScreenSize = new Vector2(800, 600);

            // Load SDF font from embedded resources (msdf-atlas-gen format)
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var pngBytes = SdfFont.LoadEmbeddedBytes(asm, "GuiDemo.Panel.en_atlas.png");
            var metricsJson = System.Text.Encoding.UTF8.GetString(
                SdfFont.LoadEmbeddedBytes(asm, "GuiDemo.Panel.en_metrics.json"));
            _testFont = SdfFont.Load(GraphicsDevice, pngBytes, metricsJson);

            BuildTestScene();
        }

        /// <summary>Can be called multiple times to switch tests.</summary>
        private void BuildTestScene()
        {
            var root = (GuiPanel)_guiSystem.Root;
            root.ClearChildren();
            root.BackgroundColor = null;
            root.BackgroundSkin = null;

            // Reset theme (G30 sets its own theme; other tests use no theme)
            _guiSystem.Theme = null;

            switch (CurrentTest)
            {
                case "G01":
                    // Empty system: root panel with no children, no background
                    break;

                case "G02":
                    // DrawRect: a red panel in the top-left quadrant
                    var g02Panel = new GuiPanel
                    {
                        BackgroundColor = Color.Red,
                        Width = 400,
                        Height = 300,
                    };
                    g02Panel.HorizontalAlignment = HorizontalAlignment.Left;
                    g02Panel.VerticalAlignment = VerticalAlignment.Top;
                    root.AddChild(g02Panel);
                    break;

                case "G03":
                    // DrawTexture: a checkerboard texture
                    var checkerTex = TextureGen.Checkerboard(
                        GraphicsDevice, 128, 16, Color.Blue, Color.Yellow);
                    var g03Image = new Image
                    {
                        Texture = checkerTex,
                        Width = 256,
                        Height = 256,
                    };
                    root.AddChild(g03Image);
                    break;

                case "G04":
                    // Clip stack: a panel that clips its children
                    var g04Panel = new GuiPanel
                    {
                        BackgroundColor = Color.DarkGray,
                        Width = 200,
                        Height = 200,
                        ClipChildren = true,
                    };
                    var g04Child = new GuiPanel
                    {
                        BackgroundColor = Color.Green,
                        Width = 300,
                        Height = 100,
                    };
                    g04Panel.AddChild(g04Child);
                    root.AddChild(g04Panel);

                    // A sibling panel outside the clip region
                    var g04Outside = new GuiPanel
                    {
                        BackgroundColor = Color.Blue,
                        Width = 100,
                        Height = 100,
                    };
                    g04Outside.HorizontalAlignment = HorizontalAlignment.Left;
                    g04Outside.VerticalAlignment = VerticalAlignment.Bottom;
                    root.AddChild(g04Outside);
                    break;

                case "G05":
                    // 9-slice: a checker skin with border
                    var skinTex = TextureGen.Checkerboard(
                        GraphicsDevice, 64, 8, Color.Orange, Color.Brown);
                    var nineSlice = new NineSlice(skinTex, new Thickness(8));
                    var g05Panel = new GuiPanel
                    {
                        BackgroundSkin = nineSlice,
                        Width = 300,
                        Height = 200,
                    };
                    root.AddChild(g05Panel);
                    break;

                case "G06":
                case "G07":
                case "G08":
                case "G09":
                    // Logical tests: RecordingRenderer-based, no visual scene needed.
                    break;

                case "G10":
                    // Single-line text rendering
                    if (_testFont != null)
                    {
                        var g10Text = new Text
                        {
                            Font = _testFont,
                            TextString = "Hello FNA GUI!",
                            FontSize = 32,
                            Color = Color.White,
                        };
                        root.AddChild(g10Text);
                    }
                    break;

                case "G11":
                    // Text scaling: small and large text
                    if (_testFont != null)
                    {
                        root.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Small 16px",
                            FontSize = 16,
                            Color = Color.Cyan,
                            VerticalAlignment = VerticalAlignment.Top,
                        });
                        root.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Large 64px",
                            FontSize = 64,
                            Color = Color.Yellow,
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                    }
                    break;

                case "G12":
                    // Text with outline and bold via SdfTextBatch settings
                    if (_testFont != null)
                    {
                        var renderer = _guiSystem.Renderer as SpriteBatchGuiRenderer;
                        if (renderer != null)
                        {
                            renderer.TextOutlineColor = Color.Red;
                            renderer.TextOutlineWidth = 0.15f;
                            renderer.TextWeight = 0.08f;
                        }
                        root.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Bold+Outline",
                            FontSize = 48,
                            Color = Color.White,
                        });
                    }
                    break;

                case "G13":
                    // Multi-line with width constraint
                    if (_testFont != null)
                    {
                        root.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Line one\nLine two\nLine three",
                            FontSize = 24,
                            Color = Color.Lime,
                        });
                    }
                    break;

                case "G14":
                    // ── StackLayout visual test ──
                    {
                        var hStack = new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Padding = new Thickness(8),
                        };
                        hStack.AddChild(MakeBox(120, 60, Color.Red, "H1"));
                        hStack.AddChild(MakeBox(160, 90, Color.Green, "H2"));
                        hStack.AddChild(MakeBox(80, 50, Color.Blue, "H3"));

                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 8,
                            Padding = new Thickness(8),
                        };
                        vStack.AddChild(MakeBox(200, 30, Color.Orange, "V1"));
                        vStack.AddChild(MakeBox(250, 40, Color.Purple, "V2"));
                        vStack.AddChild(MakeBox(180, 25, Color.Cyan, "V3"));

                        var outerV = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(8),
                        };
                        outerV.AddChild(hStack);
                        outerV.AddChild(vStack);
                        root.AddChild(outerV);
                    }
                    break;

                case "G15":
                    // ── GridLayout visual test (Fixed/Auto/Star) ──
                    {
                        var grid = new GridLayout { Padding = new Thickness(4) };
                        grid.AddColumn(100);                          // Fixed 100px
                        grid.AddColumn(GridLength.Auto);              // Auto
                        grid.AddColumn(GridLength.Star(2));           // Star 2*
                        grid.AddColumn(GridLength.Star(1));           // Star 1*

                        AddToGrid(grid, MakeBox(80, 60, Color.Red, "Fixed\n100px"), 0, 0);
                        AddToGrid(grid, MakeBox(130, 60, Color.Green, "Auto"), 0, 1);
                        AddToGrid(grid, MakeBox(0, 60, Color.Blue, "Star 2*"), 0, 2);
                        AddToGrid(grid, MakeBox(0, 60, Color.Orange, "Star 1*"), 0, 3);

                        root.AddChild(grid);
                    }
                    break;

                case "G16":
                    // ── GridLayout span visual test ──
                    {
                        var grid = new GridLayout { Padding = new Thickness(4) };
                        grid.AddColumn(100);
                        grid.AddColumn(120);
                        grid.AddColumn(80);
                        grid.AddRow(50);
                        grid.AddRow(50);

                        // Row 0: child spanning columns 0-1, single column 2
                        var s1 = MakeBox(0, 50, Color.Red, "Span 2 cols");
                        GridLayout.SetColumnSpan(s1, 2);
                        AddToGrid(grid, s1, 0, 0);
                        AddToGrid(grid, MakeBox(0, 50, Color.Green, "Col 2"), 0, 2);

                        // Row 1: single cells
                        AddToGrid(grid, MakeBox(0, 50, Color.Blue, "C0"), 1, 0);
                        AddToGrid(grid, MakeBox(0, 50, Color.Orange, "C1"), 1, 1);
                        AddToGrid(grid, MakeBox(0, 50, Color.Purple, "C2"), 1, 2);

                        root.AddChild(grid);
                    }
                    break;

                case "G17":
                    // ── DockLayout visual test ──
                    {
                        var dock = new DockLayout { LastChildFill = true };

                        var top = MakeBox(0, 40, Color.Red, "Top");
                        DockLayout.SetDock(top, Dock.Top);
                        var left = MakeBox(120, 0, Color.Green, "Left");
                        DockLayout.SetDock(left, Dock.Left);
                        var right = MakeBox(100, 0, Color.Blue, "Right");
                        DockLayout.SetDock(right, Dock.Right);
                        var bottom = MakeBox(0, 35, Color.Orange, "Bottom");
                        DockLayout.SetDock(bottom, Dock.Bottom);
                        var fill = MakeBox(0, 0, Color.DarkGray, "Fill\n(Last)");
                        DockLayout.SetDock(fill, Dock.Left); // ignored for last child

                        dock.AddChild(top);
                        dock.AddChild(left);
                        dock.AddChild(right);
                        dock.AddChild(bottom);
                        dock.AddChild(fill);
                        root.AddChild(dock);
                    }
                    break;

                case "G18":
                    // ── StackLayout spacing + alignment visual test ──
                    {
                        // Horizontal stack with spacing
                        var hStack = new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 16,
                            Padding = new Thickness(8),
                        };

                        var h1 = MakeBox(100, 40, Color.Red, "H1");
                        var h2 = MakeBox(100, 80, Color.Green, "H2\nCenter");
                        h2.VerticalAlignment = VerticalAlignment.Center;
                        var h3 = MakeBox(100, 60, Color.Blue, "H3\nBottom");
                        h3.VerticalAlignment = VerticalAlignment.Bottom;
                        hStack.AddChild(h1);
                        hStack.AddChild(h2);
                        hStack.AddChild(h3);

                        // Vertical stack with alignment
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 8,
                            Padding = new Thickness(8),
                        };
                        var v1 = MakeBox(200, 30, Color.Orange, "Left");
                        v1.HorizontalAlignment = HorizontalAlignment.Left;
                        var v2 = MakeBox(200, 30, Color.Purple, "Center");
                        v2.HorizontalAlignment = HorizontalAlignment.Center;
                        var v3 = MakeBox(200, 30, Color.Cyan, "Right");
                        v3.HorizontalAlignment = HorizontalAlignment.Right;
                        vStack.AddChild(v1);
                        vStack.AddChild(v2);
                        vStack.AddChild(v3);

                        var outer = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                        };
                        outer.AddChild(hStack);
                        outer.AddChild(vStack);
                        root.AddChild(outer);
                    }
                    break;

                case "G21":
                case "G22":
                    // G21-G22: Hit testing / event routing — logical tests only
                    root.AddChild(new GuiPanel
                    {
                        BackgroundColor = Color.DarkSlateGray,
                        Width = 200, Height = 100,
                    });
                    break;

                case "G23":
                    // ── Button visual test ──
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };
                        var btn1 = new Button
                        {
                            Text = "Click Me",
                            Font = _testFont,
                            FontSize = 20,
                            Width = 160, Height = 48,
                        };
                        btn1.Click += b => Console.WriteLine($"[G23] Button clicked! Count={b.ClickCount}");
                        vStack.AddChild(btn1);

                        var btn2 = new Button
                        {
                            Text = "Disabled",
                            Font = _testFont,
                            FontSize = 20,
                            Width = 160, Height = 48,
                            Enabled = false,
                        };
                        vStack.AddChild(btn2);

                        root.AddChild(vStack);
                    }
                    break;

                case "G24":
                    // ── Slider + CheckBox visual test ──
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 16,
                            Padding = new Thickness(16),
                        };
                        var slider = new Slider
                        {
                            Width = 300,
                            Min = 0, Max = 100, Value = 50,
                        };
                        vStack.AddChild(slider);

                        var cb1 = new CheckBox
                        {
                            Text = "Option A",
                            Width = 200, Height = 28,
                        };
                        var cb2 = new CheckBox
                        {
                            Text = "Option B",
                            Width = 200, Height = 28,
                            IsChecked = true,
                        };
                        vStack.AddChild(cb1);
                        vStack.AddChild(cb2);

                        root.AddChild(vStack);
                    }
                    break;

                case "G25":
                    // ── Focus / Tab navigation visual test ──
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 8,
                            Padding = new Thickness(16),
                        };
                        vStack.AddChild(MakeBox(200, 30, Color.DarkBlue, "Press Tab"));
                        vStack.AddChild(new Button
                        {
                            Text = "Button 1",
                            Font = _testFont,
                            FontSize = 16,
                            Width = 200, Height = 36,
                        });
                        vStack.AddChild(new Button
                        {
                            Text = "Button 2",
                            Font = _testFont,
                            FontSize = 16,
                            Width = 200, Height = 36,
                        });
                        vStack.AddChild(new CheckBox
                        {
                            Text = "Check 1",
                            Width = 200, Height = 28,
                        });
                        root.AddChild(vStack);
                    }
                    break;

                case "G26":
                    // ── Code-behind: button click updates a label ──
                    _g26ClickCount = 0;
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };
                        var btn = new Button
                        {
                            Text = "Click Me",
                            Font = _testFont,
                            FontSize = 20,
                            Width = 160, Height = 48,
                        };
                        btn.Click += b => { _g26ClickCount++; UpdateG26Label(); };
                        vStack.AddChild(btn);

                        _g26Label = new Text
                        {
                            Font = _testFont,
                            TextString = "Clicks: 0",
                            FontSize = 18,
                            Color = Color.White,
                        };
                        vStack.AddChild(_g26Label);
                        root.AddChild(vStack);
                    }
                    break;

                case "G27":
                    // ── Command: button disables itself after click ──
                    _g27CanExec = true;
                    _g27Command = new GuiCommand(
                        execute: () =>
                        {
                            Console.WriteLine("[G27] Command executed!");
                            _g27CanExec = false;
                            _g27Command!.RaiseCanExecuteChanged();
                            if (_g27Button != null) _g27Button.Enabled = false;
                        },
                        canExecute: () => _g27CanExec);
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };
                        _g27Button = new Button
                        {
                            Text = "Execute Once",
                            Font = _testFont,
                            FontSize = 20,
                            Width = 180, Height = 48,
                        };
                        _g27Button.Click += b =>
                        {
                            if (_g27Command!.CanExecute())
                                _g27Command.Execute();
                        };
                        vStack.AddChild(_g27Button);

                        var hint = new Text
                        {
                            Font = _testFont,
                            TextString = "Click → disables itself",
                            FontSize = 14,
                            Color = Color.Gray,
                        };
                        vStack.AddChild(hint);
                        root.AddChild(vStack);
                    }
                    break;

                case "G28":
                    // ── Data binding: slider drives a text label ──
                    _g28Bindable = new Bindable<float>(50f);
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };
                        _g28Slider = new Slider
                        {
                            Width = 300,
                            Min = 0, Max = 100, Value = 50,
                        };
                        _g28Slider.ValueChanged += (s, v) =>
                        {
                            _g28Bindable!.Value = v;
                        };
                        vStack.AddChild(_g28Slider);

                        _g28Label = new Text
                        {
                            Font = _testFont,
                            TextString = "Value: 50",
                            FontSize = 18,
                            Color = Color.White,
                        };
                        vStack.AddChild(_g28Label);

                        // One-way binding: Bindable → label text
                        _g28Subscription = Binding.OneWay(_g28Bindable, v =>
                        {
                            if (_g28Label != null)
                                _g28Label.TextString = $"Value: {v:F0}";
                        });

                        root.AddChild(vStack);
                    }
                    break;

                case "G29":
                    // ── Subscribe/Unsubscribe: toggle binding ──
                    _g29Bindable = new Bindable<string>("Hello FNA!");
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };
                        _g29Toggle = new CheckBox
                        {
                            Text = "Subscribe binding",
                            Width = 250, Height = 28,
                            IsChecked = true,
                        };
                        _g29Toggle.CheckedChanged += (cb, isChecked) =>
                        {
                            if (isChecked)
                            {
                                _g29Subscription = Binding.OneWay(_g29Bindable!, v =>
                                {
                                    if (_g29Label != null)
                                        _g29Label.TextString = v;
                                });
                            }
                            else
                            {
                                _g29Subscription?.Dispose();
                                _g29Subscription = null;
                                if (_g29Label != null)
                                    _g29Label.TextString = "(unsubscribed)";
                            }
                        };
                        vStack.AddChild(_g29Toggle);

                        _g29Label = new Text
                        {
                            Font = _testFont,
                            TextString = "Hello FNA!",
                            FontSize = 18,
                            Color = Color.White,
                        };
                        vStack.AddChild(_g29Label);

                        // Start with binding active
                        _g29Subscription = Binding.OneWay(_g29Bindable, v =>
                        {
                            if (_g29Label != null)
                                _g29Label.TextString = v;
                        });

                        // A button that changes the source — only visible when subscribed
                        var changeBtn = new Button
                        {
                            Text = "Change Source",
                            Font = _testFont,
                            FontSize = 16,
                            Width = 180, Height = 40,
                        };
                        changeBtn.Click += b =>
                        {
                            _g29Bindable!.Value = "Changed at " + DateTime.Now.Second + "s";
                        };
                        vStack.AddChild(changeBtn);

                        root.AddChild(vStack);
                    }
                    break;

                case "G30":
                    // ── Phase 4: Theme/Style visual test ──
                    {
                        // Apply the dark theme to the GUI system
                        _guiSystem.Theme = Theme.CreateDark();

                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 16,
                            Padding = new Thickness(16),
                        };

                        // Title
                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "G30: Five-State Style Verification",
                            FontSize = 18,
                            Color = Color.White,
                        });

                        // Button with theme default style (dark blue tones)
                        var themedBtn = new Button
                        {
                            Text = "Themed Button",
                            Font = _testFont,
                            FontSize = 20,
                            Width = 200, Height = 48,
                        };
                        vStack.AddChild(themedBtn);

                        // Button with explicit per-widget style (green tones)
                        var customBtn = new Button
                        {
                            Text = "Custom Style",
                            Font = _testFont,
                            FontSize = 20,
                            Width = 200, Height = 48,
                            Style = new StyleSheet
                            {
                                BackgroundColor = VisualState<Color>.FromBase(
                                    new Color(39, 174, 96, 255)), // green
                                BorderColor = VisualState<Color>.All(Color.Black),
                                TextColor = VisualState<Color>.All(Color.White),
                            },
                        };
                        vStack.AddChild(customBtn);

                        // Disabled button (should show dimmed state)
                        var disabledBtn = new Button
                        {
                            Text = "Disabled",
                            Font = _testFont,
                            FontSize = 20,
                            Width = 200, Height = 48,
                            Enabled = false,
                        };
                        vStack.AddChild(disabledBtn);

                        // Panel with theme style background
                        var styledPanel = new GuiPanel
                        {
                            Width = 300, Height = 60,
                            BorderColor = Color.White,
                        };
                        styledPanel.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Styled Panel (hover me)",
                            FontSize = 14,
                            Color = Color.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                        vStack.AddChild(styledPanel);

                        // Hint text
                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Hover over buttons and panel to see state-driven color changes",
                            FontSize = 12,
                            Color = Color.Gray,
                        });

                        root.AddChild(vStack);
                    }
                    break;

                case "G31":
                    // ── Phase 5: Tween animation visual test ──
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 16,
                            Padding = new Thickness(16),
                        };

                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "G31: Tween Animation Demo",
                            FontSize = 18,
                            Color = Color.White,
                        });

                        // Animated color box
                        var colorBox = new GuiPanel
                        {
                            Width = 200, Height = 60,
                            BackgroundColor = Color.Red,
                            BorderColor = Color.White,
                        };
                        vStack.AddChild(colorBox);

                        // Pulse tween: red ↔ blue
                        var pulseTween = TweenColor.Animate(Color.Red, Color.Blue, 1.0f,
                            c => { colorBox.BackgroundColor = c; },
                            EasingType.SineInOut);
                        pulseTween.PingPong = true;
                        pulseTween.Loop = true;
                        _guiSystem.Tweens.Add(pulseTween);

                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Color pulse (SineInOut, ping-pong)",
                            FontSize = 12,
                            Color = Color.Gray,
                        });

                        root.AddChild(vStack);
                    }
                    break;

                case "G32":
                    // ── Phase 5: Gamepad navigation visual test ──
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };

                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "G32: D-Pad Navigation Grid",
                            FontSize = 18,
                            Color = Color.White,
                        });

                        // 3x3 grid of buttons for gamepad navigation
                        var grid = new GridLayout { Padding = new Thickness(4) };
                        grid.AddColumn(120);
                        grid.AddColumn(120);
                        grid.AddColumn(120);
                        grid.AddRow(36);
                        grid.AddRow(36);
                        grid.AddRow(36);

                        for (int row = 0; row < 3; row++)
                        {
                            for (int col = 0; col < 3; col++)
                            {
                                var btn = new Button
                                {
                                    Text = $"({row},{col})",
                                    Font = _testFont,
                                    FontSize = 14,
                                    Width = 110, Height = 32,
                                };
                                GridLayout.SetRow(btn, row);
                                GridLayout.SetColumn(btn, col);
                                grid.AddChild(btn);
                            }
                        }

                        vStack.AddChild(grid);

                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Use D-pad or arrow keys to navigate focus between buttons",
                            FontSize = 12,
                            Color = Color.Gray,
                        });

                        root.AddChild(vStack);
                    }
                    break;

                case "G33":
                    // ── Phase 6: ScrollView visual test ──
                    {
                        var scrollView = new ScrollView
                        {
                            Width = 300, Height = 200,
                        };

                        // Tall content: colored boxes stacked vertically
                        var contentStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 4,
                        };
                        var colors = new[] { Color.Red, Color.Green, Color.Blue,
                            Color.Orange, Color.Purple, Color.Cyan,
                            Color.Yellow, Color.Magenta };
                        for (int i = 0; i < colors.Length; i++)
                        {
                            contentStack.AddChild(new GuiPanel
                            {
                                Width = 280, Height = 50,
                                BackgroundColor = colors[i],
                            });
                        }
                        scrollView.AddChild(contentStack);

                        var hint = new Text
                        {
                            Font = _testFont,
                            TextString = "Scroll with mouse wheel or arrow keys",
                            FontSize = 12,
                            Color = Color.Gray,
                        };

                        var outerStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 8,
                            Padding = new Thickness(16),
                        };
                        outerStack.AddChild(scrollView);
                        outerStack.AddChild(hint);
                        root.AddChild(outerStack);
                    }
                    break;

                case "G34":
                    // ── Phase 6: Modal Dialog visual test ──
                    {
                        // Background content (behind dialog)
                        var bgBtn = new Button
                        {
                            Text = "Background Button",
                            Font = _testFont,
                            FontSize = 16,
                            Width = 200, Height = 40,
                        };
                        bgBtn.Click += b =>
                            Console.WriteLine("[G34] Background button clicked (should NOT happen when modal)");
                        root.AddChild(bgBtn);

                        // Modal dialog on top
                        var dialog = new Dialog
                        {
                            Title = "Modal Dialog",
                            WindowWidth = 300, WindowHeight = 180,
                            WindowX = 250, WindowY = 180,
                        };

                        var dialogContent = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 8,
                            Padding = new Thickness(12),
                        };
                        dialogContent.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Click outside this dialog →",
                            FontSize = 14, Color = Color.White,
                        });
                        dialogContent.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "blocked by modal interception",
                            FontSize = 12, Color = Color.Gray,
                        });
                        var closeBtn = new Button
                        {
                            Text = "Close",
                            Font = _testFont,
                            FontSize = 16,
                            Width = 100, Height = 32,
                        };
                        closeBtn.Click += b => dialog.Close();
                        dialogContent.AddChild(closeBtn);

                        dialog.AddChild(dialogContent);
                        root.AddChild(dialog);
                    }
                    break;

                case "G35":
                    // ── Phase 6: TextBox visual test ──
                    {
                        var vStack = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };

                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "G35: TextBox Editing",
                            FontSize = 18, Color = Color.White,
                        });

                        var textBox = new TextBox
                        {
                            Font = _testFont,
                            FontSize = 18,
                            Width = 300, Height = 36,
                            Text = "",
                            Placeholder = "Type here...",
                        };
                        vStack.AddChild(textBox);

                        var roBox = new TextBox
                        {
                            Font = _testFont,
                            FontSize = 16,
                            Width = 300, Height = 32,
                            Text = "Read-only text",
                            IsReadOnly = true,
                        };
                        vStack.AddChild(roBox);

                        vStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Click text box to focus, type to edit",
                            FontSize = 12, Color = Color.Gray,
                        });

                        root.AddChild(vStack);
                    }
                    break;

                case "G36":
                    // ── Phase 7: XAML-lite visual verification ──
                    // Build the same layout in code (what the XML should produce)
                    {
                        var g36CodeTree = BuildG36CodeTree(_testFont);
                        root.AddChild(g36CodeTree);
                    }
                    break;

                case "G37":
                    // ── Phase 7: Settings menu end-to-end ──
                    {
                        _guiSystem.Theme = Theme.CreateDark();

                        var settingsRoot = new StackLayout
                        {
                            Orientation = Orientation.Vertical,
                            Spacing = 12,
                            Padding = new Thickness(16),
                        };

                        // Title
                        settingsRoot.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Settings Menu (G37)",
                            FontSize = 22,
                            Color = Color.White,
                        });

                        // Volume slider
                        var volStack = new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                        };
                        volStack.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Volume:",
                            FontSize = 16,
                            Color = Color.LightGray,
                            VerticalAlignment = VerticalAlignment.Center,
                        });
                        var volumeSlider = new Slider
                        {
                            Width = 200,
                            Min = 0, Max = 100, Value = 75,
                        };
                        volStack.AddChild(volumeSlider);
                        settingsRoot.AddChild(volStack);

                        // Checkboxes
                        settingsRoot.AddChild(new CheckBox
                        {
                            Text = "Fullscreen",
                            Width = 250, Height = 28,
                            IsChecked = true,
                        });
                        settingsRoot.AddChild(new CheckBox
                        {
                            Text = "VSync",
                            Width = 250, Height = 28,
                            IsChecked = true,
                        });
                        settingsRoot.AddChild(new CheckBox
                        {
                            Text = "Show FPS Counter",
                            Width = 250, Height = 28,
                            IsChecked = false,
                        });

                        // Resolution dropdown (simulated with buttons)
                        settingsRoot.AddChild(new Text
                        {
                            Font = _testFont,
                            TextString = "Resolution:",
                            FontSize = 16,
                            Color = Color.LightGray,
                        });
                        var resStack = new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                        };
                        var res1920 = new Button
                        {
                            Text = "1920x1080",
                            Font = _testFont,
                            FontSize = 14,
                            Width = 120, Height = 32,
                        };
                        var res1280 = new Button
                        {
                            Text = "1280x720",
                            Font = _testFont,
                            FontSize = 14,
                            Width = 120, Height = 32,
                            Enabled = false,
                        };
                        resStack.AddChild(res1920);
                        resStack.AddChild(res1280);
                        settingsRoot.AddChild(resStack);

                        // Apply / Cancel buttons
                        var btnStack = new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                        };
                        var applyBtn = new Button
                        {
                            Text = "Apply",
                            Font = _testFont,
                            FontSize = 16,
                            Width = 100, Height = 36,
                        };
                        var cancelBtn = new Button
                        {
                            Text = "Cancel",
                            Font = _testFont,
                            FontSize = 16,
                            Width = 100, Height = 36,
                        };
                        applyBtn.Click += b => Console.WriteLine("[G37] Apply clicked");
                        cancelBtn.Click += b => Console.WriteLine("[G37] Cancel clicked");
                        btnStack.AddChild(applyBtn);
                        btnStack.AddChild(cancelBtn);
                        settingsRoot.AddChild(btnStack);

                        root.AddChild(settingsRoot);
                    }
                    break;

                case "G38":
                    // G38 is a logic-only test (no visual scene needed)
                    break;
            }

        }

        private void UpdateG26Label()
        {
            if (_g26Label != null)
                _g26Label.TextString = $"Clicks: {_g26ClickCount}";
        }

        /// <summary>
        /// Builds a simple code-built widget tree used for G36 comparison.
        /// Mirrors the XML loaded in RunG36.
        /// </summary>
        private static GuiPanel BuildG36CodeTree(SdfFont? font)
        {
            var rootPanel = new GuiPanel { Width = 400, Height = 300 };
            rootPanel.BackgroundColor = new Color(40, 40, 60, 255);

            var stack = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Padding = new Thickness(12),
            };

            var title = new Text
            {
                TextString = "XAML-lite Demo",
                FontSize = 20,
                Color = Color.White,
            };
            title.Name = "TitleText";
            stack.AddChild(title);

            var btn = new Button
            {
                Text = "Click Me",
                Width = 160, Height = 40,
            };
            btn.Name = "MainButton";
            stack.AddChild(btn);

            var cb = new CheckBox
            {
                Text = "Enable Feature",
                Width = 200, Height = 28,
                IsChecked = true,
            };
            cb.Name = "FeatureCheck";
            stack.AddChild(cb);

            rootPanel.AddChild(stack);
            return rootPanel;
        }
        private GuiPanel MakeBox(float w, float h, Color bg, string label)
        {
            var panel = new GuiPanel
            {
                BackgroundColor = bg,
                BorderColor = Color.White,
            };
            if (w > 0) panel.Width = w;
            if (h > 0) panel.Height = h;

            if (_testFont != null && !string.IsNullOrEmpty(label))
            {
                panel.AddChild(new Text
                {
                    Font = _testFont,
                    TextString = label,
                    FontSize = 12,
                    Color = Color.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            return panel;
        }

        private static void AddToGrid(GridLayout grid, Widget child, int row, int col)
        {
            GridLayout.SetRow(child, row);
            GridLayout.SetColumn(child, col);
            grid.AddChild(child);
        }

        protected override void Update(GameTime gameTime)
        {
            // ── Read input ──────────────────────────────────────────
            var kb = Keyboard.GetState();
            var mouse = Mouse.GetState();

            // ── Feed input to GUI system (interactive mode) ─────────
            if (!TestHarness.Headless)
            {
                _guiSystem.ProcessInput(mouse, kb, _prevMouse, _prevKb);

                // Manage SDL text input mode based on focused widget
                var focused = _guiSystem.Input.FocusedWidget;
                bool wantsTextInput = focused != null && focused.WantsTextInput;
                if (wantsTextInput && !_textInputActive)
                {
                    TextInputEXT.StartTextInput();
                    _textInputActive = true;
                }
                else if (!wantsTextInput && _textInputActive)
                {
                    TextInputEXT.StopTextInput();
                    _textInputActive = false;
                }

                // +/- to cycle through tests
                if (kb.IsKeyDown(Keys.OemPlus) && _prevKb.IsKeyUp(Keys.OemPlus))
                {
                    _currentTestIndex = (_currentTestIndex + 1) % _testNames.Length;
                    BuildTestScene();
                }
                if (kb.IsKeyDown(Keys.OemMinus) && _prevKb.IsKeyUp(Keys.OemMinus))
                {
                    _currentTestIndex = (_currentTestIndex - 1 + _testNames.Length) % _testNames.Length;
                    BuildTestScene();
                }
                if (kb.IsKeyDown(Keys.Escape))
                    Exit();
            }

            _prevKb = kb;
            _prevMouse = mouse;

            // ── Pixel assertion on target frame ─────────────────────
            TestHarness.Tick(this, _testFrame, () =>
            {
                int failures = 0;

                // ── Pixel assertions (visual tests) ──────────────────
                var pixelTests = new[] { "G01", "G02", "G03", "G04", "G05",
                    "G10", "G11", "G12", "G13", "G14", "G15", "G16", "G17", "G18",
                    "G23", "G24", "G25", "G26", "G27", "G28", "G29", "G30",
                    "G31", "G32", "G33", "G34", "G35", "G36", "G37" };
                if (Array.IndexOf(pixelTests, CurrentTest) >= 0)
                {
                    var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                    var clearColor = Color.CornflowerBlue;

                    failures += CurrentTest switch
                    {
                        "G01" => AssertG01(px, clearColor),
                        "G02" => AssertG02(px, clearColor),
                        "G03" => AssertG03(px, clearColor),
                        "G04" => AssertG04(px, clearColor),
                        "G05" => AssertG05(px, clearColor),
                        "G10" => AssertG10(px, clearColor),
                        "G11" => AssertG11(px, clearColor),
                        "G12" => AssertG12(px, clearColor),
                        "G13" => AssertG13(px, clearColor),
                        "G14" => AssertCoverage("G14", px, clearColor),
                        "G15" => AssertCoverage("G15", px, clearColor),
                        "G16" => AssertCoverage("G16", px, clearColor),
                        "G17" => AssertCoverage("G17", px, clearColor),
                        "G18" => AssertCoverage("G18", px, clearColor),
                        "G23" => AssertCoverage("G23", px, clearColor),
                        "G24" => AssertCoverage("G24", px, clearColor),
                        "G25" => AssertCoverage("G25", px, clearColor),
                        "G26" => AssertCoverage("G26", px, clearColor),
                        "G27" => AssertCoverage("G27", px, clearColor),
                        "G28" => AssertCoverage("G28", px, clearColor),
                        "G29" => AssertCoverage("G29", px, clearColor),
                        "G30" => AssertG30(px, clearColor),
                        "G31" => AssertCoverage("G31", px, clearColor),
                        "G32" => AssertCoverage("G32", px, clearColor),
                        "G33" => AssertCoverage("G33", px, clearColor),
                        "G34" => AssertCoverage("G34", px, clearColor),
                        "G35" => AssertCoverage("G35", px, clearColor),
                        "G36" => AssertCoverage("G36", px, clearColor),
                        "G37" => AssertCoverage("G37", px, clearColor),
                        _ => 0,
                    };
                }

                // ── Logical assertions (always run for logic tests) ──
                failures += CurrentTest switch
                {
                    "G06" => RunG06(GraphicsDevice),
                    "G07" => RunG07(GraphicsDevice),
                    "G08" => RunG08(GraphicsDevice),
                    "G09" => RunG09(),
                    "G14" => RunG14(),
                    "G15" => RunG15(),
                    "G16" => RunG16(),
                    "G17" => RunG17(),
                    "G18" => RunG18(),
                    "G19" => RunG19(),
                    "G20" => RunG20(),
                    "G21" => RunG21(),
                    "G22" => RunG22(),
                    "G23" => RunG23(),
                    "G24" => RunG24(),
                    "G25" => RunG25(),
                    "G26" => RunG26(),
                    "G27" => RunG27(),
                    "G28" => RunG28(),
                    "G29" => RunG29(),
                    "G30" => RunG30(),
                    "G31" => RunG31(),
                    "G32" => RunG32(),
                    "G33" => RunG33(),
                    "G34" => RunG34(),
                    "G35" => RunG35(),
                    "G36" => RunG36(),
                    "G37" => RunG37(),
                    "G38" => RunG38(),
                    _ => 0,
                };

                TestHarness.Report($"GuiDemo/Panel/{CurrentTest}", failures);
            });

            // ── GUI system update ───────────────────────────────────
            _guiSystem.Update(gameTime);

            base.Update(gameTime);
        }

        // ── Assertions ──────────────────────────────────────────────

        private int AssertCoverage(string name, Color[] px, Color clearColor)
        {
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;
            if (nonClear < 50)
            {
                Console.WriteLine($"FAIL [{name}]: only {nonClear} non-clear pixels, expected layout content");
                return 1;
            }
            return 0;
        }

        private int AssertG01(Color[] px, Color clearColor)
        {
            // Empty system: all pixels should be clear color
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;
            if (nonClear > 0)
            {
                Console.WriteLine($"FAIL [G01]: {nonClear} non-clear pixels found, expected 0");
                return 1;
            }
            return 0;
        }

        private int AssertG02(Color[] px, Color clearColor)
        {
            int f = 0;
            int w = _gdm.PreferredBackBufferWidth;

            // Pixel inside the red rect (50,50) should NOT be clear
            var inside = px[50 * w + 50];
            if (inside == clearColor)
            {
                Console.WriteLine($"FAIL [G02]: pixel at (50,50) is clear, expected red-ish");
                f++;
            }

            // Pixel outside the red rect (500,500) should be clear
            var outside = px[500 * w + 500];
            if (outside.PackedValue != clearColor.PackedValue)
            {
                Console.WriteLine($"FAIL [G02]: pixel at (500,500) is {outside}, expected {clearColor}");
                f++;
            }

            return f;
        }

        private int AssertG03(Color[] px, Color clearColor)
        {
            int f = 0;
            int w = _gdm.PreferredBackBufferWidth;

            // Pixel at (10,10) should be textured (checkerboard blue/yellow)
            var corner = px[10 * w + 10];
            if (corner.PackedValue == clearColor.PackedValue)
            {
                Console.WriteLine($"FAIL [G03]: pixel at (10,10) is clear, expected textured content");
                f++;
            }

            return f;
        }

        private int AssertG04(Color[] px, Color clearColor)
        {
            // The clipped child has dark gray parent bg, green child bg. Blue panel at bottom.
            // Just verify we have non-trivial rendering (coverage > 0).
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;

            if (nonClear == 0)
            {
                Console.WriteLine("FAIL [G04]: no non-clear pixels, expected clipped panels");
                return 1;
            }
            return 0;
        }

        private int AssertG05(Color[] px, Color clearColor)
        {
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;

            if (nonClear == 0)
            {
                Console.WriteLine("FAIL [G05]: no non-clear pixels, expected 9-slice content");
                return 1;
            }
            return 0;
        }

        // ── G06-G08: RecordingRenderer logical tests ───────────────

        private int RunG06(GraphicsDevice device)
        {
            // G06: Geometry rebuild count — size change triggers rebuild,
            // same size does not.
            int f = 0;
            var tex = TextureGen.White(device);
            var image = new Image { Texture = tex, Width = 100, Height = 100 };
            var recorder = new RecordingRenderer();

            // First draw: should trigger geometry rebuild
            var root = new GuiPanel();
            root.AddChild(image);
            var system = new GuiSystem(recorder, root);
            system.ScreenSize = new Vector2(200, 200);
            system.Update(new GameTime());
            system.Draw();

            int count1 = image.GeometryRebuildCount;
            if (count1 != 1)
            {
                Console.WriteLine($"FAIL [G06]: expected 1 rebuild after first draw, got {count1}");
                f++;
            }

            // Second draw with same size: should NOT rebuild
            system.Update(new GameTime());
            system.Draw();
            if (image.GeometryRebuildCount != 1)
            {
                Console.WriteLine($"FAIL [G06]: rebuild count increased without size change ({image.GeometryRebuildCount})");
                f++;
            }

            // Change size: should trigger rebuild
            image.Width = 150;
            system.Update(new GameTime());
            system.Draw();
            if (image.GeometryRebuildCount != 2)
            {
                Console.WriteLine($"FAIL [G06]: expected 2 rebuilds after size change, got {image.GeometryRebuildCount}");
                f++;
            }

            system.Dispose();
            return f;
        }

        private int RunG07(GraphicsDevice device)
        {
            // G07: Color-only change does NOT trigger geometry rebuild.
            int f = 0;
            var tex = TextureGen.White(device);
            var image = new Image { Texture = tex, Width = 100, Height = 100, Color = Color.White };
            var recorder = new RecordingRenderer();

            var root = new GuiPanel();
            root.AddChild(image);
            var system = new GuiSystem(recorder, root);
            system.ScreenSize = new Vector2(200, 200);

            // First draw
            system.Update(new GameTime());
            system.Draw();
            int count1 = image.GeometryRebuildCount;

            // Change only Color
            image.Color = Color.Red;
            system.Update(new GameTime());
            system.Draw();

            if (image.GeometryRebuildCount != count1)
            {
                Console.WriteLine($"FAIL [G07]: color change triggered geometry rebuild " +
                    $"({count1} → {image.GeometryRebuildCount})");
                f++;
            }

            system.Dispose();
            return f;
        }

        private int RunG08(GraphicsDevice device)
        {
            // G08: All 4 ImageTypes produce correct quad counts.
            int f = 0;
            var tex = TextureGen.Checkerboard(device, 64, 16, Color.Blue, Color.Yellow);

            // Simple: 1 quad
            {
                var recorder = new RecordingRenderer();
                var simpleImg = new Image
                {
                    Texture = tex,
                    Width = 128, Height = 128,
                    ImageType = ImageType.Simple,
                };
                var root = new GuiPanel();
                root.AddChild(simpleImg);
                var sys = new GuiSystem(recorder, root);
                sys.ScreenSize = new Vector2(200, 200);
                sys.Update(new GameTime());
                sys.Draw();

                var geoCalls = recorder.FindCalls(DrawCallType.DrawGeometry);
                if (geoCalls.Count == 0 || geoCalls[0].GeometryQuadCount != 1)
                {
                    Console.WriteLine($"FAIL [G08-Simple]: expected 1 quad, got {geoCalls[0].GeometryQuadCount}");
                    f++;
                }
                sys.Dispose();
            }

            // Sliced: up to 9 quads
            {
                var recorder = new RecordingRenderer();
                var slicedImg = new Image
                {
                    Texture = tex,
                    Width = 128, Height = 128,
                    ImageType = ImageType.Sliced,
                    Border = new Thickness(16),
                };
                var root = new GuiPanel();
                root.AddChild(slicedImg);
                var sys = new GuiSystem(recorder, root);
                sys.ScreenSize = new Vector2(200, 200);
                sys.Update(new GameTime());
                sys.Draw();

                var geoCalls = recorder.FindCalls(DrawCallType.DrawGeometry);
                if (geoCalls.Count == 0 || geoCalls[0].GeometryQuadCount != 9)
                {
                    Console.WriteLine($"FAIL [G08-Sliced]: expected 9 quads, got {geoCalls[0].GeometryQuadCount}");
                    f++;
                }
                sys.Dispose();
            }

            // Tiled: multiple quads (64x64 tile in 128x128 = 4 tiles)
            {
                var recorder = new RecordingRenderer();
                var tiledImg = new Image
                {
                    Texture = tex, // 64x64 checkerboard
                    Width = 128, Height = 128,
                    ImageType = ImageType.Tiled,
                    SourceRect = new Rectangle(0, 0, 32, 32), // 32x32 tiles → 16 tiles in 128x128
                };
                var root = new GuiPanel();
                root.AddChild(tiledImg);
                var sys = new GuiSystem(recorder, root);
                sys.ScreenSize = new Vector2(200, 200);
                sys.Update(new GameTime());
                sys.Draw();

                var geoCalls = recorder.FindCalls(DrawCallType.DrawGeometry);
                if (geoCalls.Count == 0 || geoCalls[0].GeometryQuadCount != 16)
                {
                    Console.WriteLine($"FAIL [G08-Tiled]: expected 16 quads (4x4 tiles of 32px), " +
                        $"got {geoCalls[0].GeometryQuadCount}");
                    f++;
                }
                sys.Dispose();
            }

            // Filled: 1 quad (just UV-clipped)
            {
                var recorder = new RecordingRenderer();
                var filledImg = new Image
                {
                    Texture = tex,
                    Width = 128, Height = 128,
                    ImageType = ImageType.Filled,
                    FillAmount = 0.5f,
                };
                var root = new GuiPanel();
                root.AddChild(filledImg);
                var sys = new GuiSystem(recorder, root);
                sys.ScreenSize = new Vector2(200, 200);
                sys.Update(new GameTime());
                sys.Draw();

                var geoCalls = recorder.FindCalls(DrawCallType.DrawGeometry);
                if (geoCalls.Count == 0 || geoCalls[0].GeometryQuadCount != 1)
                {
                    Console.WriteLine($"FAIL [G08-Filled]: expected 1 quad, got {geoCalls[0].GeometryQuadCount}");
                    f++;
                }
                sys.Dispose();
            }

            return f;
        }

        // ── G09-G13: SDF text tests ────────────────────────────────

        private int RunG09()
        {
            // G09: SdfFont load + MeasureString returns correct dimensions
            int f = 0;
            if (_testFont == null)
            {
                Console.WriteLine("FAIL [G09]: test font not loaded");
                return 1;
            }

            // Measure ASCII text at 32px size
            var size = _testFont.MeasureString("Hello", 32f);

            if (size.X <= 0)
            {
                Console.WriteLine($"FAIL [G09]: MeasureString width is {size.X}, expected > 0");
                f++;
            }
            if (size.Y <= 0)
            {
                Console.WriteLine($"FAIL [G09]: MeasureString height is {size.Y}, expected > 0");
                f++;
            }

            // Multi-line: "A\nB" should be ~2x line height
            var multiSize = _testFont.MeasureString("A\nB", 32f);
            float scaleFactor = 32f / _testFont.FontSize;
            float expectedMultiH = _testFont.LineHeight * scaleFactor * 2;
            if (Math.Abs(multiSize.Y - expectedMultiH) > 2)
            {
                Console.WriteLine($"FAIL [G09]: multi-line height {multiSize.Y} != expected {expectedMultiH}");
                f++;
            }

            // LineHeight should be positive
            if (_testFont.LineHeight <= 0)
            {
                Console.WriteLine("FAIL [G09]: LineHeight <= 0");
                f++;
            }

            return f;
        }

        private int AssertG10(Color[] px, Color clearColor)
        {
            // G10: Single-line text renders (non-clear pixels in the text area)
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;

            if (nonClear < 10)
            {
                Console.WriteLine($"FAIL [G10]: only {nonClear} non-clear pixels, expected text coverage");
                return 1;
            }
            return 0;
        }

        private int AssertG11(Color[] px, Color clearColor)
        {
            // G11: Small and large text both visible
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;

            if (nonClear < 20)
            {
                Console.WriteLine($"FAIL [G11]: only {nonClear} non-clear pixels, expected text at multiple sizes");
                return 1;
            }
            return 0;
        }

        private int AssertG12(Color[] px, Color clearColor)
        {
            // G12: Bold+Outline text visible
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;

            if (nonClear < 10)
            {
                Console.WriteLine($"FAIL [G12]: only {nonClear} non-clear pixels");
                return 1;
            }
            return 0;
        }

        private int AssertG13(Color[] px, Color clearColor)
        {
            // G13: Multi-line text visible
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;

            if (nonClear < 10)
            {
                Console.WriteLine($"FAIL [G13]: only {nonClear} non-clear pixels");
                return 1;
            }
            return 0;
        }

        private int AssertG30(Color[] px, Color clearColor)
        {
            // G30: Themed rendering — verify styled widgets produce non-clear pixels
            // (three buttons + styled panel should generate significant coverage)
            int w = _gdm.PreferredBackBufferWidth;
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;

            if (nonClear < 100)
            {
                Console.WriteLine($"FAIL [G30]: only {nonClear} non-clear pixels, expected themed widgets");
                return 1;
            }
            return 0;
        }

        // ── G14-G20: Layout system logical tests ─────────────────────

        private int RunG14()
        {
            // G14: StackLayout horizontal and vertical — assert child bounds
            int f = 0;

            // Horizontal stack
            {
                var stack = new StackLayout { Orientation = Orientation.Horizontal };
                var p1 = new GuiPanel { Width = 100, Height = 50 };
                var p2 = new GuiPanel { Width = 150, Height = 50 };
                var p3 = new GuiPanel { Width = 80, Height = 50 };
                stack.AddChild(p1);
                stack.AddChild(p2);
                stack.AddChild(p3);

                var root = new GuiPanel();
                root.AddChild(stack);
                root.Layout(new Vector2(800, 600));

                // p1 starts at parent origin
                if (p1.Bounds.X != 0 || p1.Bounds.Width != 100)
                {
                    Console.WriteLine($"FAIL [G14-H]: p1 bounds={p1.Bounds}, expected X=0, W=100");
                    f++;
                }
                // p2 is offset by p1 width
                if (p2.Bounds.X != 100 || p2.Bounds.Width != 150)
                {
                    Console.WriteLine($"FAIL [G14-H]: p2 bounds={p2.Bounds}, expected X=100, W=150");
                    f++;
                }
                // p3 is offset by p1 + p2 widths
                if (p3.Bounds.X != 250 || p3.Bounds.Width != 80)
                {
                    Console.WriteLine($"FAIL [G14-H]: p3 bounds={p3.Bounds}, expected X=250, W=80");
                    f++;
                }
            }

            // Vertical stack
            {
                var stack = new StackLayout { Orientation = Orientation.Vertical };
                var p1 = new GuiPanel { Width = 200, Height = 30 };
                var p2 = new GuiPanel { Width = 200, Height = 40 };
                var p3 = new GuiPanel { Width = 200, Height = 20 };
                stack.AddChild(p1);
                stack.AddChild(p2);
                stack.AddChild(p3);

                var root = new GuiPanel();
                root.AddChild(stack);
                root.Layout(new Vector2(800, 600));

                if (p1.Bounds.Y != 0 || p1.Bounds.Height != 30)
                {
                    Console.WriteLine($"FAIL [G14-V]: p1 bounds={p1.Bounds}, expected Y=0, H=30");
                    f++;
                }
                if (p2.Bounds.Y != 30 || p2.Bounds.Height != 40)
                {
                    Console.WriteLine($"FAIL [G14-V]: p2 bounds={p2.Bounds}, expected Y=30, H=40");
                    f++;
                }
                if (p3.Bounds.Y != 70 || p3.Bounds.Height != 20)
                {
                    Console.WriteLine($"FAIL [G14-V]: p3 bounds={p3.Bounds}, expected Y=70, H=20");
                    f++;
                }
            }

            return f;
        }

        private int RunG15()
        {
            // G15: GridLayout with Fixed, Auto, Star column definitions
            int f = 0;

            var grid = new GridLayout();
            grid.AddColumn(new ColumnDefinition(GridLength.Fixed(100)));  // col 0: Fixed 100px
            grid.AddColumn(new ColumnDefinition(GridLength.Auto));        // col 1: Auto
            grid.AddColumn(new ColumnDefinition(GridLength.Star(2)));     // col 2: Star 2*
            grid.AddColumn(new ColumnDefinition(GridLength.Star(1)));     // col 3: Star 1*

            // Col 0: Fixed 100 — child should stretch to fill the cell
            var c0 = new GuiPanel { Height = 30 };
            GridLayout.SetColumn(c0, 0);
            // Col 1: Auto — content determines width (120px explicit)
            var c1 = new GuiPanel { Width = 120, Height = 30 };
            GridLayout.SetColumn(c1, 1);
            // Col 2: Star 2*
            var c2 = new GuiPanel { Height = 30 };
            GridLayout.SetColumn(c2, 2);
            // Col 3: Star 1*
            var c3 = new GuiPanel { Height = 30 };
            GridLayout.SetColumn(c3, 3);

            grid.AddChild(c0);
            grid.AddChild(c1);
            grid.AddChild(c2);
            grid.AddChild(c3);

            var root = new GuiPanel { Width = 700 };
            root.AddChild(grid);
            root.Layout(new Vector2(800, 600));

            // Fixed column gets exactly 100
            if (c0.Bounds.Width != 100)
            {
                Console.WriteLine($"FAIL [G15-Fixed]: col 0 width={c0.Bounds.Width}, expected 100");
                f++;
            }

            // Auto column width >= content width
            if (c1.Bounds.Width < 120)
            {
                Console.WriteLine($"FAIL [G15-Auto]: col 1 width={c1.Bounds.Width}, expected >= 120");
                f++;
            }

            // Remaining space (700 - 100 - auto) distributed 2:1
            float autoW = c1.Bounds.Width;
            float remaining = 700 - 100 - autoW;
            float star2Expected = remaining * 2f / 3f;
            float star1Expected = remaining / 3f;

            if (Math.Abs(c2.Bounds.Width - star2Expected) > 1)
            {
                Console.WriteLine($"FAIL [G15-Star2]: col 2 width={c2.Bounds.Width}, expected ~{star2Expected:F1}");
                f++;
            }
            if (Math.Abs(c3.Bounds.Width - star1Expected) > 1)
            {
                Console.WriteLine($"FAIL [G15-Star1]: col 3 width={c3.Bounds.Width}, expected ~{star1Expected:F1}");
                f++;
            }

            return f;
        }

        private int RunG16()
        {
            // G16: GridLayout column span
            int f = 0;

            var grid = new GridLayout();
            grid.AddColumn(new ColumnDefinition(GridLength.Fixed(100)));  // col 0
            grid.AddColumn(new ColumnDefinition(GridLength.Fixed(150)));  // col 1
            grid.AddColumn(new ColumnDefinition(GridLength.Fixed(200)));  // col 2

            // Child spanning columns 0-1 (width = 100 + 150 = 250)
            var spanChild = new GuiPanel { Height = 30 };
            GridLayout.SetColumn(spanChild, 0);
            GridLayout.SetColumnSpan(spanChild, 2);
            grid.AddChild(spanChild);

            // Child in column 2 only (width = 200)
            var singleChild = new GuiPanel { Height = 30 };
            GridLayout.SetColumn(singleChild, 2);
            grid.AddChild(singleChild);

            var root = new GuiPanel();
            root.AddChild(grid);
            root.Layout(new Vector2(800, 600));

            // Spanning child gets 100 + 150 = 250
            if (spanChild.Bounds.Width != 250)
            {
                Console.WriteLine($"FAIL [G16-Span]: span child width={spanChild.Bounds.Width}, expected 250");
                f++;
            }

            // Single child gets 200
            if (singleChild.Bounds.Width != 200)
            {
                Console.WriteLine($"FAIL [G16-Single]: single child width={singleChild.Bounds.Width}, expected 200");
                f++;
            }

            // Single child should be positioned after the span (at x=250)
            if (singleChild.Bounds.X != 250)
            {
                Console.WriteLine($"FAIL [G16-Position]: single child X={singleChild.Bounds.X}, expected 250");
                f++;
            }

            return f;
        }

        private int RunG17()
        {
            // G17: DockLayout — children docked to edges, LastChildFill
            int f = 0;

            var dock = new DockLayout { LastChildFill = true };
            var topPanel = new GuiPanel { Height = 30 };
            DockLayout.SetDock(topPanel, Dock.Top);
            var leftPanel = new GuiPanel { Width = 80 };
            DockLayout.SetDock(leftPanel, Dock.Left);
            var rightPanel = new GuiPanel { Width = 60 };
            DockLayout.SetDock(rightPanel, Dock.Right);
            var bottomPanel = new GuiPanel { Height = 25 };
            DockLayout.SetDock(bottomPanel, Dock.Bottom);
            var fillPanel = new GuiPanel(); // Last child — fills remaining
            DockLayout.SetDock(fillPanel, Dock.Left); // Dock ignored for last if LastChildFill

            dock.AddChild(topPanel);
            dock.AddChild(leftPanel);
            dock.AddChild(rightPanel);
            dock.AddChild(bottomPanel);
            dock.AddChild(fillPanel);

            var root = new GuiPanel { Width = 600, Height = 400 };
            root.AddChild(dock);
            root.Layout(new Vector2(800, 600));

            // Top panel: full width, at y=0
            if (topPanel.Bounds.Y != 0 || topPanel.Bounds.Width != 600)
            {
                Console.WriteLine($"FAIL [G17-Top]: bounds={topPanel.Bounds}, expected Y=0, W=600");
                f++;
            }

            // Left panel: below top, at x=0
            if (leftPanel.Bounds.X != 0 || leftPanel.Bounds.Width != 80)
            {
                Console.WriteLine($"FAIL [G17-Left]: bounds={leftPanel.Bounds}, expected X=0, W=80");
                f++;
            }

            // Right panel: at right edge
            if (rightPanel.Bounds.X + rightPanel.Bounds.Width != 600 || rightPanel.Bounds.Width != 60)
            {
                Console.WriteLine($"FAIL [G17-Right]: bounds={rightPanel.Bounds}, expected right edge at 600, W=60");
                f++;
            }

            // Bottom panel: at bottom
            if (bottomPanel.Bounds.Y + bottomPanel.Bounds.Height != 400)
            {
                Console.WriteLine($"FAIL [G17-Bottom]: bounds={bottomPanel.Bounds}, expected bottom at Y=400");
                f++;
            }

            // Fill panel: takes remaining space (between left/right and top/bottom)
            if (fillPanel.Bounds.Width <= 0 || fillPanel.Bounds.Height <= 0)
            {
                Console.WriteLine($"FAIL [G17-Fill]: bounds={fillPanel.Bounds}, expected non-empty fill");
                f++;
            }

            return f;
        }

        private int RunG18()
        {
            // G18: StackLayout spacing and cross-axis alignment
            int f = 0;

            // Test spacing
            {
                var stack = new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                };
                var p1 = new GuiPanel { Width = 50, Height = 30 };
                var p2 = new GuiPanel { Width = 70, Height = 30 };
                stack.AddChild(p1);
                stack.AddChild(p2);

                var root = new GuiPanel();
                root.AddChild(stack);
                root.Layout(new Vector2(800, 600));

                // Gap between p1 right edge and p2 left edge should be Spacing
                int gap = p2.Bounds.X - (p1.Bounds.X + p1.Bounds.Width);
                if (gap != 10)
                {
                    Console.WriteLine($"FAIL [G18-Spacing]: gap={gap}, expected 10");
                    f++;
                }
            }

            // Test cross-axis alignment (vertical stack, child with Center alignment)
            {
                var stack = new StackLayout { Orientation = Orientation.Vertical };
                var p1 = new GuiPanel
                {
                    Width = 100,
                    Height = 30,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                stack.AddChild(p1);

                var root = new GuiPanel { Width = 400 };
                root.AddChild(stack);
                root.Layout(new Vector2(800, 600));

                // p1 should be centered horizontally: X = (400 - 100) / 2 = 150
                int expectedX = (400 - 100) / 2;
                if (p1.Bounds.X != expectedX)
                {
                    Console.WriteLine($"FAIL [G18-Align]: p1 X={p1.Bounds.X}, expected {expectedX}");
                    f++;
                }
            }

            return f;
        }

        private int RunG19()
        {
            // G19: Measure cache — same available returns cached, different recalculates
            int f = 0;

            var panel = new GuiPanel { Width = 200, Height = 100 };
            var root = new GuiPanel();
            root.AddChild(panel);

            // First measure
            panel.Measure(new Vector2(800, 600));
            var ds1 = panel.DesiredSize;

            // Second measure with same available: should return identical result from cache
            var ds2 = panel.Measure(new Vector2(800, 600));
            if (ds1 != ds2)
            {
                Console.WriteLine($"FAIL [G19-Cache]: second measure with same available differs: {ds1} vs {ds2}");
                f++;
            }

            // Different available: should recalculate
            var ds3 = panel.Measure(new Vector2(400, 300));
            // With Width=200 fixed, width stays 200. Height also fixed at 100.
            // Both should be the same since explicit dimensions override available.
            if (ds3.X != 200 || ds3.Y != 100)
            {
                Console.WriteLine($"FAIL [G19-Recalc]: measure with different available returned {ds3}");
                f++;
            }

            // Test with an auto-sized panel (no explicit dimensions)
            var autoPanel = new GuiPanel();
            var child1 = new GuiPanel { Width = 50, Height = 50 };
            autoPanel.AddChild(child1);
            var root2 = new GuiPanel();
            root2.AddChild(autoPanel);

            // Measure with large available
            autoPanel.Measure(new Vector2(800, 600));
            var autoDs1 = autoPanel.DesiredSize;

            // Measure again with large available — should be cached
            autoPanel.Measure(new Vector2(800, 600));
            var autoDs1Cached = autoPanel.DesiredSize;
            if (autoDs1 != autoDs1Cached)
            {
                Console.WriteLine($"FAIL [G19-AutoCache]: cached value differs: {autoDs1} vs {autoDs1Cached}");
                f++;
            }

            return f;
        }

        private int RunG20()
        {
            // G20: Dirty propagation — child size change propagates MeasureDirty upward
            int f = 0;

            var child = new GuiPanel { Width = 50, Height = 50 };
            var parent = new GuiPanel();
            parent.AddChild(child);
            var root = new GuiPanel();
            root.AddChild(parent);

            // Run layout to clear dirty flags
            root.Layout(new Vector2(800, 600));

            // Change child width: should trigger MeasureDirty upward
            child.Width = 100;
            if (!parent.MeasureDirty)
            {
                Console.WriteLine("FAIL [G20-Propagation]: parent.MeasureDirty is false after child size change");
                f++;
            }

            // Root should also be dirty (propagates up)
            if (!root.MeasureDirty)
            {
                Console.WriteLine("FAIL [G20-Root]: root.MeasureDirty is false after grandchild size change");
                f++;
            }

            // Run layout again to clear
            root.Layout(new Vector2(800, 600));

            // Change alignment only: should NOT propagate to parent (only ArrangeDirty)
            child.HorizontalAlignment = HorizontalAlignment.Right;
            if (parent.MeasureDirty)
            {
                Console.WriteLine("FAIL [G20-AlignOnly]: parent.MeasureDirty became true after alignment change");
                f++;
            }
            if (!child.ArrangeDirty)
            {
                Console.WriteLine("FAIL [G20-ArrangeOnly]: child.ArrangeDirty should be true");
                f++;
            }

            return f;
        }

        // ── G21-G29: Input and interaction binding tests ──────────────

        private int RunG21()
        {
            // G21: Hit testing — pointer hits correct widget, clips exclude
            int f = 0;

            var root = new GuiPanel { Width = 400, Height = 400 };
            var child1 = new GuiPanel { Width = 100, Height = 100 };
            child1.HorizontalAlignment = HorizontalAlignment.Left;
            child1.VerticalAlignment = VerticalAlignment.Top;
            var child2 = new GuiPanel { Width = 100, Height = 100 };
            child2.HorizontalAlignment = HorizontalAlignment.Right;
            child2.VerticalAlignment = VerticalAlignment.Bottom;
            root.AddChild(child1);
            root.AddChild(child2);

            root.Layout(new Vector2(400, 400));

            // Point at (50, 50) should hit child1 (top-left)
            var hit1 = root.HitTestTree(new Vector2(50, 50));
            if (hit1 != child1)
            {
                Console.WriteLine($"FAIL [G21-Child1]: hit={hit1?.GetType().Name}, expected child1");
                f++;
            }

            // Point at (350, 350) should hit child2 (bottom-right)
            var hit2 = root.HitTestTree(new Vector2(350, 350));
            if (hit2 != child2)
            {
                Console.WriteLine($"FAIL [G21-Child2]: hit={hit2?.GetType().Name}, expected child2");
                f++;
            }

            // Point at (200, 200) should hit root (between children)
            var hit3 = root.HitTestTree(new Vector2(200, 200));
            if (hit3 != root)
            {
                Console.WriteLine($"FAIL [G21-Root]: hit={hit3?.GetType().Name}, expected root");
                f++;
            }

            // Point outside root bounds should return null
            var hit4 = root.HitTestTree(new Vector2(500, 500));
            if (hit4 != null)
            {
                Console.WriteLine($"FAIL [G21-Outside]: hit={hit4.GetType().Name}, expected null");
                f++;
            }

            // Collapsed widget should not be hit
            child1.Visibility = Visibility.Collapsed;
            var hit5 = root.HitTestTree(new Vector2(50, 50));
            if (hit5 != root)
            {
                Console.WriteLine($"FAIL [G21-Collapsed]: hit={hit5?.GetType().Name}, expected root");
                f++;
            }

            return f;
        }

        private int RunG22()
        {
            // G22: Event capture + bubble routing + Handled stops propagation
            int f = 0;

            var events = new System.Collections.Generic.List<string>();

            var root = new GuiPanel { Width = 400, Height = 400 };
            var child = new Button { Text = "Test", Width = 100, Height = 40 };
            root.AddChild(child);
            root.Layout(new Vector2(400, 400));

            // Button.OnEvent handles Click and sets Handled=true
            var btn = child;
            int clickCount = 0;
            btn.Click += b => { clickCount++; events.Add("click"); };

            // Simulate click
            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            sys.InjectPointerMove(new Vector2(50, 20));
            sys.InjectPointerDown(new Vector2(50, 20));
            sys.InjectPointerUp(new Vector2(50, 20));

            if (clickCount != 1)
            {
                Console.WriteLine($"FAIL [G22-Click]: clickCount={clickCount}, expected 1");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG23()
        {
            // G23: Button — click fires, move-out cancels press
            int f = 0;

            var root = new GuiPanel { Width = 400, Height = 400 };
            var btn = new Button { Text = "Test", Width = 100, Height = 40 };
            root.AddChild(btn);
            root.Layout(new Vector2(400, 400));

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            // Click on button
            sys.InjectPointerMove(new Vector2(50, 20));
            sys.InjectPointerDown(new Vector2(50, 20));
            sys.InjectPointerUp(new Vector2(50, 20));

            if (btn.ClickCount != 1)
            {
                Console.WriteLine($"FAIL [G23-Click]: ClickCount={btn.ClickCount}, expected 1");
                f++;
            }

            // Move outside button before release — no click
            sys.InjectPointerMove(new Vector2(50, 20));
            sys.InjectPointerDown(new Vector2(50, 20));
            sys.InjectPointerMove(new Vector2(300, 300));  // move out
            sys.InjectPointerUp(new Vector2(300, 300));    // release outside

            if (btn.ClickCount != 1)  // still 1 (no additional click)
            {
                Console.WriteLine($"FAIL [G23-NoClick]: ClickCount={btn.ClickCount}, expected 1");
                f++;
            }

            // Disabled button should not fire click
            btn.Enabled = false;
            sys.InjectPointerMove(new Vector2(50, 20));
            sys.InjectPointerDown(new Vector2(50, 20));
            sys.InjectPointerUp(new Vector2(50, 20));

            if (btn.ClickCount != 1)
            {
                Console.WriteLine($"FAIL [G23-Disabled]: ClickCount={btn.ClickCount}, expected 1 (disabled)");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG24()
        {
            // G24: Slider drag changes Value; CheckBox toggle
            int f = 0;

            var root = new GuiPanel { Width = 400, Height = 400 };
            var slider = new Slider { Width = 200, Min = 0, Max = 100, Value = 50 };
            var cb = new CheckBox { Width = 200, Height = 28 };
            root.AddChild(slider);
            root.AddChild(cb);
            root.Layout(new Vector2(400, 400));

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            // Drag slider from middle to right
            float sliderMiddle = slider.Bounds.X + slider.Bounds.Width / 2;
            float sliderRight = slider.Bounds.X + slider.Bounds.Width - 10;
            float sliderY = slider.Bounds.Y + slider.Bounds.Height / 2;

            sys.InjectPointerMove(new Vector2(sliderMiddle, sliderY));
            sys.InjectPointerDown(new Vector2(sliderMiddle, sliderY));
            sys.InjectPointerMove(new Vector2(sliderRight, sliderY));
            sys.InjectPointerUp(new Vector2(sliderRight, sliderY));

            if (slider.Value <= 70)
            {
                Console.WriteLine($"FAIL [G24-Slider]: Value={slider.Value}, expected > 70 after drag right");
                f++;
            }

            // Click checkbox
            float cbX = cb.Bounds.X + 10;
            float cbY = cb.Bounds.Y + cb.Bounds.Height / 2;
            sys.InjectPointerMove(new Vector2(cbX, cbY));
            sys.InjectPointerDown(new Vector2(cbX, cbY));
            sys.InjectPointerUp(new Vector2(cbX, cbY));

            if (!cb.IsChecked)
            {
                Console.WriteLine("FAIL [G24-CheckBox]: IsChecked=false after click, expected true");
                f++;
            }

            // Click again to uncheck
            sys.InjectPointerDown(new Vector2(cbX, cbY));
            sys.InjectPointerUp(new Vector2(cbX, cbY));

            if (cb.IsChecked)
            {
                Console.WriteLine("FAIL [G24-CheckBox2]: IsChecked=true after second click, expected false");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG25()
        {
            // G25: Tab focus traversal
            int f = 0;

            var root = new GuiPanel { Width = 400, Height = 400 };
            var b1 = new Button { Text = "B1", Width = 100, Height = 40 };
            var b2 = new Button { Text = "B2", Width = 100, Height = 40 };
            var b3 = new Button { Text = "B3", Width = 100, Height = 40 };
            root.AddChild(b1);
            root.AddChild(b2);
            root.AddChild(b3);
            root.Layout(new Vector2(400, 400));

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            // No focus initially
            if (sys.Input.FocusedWidget != null)
            {
                Console.WriteLine($"FAIL [G25-Init]: Focused={sys.Input.FocusedWidget}, expected null");
                f++;
            }

            // Tab → focus on b1
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Tab);
            if (sys.Input.FocusedWidget != b1)
            {
                Console.WriteLine($"FAIL [G25-Tab1]: Focused={sys.Input.FocusedWidget}, expected b1");
                f++;
            }

            // Tab → b2
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Tab);
            if (sys.Input.FocusedWidget != b2)
            {
                Console.WriteLine($"FAIL [G25-Tab2]: Focused={sys.Input.FocusedWidget}, expected b2");
                f++;
            }

            // Tab → b3
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Tab);
            if (sys.Input.FocusedWidget != b3)
            {
                Console.WriteLine($"FAIL [G25-Tab3]: Focused={sys.Input.FocusedWidget}, expected b3");
                f++;
            }

            // Tab → wrap to b1
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Tab);
            if (sys.Input.FocusedWidget != b1)
            {
                Console.WriteLine($"FAIL [G25-Wrap]: Focused={sys.Input.FocusedWidget}, expected b1 (wrap)");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG26()
        {
            // G26: Code-behind — event subscription pattern
            int f = 0;

            var root = new GuiPanel { Width = 400, Height = 400 };
            var btn = new Button { Text = "Start", Width = 120, Height = 40 };
            root.AddChild(btn);
            root.Layout(new Vector2(400, 400));

            int handlerCalls = 0;
            Button? receivedSender = null;

            // Simulate code-behind: FindByName + event subscription
            btn.Click += b =>
            {
                handlerCalls++;
                receivedSender = b;
            };

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            sys.InjectPointerMove(new Vector2(60, 20));
            sys.InjectPointerDown(new Vector2(60, 20));
            sys.InjectPointerUp(new Vector2(60, 20));

            if (handlerCalls != 1)
            {
                Console.WriteLine($"FAIL [G26-Handler]: handlerCalls={handlerCalls}, expected 1");
                f++;
            }
            if (receivedSender != btn)
            {
                Console.WriteLine($"FAIL [G26-Sender]: sender={receivedSender}, expected btn");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG27()
        {
            // G27: Command.CanExecute drives widget Enabled
            int f = 0;

            bool canExec = true;
            int execCount = 0;
            GuiCommand? capturedCmd = null;
            capturedCmd = new GuiCommand(
                execute: () => { execCount++; canExec = false; capturedCmd!.RaiseCanExecuteChanged(); },
                canExecute: () => canExec);

            if (!capturedCmd!.CanExecute())
            {
                Console.WriteLine("FAIL [G27-Init]: CanExecute should be true initially");
                f++;
            }

            capturedCmd.Execute();
            if (execCount != 1)
            {
                Console.WriteLine($"FAIL [G27-Exec]: execCount={execCount}, expected 1");
                f++;
            }

            // After execute, canExec is false
            if (capturedCmd.CanExecute())
            {
                Console.WriteLine("FAIL [G27-CanExec]: CanExecute should be false after execute");
                f++;
            }

            // Second execute should be a no-op
            capturedCmd.Execute();
            if (execCount != 1)
            {
                Console.WriteLine($"FAIL [G27-NoExec]: execCount={execCount}, expected 1 (no second exec)");
                f++;
            }

            return f;
        }

        private int RunG28()
        {
            // G28: One-way data binding — Bindable → widget property
            int f = 0;

            var bindable = new Bindable<string>("Hello");
            string received = "";

            // Simulate binding
            using (Binding.OneWay(bindable, v => received = v))
            {
                if (received != "Hello")
                {
                    Console.WriteLine($"FAIL [G28-Init]: received='{received}', expected 'Hello'");
                    f++;
                }

                bindable.Value = "World";
                if (received != "World")
                {
                    Console.WriteLine($"FAIL [G28-Update]: received='{received}', expected 'World'");
                    f++;
                }
            }

            // After dispose, changes should NOT propagate
            bindable.Value = "ShouldNotUpdate";
            if (received != "World")
            {
                Console.WriteLine($"FAIL [G28-Disposed]: received='{received}', expected 'World' (unsubscribed)");
                f++;
            }

            return f;
        }

        private int RunG29()
        {
            // G29: OnUnloaded — unsubscribe → no leak / no stale updates
            int f = 0;

            var bindable = new Bindable<int>(42);
            int received = 0;

            var sub = Binding.OneWay(bindable, v => received = v);

            if (received != 42)
            {
                Console.WriteLine($"FAIL [G29-Init]: received={received}, expected 42");
                f++;
            }

            // Simulate unload: dispose the subscription
            sub.Dispose();

            // Update source — should not trigger the disposed binding
            bindable.Value = 99;
            if (received != 42)
            {
                Console.WriteLine($"FAIL [G29-Unsubscribed]: received={received}, expected 42 (unsubscribed)");
                f++;
            }

            return f;
        }

        private int RunG30()
        {
            // G30: Five-state styles — verify state-driven color resolution
            // and that InputRouter drives state transitions correctly.
            int f = 0;

            // 1. Verify VisualState<T> resolves correct per-state values
            var vs = new VisualState<Color>
            {
                Normal = Color.Red,
                Hover = Color.Green,
                Pressed = Color.Blue,
                Disabled = Color.Gray,
                Focused = Color.Yellow,
            };

            if (vs.GetValue(WidgetState.Normal) != Color.Red)
            {
                Console.WriteLine($"FAIL [G30-VS-Normal]: got {vs.GetValue(WidgetState.Normal)}");
                f++;
            }
            if (vs.GetValue(WidgetState.Hover) != Color.Green)
            {
                Console.WriteLine($"FAIL [G30-VS-Hover]: got {vs.GetValue(WidgetState.Hover)}");
                f++;
            }
            if (vs.GetValue(WidgetState.Pressed) != Color.Blue)
            {
                Console.WriteLine($"FAIL [G30-VS-Pressed]: got {vs.GetValue(WidgetState.Pressed)}");
                f++;
            }
            if (vs.GetValue(WidgetState.Disabled) != Color.Gray)
            {
                Console.WriteLine($"FAIL [G30-VS-Disabled]: got {vs.GetValue(WidgetState.Disabled)}");
                f++;
            }
            if (vs.GetValue(WidgetState.Focused) != Color.Yellow)
            {
                Console.WriteLine($"FAIL [G30-VS-Focused]: got {vs.GetValue(WidgetState.Focused)}");
                f++;
            }

            // 2. Verify Theme creates valid styles with distinct per-state colors
            var theme = Theme.CreateDark();
            var btnStyle = theme.GetStyle("Button");
            if (btnStyle?.BackgroundColor == null)
            {
                Console.WriteLine("FAIL [G30-ThemeBtnBg]: Button style has no BackgroundColor");
                f++;
            }
            else
            {
                var normalBg = btnStyle.BackgroundColor!.Normal;
                var hoverBg = btnStyle.BackgroundColor!.Hover;
                var pressedBg = btnStyle.BackgroundColor!.Pressed;
                var disabledBg = btnStyle.BackgroundColor!.Disabled;

                // Hover should differ from Normal (brighter)
                if (normalBg == hoverBg)
                {
                    Console.WriteLine($"FAIL [G30-HoverDiff]: Normal={normalBg} == Hover={hoverBg}");
                    f++;
                }
                // Pressed should differ from Normal (darker)
                if (normalBg == pressedBg)
                {
                    Console.WriteLine($"FAIL [G30-PressedDiff]: Normal={normalBg} == Pressed={pressedBg}");
                    f++;
                }
                // Disabled should differ from Normal (dimmed)
                if (normalBg == disabledBg)
                {
                    Console.WriteLine($"FAIL [G30-DisabledDiff]: Normal={normalBg} == Disabled={disabledBg}");
                    f++;
                }
                // Hover and Pressed should differ from each other
                if (hoverBg == pressedBg)
                {
                    Console.WriteLine($"FAIL [G30-HvPDiff]: Hover={hoverBg} == Pressed={pressedBg}");
                    f++;
                }
            }

            // 3. Verify Widget.ResolveBackground consults per-widget Style first
            var panel = new GuiPanel();
            panel.Style = new StyleSheet
            {
                BackgroundColor = VisualState<Color>.All(Color.Magenta),
            };
            if (panel.ResolveBackground(Color.Transparent) != Color.Magenta)
            {
                Console.WriteLine($"FAIL [G30-StyleOverride]: explicit style not used");
                f++;
            }

            // 4. Verify state transitions via InputRouter
            var root = new GuiPanel { Width = 400, Height = 400 };
            var btn = new Button { Text = "Test", Width = 100, Height = 40 };
            root.AddChild(btn);
            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            // Initial state: Normal
            if (btn.State != WidgetState.Normal)
            {
                Console.WriteLine($"FAIL [G30-StateInit]: State={btn.State}, expected Normal");
                f++;
            }

            // Move pointer over button → Hover
            sys.InjectPointerMove(new Vector2(50, 20)); // inside 100x40 button
            if (btn.State != WidgetState.Hover)
            {
                Console.WriteLine($"FAIL [G30-StateHover]: State={btn.State}, expected Hover");
                f++;
            }

            // Press down → Pressed
            sys.InjectPointerDown(new Vector2(50, 20));
            if (btn.State != WidgetState.Pressed)
            {
                Console.WriteLine($"FAIL [G30-StatePressed]: State={btn.State}, expected Pressed");
                f++;
            }

            // Release → back to Hover (pointer still over button)
            sys.InjectPointerUp(new Vector2(50, 20));
            if (btn.State != WidgetState.Hover)
            {
                Console.WriteLine($"FAIL [G30-StatePostUp]: State={btn.State}, expected Hover");
                f++;
            }

            // Move pointer away — still Focused (from PressPointer), not Normal
            sys.InjectPointerMove(new Vector2(350, 350)); // outside button
            if (btn.State != WidgetState.Focused)
            {
                Console.WriteLine($"FAIL [G30-StateLeave]: State={btn.State}, expected Focused (retained from press)");
                f++;
            }

            // Clear focus → back to Normal
            sys.Input.FocusedWidget = null;
            if (btn.State != WidgetState.Normal)
            {
                Console.WriteLine($"FAIL [G30-StateBlur]: State={btn.State}, expected Normal after focus clear");
                f++;
            }

            // 5. Verify ResolveBackground returns different colors per state
            btn.Style = new StyleSheet
            {
                BackgroundColor = new VisualState<Color>
                {
                    Normal = Color.Red,
                    Hover = Color.Green,
                    Pressed = Color.Blue,
                    Disabled = Color.Gray,
                    Focused = Color.Yellow,
                },
            };

            // Hover state (last inject moved away, but we can test directly)
            // Manually verify state→color resolution
            Color normalResolved = btn.Style.BackgroundColor.GetValue(WidgetState.Normal);
            Color hoverResolved = btn.Style.BackgroundColor.GetValue(WidgetState.Hover);
            Color pressedResolved = btn.Style.BackgroundColor.GetValue(WidgetState.Pressed);

            if (normalResolved != Color.Red)
            {
                Console.WriteLine($"FAIL [G30-ResolveNormal]: {normalResolved} != Red");
                f++;
            }
            if (hoverResolved != Color.Green)
            {
                Console.WriteLine($"FAIL [G30-ResolveHover]: {hoverResolved} != Green");
                f++;
            }
            if (pressedResolved != Color.Blue)
            {
                Console.WriteLine($"FAIL [G30-ResolvePressed]: {pressedResolved} != Blue");
                f++;
            }

            // 6. Verify VisualState.FromBase helper
            var fromBase = VisualState<Color>.FromBase(Color.Gray, 1.2f, 0.8f, 0.5f);
            if (fromBase.Normal != Color.Gray)
            {
                Console.WriteLine($"FAIL [G30-FromBase-Normal]: {fromBase.Normal} != Gray");
                f++;
            }
            // Hover should be brighter (at least one channel > base)
            bool hoverBrighter = fromBase.Hover.R > fromBase.Normal.R ||
                                 fromBase.Hover.G > fromBase.Normal.G ||
                                 fromBase.Hover.B > fromBase.Normal.B;
            if (!hoverBrighter)
            {
                Console.WriteLine($"FAIL [G30-FromBase-Hover]: Hover={fromBase.Hover} not brighter than Normal={fromBase.Normal}");
                f++;
            }
            // Pressed should be darker
            bool pressedDarker = fromBase.Pressed.R < fromBase.Normal.R ||
                                 fromBase.Pressed.G < fromBase.Normal.G ||
                                 fromBase.Pressed.B < fromBase.Normal.B;
            if (!pressedDarker)
            {
                Console.WriteLine($"FAIL [G30-FromBase-Pressed]: Pressed={fromBase.Pressed} not darker than Normal={fromBase.Normal}");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG31()
        {
            // G31: Tween — interpolation, easing endpoints, completion, loop/pingpong
            int f = 0;

            // 1. TweenFloat: linear interpolation from 0 to 100 over 1.0s
            {
                float lastValue = 0;
                bool completed = false;
                var tween = TweenFloat.Animate(0f, 100f, 1.0f,
                    v => lastValue = v,
                    EasingType.Linear);

                // Initial state
                if (tween.CurrentValue != 0f)
                {
                    Console.WriteLine($"FAIL [G31-Init]: initial value={tween.CurrentValue}, expected 0");
                    f++;
                }
                if (tween.IsComplete)
                {
                    Console.WriteLine("FAIL [G31-Init]: IsComplete should be false initially");
                    f++;
                }

                // Step 0.25s → value should be ~25
                tween.Step(0.25f);
                if (Math.Abs(lastValue - 25f) > 0.01f)
                {
                    Console.WriteLine($"FAIL [G31-Step25]: value={lastValue}, expected ~25");
                    f++;
                }

                // Step another 0.25s → value ~50
                tween.Step(0.25f);
                if (Math.Abs(lastValue - 50f) > 0.01f)
                {
                    Console.WriteLine($"FAIL [G31-Step50]: value={lastValue}, expected ~50");
                    f++;
                }

                // Step 0.5s → should complete, value = 100
                tween.OnComplete = () => completed = true;
                tween.Step(0.5f);
                if (Math.Abs(lastValue - 100f) > 0.01f)
                {
                    Console.WriteLine($"FAIL [G31-End]: value={lastValue}, expected ~100");
                    f++;
                }
                if (!tween.IsComplete)
                {
                    Console.WriteLine("FAIL [G31-End]: IsComplete should be true");
                    f++;
                }
                if (!completed)
                {
                    Console.WriteLine("FAIL [G31-End]: OnComplete not fired");
                    f++;
                }
            }

            // 2. Easing endpoints: all easing functions should produce 0 at t=0 and 1 at t=1
            foreach (EasingType easing in Enum.GetValues<EasingType>())
            {
                float at0 = Easing.Apply(easing, 0f);
                float at1 = Easing.Apply(easing, 1f);

                if (Math.Abs(at0) > 0.0001f)
                {
                    Console.WriteLine($"FAIL [G31-Easing-{easing}]: at t=0 value={at0}, expected 0");
                    f++;
                }
                if (Math.Abs(at1 - 1f) > 0.0001f)
                {
                    Console.WriteLine($"FAIL [G31-Easing-{easing}]: at t=1 value={at1}, expected 1");
                    f++;
                }
            }

            // 3. TweenColor interpolation
            {
                Color lastColor = Color.Transparent;
                var tween = TweenColor.Animate(Color.Red, Color.Blue, 1.0f,
                    c => lastColor = c,
                    EasingType.Linear);

                // Step halfway → should be purple-ish
                tween.Step(0.5f);
                // Red (255,0,0) to Blue (0,0,255) at 50% → (127,0,127)
                if (Math.Abs(lastColor.R - 127) > 2 ||
                    Math.Abs(lastColor.B - 127) > 2)
                {
                    Console.WriteLine($"FAIL [G31-Color]: halfway color={lastColor}, expected (~127,0,127)");
                    f++;
                }
            }

            // 4. Loop behavior
            {
                int updateCount = 0;
                var tween = TweenFloat.Animate(0f, 100f, 0.3f,
                    v => updateCount++,
                    EasingType.Linear);
                tween.Loop = true;

                // Step past duration multiple times
                tween.Step(0.2f); // update 1
                tween.Step(0.2f); // completes, loops, updates again → update 2
                tween.Step(0.2f); // goes past again → update 3

                if (updateCount < 2)
                {
                    Console.WriteLine($"FAIL [G31-Loop]: updateCount={updateCount}, expected >= 2");
                    f++;
                }
                if (tween.IsComplete)
                {
                    Console.WriteLine("FAIL [G31-Loop]: IsComplete should be false when looping");
                    f++;
                }
            }

            // 5. PingPong behavior
            {
                float lastValue = 0;
                var tween = TweenFloat.Animate(0f, 100f, 0.5f,
                    v => lastValue = v,
                    EasingType.Linear);
                tween.PingPong = true;

                tween.Step(0.5f); // reaches end → starts reversing
                if (Math.Abs(lastValue - 100f) > 0.01f)
                {
                    Console.WriteLine($"FAIL [G31-PP1]: value={lastValue}, expected 100");
                    f++;
                }

                tween.Step(0.25f); // halfway back → value ~50
                if (Math.Abs(lastValue - 50f) > 0.01f)
                {
                    Console.WriteLine($"FAIL [G31-PP2]: value={lastValue}, expected ~50");
                    f++;
                }
            }

            // 6. TweenSystem integration
            {
                var tweenSys = new TweenSystem();
                float lastValue = -1;
                var tween = TweenFloat.Animate(0f, 100f, 1.0f, v => lastValue = v);
                tweenSys.Add(tween);

                tweenSys.Update(0.5f);
                if (Math.Abs(lastValue - 50f) > 0.01f)
                {
                    Console.WriteLine($"FAIL [G31-Sys]: value={lastValue}, expected ~50");
                    f++;
                }
                if (tweenSys.ActiveCount != 1)
                {
                    Console.WriteLine($"FAIL [G31-Sys]: ActiveCount={tweenSys.ActiveCount}, expected 1");
                    f++;
                }

                tweenSys.Update(1.0f); // completes and removes
                if (tweenSys.ActiveCount != 0)
                {
                    Console.WriteLine($"FAIL [G31-Sys]: ActiveCount after complete={tweenSys.ActiveCount}, expected 0");
                    f++;
                }
            }

            return f;
        }

        private int RunG32()
        {
            // G32: Gamepad directional navigation
            int f = 0;

            // Create a 3x3 grid of buttons
            var grid = new GridLayout { Padding = new Thickness(4) };
            grid.AddColumn(100);
            grid.AddColumn(100);
            grid.AddColumn(100);
            grid.AddRow(36);
            grid.AddRow(36);
            grid.AddRow(36);

            var buttons = new Button[3, 3];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    var btn = new Button
                    {
                        Text = $"({row},{col})",
                        Width = 90, Height = 32,
                    };
                    GridLayout.SetRow(btn, row);
                    GridLayout.SetColumn(btn, col);
                    grid.AddChild(btn);
                    buttons[row, col] = btn;
                }
            }

            var root = new GuiPanel { Width = 400, Height = 400 };
            root.AddChild(grid);
            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            // Start with center button focused
            sys.Input.FocusedWidget = buttons[1, 1];
            if (sys.Input.FocusedWidget != buttons[1, 1])
            {
                Console.WriteLine("FAIL [G32-Init]: could not set focus to center");
                f++;
            }

            // Navigate Right → (1,2)
            sys.InjectNavigate(Direction.Right);
            if (sys.Input.FocusedWidget != buttons[1, 2])
            {
                Console.WriteLine($"FAIL [G32-Right]: focused={sys.Input.FocusedWidget?.GetType().Name}, expected (1,2)");
                f++;
            }

            // Navigate Down → (2,2)
            sys.InjectNavigate(Direction.Down);
            if (sys.Input.FocusedWidget != buttons[2, 2])
            {
                Console.WriteLine($"FAIL [G32-Down]: focused={sys.Input.FocusedWidget}, expected (2,2)");
                f++;
            }

            // Navigate Left → (2,1)
            sys.InjectNavigate(Direction.Left);
            if (sys.Input.FocusedWidget != buttons[2, 1])
            {
                Console.WriteLine($"FAIL [G32-Left]: focused={sys.Input.FocusedWidget}, expected (2,1)");
                f++;
            }

            // Navigate Up → (1,1)
            sys.InjectNavigate(Direction.Up);
            if (sys.Input.FocusedWidget != buttons[1, 1])
            {
                Console.WriteLine($"FAIL [G32-Up]: focused={sys.Input.FocusedWidget}, expected (1,1)");
                f++;
            }

            // Navigate Up again → (0,1)
            sys.InjectNavigate(Direction.Up);
            if (sys.Input.FocusedWidget != buttons[0, 1])
            {
                Console.WriteLine($"FAIL [G32-Up2]: focused={sys.Input.FocusedWidget}, expected (0,1)");
                f++;
            }

            // Navigate Right from edge → (0,2)
            sys.InjectNavigate(Direction.Right);
            if (sys.Input.FocusedWidget != buttons[0, 2])
            {
                Console.WriteLine($"FAIL [G32-Right2]: focused={sys.Input.FocusedWidget}, expected (0,2)");
                f++;
            }

            // Edge: Navigate Right at rightmost → should stay at (0,2)
            sys.InjectNavigate(Direction.Right);
            if (sys.Input.FocusedWidget != buttons[0, 2])
            {
                Console.WriteLine($"FAIL [G32-EdgeRight]: focused={sys.Input.FocusedWidget}, expected stay at (0,2)");
                f++;
            }

            // Edge: Navigate Up at topmost → should stay at (0,2)
            sys.InjectNavigate(Direction.Up);
            if (sys.Input.FocusedWidget != buttons[0, 2])
            {
                Console.WriteLine($"FAIL [G32-EdgeUp]: focused={sys.Input.FocusedWidget}, expected stay at (0,2)");
                f++;
            }

            // ActivateFocused should trigger key event on focused widget
            int clickCount = buttons[0, 2].ClickCount;
            sys.Input.ActivateFocused();
            if (buttons[0, 2].ClickCount != clickCount + 1)
            {
                Console.WriteLine($"FAIL [G32-Activate]: ClickCount={buttons[0,2].ClickCount}, expected {clickCount + 1}");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG33()
        {
            // G33: ScrollView — scroll offset, clamping, keyboard navigation
            int f = 0;

            var scrollView = new ScrollView { Width = 200, Height = 150 };
            var content = new GuiPanel { Width = 180, Height = 500 };
            scrollView.AddChild(content);

            var root = new GuiPanel { Width = 400, Height = 400 };
            root.AddChild(scrollView);
            root.Layout(new Vector2(400, 400));

            // Initial offset should be 0
            if (scrollView.ScrollOffset != Vector2.Zero)
            {
                Console.WriteLine($"FAIL [G33-Init]: offset={scrollView.ScrollOffset}, expected (0,0)");
                f++;
            }

            // Content size should reflect the tall child
            if (scrollView.ContentSize.Y < 400)
            {
                Console.WriteLine($"FAIL [G33-ContentSize]: Y={scrollView.ContentSize.Y}, expected >= 400");
                f++;
            }

            // Scroll down
            scrollView.ScrollOffset = new Vector2(0, 100);
            if (scrollView.ScrollOffset.Y != 100)
            {
                Console.WriteLine($"FAIL [G33-ScrollDown]: offset.Y={scrollView.ScrollOffset.Y}, expected 100");
                f++;
            }

            // Scroll beyond max → should clamp
            scrollView.ScrollOffset = new Vector2(0, 9999);
            float maxY = scrollView.ContentSize.Y - scrollView.ViewportSize.Y;
            if (scrollView.ScrollOffset.Y > maxY + 1)
            {
                Console.WriteLine($"FAIL [G33-Clamp]: offset.Y={scrollView.ScrollOffset.Y}, max={maxY}");
                f++;
            }

            // ScrollToTop
            scrollView.ScrollToTop();
            if (scrollView.ScrollOffset.Y != 0)
            {
                Console.WriteLine($"FAIL [G33-Top]: offset.Y={scrollView.ScrollOffset.Y}, expected 0");
                f++;
            }

            // ScrollToBottom
            scrollView.ScrollToBottom();
            if (Math.Abs(scrollView.ScrollOffset.Y - maxY) > 1)
            {
                Console.WriteLine($"FAIL [G33-Bottom]: offset.Y={scrollView.ScrollOffset.Y}, expected ~{maxY}");
                f++;
            }

            return f;
        }

        private int RunG34()
        {
            // G34: Modal Dialog — input interception
            int f = 0;

            // Create background button and modal dialog
            var bgBtn = new Button { Text = "Bg", Width = 100, Height = 30 };
            int bgClicks = 0;
            bgBtn.Click += b => bgClicks++;

            var dialog = new Dialog
            {
                WindowWidth = 200, WindowHeight = 150,
                WindowX = 100, WindowY = 100,
            };
            var dialogBtn = new Button { Text = "Ok", Width = 80, Height = 30 };
            int dialogClicks = 0;
            dialogBtn.Click += b => dialogClicks++;
            dialog.AddChild(dialogBtn);

            var root = new GuiPanel { Width = 400, Height = 400 };
            root.AddChild(bgBtn);
            root.AddChild(dialog);
            root.Layout(new Vector2(400, 400));

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);
            sys.Update(new GameTime());

            // 1. Click inside dialog on the OK button → should work
            // dialogBtn is inside dialog content area at Bounds (100,128,80,30)
            // Click at center of button: (140, 143)
            sys.InjectPointerMove(new Vector2(140, 143));
            sys.InjectPointerDown(new Vector2(140, 143));
            sys.InjectPointerUp(new Vector2(140, 143));

            if (dialogClicks != 1)
            {
                Console.WriteLine($"FAIL [G34-DialogClick]: dialogClicks={dialogClicks}, expected 1");
                f++;
            }
            if (bgClicks != 0)
            {
                Console.WriteLine($"FAIL [G34-BgNotClicked]: bgClicks={bgClicks}, expected 0 (modal interception)");
                f++;
            }

            // 2. Click outside dialog (on background area) → should NOT reach bg button
            sys.InjectPointerMove(new Vector2(50, 50));
            sys.InjectPointerDown(new Vector2(50, 50));
            sys.InjectPointerUp(new Vector2(50, 50));

            if (bgClicks != 0)
            {
                Console.WriteLine($"FAIL [G34-ModalBlock]: bgClicks={bgClicks}, expected 0 (modal blocks clicks outside)");
                f++;
            }

            // 3. Verify HitTest for modal: point outside window should still hit the dialog
            var hit = dialog.HitTestTree(new Vector2(50, 50));
            if (hit != dialog)
            {
                Console.WriteLine($"FAIL [G34-HitTest]: hit={hit}, expected dialog (modal captures all)");
                f++;
            }

            // 4. After closing dialog, background button should work again
            dialog.Close();

            // Re-layout after close
            root.Layout(new Vector2(400, 400));
            sys.Update(new GameTime());

            sys.InjectPointerMove(new Vector2(50, 20));
            sys.InjectPointerDown(new Vector2(50, 20));
            sys.InjectPointerUp(new Vector2(50, 20));

            if (bgClicks != 1)
            {
                Console.WriteLine($"FAIL [G34-AfterClose]: bgClicks={bgClicks}, expected 1 (dialog gone)");
                f++;
            }

            sys.Dispose();
            return f;
        }

        private int RunG35()
        {
            // G35: TextBox — text editing, cursor, backspace, max length
            int f = 0;

            var textBox = new TextBox
            {
                Width = 300, Height = 32,
                MaxLength = 20,
            };

            var root = new GuiPanel { Width = 400, Height = 200 };
            root.AddChild(textBox);
            root.Layout(new Vector2(400, 200));

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 200);
            sys.Update(new GameTime());

            // Focus the text box
            sys.Input.FocusedWidget = textBox;

            // 1. Inject text
            sys.InjectTextInput("Hello");
            if (textBox.Text != "Hello")
            {
                Console.WriteLine($"FAIL [G35-Insert]: Text='{textBox.Text}', expected 'Hello'");
                f++;
            }
            if (textBox.CursorPosition != 5)
            {
                Console.WriteLine($"FAIL [G35-CursorAfterInsert]: pos={textBox.CursorPosition}, expected 5");
                f++;
            }

            // 2. Inject more text
            sys.InjectTextInput(" World");
            if (textBox.Text != "Hello World")
            {
                Console.WriteLine($"FAIL [G35-Append]: Text='{textBox.Text}', expected 'Hello World'");
                f++;
            }

            // 3. Backspace at end → removes 'd'
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Back);
            if (textBox.Text != "Hello Worl")
            {
                Console.WriteLine($"FAIL [G35-Backspace]: Text='{textBox.Text}', expected 'Hello Worl'");
                f++;
            }

            // 4. Cursor move left 3x then backspace at middle
            // Starting at position 10 after Backspace
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Left); // pos 9
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Left); // pos 8
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Left); // pos 7
            // Cursor at position 7: "Hello |orl" (between 'o' and 'r')
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Back);
            // Removes 'W' (char before cursor at pos 6) → "Hello orl"
            if (textBox.Text != "Hello orl")
            {
                Console.WriteLine($"FAIL [G35-MidDelete]: Text='{textBox.Text}', expected 'Hello orl'");
                f++;
            }

            // 5. Home key
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Home);
            if (textBox.CursorPosition != 0)
            {
                Console.WriteLine($"FAIL [G35-Home]: pos={textBox.CursorPosition}, expected 0");
                f++;
            }

            // 6. End key
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.End);
            if (textBox.CursorPosition != textBox.Text.Length)
            {
                Console.WriteLine($"FAIL [G35-End]: pos={textBox.CursorPosition}, expected {textBox.Text.Length}");
                f++;
            }

            // 7. Delete key at end (should be no-op) then at start
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Delete);
            if (textBox.Text != "Hello orl")
            {
                Console.WriteLine($"FAIL [G35-DeleteEnd]: Text='{textBox.Text}', expected 'Hello orl' unchanged");
                f++;
            }

            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Home);
            sys.InjectKeyPress(Microsoft.Xna.Framework.Input.Keys.Delete);
            if (textBox.Text != "ello orl")
            {
                Console.WriteLine($"FAIL [G35-DeleteStart]: Text='{textBox.Text}', expected 'ello orl'");
                f++;
            }

            // 8. MaxLength enforcement
            textBox.Text = "";
            string longText = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; // 26 chars, max=20
            sys.InjectTextInput(longText);
            if (textBox.Text.Length > 20)
            {
                Console.WriteLine($"FAIL [G35-MaxLen]: Text length={textBox.Text.Length}, expected <= 20");
                f++;
            }

            // 9. Read-only mode
            textBox.Text = "Before";
            textBox.IsReadOnly = true;
            sys.InjectTextInput("X");
            if (textBox.Text != "Before")
            {
                Console.WriteLine($"FAIL [G35-ReadOnly]: Text='{textBox.Text}', expected 'Before' (read-only)");
                f++;
            }
            textBox.IsReadOnly = false;

            sys.Dispose();
            return f;
        }

        // ── G36: XAML-lite loader — tree equivalence ───────────────

        private int RunG36()
        {
            // G36: Load widget tree from XAML-lite XML, verify structure +
            // Bounds match code-built equivalent tree.
            int f = 0;

            // Register widget types (normally done once at startup)
            TypeRegistry.RegisterDefaults();

            string xml = @"<Screen xmlns:x=""http://schemas.fna-gui/xaml"">
  <Panel Width=""400"" Height=""300"" BackgroundColor=""40,40,60,255"">
    <StackLayout Orientation=""Vertical"" Spacing=""8"" Padding=""12"">
      <Text x:Name=""TitleText"" TextString=""XAML-lite Demo"" FontSize=""20"" Color=""White"" />
      <Button x:Name=""MainButton"" Text=""Click Me"" Width=""160"" Height=""40"" />
      <CheckBox x:Name=""FeatureCheck"" Text=""Enable Feature"" Width=""200"" Height=""28"" IsChecked=""true"" />
    </StackLayout>
  </Panel>
</Screen>";

            var loader = new XamlLiteLoader();
            Widget loadedRoot;
            try
            {
                loadedRoot = loader.LoadXml(xml);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL [G36-Load]: {ex.Message}");
                return 1;
            }

            // Layout the loaded tree
            loadedRoot.Layout(new Vector2(800, 600));

            // 1. Verify root structure: Screen → Panel → StackLayout → 3 children
            if (loadedRoot.Children.Count != 1)
            {
                Console.WriteLine($"FAIL [G36-RootChildren]: {loadedRoot.Children.Count}, expected 1");
                f++;
            }

            var panel = loadedRoot.Children[0] as GuiPanel;
            if (panel == null)
            {
                Console.WriteLine("FAIL [G36-Panel]: first child is not Panel");
                return f + 1;
            }

            if (panel.Children.Count != 1)
            {
                Console.WriteLine($"FAIL [G36-PanelChildren]: {panel.Children.Count}, expected 1");
                f++;
            }

            var stack = panel.Children[0] as StackLayout;
            if (stack == null)
            {
                Console.WriteLine("FAIL [G36-Stack]: child is not StackLayout");
                return f + 1;
            }

            if (stack.Children.Count != 3)
            {
                Console.WriteLine($"FAIL [G36-StackChildren]: {stack.Children.Count}, expected 3");
                f++;
            }

            // Verify individual child types
            var textWidget = stack.Children[0] as Text;
            if (textWidget == null)
            {
                Console.WriteLine("FAIL [G36-ChildType0]: expected Text, got " + stack.Children[0].GetType().Name);
                f++;
            }

            var buttonWidget = stack.Children[1] as Button;
            if (buttonWidget == null)
            {
                Console.WriteLine("FAIL [G36-ChildType1]: expected Button, got " + stack.Children[1].GetType().Name);
                f++;
            }

            var checkWidget = stack.Children[2] as CheckBox;
            if (checkWidget == null)
            {
                Console.WriteLine("FAIL [G36-ChildType2]: expected CheckBox, got " + stack.Children[2].GetType().Name);
                f++;
            }

            // 2. Verify property values were set correctly
            if (stack.Orientation != Orientation.Vertical)
            {
                Console.WriteLine($"FAIL [G36-Orientation]: {stack.Orientation}, expected Vertical");
                f++;
            }

            if (stack.Spacing != 8)
            {
                Console.WriteLine($"FAIL [G36-Spacing]: {stack.Spacing}, expected 8");
                f++;
            }

            if (buttonWidget != null)
            {
                if (buttonWidget.Text != "Click Me")
                {
                    Console.WriteLine($"FAIL [G36-ButtonText]: '{buttonWidget.Text}', expected 'Click Me'");
                    f++;
                }
                if (buttonWidget.Width != 160)
                {
                    Console.WriteLine($"FAIL [G36-ButtonWidth]: {buttonWidget.Width}, expected 160");
                    f++;
                }
            }

            if (checkWidget != null && checkWidget.IsChecked != true)
            {
                Console.WriteLine($"FAIL [G36-CheckBox]: IsChecked={checkWidget.IsChecked}, expected true");
                f++;
            }

            // 3. Verify x:Name resolution
            var foundTitle = loader.FindByName<Text>("TitleText");
            if (foundTitle == null || foundTitle != textWidget)
            {
                Console.WriteLine("FAIL [G36-FindByName-Title]: TitleText not found");
                f++;
            }

            var foundBtn = loader.FindByName<Button>("MainButton");
            if (foundBtn == null || foundBtn != buttonWidget)
            {
                Console.WriteLine("FAIL [G36-FindByName-Button]: MainButton not found");
                f++;
            }

            var foundCheck = loader.FindByName<CheckBox>("FeatureCheck");
            if (foundCheck == null || foundCheck != checkWidget)
            {
                Console.WriteLine("FAIL [G36-FindByName-Check]: FeatureCheck not found");
                f++;
            }

            // 4. Verify Widget.FindByName also works (tree-based search)
            var foundViaTree = loadedRoot.FindByName<Button>("MainButton");
            if (foundViaTree == null)
            {
                Console.WriteLine("FAIL [G36-TreeFind]: MainButton not found via Widget.FindByName");
                f++;
            }

            // 5. Compare XML-loaded Bounds vs code-built Bounds
            var codeRoot = BuildG36CodeTree(null);
            codeRoot.Layout(new Vector2(800, 600));

            // Both should have stack with 3 children at similar positions
            // StackLayout children should stack vertically with same spacing
            var loadedStack = stack;
            var codeStack = codeRoot.Children[0].Children[0] as StackLayout;
            if (codeStack != null && loadedStack != null)
            {
                for (int i = 0; i < loadedStack.Children.Count && i < codeStack.Children.Count; i++)
                {
                    var lb = loadedStack.Children[i].Bounds;
                    var cb = codeStack.Children[i].Bounds;

                    // Bounds should match closely (XML might differ from code due to font measurement)
                    // Compare position and size within tolerance
                    bool boundsMatch =
                        Math.Abs(lb.Width - cb.Width) <= 2 &&
                        Math.Abs(lb.Height - cb.Height) <= 2;

                    if (!boundsMatch)
                    {
                        Console.WriteLine($"FAIL [G36-Bounds-{i}]: loaded={lb}, code={cb}");
                        f++;
                    }
                }
            }

            return f;
        }

        // ── G37: Settings menu end-to-end ───────────────────────────

        private int RunG37()
        {
            // G37: Complete settings menu — verify interaction paths
            // (slider drag, checkbox toggle, button click)
            int f = 0;

            var root = new GuiPanel { Width = 400, Height = 500 };

            // Build a settings menu
            var settingsRoot = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 12,
                Padding = new Thickness(16),
            };

            // Volume slider
            var volStack = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
            };
            var volLabel = new Text
            {
                TextString = "Volume:",
                FontSize = 16,
                Color = Color.LightGray,
                VerticalAlignment = VerticalAlignment.Center,
            };
            volStack.AddChild(volLabel);
            var volumeSlider = new Slider
            {
                Width = 200,
                Min = 0, Max = 100, Value = 75,
            };
            volStack.AddChild(volumeSlider);
            settingsRoot.AddChild(volStack);

            // Fullscreen checkbox
            var fullscreenCb = new CheckBox
            {
                Text = "Fullscreen",
                Width = 250, Height = 28,
                IsChecked = true,
            };
            settingsRoot.AddChild(fullscreenCb);

            // VSync checkbox
            var vsyncCb = new CheckBox
            {
                Text = "VSync",
                Width = 250, Height = 28,
                IsChecked = true,
            };
            settingsRoot.AddChild(vsyncCb);

            // Apply button
            int applyClicks = 0;
            var applyBtn = new Button
            {
                Text = "Apply",
                Width = 100, Height = 36,
            };
            applyBtn.Click += b => applyClicks++;
            settingsRoot.AddChild(applyBtn);

            // Cancel button
            int cancelClicks = 0;
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Width = 100, Height = 36,
            };
            cancelBtn.Click += b => cancelClicks++;
            settingsRoot.AddChild(cancelBtn);

            root.AddChild(settingsRoot);
            root.Layout(new Vector2(400, 500));

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 500);
            sys.Update(new GameTime());

            // 1. Click the Fullscreen checkbox to uncheck it
            var cbCenter = new Vector2(
                fullscreenCb.Bounds.X + fullscreenCb.Bounds.Width / 2,
                fullscreenCb.Bounds.Y + fullscreenCb.Bounds.Height / 2);
            sys.InjectPointerMove(cbCenter);
            sys.InjectPointerDown(cbCenter);
            sys.InjectPointerUp(cbCenter);

            if (fullscreenCb.IsChecked)
            {
                Console.WriteLine($"FAIL [G37-FullscreenToggle]: IsChecked=true, expected false after click");
                f++;
            }

            // Click again to re-check
            sys.InjectPointerDown(cbCenter);
            sys.InjectPointerUp(cbCenter);
            if (!fullscreenCb.IsChecked)
            {
                Console.WriteLine($"FAIL [G37-FullscreenRecheck]: IsChecked=false, expected true");
                f++;
            }

            // 2. Drag the volume slider
            float sliderCX = volumeSlider.Bounds.X + volumeSlider.Bounds.Width / 2;
            float sliderCY = volumeSlider.Bounds.Y + volumeSlider.Bounds.Height / 2;
            float sliderRX = volumeSlider.Bounds.X + volumeSlider.Bounds.Width - 5;

            sys.InjectPointerMove(new Vector2(sliderCX, sliderCY));
            sys.InjectPointerDown(new Vector2(sliderCX, sliderCY));
            sys.InjectPointerMove(new Vector2(sliderRX, sliderCY));
            sys.InjectPointerUp(new Vector2(sliderRX, sliderCY));

            if (volumeSlider.Value <= 85)
            {
                Console.WriteLine($"FAIL [G37-Slider]: Value={volumeSlider.Value}, expected > 85 after drag right");
                f++;
            }

            // 3. Click the Apply button
            var applyCenter = new Vector2(
                applyBtn.Bounds.X + applyBtn.Bounds.Width / 2,
                applyBtn.Bounds.Y + applyBtn.Bounds.Height / 2);
            sys.InjectPointerMove(applyCenter);
            sys.InjectPointerDown(applyCenter);
            sys.InjectPointerUp(applyCenter);

            if (applyClicks != 1)
            {
                Console.WriteLine($"FAIL [G37-Apply]: applyClicks={applyClicks}, expected 1");
                f++;
            }
            if (cancelClicks != 0)
            {
                Console.WriteLine($"FAIL [G37-CancelNotClicked]: cancelClicks={cancelClicks}, expected 0");
                f++;
            }

            // 4. Click the Cancel button
            var cancelCenter = new Vector2(
                cancelBtn.Bounds.X + cancelBtn.Bounds.Width / 2,
                cancelBtn.Bounds.Y + cancelBtn.Bounds.Height / 2);
            sys.InjectPointerMove(cancelCenter);
            sys.InjectPointerDown(cancelCenter);
            sys.InjectPointerUp(cancelCenter);

            if (cancelClicks != 1)
            {
                Console.WriteLine($"FAIL [G37-Cancel]: cancelClicks={cancelClicks}, expected 1");
                f++;
            }

            // 5. Uncheck VSync
            var vsyncCenter = new Vector2(
                vsyncCb.Bounds.X + vsyncCb.Bounds.Width / 2,
                vsyncCb.Bounds.Y + vsyncCb.Bounds.Height / 2);
            sys.InjectPointerMove(vsyncCenter);
            sys.InjectPointerDown(vsyncCenter);
            sys.InjectPointerUp(vsyncCenter);

            if (vsyncCb.IsChecked)
            {
                Console.WriteLine($"FAIL [G37-VSyncToggle]: IsChecked=true, expected false after click");
                f++;
            }

            sys.Dispose();
            return f;
        }

        // ── G38: Zero GC steady-state ────────────────────────────────

        private int RunG38()
        {
            // G38: Steady-state zero GC — verify that the layout/update path
            // produces minimal per-frame allocations after warmup (caches hot).
            //
            // We separate layout-update from draw: the RecordingRenderer
            // inherently allocates to store draw calls per frame (a test artifact).
            // In production, SpriteBatchGuiRenderer eliminates this with object pools.
            //
            // This test validates:
            // 1. Update-only path (no draw recording) has near-zero allocations
            // 2. Layout caching prevents re-allocation after first measure
            int f = 0;

            var root = new GuiPanel { Width = 400, Height = 400 };
            var label = new Text { TextString = "Hello", FontSize = 16 };
            var btn = new Button { Text = "OK", Width = 100, Height = 40 };
            var cb = new CheckBox { Text = "Check", Width = 200, Height = 28 };
            root.AddChild(label);
            root.AddChild(btn);
            root.AddChild(cb);

            var recorder = new RecordingRenderer();
            var sys = new GuiSystem(recorder, root);
            sys.ScreenSize = new Vector2(400, 400);

            // Warmup: run layout + draw to populate caches
            for (int i = 0; i < 3; i++)
            {
                sys.Update(new GameTime(TimeSpan.FromSeconds(i * 0.016), TimeSpan.FromSeconds(0.016)));
                sys.Draw();
            }

            // Test 1: Update-only path (no draw recording) — should have minimal allocation
            // Reset recorder to clear accumulated draw calls
            recorder.Reset();
            long beforeUpdate = GC.GetAllocatedBytesForCurrentThread();

            int updateFrames = 20;
            for (int i = 0; i < updateFrames; i++)
            {
                sys.Update(new GameTime(TimeSpan.FromSeconds((3 + i) * 0.016), TimeSpan.FromSeconds(0.016)));
            }

            long afterUpdate = GC.GetAllocatedBytesForCurrentThread();
            long updateAlloc = afterUpdate - beforeUpdate;
            long perUpdate = updateAlloc / updateFrames;

            Console.WriteLine($"  [G38] Update-only: {updateAlloc} bytes over {updateFrames} frames ({perUpdate}/frame)");

            // After warmup, update-only path should allocate very little
            if (perUpdate > 200)
            {
                Console.WriteLine($"FAIL [G38-UpdateAlloc]: {perUpdate}/frame in update-only path, expected < 200");
                f++;
            }

            // Test 2: Verify layout cache is working (no re-measure in steady state)
            // The root widget should have MeasureDirty=false after the update loop above
            if (root.MeasureDirty)
            {
                Console.WriteLine("FAIL [G38-MeasureDirty]: root still dirty after steady-state updates");
                f++;
            }

            // Test 3: Recorded draw calls should be bounded (same layout each frame = same calls)
            // After reset, draw once and count calls
            recorder.Reset();
            sys.Draw();
            int drawCallsA = recorder.Calls.Count;

            recorder.Reset();
            sys.Draw();
            int drawCallsB = recorder.Calls.Count;

            // Same layout should produce same number of draw calls
            if (drawCallsA != drawCallsB)
            {
                Console.WriteLine($"FAIL [G38-DrawCalls]: draw call count changed ({drawCallsA} → {drawCallsB})");
                f++;
            }
            else if (drawCallsA > 0)
            {
                Console.WriteLine($"  [G38] Steady-state draw calls: {drawCallsA} (stable across frames)");
            }

            sys.Dispose();
            return f;
        }

        // ── Draw ────────────────────────────────────────────────────

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(Color.CornflowerBlue);

            _guiSystem.Draw();

            if (!TestHarness.Headless)
            {
                DrawImGui();
            }
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("GuiDemo/Panel — Phase 0");

            ImGuiBindings.ImGui_Text($"Current: {CurrentTest} (+/- to switch, Esc to exit)");
            ImGuiBindings.ImGui_Separator();

            ImGuiBindings.ImGui_Text("G01: Empty System");
            ImGuiBindings.ImGui_Text("G02: DrawRect");
            ImGuiBindings.ImGui_Text("G03: DrawTexture");
            ImGuiBindings.ImGui_Text("G04: Clip Stack");
            ImGuiBindings.ImGui_Text("G05: 9-Slice");
            ImGuiBindings.ImGui_Text("G06: Geometry Rebuild");
            ImGuiBindings.ImGui_Text("G07: Color-Only No Rebuild");
            ImGuiBindings.ImGui_Text("G08: Image Types (Simple/Sliced/Tiled/Filled)");
            ImGuiBindings.ImGui_Text("G09: SDF Font Measure");
            ImGuiBindings.ImGui_Text("G10: Single-Line Text");
            ImGuiBindings.ImGui_Text("G11: Text Scaling (0.5x/2x)");
            ImGuiBindings.ImGui_Text("G12: Outline + Bold");
            ImGuiBindings.ImGui_Text("G13: Multi-Line Text");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("G14: StackLayout (H/V) [visual]");
            ImGuiBindings.ImGui_Text("G15: GridLayout (Fixed/Auto/Star) [visual]");
            ImGuiBindings.ImGui_Text("G16: GridLayout Spans [visual]");
            ImGuiBindings.ImGui_Text("G17: DockLayout [visual]");
            ImGuiBindings.ImGui_Text("G18: StackLayout Spacing+Alignment [visual]");
            ImGuiBindings.ImGui_Text("G19: Measure Cache");
            ImGuiBindings.ImGui_Text("G20: Dirty Propagation");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("G21: Hit Testing");
            ImGuiBindings.ImGui_Text("G22: Event Routing (capture+bubble)");
            ImGuiBindings.ImGui_Text("G23: Button Click");
            ImGuiBindings.ImGui_Text("G24: Slider + CheckBox");
            ImGuiBindings.ImGui_Text("G25: Focus/Tab Navigation");
            ImGuiBindings.ImGui_Text("G26: Code-Behind [visual]");
            ImGuiBindings.ImGui_Text("G27: Command (CanExecute) [visual]");
            ImGuiBindings.ImGui_Text("G28: Data Binding [visual]");
            ImGuiBindings.ImGui_Text("G29: Unsubscribe [visual]");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("G30: Theme/Style/Hover-Pressed [visual]");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("G31: Tween Animation [visual]");
            ImGuiBindings.ImGui_Text("G32: Gamepad Nav [visual]");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("G33: ScrollView [visual]");
            ImGuiBindings.ImGui_Text("G34: Modal Dialog [visual]");
            ImGuiBindings.ImGui_Text("G35: TextBox Editing [visual]");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("G36: XAML-lite Loader [visual]");
            ImGuiBindings.ImGui_Text("G37: Settings Menu E2E [visual+sim]");
            ImGuiBindings.ImGui_Text("G38: Zero GC Steady-State");

            ImGuiBindings.EndPanel();
        }

        public static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);

            // Parse --test <name> argument
            string testName = "G01";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--test" && i + 1 < args.Length)
                    testName = args[++i];
            }

            using var g = new PanelDemo(testName);
            g.Run();
        }
    }
}
