using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace KinectIrWandGestures
{
    public partial class MainWindow : Window
    {
        private KinectSensor _sensor;
        private MultiSourceFrameReader _multiReader;

        // Color underlay
        private WriteableBitmap _colorBitmap;
        private byte[] _colorPixels;
        private int _colorW, _colorH;

        // Red dot for wand tracking location
        private Ellipse _wandDot;
        private const double WandDotSize = 10.0;

        // Depth data
        private ushort[] _depthData;
        private int _depthW, _depthH;

        // IR Buffers
        private ushort[] _irData;
        private int _irW, _irH;

        // Drawing / stroke
        private readonly List<Point2D> _stroke = new List<Point2D>();
        private Polyline _polyline;

        private int _missingFrames = 0;
        private const int MissingFramesToEndStroke = 14;
        private DateTime _lastPointTime = DateTime.MinValue;

        // Keep last depth pixel to stabilize tracking
        private bool _hasLastDepthPixel = false;
        private int _lastDepthX = 0;
        private int _lastDepthY = 0;

        // Sensor active indicator
        private DateTime _lastFrameTimeUtc = DateTime.MinValue;
        private readonly TimeSpan _frameAliveWindow = TimeSpan.FromMilliseconds(750);

        // Recognition timing / behavior (NON-recording)
        private bool _shapeCommitted = false;
        private DateTime _strokeStartUtc = DateTime.MinValue;
        private readonly TimeSpan _maxStrokeDuration = TimeSpan.FromSeconds(2.5);
        private readonly TimeSpan _idleClear = TimeSpan.FromSeconds(1.5);
        private Point2D _lastMovePoint = null;
        private DateTime _lastMoveUtc = DateTime.MinValue;
        private const int MinPointsForEarlyCommit = 40;

        // Spell templates
        private readonly OneDollarRecognizer _spellRecognizer = new OneDollarRecognizer();
        private readonly List<SpellTemplate> _templates = new List<SpellTemplate>();
        private TemplateStore _templateStore;

        // Recording state machine
        private enum RecordState { Off, Armed, Recording }
        private RecordState _recordState = RecordState.Off;
        private DateTime _recordStartUtc = DateTime.MinValue;

        private const int MinRecordPoints = 30;
        private readonly TimeSpan _minRecordDuration = TimeSpan.FromMilliseconds(400);

        // Configurable settings for timeout and length
        // how far a user has to change points before it is counted as "moved"
        private const double MinMovementPixels = 20.0;
        private readonly TimeSpan StationaryTimeout = TimeSpan.FromMilliseconds(700);

        // total path length before we fail
        private const double MaxStrokeLengthPixels = 1800.0;
        private readonly TimeSpan MaxSpellDuration = TimeSpan.FromSeconds(3.0);


        public MainWindow()
        {
            InitializeComponent();

            ThresholdSlider.ValueChanged += (s, e) => ThresholdValue.Text = ((int)ThresholdSlider.Value).ToString();

            // Keyboard shortcuts
            this.KeyDown += Window_KeyDown;
            this.Focusable = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetupViewport();
            UpdateSensorIndicator("Not started", false);

            // Ensure keyboard focus
            Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(this)), DispatcherPriority.Background);

            // Templates.json (avoid Path ambiguity)
            var templatePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates.json");
            _templateStore = new TemplateStore(templatePath);
            ReloadTemplates();

            try
            {
                _sensor = KinectSensor.GetDefault();
                if (_sensor == null)
                {
                    Log("ERROR: No Kinect sensor detected.");
                    Status("No Kinect detected.");
                    UpdateSensorIndicator("No sensor", false);
                    return;
                }

                _sensor.IsAvailableChanged += Sensor_IsAvailableChanged;

                // Init COLOR
                _colorW = _sensor.ColorFrameSource.FrameDescription.Width;
                _colorH = _sensor.ColorFrameSource.FrameDescription.Height;
                _colorPixels = new byte[_colorW * _colorH * 4];
                _colorBitmap = new WriteableBitmap(_colorW, _colorH, 96, 96, PixelFormats.Bgra32, null);
                CameraImage.Source = _colorBitmap;

                // Init DEPTH
                _depthW = _sensor.DepthFrameSource.FrameDescription.Width;
                _depthH = _sensor.DepthFrameSource.FrameDescription.Height;
                _depthData = new ushort[_depthW * _depthH];

                // Init IR
                _irW = _sensor.InfraredFrameSource.FrameDescription.Width;   // 512
                _irH = _sensor.InfraredFrameSource.FrameDescription.Height;  // 424
                _irData = new ushort[_irW * _irH];

                // Open sensor and MultiSource reader (Color + Depth)
                _sensor.Open();
                _multiReader = _sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.Infrared);
                _multiReader.MultiSourceFrameArrived += MultiReader_MultiSourceFrameArrived;

                Log("Kinect opened. Color underlay + Depth-based wand tracking active.");
                Status("Ready. Arm Record to add spells. Space=Start, Enter=Stop, Esc=Cancel.");

                // Watchdog indicator
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (s2, e2) =>
                {
                    if (_sensor == null) return;

                    bool recently = (DateTime.UtcNow - _lastFrameTimeUtc) <= _frameAliveWindow;

                    if (_sensor.IsAvailable)
                        UpdateSensorIndicator(recently ? "Streaming (active)" : "Available (no frames yet)", recently);
                    else
                        UpdateSensorIndicator("Not available", false);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Log("ERROR initializing Kinect: " + ex);
                Status("Init failed.");
                UpdateSensorIndicator("Init failed", false);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                if (_multiReader != null)
                {
                    _multiReader.MultiSourceFrameArrived -= MultiReader_MultiSourceFrameArrived;
                    _multiReader.Dispose();
                    _multiReader = null;
                }

                if (_sensor != null)
                {
                    _sensor.IsAvailableChanged -= Sensor_IsAvailableChanged;
                    _sensor.Close();
                    _sensor = null;
                }
            }
            catch { }
        }

        // ---------- Keyboard shortcuts ----------
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelRecording("Esc");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                if (_recordState == RecordState.Armed)
                {
                    StartRecord_Click(null, null);
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (_recordState == RecordState.Recording)
                {
                    StopRecord_Click(null, null);
                    e.Handled = true;
                }
                return;
            }
        }

        // ---------- Viewport ----------
        private void SetupViewport()
        {
            DrawCanvas.Children.Clear();

            _polyline = new Polyline
            {
                Stroke = Brushes.Lime,
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            DrawCanvas.Children.Add(_polyline);

            _wandDot = new Ellipse
            {
                Width = WandDotSize,
                Height = WandDotSize,
                Fill = Brushes.Red,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Opacity = 0.9,
                Visibility = Visibility.Hidden,
                IsHitTestVisible = false
            };
            DrawCanvas.Children.Add(_wandDot);
        }

        // ---------- Sensor indicator ----------
        private void UpdateSensorIndicator(string state, bool ok)
        {
            Dispatcher.Invoke(() =>
            {
                SensorStateText.Text = state;
                SensorDot.Fill = ok ? Brushes.LimeGreen : Brushes.OrangeRed;
            });
        }

        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            var msg = e.IsAvailable ? "Available" : "Not available (unplugged/driver/power)";
            Log("Sensor availability changed: " + msg);
            UpdateSensorIndicator(msg, e.IsAvailable);
        }

        // ---------- MultiSource handler (Color underlay + Depth detection + Depth->Color mapping) ----------
        private void MultiReader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            var msFrame = e.FrameReference.AcquireFrame();
            if (msFrame == null) return;

            // --- COLOR UNDERLAY ---
            using (var colorFrame = msFrame.ColorFrameReference.AcquireFrame())
            {
                if (colorFrame != null)
                {
                    if (colorFrame.RawColorImageFormat == ColorImageFormat.Bgra)
                        colorFrame.CopyRawFrameDataToArray(_colorPixels);
                    else
                        colorFrame.CopyConvertedFrameDataToArray(_colorPixels, ColorImageFormat.Bgra);

                    Dispatcher.Invoke(() =>
                    {
                        _colorBitmap.WritePixels(
                            new Int32Rect(0, 0, _colorW, _colorH),
                            _colorPixels,
                            _colorW * 4,
                            0
                        );
                    });

                    _lastFrameTimeUtc = DateTime.UtcNow;
                }
            }

            // We need BOTH IR + Depth to do: IR detect -> Depth lookup -> Depth->Color map
            using (var irFrame = msFrame.InfraredFrameReference.AcquireFrame())
            using (var depthFrame = msFrame.DepthFrameReference.AcquireFrame())
            {
                if (irFrame == null || depthFrame == null)
                {
                    OnPointMissing();
                    return;
                }

                irFrame.CopyFrameDataToArray(_irData);
                depthFrame.CopyFrameDataToArray(_depthData);

                // 1) Find wand in IR (brightest-spot centroid)
                if (!TryFindWandInInfrared(_irData, _irW, _irH, out int irX, out int irY))
                {
                    OnPointMissing();
                    return;
                }

                // 2) Depth lookup at same pixel (IR & Depth share geometry on Kinect v2)
                int idx = irY * _depthW + irX;
                if (idx < 0 || idx >= _depthData.Length)
                {
                    OnPointMissing();
                    return;
                }

                ushort depthMm = _depthData[idx];

                // If depth is invalid (0), estimate from neighborhood so mapping works
                if (depthMm == 0)
                {
                    if (!TryEstimateDepthFromNeighbors(_depthData, _depthW, _depthH, irX, irY, out depthMm))
                    {
                        OnPointMissing();
                        return;
                    }
                }

                // 3) Map Depth pixel -> Color pixel
                var mapper = _sensor.CoordinateMapper;
                var colorPt = mapper.MapDepthPointToColorSpace(
                    new DepthSpacePoint { X = irX, Y = irY },
                    depthMm
                );

                if (float.IsInfinity(colorPt.X) || float.IsInfinity(colorPt.Y))
                {
                    OnPointMissing();
                    return;
                }

                // 4) Color pixel -> Canvas pixel (aspect correct) and draw the red dot for the wand
                if (TryMapColorPixelToCanvas(colorPt.X, colorPt.Y, out double canvasX, out double canvasY))
                {
                    SetWandDot(canvasX, canvasY, true);
                    OnPointDetectedCanvas(canvasX, canvasY);
                }
                else
                {
                    SetWandDot(0, 0, false);
                    OnPointMissing();
                }

            }
        }

        private bool TryFindWandInInfrared(ushort[] ir, int w, int h, out int outX, out int outY)
        {
            outX = outY = 0;

            ushort threshold = (ushort)ThresholdSlider.Value;
            ushort peakMin = (ushort)Math.Max((int)threshold, 25000);

            int n = w * h;

            // Find brightest pixel
            ushort maxVal = 0;
            int maxIdx = -1;
            for (int i = 0; i < n; i++)
            {
                ushort v = ir[i];
                if (v > maxVal) { maxVal = v; maxIdx = i; }
            }

            if (maxIdx < 0 || maxVal < peakMin)
                return false;

            int peakX = maxIdx % w;
            int peakY = maxIdx / w;

            // Centroid around brightest pixel
            const int R = 6;
            int x0 = Math.Max(0, peakX - R);
            int x1 = Math.Min(w - 1, peakX + R);
            int y0 = Math.Max(0, peakY - R);
            int y1 = Math.Min(h - 1, peakY + R);

            long sumX = 0, sumY = 0, sumW = 0;
            int hits = 0;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    ushort v = ir[row + x];
                    if (v >= threshold)
                    {
                        sumX += (long)x * v;
                        sumY += (long)y * v;
                        sumW += v;
                        hits++;
                    }
                }
            }

            if (sumW <= 0 || hits < 4)
                return false;

            double cx = (double)sumX / sumW;
            double cy = (double)sumY / sumW;

            outX = (int)Math.Round(cx);
            outY = (int)Math.Round(cy);

            if (outX < 0 || outX >= w || outY < 0 || outY >= h)
                return false;

            return true;
        }
        private void SetWandDot(double x, double y, bool visible)
        {
            if (_wandDot == null) return;

            Dispatcher.Invoke(() =>
            {
                _wandDot.Visibility = visible ? Visibility.Visible : Visibility.Hidden;

                if (visible)
                {
                    Canvas.SetLeft(_wandDot, x - (WandDotSize / 2.0));
                    Canvas.SetTop(_wandDot, y - (WandDotSize / 2.0));
                }
            });
        }

        private bool TryEstimateDepthFromNeighbors(ushort[] depth, int w, int h, int x, int y, out ushort depthMm)
        {
            depthMm = 0;

            const ushort MinValid = 400;
            const ushort MaxValid = 4500;

            // Expand search a bit because the wand hotspot can punch a "hole" in depth
            int r = 4;
            long sum = 0;
            int count = 0;

            int x0 = Math.Max(0, x - r);
            int x1 = Math.Min(w - 1, x + r);
            int y0 = Math.Max(0, y - r);
            int y1 = Math.Min(h - 1, y + r);

            for (int yy = y0; yy <= y1; yy++)
            {
                int row = yy * w;
                for (int xx = x0; xx <= x1; xx++)
                {
                    ushort d = depth[row + xx];
                    if (d >= MinValid && d <= MaxValid)
                    {
                        sum += d;
                        count++;
                    }
                }
            }

            if (count < 6)
                return false;

            depthMm = (ushort)(sum / count);
            return true;
        }


        /// <summary>
        /// Attempts to find the wand point in the depth frame.
        /// Heuristic:
        ///  - Prefer a small "invalid depth hole" cluster (depth==0) near the last point (wand IR often breaks depth).
        ///  - Validate candidates by checking for enough valid-depth neighbors (so we don't pick far-range "no depth").
        ///  - If no invalid-hole candidate found, fall back to nearest valid depth in a region near last point (or whole frame).
        /// </summary>
        private bool TryFindWandPointInDepth(ushort[] depth, int w, int h, out int bestX, out int bestY, out ushort bestDepthMm)
        {
            bestX = bestY = 0;
            bestDepthMm = 0;

            // Depth constraints (mm) for a typical room + wand range
            const ushort MinValid = 400;
            const ushort MaxValid = 4500;

            // Search windows
            int nearR = 90;   // search radius near last point
            int localR = 4;   // neighborhood radius for validation

            // Step A: Prefer invalid-depth "hole" (depth==0) near last known location
            if (_hasLastDepthPixel)
            {
                if (TryFindInvalidHoleNear(depth, w, h, _lastDepthX, _lastDepthY, nearR, localR, MinValid, MaxValid,
                        out bestX, out bestY, out bestDepthMm))
                {
                    return true;
                }
            }
            else
            {
                // no last point: try a smaller central region to avoid edge garbage
                int cx = w / 2, cy = h / 2;
                if (TryFindInvalidHoleNear(depth, w, h, cx, cy, 140, localR, MinValid, MaxValid,
                        out bestX, out bestY, out bestDepthMm))
                {
                    return true;
                }
            }

            // Step B: Fall back to nearest valid depth pixel (close object) near last point
            if (_hasLastDepthPixel)
            {
                if (TryFindNearestValidNear(depth, w, h, _lastDepthX, _lastDepthY, nearR, MinValid, MaxValid,
                        out bestX, out bestY, out bestDepthMm))
                {
                    return true;
                }
            }

            // Step C: Global fallback (nearest valid depth in frame)
            if (TryFindNearestValidGlobal(depth, w, h, MinValid, MaxValid, out bestX, out bestY, out bestDepthMm))
            {
                return true;
            }

            return false;
        }

        private bool TryFindInvalidHoleNear(
            ushort[] depth, int w, int h,
            int centerX, int centerY,
            int searchR, int neighR,
            ushort minValid, ushort maxValid,
            out int outX, out int outY, out ushort outDepthMm)
        {
            outX = outY = 0;
            outDepthMm = 0;

            int x0 = Math.Max(0, centerX - searchR);
            int x1 = Math.Min(w - 1, centerX + searchR);
            int y0 = Math.Max(0, centerY - searchR);
            int y1 = Math.Min(h - 1, centerY + searchR);

            int bestScore = -1;
            int bestIdx = -1;
            ushort bestNeighborDepth = 0;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    int idx = row + x;
                    if (depth[idx] != 0) continue; // looking for invalid hole pixels

                    // Validate: must have enough valid neighbors (so it's a local hole, not global no-depth)
                    if (TryNeighborStats(depth, w, h, x, y, neighR, minValid, maxValid,
                        out int validCount, out ushort avgDepth))
                    {
                        // Score: more valid neighbors = better; closer to center = better
                        int dx = x - centerX;
                        int dy = y - centerY;
                        int dist2 = dx * dx + dy * dy;

                        int score = (validCount * 1000) - dist2;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestIdx = idx;
                            bestNeighborDepth = avgDepth;
                        }
                    }
                }
            }

            if (bestIdx < 0) return false;

            outX = bestIdx % w;
            outY = bestIdx / w;
            outDepthMm = bestNeighborDepth > 0 ? bestNeighborDepth : (ushort)1000; // reasonable default
            return true;
        }

        private bool TryNeighborStats(
            ushort[] depth, int w, int h,
            int x, int y,
            int r,
            ushort minValid, ushort maxValid,
            out int validCount, out ushort avgDepth)
        {
            validCount = 0;
            long sum = 0;

            int x0 = Math.Max(0, x - r);
            int x1 = Math.Min(w - 1, x + r);
            int y0 = Math.Max(0, y - r);
            int y1 = Math.Min(h - 1, y + r);

            for (int yy = y0; yy <= y1; yy++)
            {
                int row = yy * w;
                for (int xx = x0; xx <= x1; xx++)
                {
                    ushort d = depth[row + xx];
                    if (d >= minValid && d <= maxValid)
                    {
                        validCount++;
                        sum += d;
                    }
                }
            }

            // Require enough valid neighbors so we know this invalid pixel is a "hole" inside valid geometry
            if (validCount < 12)
            {
                avgDepth = 0;
                return false;
            }

            avgDepth = (ushort)(sum / validCount);
            return true;
        }

        private bool TryFindNearestValidNear(
            ushort[] depth, int w, int h,
            int centerX, int centerY, int searchR,
            ushort minValid, ushort maxValid,
            out int outX, out int outY, out ushort outDepthMm)
        {
            outX = outY = 0;
            outDepthMm = 0;

            int x0 = Math.Max(0, centerX - searchR);
            int x1 = Math.Min(w - 1, centerX + searchR);
            int y0 = Math.Max(0, centerY - searchR);
            int y1 = Math.Min(h - 1, centerY + searchR);

            ushort bestDepth = ushort.MaxValue;
            int bestIdx = -1;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    ushort d = depth[row + x];
                    if (d < minValid || d > maxValid) continue;

                    if (d < bestDepth)
                    {
                        bestDepth = d;
                        bestIdx = row + x;
                    }
                }
            }

            if (bestIdx < 0) return false;

            outX = bestIdx % w;
            outY = bestIdx / w;
            outDepthMm = bestDepth;
            return true;
        }

        private bool TryFindNearestValidGlobal(
            ushort[] depth, int w, int h,
            ushort minValid, ushort maxValid,
            out int outX, out int outY, out ushort outDepthMm)
        {
            outX = outY = 0;
            outDepthMm = 0;

            ushort bestDepth = ushort.MaxValue;
            int bestIdx = -1;

            int n = w * h;
            for (int i = 0; i < n; i++)
            {
                ushort d = depth[i];
                if (d < minValid || d > maxValid) continue;

                if (d < bestDepth)
                {
                    bestDepth = d;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;

            outX = bestIdx % w;
            outY = bestIdx / w;
            outDepthMm = bestDepth;
            return true;
        }

        // Color pixel -> DrawCanvas pixel (aspect correct, centered). Requires CameraImage Stretch="Uniform".
        private bool TryMapColorPixelToCanvas(double colorX, double colorY, out double canvasX, out double canvasY)
        {
            canvasX = canvasY = 0;

            double cw = DrawCanvas.ActualWidth;
            double ch = DrawCanvas.ActualHeight;
            if (cw < 10 || ch < 10) return false;

            double scale = Math.Min(cw / _colorW, ch / _colorH);
            double offsetX = (cw - (_colorW * scale)) / 2.0;
            double offsetY = (ch - (_colorH * scale)) / 2.0;

            canvasX = (colorX * scale) + offsetX;
            canvasY = (colorY * scale) + offsetY;

            // Reject points outside the displayed image region
            if (canvasX < offsetX || canvasY < offsetY) return false;
            if (canvasX > offsetX + (_colorW * scale)) return false;
            if (canvasY > offsetY + (_colorH * scale)) return false;

            return true;
        }

        // Called when we have a valid mapped point already in canvas space
        private void OnPointDetectedCanvas(double x, double y)
        {
            _lastPointTime = DateTime.UtcNow;
            _missingFrames = 0;

            // Armed: ignore so you can recenter without drawing
            if (_recordState == RecordState.Armed)
            {
                Dispatcher.Invoke(() => Status("Armed. Recenter wand, then press Space or click Start."));
                return;
            }

            if (_stroke.Count == 0)
            {
                _strokeStartUtc = DateTime.UtcNow;
                _shapeCommitted = false;

                _lastMovePoint = new Point2D(x, y, DateTime.UtcNow);
                _lastMoveUtc = DateTime.UtcNow;

                if (_recordState == RecordState.Recording)
                    _recordStartUtc = DateTime.UtcNow;
            }

            _stroke.Add(new Point2D(x, y, DateTime.UtcNow));

            // --- FAIL RULES (only when NOT recording templates) ---
            if (_recordState != RecordState.Recording)
            {
                // Check to see if the user moved their wand far enough to continue a cast
                if (_lastMovePoint != null)
                {
                    double dxm = x - _lastMovePoint.X;
                    double dym = y - _lastMovePoint.Y;
                    double dist = Math.Sqrt(dxm * dxm + dym * dym);

                    if (dist >= MinMovementPixels)
                    {
                        _lastMovePoint = new Point2D(x, y, DateTime.UtcNow);
                        _lastMoveUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        if ((DateTime.UtcNow - _lastMoveUtc) > StationaryTimeout && _stroke.Count > 8)
                        {
                            // If there is ANY meaningful stroke, holding still means "I'm done" -> recognize.
                            double strokeLen = PathLen(_stroke);

                            const double MinLenToSubmit = 60.0;   // much lower than before so a simple line works
                            const int MinPtsToSubmit = 12;

                            if (strokeLen >= MinLenToSubmit && _stroke.Count >= MinPtsToSubmit)
                            {
                                _shapeCommitted = true;
                                CommitStrokeForRecognition("Hold-to-finish");
                                return;
                            }

                            // If it really is tiny/noise, just clear quietly (don’t call it a failed cast).
                            ClearViewportAndBuffers("Cleared: tiny/noise stroke");
                            return;
                        }
                    }
                }

                // Check to see if the user took too long to cast the spell overall
                if (_strokeStartUtc != DateTime.MinValue && (DateTime.UtcNow - _strokeStartUtc) > MaxSpellDuration)
                {
                    FailSpell("Spell took too long");
                    return;
                }

                // Check to see if the user casted too long of a stroke
                double len = PathLen(_stroke);
                if (len > MaxStrokeLengthPixels)
                {
                    FailSpell("Spell path too long");
                    return;
                }
            }


            Dispatcher.Invoke(() =>
            {
                _polyline.Points.Add(new System.Windows.Point(x, y));
                Status(_recordState == RecordState.Recording
                    ? $"Recording... (Enter=Stop, Esc=Cancel) points={_stroke.Count}"
                    : $"Tracking... points={_stroke.Count}");
            });

            // Don’t auto-commit while recording
            if (_recordState == RecordState.Recording)
                return;

            // Early commit (closed shape)
            if (!_shapeCommitted && _stroke.Count >= MinPointsForEarlyCommit)
            {
                double closeDist = Dist(_stroke[0], _stroke[_stroke.Count - 1]);
                double len = PathLen(_stroke);
                if (closeDist < 35 && len > 250)
                {
                    _shapeCommitted = true;
                    CommitStrokeForRecognition("Closed shape detected");
                    return;
                }
            }

            // Safety commit (max duration)
            if (!_shapeCommitted && _strokeStartUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _strokeStartUtc) > _maxStrokeDuration)
            {
                _shapeCommitted = true;
                CommitStrokeForRecognition("Max duration reached");
                return;
            }
        }

        private void OnPointMissing()
        {
            // Clear the wand dot because we lost tracking
            SetWandDot(0, 0, false);

            _missingFrames++;
             
            // If recording: treat tracking loss as pen-up and auto-stop if enough data
            if (_recordState == RecordState.Recording)
            {
                if (_stroke.Count >= MinRecordPoints && _missingFrames >= MissingFramesToEndStroke)
                {
                    FinalizeRecording("Tracking lost (auto-stop)");
                }
                return;
            }

            // Idle clear (non-recording)
            if (_stroke.Count > 0 &&
                _lastPointTime != DateTime.MinValue &&
                (DateTime.UtcNow - _lastPointTime) > _idleClear)
            {
                Dispatcher.Invoke(() => ClearViewportAndBuffers("Idle: no input"));
                return;
            }

            // End stroke (non-recording) on missing frames
            if (_stroke.Count > 10 && _missingFrames >= MissingFramesToEndStroke)
            {
                CommitStrokeForRecognition("Stroke ended (tracking lost)");
            }
        }

        private void CommitStrokeForRecognition(string reason)
        {
            if (_stroke.Count < 10) return;

            var strokeCopy = _stroke.ToList();
            _stroke.Clear();
            _missingFrames = 0;

            Dispatcher.Invoke(() =>
            {
                Status(reason + ". Recognizing...");
                RecognizeStroke(strokeCopy);
            });
        }

        private void RecognizeStroke(List<Point2D> stroke)
        {
            try
            {
                var cleaned = StrokeCleanup.CleanStroke(stroke);
                var result = _spellRecognizer.Recognize(cleaned);

                if (result.Success)
                {
                    Log("CAST: " + result.Name + " (score=" + result.Score.ToString("0.00") + ")");
                    Status("Cast: " + result.Name);

                    ClearViewportAndBuffers("Cast " + result.Name); // clears the drawn spell
                    return;
                }

                Log("FAIL: " + result.Reason);
                Status("Not recognized.");
            }
            catch (Exception ex)
            {
                Log("ERROR recognizing stroke: " + ex.Message);
                Status("Recognition error.");
            }
        }

        // ---------- Recording UI ----------
        private void ArmRecord_Click(object sender, RoutedEventArgs e)
        {
            var name = (SpellNameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                Log("Enter a spell name before recording.");
                Status("Enter spell name first.");
                return;
            }

            _recordState = RecordState.Armed;
            StartRecordBtn.IsEnabled = true;
            StopRecordBtn.IsEnabled = false;

            ClearViewportAndBuffers("Armed recording (recenter now)");
            Log("ARMED: " + name + " | Recenter wand, then press Space or click Start. (Esc cancels)");
            Status("Armed. Recenter wand, then Start.");
        }

        private void StartRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_recordState != RecordState.Armed)
                return;

            _recordState = RecordState.Recording;
            _recordStartUtc = DateTime.UtcNow;

            StartRecordBtn.IsEnabled = false;
            StopRecordBtn.IsEnabled = true;

            ClearViewportAndBuffers("Recording started");
            Log("RECORDING: capturing points... Press Enter or click Stop when finished. (Esc cancels)");
            Status("Recording... draw spell, then Stop.");
        }

        private void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_recordState != RecordState.Recording)
                return;

            FinalizeRecording("Stop clicked");
        }

        private void CancelRecording(string reason)
        {
            if (_recordState == RecordState.Off)
                return;

            _recordState = RecordState.Off;
            StartRecordBtn.IsEnabled = false;
            StopRecordBtn.IsEnabled = false;

            ClearViewportAndBuffers("Recording canceled (" + reason + ")");
            Log("Recording canceled (" + reason + ").");
            Status("Ready.");
        }

        private void FinalizeRecording(string reason)
        {
            var name = (SpellNameBox.Text ?? "").Trim();

            var strokeCopy = _stroke.ToList();
            _stroke.Clear();
            _missingFrames = 0;

            _recordState = RecordState.Off;
            StartRecordBtn.IsEnabled = false;
            StopRecordBtn.IsEnabled = false;

            var cleaned = StrokeCleanup.CleanStroke(strokeCopy);
            var dur = DateTime.UtcNow - _recordStartUtc;

            if (string.IsNullOrWhiteSpace(name) || cleaned.Count < MinRecordPoints || dur < _minRecordDuration)
            {
                Log($"RECORD FAIL: Too short/empty. name='{name}', points={cleaned.Count}, durMs={dur.TotalMilliseconds:0} ({reason})");
                Status("Record failed: too short.");
                ClearViewportAndBuffers("Record failed");
                return;
            }

            var t = new SpellTemplate
            {
                Name = name,
                Points = cleaned.Select(p => new XY { X = p.X, Y = p.Y }).ToList()
            };

            _templates.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            _templates.Add(t);

            // Refresh the legend with the new recording
            RefreshSpellLegend();

            RebuildRecognizerFromTemplates();

            Log($"RECORDED: {name} points={cleaned.Count} ({reason}). Click Save Templates to persist.");
            Status("Recorded: " + name);
            ClearViewportAndBuffers("Template recorded");
        }

        // ---------- Template management ----------
        private void ReloadTemplates()
        {
            // Try to load any templates recorded
            _templates.Clear();
            _templates.AddRange(_templateStore.LoadOrEmpty());
            RebuildRecognizerFromTemplates();
            Log("Templates loaded: " + _templates.Count);

            // Load default spell templates if we don't find any
            if (_templates.Count == 0)
            {
                _templates.AddRange(DefaultSpellTemplates.Build());
                Log("Loaded default spell templates (starter set).");
            }
            RebuildRecognizerFromTemplates();
            
            // Fill in the spell legend
            RefreshSpellLegend();
        }

        private void RebuildRecognizerFromTemplates()
        {
            _spellRecognizer.ClearTemplates();

            foreach (var t in _templates)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.Name) || t.Points == null || t.Points.Count < 10)
                    continue;

                var pts = t.Points.Select(p => new Point2D(p.X, p.Y, DateTime.UtcNow)).ToList();
                try { _spellRecognizer.AddTemplate(t.Name, pts); }
                catch { /* ignore bad templates */ }
            }

            Log("Recognizer ready. TemplateCount=" + _spellRecognizer.TemplateCount);
        }

        private void SaveTemplates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _templateStore.Save(_templates);
                Log("Templates saved to: " + _templateStore.FilePath);
                Status("Templates saved.");
            }
            catch (Exception ex)
            {
                Log("ERROR saving templates: " + ex.Message);
                Status("Save failed.");
            }
        }

        private void ReloadTemplates_Click(object sender, RoutedEventArgs e)
        {
            ReloadTemplates();
            Status("Templates reloaded.");
        }

        // ---------- Other UI ----------
        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            CancelRecording("Manual clear");
            ClearViewportAndBuffers("Manual clear");
        }

        private void ForceRecognize_Click(object sender, RoutedEventArgs e)
        {
            if (_recordState == RecordState.Recording)
            {
                FinalizeRecording("Force (Enter/Stop equivalent)");
                return;
            }

            if (_stroke.Count < 10)
            {
                Log("Force recognize: not enough points.");
                return;
            }

            CommitStrokeForRecognition("Force recognize");
        }

        // Helper to log out failure reason
        private void FailSpell(string reason)
        {
            Log("FAILED CAST: " + reason);
            Status("Failed: " + reason);
            ClearViewportAndBuffers("Failed cast");
        }


        private void ClearViewportAndBuffers(string reason)
        {
            _stroke.Clear();
            _missingFrames = 0;
            _shapeCommitted = false;
            _strokeStartUtc = DateTime.MinValue;

            _hasLastDepthPixel = false;

            _lastMovePoint = null;
            _lastMoveUtc = DateTime.MinValue;


            SetupViewport();
            Log("Cleared: " + reason);
            Status("Cleared.");
        }
        //Fill in the name of the spell in the spell legend text box
        private void RefreshSpellLegend()
        {
            Dispatcher.Invoke(() =>
            {
                // Build one legend item per unique spell name (use the first template found)
                var items = _templates
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Name) && t.Points != null && t.Points.Count >= 2)
                    .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var t = g.First();
                        return new SpellLegendItem
                        {
                            Name = g.Key,
                            PreviewPoints = BuildPreviewPointCollection(t.Points, 86, 48, 6)
                        };
                    })
                    .OrderBy(x => x.Name)
                    .ToList();

                SpellListBox.ItemsSource = items;
            });
        }

        // Build a spell legen ditem that includes the name and a preview of what must be drawn
        public class SpellLegendItem
        {
            public string Name { get; set; }
            public System.Windows.Media.PointCollection PreviewPoints { get; set; }
        }
        // Helper method to convert the points colleciton to a preview for the legend
        private System.Windows.Media.PointCollection BuildPreviewPointCollection(List<XY> pts, double targetW, double targetH, double padding)
        {
            // Convert to WPF Points
            var src = pts.Select(p => new System.Windows.Point(p.X, p.Y)).ToList();

            double minX = src.Min(p => p.X);
            double maxX = src.Max(p => p.X);
            double minY = src.Min(p => p.Y);
            double maxY = src.Max(p => p.Y);

            double w = Math.Max(1.0, maxX - minX);
            double h = Math.Max(1.0, maxY - minY);

            // Fit uniformly into target box with padding (preserve aspect ratio)
            double scale = Math.Min((targetW - 2 * padding) / w, (targetH - 2 * padding) / h);

            double offsetX = (targetW - (w * scale)) / 2.0;
            double offsetY = (targetH - (h * scale)) / 2.0;

            var pc = new System.Windows.Media.PointCollection();
            foreach (var p in src)
            {
                double x = ((p.X - minX) * scale) + offsetX;
                double y = ((p.Y - minY) * scale) + offsetY;
                pc.Add(new System.Windows.Point(x, y));
            }
            return pc;
        }


        // ---------- Logging ----------
        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
                LogBox.ScrollToEnd();
            });
        }

        private void Status(string msg)
        {
            Dispatcher.Invoke(() => StatusText.Text = msg);
        }

        // ---------- Geometry helpers ----------
        private static double Dist(Point2D a, Point2D b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PathLen(List<Point2D> pts)
        {
            double sum = 0;
            for (int i = 1; i < pts.Count; i++) sum += Dist(pts[i - 1], pts[i]);
            return sum;
        }
    }
}
