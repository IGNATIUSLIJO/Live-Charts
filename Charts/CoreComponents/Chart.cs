﻿//The MIT License(MIT)

//Copyright(c) 2015 Alberto Rodriguez

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LiveCharts.Tooltip;
using LiveCharts.Viewers;

namespace LiveCharts.CoreComponents
{
    public abstract class Chart : UserControl
    {
        public event Action<Chart> Plot;
        public event Action<ChartPoint> DataClick;

        internal Rect PlotArea;
        internal Point Max;
        internal Point Min;
        internal Point S;
        internal int ColorStartIndex;
        internal bool RequiresScale;
        internal List<Series> EraseSerieBuffer = new List<Series>();
        internal bool SeriesInitialized;
        internal double From = double.MinValue;
        internal double To = double.MaxValue;
        internal AxisTags ZoomingAxis = AxisTags.None;
        internal bool SupportsMultipleSeries = true;

        protected double CurrentScale;
        protected ShapeHoverBehavior ShapeHoverBehavior;
        protected bool AlphaLabel;
        protected readonly DispatcherTimer TooltipTimer;
        protected double DefaultFillOpacity = 0.35;

        private static readonly Random Randomizer;
        private readonly DispatcherTimer _resizeTimer;
        private readonly DispatcherTimer _serieValuesChanged;
        private readonly DispatcherTimer _seriesChanged;
        private Point _panOrigin;
        private bool _isDragging;
        private UIElement _dataToolTip;
        private int _colorIndexer;

        static Chart()
        {
            Colors = new List<Color>
            {
                Color.FromRgb(33, 149, 242),
                Color.FromRgb(243, 67, 54),
                Color.FromRgb(254, 192, 7),
                Color.FromRgb(96, 125, 138),
                Color.FromRgb(155, 39, 175),
                Color.FromRgb(0, 149, 135),
                Color.FromRgb(76, 174, 80),
                Color.FromRgb(121, 85, 72),
                Color.FromRgb(157, 157, 157),
                Color.FromRgb(232, 30, 99),
                Color.FromRgb(63, 81, 180),
                Color.FromRgb(0, 187, 211),
                Color.FromRgb(254, 234, 59),
                Color.FromRgb(254, 87, 34)
            };
            Randomizer = new Random();
        }

        protected Chart()
        {
            var b = new Border {ClipToBounds = true};
            Canvas = new Canvas {RenderTransform = new TranslateTransform(0, 0)};
            b.Child = Canvas;
            Content = b;

            if (RandomizeStartingColor) ColorStartIndex = Randomizer.Next(0, Colors.Count - 1);

            AnimatesNewPoints = false;
            CurrentScale = 1;

            var defaultConfig = new SeriesConfiguration<double>().Y(x => x);
            SetCurrentValue(SeriesProperty, new SeriesCollection(defaultConfig));
            DataToolTip = new DefaultIndexedTooltip();
            Shapes = new List<FrameworkElement>();
            HoverableShapes = new List<HoverableShape>();
            PointHoverColor = System.Windows.Media.Colors.White; 

            Background = Brushes.Transparent;

            SizeChanged += Chart_OnsizeChanged;
            MouseWheel += MouseWheelOnRoll;
            MouseLeftButtonDown += MouseDownForPan;
            MouseLeftButtonUp += MouseUpForPan;
            MouseMove += MouseMoveForPan;

            _resizeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _resizeTimer.Tick += (sender, e) =>
            {
                _resizeTimer.Stop();
                ClearAndPlot();
            };
            TooltipTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            TooltipTimer.Tick += TooltipTimerOnTick;

            _serieValuesChanged = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(100)};
            _serieValuesChanged.Tick += UpdateModifiedDataSeries;

            _seriesChanged = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(100)};
            _seriesChanged.Tick += UpdateSeries;
        }

        #region StaticProperties
        /// <summary>
        /// List of Colors series will use, yu can change this list to your own colors.
        /// </summary>
        public static List<Color> Colors { get; set; }

        /// <summary>
        /// indicates wether each instance of chart you create needs to randomize starting color
        /// </summary>
        public static bool RandomizeStartingColor { get; set; }

        #endregion

        #region Dependency Properties

        public static readonly DependencyProperty LegendProperty = DependencyProperty.Register(
            "Legend", typeof (ChartLegend), typeof (Chart), new PropertyMetadata(null));

        public ChartLegend Legend
        {
            get { return (ChartLegend) GetValue(LegendProperty); }
            set { SetValue(LegendProperty, value); }
        }

        public static readonly DependencyProperty LegendLocationProperty = DependencyProperty.Register(
            "LegendLocation", typeof (LegendLocation), typeof (Chart), new PropertyMetadata(LegendLocation.None));

        public LegendLocation LegendLocation
        {
            get { return (LegendLocation) GetValue(LegendLocationProperty); }
            set { SetValue(LegendLocationProperty, value); }
        }

        public static readonly DependencyProperty InvertProperty = DependencyProperty.Register(
            "Invert", typeof (bool), typeof (Chart), new PropertyMetadata(default(bool)));

        public bool Invert
        {
            get { return (bool) GetValue(InvertProperty); }
            set { SetValue(InvertProperty, value); }
        }

        public static readonly DependencyProperty HoverableProperty = DependencyProperty.Register(
            "Hoverable", typeof (bool), typeof (Chart));

        /// <summary>
        /// Indicates weather chart is hoverable or not
        /// </summary>
        public bool Hoverable
        {
            get { return (bool) GetValue(HoverableProperty); }
            set { SetValue(HoverableProperty, value); }
        }

        public static readonly DependencyProperty PointHoverColorProperty = DependencyProperty.Register(
            "PointHoverColor", typeof (Color), typeof (Chart));

        /// <summary>
        /// Indicates Point hover color.
        /// </summary>
        public Color PointHoverColor
        {
            get { return (Color) GetValue(PointHoverColorProperty); }
            set { SetValue(PointHoverColorProperty, value); }
        }

        public static readonly DependencyProperty DisableAnimationProperty = DependencyProperty.Register(
            "DisableAnimation", typeof (bool), typeof (Chart));

        /// <summary>
        /// Indicates weather to show animation or not.
        /// </summary>
        public bool DisableAnimation
        {
            get { return (bool) GetValue(DisableAnimationProperty); }
            set { SetValue(DisableAnimationProperty, value); }
        }

        public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
            "Series", typeof (SeriesCollection), typeof (Chart),
            new PropertyMetadata(null, SeriesChangedCallback ));
        /// <summary>
        /// Gets or sets chart series to plot
        /// </summary>
        public SeriesCollection Series
        {
            get { return (SeriesCollection)  GetValue(SeriesProperty); }
            set { SetValue(SeriesProperty, value); }
        }
        #endregion

        #region Properties
        /// <summary>
        /// Gets chart canvas
        /// </summary>
        public Canvas Canvas { get; internal set; }
        /// <summary>
        /// Gets chart point offset
        /// </summary>
        public double XOffset { get; internal set; }
        /// <summary>
        /// Gets charts point offset
        /// </summary>
        public double YOffset { get; set; }
        /// <summary>
        /// Gets current set of shapes added to canvas by LiveCharts
        /// </summary>
        public List<FrameworkElement> Shapes { get; internal set; }
        /// <summary>
        /// Gets collection of shapes that fires tooltip on hover
        /// </summary>
        public List<HoverableShape> HoverableShapes { get; internal set; }

        /// <summary>
        /// Gets or sets X Axis
        /// </summary>
        public Axis AxisY { get; set; }

        /// <summary>
        /// Gets or sets Y Axis
        /// </summary>
        public Axis AxisX { get; set; }
        /// <summary>
        /// Gets or sets current tooltip when mouse is over a hoverable shape
        /// </summary>
        public UIElement DataToolTip
        {
            get { return _dataToolTip; }
            set
            {
                _dataToolTip = value;
                if (value == null) return;
                Panel.SetZIndex(_dataToolTip, int.MaxValue);
                Canvas.SetLeft(_dataToolTip,0);
                Canvas.SetTop(_dataToolTip, 0);
                _dataToolTip.Visibility = Visibility.Hidden;
                Canvas.Children.Add(_dataToolTip);
            }
        }

        public bool Zooming { get; set; }
        #endregion

        #region ProtectedProperties
        protected bool AnimatesNewPoints { get; set; }

        internal bool HasInvalidArea
        {
            get { return PlotArea.Width < 15 || PlotArea.Height < 15; }
        }

        internal bool HasValidRange
        {
            get { return Math.Abs(Max.X - Min.X) > S.X*.01 || Math.Abs(Max.Y - Min.Y) > S.Y*.01; }
        }

        #endregion

        #region Public Methods
        public void ClearAndPlot(bool animate = true)
        {
            if (_seriesChanged == null) return;
            _seriesChanged.Stop();
            _seriesChanged.Start();
            PrepareCanvas(animate);
        }

        public void ZoomIn(Point pivot)
        {
            if (DataToolTip != null) DataToolTip.Visibility = Visibility.Hidden;

            var mid = ZoomingAxis == AxisTags.X ? (Max.X + Min.X)*.5 : (Max.Y + Min.Y)*.5;
            mid = ToPlotArea(mid, ZoomingAxis);

            var s = ZoomingAxis == AxisTags.X ? S.X : S.Y;
            var max = ZoomingAxis == AxisTags.X ? Max.X : Max.Y;
            var min = ZoomingAxis == AxisTags.X ? Min.X : Min.Y;
            var hasMorePoints = max - min > s*1.01;
            
            if (mid < (ZoomingAxis == AxisTags.X ? pivot.X : pivot.Y))
            {
                if (hasMorePoints)
                {
                    if (Invert) To -= s;
                    else From += s;
                }
            }
            else
            {
                if (hasMorePoints)
                {
                    if (Invert) From += s;
                    else To -= s;
                }
            }

            foreach (var series in Series)
                series.Values.RequiresEvaluation = true;

            ForceRedrawNow();
        }

        public void ZoomOut(Point pivot)
        {
            if (DataToolTip != null) DataToolTip.Visibility = Visibility.Hidden;

            var s = ZoomingAxis == AxisTags.X ? S.X : S.Y;

            From -= s;
            To += s;

            foreach (var series in Series)
                series.Values.RequiresEvaluation = true;

            ForceRedrawNow();
        }

        /// <summary>
        /// Scales a graph value to screen according to an axis. 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="axis"></param>
        /// <returns></returns>
        public double ToPlotArea(double value, AxisTags axis)
        {
            return Methods.ToPlotArea(value, axis, this);
        }

        /// <summary>
        /// Scales a graph point to screen.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public Point ToPlotArea(Point value)
        {
            return new Point(ToPlotArea(value.X, AxisTags.X), ToPlotArea(value.Y, AxisTags.Y));
        }

        public double LenghtOf(double value, AxisTags axis)
        {
            return Methods.ToPlotArea(value, axis, this) - (axis == AxisTags.X ? PlotArea.X : PlotArea.Y);
        }
        #endregion

        #region ProtectedMethods
        internal double CalculateSeparator(double range, AxisTags axis)
        {
            //based on:
            //http://stackoverflow.com/questions/361681/algorithm-for-nice-grid-line-intervals-on-a-graph

            var ft = axis == AxisTags.Y
                ? new FormattedText(
                    "A label",
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(AxisY.FontFamily, AxisY.FontStyle, AxisY.FontWeight,
                        AxisY.FontStretch), AxisY.FontSize, Brushes.Black)
                : new FormattedText(
                    "A label",
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(AxisX.FontFamily, AxisX.FontStyle, AxisX.FontWeight,
                        AxisX.FontStretch), AxisX.FontSize, Brushes.Black);

            var separations = axis == AxisTags.Y
                ? Math.Round(PlotArea.Height / ((ft.Height) * AxisY.CleanFactor), 0)
                : Math.Round(PlotArea.Width / ((ft.Width) * AxisX.CleanFactor), 0);

            separations = separations < 2 ? 2 : separations;

            var minimum = range / separations;
            var magnitude = Math.Pow(10, Math.Floor(Math.Log(minimum) / Math.Log(10)));
            var residual = minimum / magnitude;
            double tick;
            if (residual > 5)
                tick = 10 * magnitude;
            else if (residual > 2)
                tick = 5 * magnitude;
            else if (residual > 1)
                tick = 2 * magnitude;
            else
                tick = magnitude;
            return tick;
        }

        protected void ConfigureXAsIndexed()
        {
            if (AxisX.Labels == null && AxisX.LabelFormatter == null) AxisX.ShowLabels = false;
            var f = GetFormatter(AxisX);
            var d = AxisX.Labels == null
                ? Max.X
                : AxisX.Labels.IndexOf(AxisX.Labels.OrderBy(x => x.Length).Reverse().First());
            var longestYLabel = new FormattedText(HasValidRange ? f(d) : "", CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(AxisX.FontFamily, AxisX.FontStyle, AxisX.FontWeight, AxisX.FontStretch), AxisX.FontSize,
                Brushes.Black);
            AxisX.Separator.Step = (longestYLabel.Width*Max.X)*1.25 > PlotArea.Width
                ? null
                : (int?) 1;
            if (AxisX.Separator.Step != null) S.X = (int) AxisX.Separator.Step;
            if (Zooming) ZoomingAxis = AxisTags.X;
        }

        protected void ConfigureYAsIndexed()
        {
            if (AxisY.Labels == null && AxisY.LabelFormatter == null) AxisY.ShowLabels = false;
            var f = GetFormatter(AxisY);
            var d = AxisY.Labels == null
                ? Max.Y
                : AxisY.Labels.IndexOf(AxisY.Labels.OrderBy(x => x.Length).Reverse().First());
            var longestYLabel = new FormattedText(HasValidRange ? f(d) : "", CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(AxisY.FontFamily, AxisY.FontStyle, AxisY.FontWeight, AxisY.FontStretch), AxisY.FontSize,
                Brushes.Black);
            AxisY.Separator.Step = (longestYLabel.Width*Max.Y)*1.25 > PlotArea.Width
                ? null
                : (int?) 1;
            if (AxisY.Separator.Step != null) S.Y = (int) AxisY.Separator.Step;
            if (Zooming) ZoomingAxis = AxisTags.Y;
        }

        protected Point GetLongestLabelSize(Axis axis)
        {
            if (!axis.ShowLabels) return new Point(0, 0);
            var label = "";
            var from = Equals(axis, AxisY) ? Min.Y : Min.X;
            var to = Equals(axis, AxisY) ? Max.Y : Max.X;
            var s = Equals(axis, AxisY) ? S.Y : S.X;
            var f = GetFormatter(axis);
            for (var i = from; i <= to; i += s)
            {
                var iL = f(i);
                if (label.Length < iL.Length)
                {
                    label = iL;
                }
            }
            var longestLabel = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(axis.FontFamily, axis.FontStyle, axis.FontWeight,
                axis.FontStretch), axis.FontSize, Brushes.Black);
            return new Point(longestLabel.Width, longestLabel.Height);
        }

        protected Point GetLabelSize(Axis axis, double value)
        {
            if (!axis.ShowLabels) return new Point(0, 0);

            var labels = axis.Labels != null ? axis.Labels.ToArray() : null;
            var fomattedValue = labels == null
                ? (AxisX.LabelFormatter == null
                    ? Min.X.ToString(CultureInfo.InvariantCulture)
                    : AxisX.LabelFormatter(value))
                : (labels.Length > value && value>=0
                    ? labels[(int)value]
                    : "");
            var uiLabelSize = new FormattedText(fomattedValue, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(axis.FontFamily, axis.FontStyle, axis.FontWeight, axis.FontStretch),
                axis.FontSize, Brushes.Black);
            return new Point(uiLabelSize.Width, uiLabelSize.Height);
        }

        protected Point GetLabelSize(Axis axis, string value)
        {
            if (!axis.ShowLabels) return new Point(0, 0);
            var fomattedValue = value;
            var uiLabelSize = new FormattedText(fomattedValue, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(axis.FontFamily, axis.FontStyle, axis.FontWeight, axis.FontStretch),
                axis.FontSize, Brushes.Black);
            return new Point(uiLabelSize.Width, uiLabelSize.Height);
        }

        protected virtual void Scale()
        {
            InitializeComponents();

            Max = new Point(
                AxisX.MaxValue ??
                Series.Where(x => x.Values != null).Select(x => x.Values.MaxChartPoint.X).DefaultIfEmpty(0).Max(),
                AxisY.MaxValue ??
                Series.Where(x => x.Values != null).Select(x => x.Values.MaxChartPoint.Y).DefaultIfEmpty(0).Max());

            Min = new Point(
                AxisX.MinValue ??
                Series.Where(x => x.Values != null).Select(x => x.Values.MinChartPoint.X).DefaultIfEmpty(0).Min(),
                AxisY.MinValue ??
                Series.Where(x => x.Values != null).Select(x => x.Values.MinChartPoint.Y).DefaultIfEmpty(0).Min());

            if (ZoomingAxis == AxisTags.X)
            {
                From = Min.X;
                To = Max.X;
            }
            if (ZoomingAxis == AxisTags.Y)
            {
                From = Min.Y;
                To = Max.Y;
            }
        }
        #endregion

        #region Virtual Methods
        protected virtual void DrawAxes()
        {
            if (!HasValidRange) return;
            foreach (var l in Shapes) Canvas.Children.Remove(l);

            //legend
            var legend = Legend ?? new ChartLegend();
            LoadLegend(legend);

            if (LegendLocation != LegendLocation.None)
            {
                Canvas.Children.Add(legend);
                Shapes.Add(legend);
                legend.UpdateLayout();
                legend.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            switch (LegendLocation)
            {
                case LegendLocation.None:
                    break;
                case LegendLocation.Top:
                    var top = new Point(ActualWidth*.5 - legend.DesiredSize.Width*.5, 0);
                    PlotArea.Y += top.Y + legend.DesiredSize.Height;
                    PlotArea.Height -= legend.DesiredSize.Height;
                    Canvas.SetTop(legend, top.Y);
                    Canvas.SetLeft(legend, top.X);
                    break;
                case LegendLocation.Bottom:
                    var bot = new Point(ActualWidth*.5 - legend.DesiredSize.Width*.5, ActualHeight - legend.DesiredSize.Height);
                    PlotArea.Height -= legend.DesiredSize.Height;
                    Canvas.SetTop(legend, Canvas.ActualHeight - legend.DesiredSize.Height);
                    Canvas.SetLeft(legend, bot.X);
                    break;
                case LegendLocation.Left:
                    PlotArea.X += legend.DesiredSize.Width;
                    PlotArea.Width -= legend.DesiredSize.Width;
                    Canvas.SetTop(legend, Canvas.ActualHeight * .5 - legend.DesiredSize.Height * .5);
                    Canvas.SetLeft(legend, 0);
                    break;
                case LegendLocation.Right:
                    PlotArea.Width -= legend.DesiredSize.Width;
                    Canvas.SetTop(legend, Canvas.ActualHeight*.5 - legend.DesiredSize.Height*.5);
                    Canvas.SetLeft(legend, ActualWidth - legend.DesiredSize.Width);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            //draw axes titles
            var longestY = GetLongestLabelSize(AxisY);
            var longestX = GetLongestLabelSize(AxisX);

            if (!string.IsNullOrWhiteSpace(AxisY.Title))
            {
                var ty = GetLabelSize(AxisY, AxisY.Title);
                var yLabel = AxisY.BuildATextBlock(-90);
                var binding = new Binding {Path = new PropertyPath("Title"), Source = AxisY};
                BindingOperations.SetBinding(yLabel, TextBlock.TextProperty, binding);
                Shapes.Add(yLabel);
                Canvas.Children.Add(yLabel);
                if (AxisY.Title.Trim().Length > 0)
                {
                    PlotArea.X += ty.Y;
                    PlotArea.Width -= ty.Y;
                }
                Canvas.SetLeft(yLabel, PlotArea.X - ty.Y -(AxisY.ShowLabels ? longestY.X +5 : 0) -5);
                Canvas.SetTop(yLabel, Canvas.DesiredSize.Height * .5 + ty.X * .5);
            }
            if (!string.IsNullOrWhiteSpace(AxisX.Title))
            {
                var tx = GetLabelSize(AxisX, AxisX.Title);
                var yLabel = AxisY.BuildATextBlock(0);
                var binding = new Binding {Path = new PropertyPath("Title"), Source = AxisX};
                BindingOperations.SetBinding(yLabel, TextBlock.TextProperty, binding);
                Shapes.Add(yLabel);
                Canvas.Children.Add(yLabel);
                if (AxisX.Title.Trim().Length > 0) PlotArea.Height -= tx.Y;
                Canvas.SetLeft(yLabel, Canvas.DesiredSize.Width*.5 - tx.X*.5);
                Canvas.SetTop(yLabel, PlotArea.Y + PlotArea.Height + (AxisX.ShowLabels ? tx.Y +5 : 0));
            }

            //YAxis
            DrawAxis(AxisY, longestY);
            //XAxis
            DrawAxis(AxisX, longestX);
            //drawing ceros.
            if (Max.Y >= 0 && Min.Y <= 0 && AxisY.IsEnabled)
            {
                var l = new Line
                {
                    Stroke = new SolidColorBrush {Color = AxisY.Color}, StrokeThickness = AxisY.Thickness, X1 = ToPlotArea(Min.X, AxisTags.X), Y1 = ToPlotArea(0, AxisTags.Y), X2 = ToPlotArea(Max.X, AxisTags.X), Y2 = ToPlotArea(0, AxisTags.Y)
                };
                Canvas.Children.Add(l);
                Shapes.Add(l);
            }
            if (Max.X >= 0 && Min.X <= 0 && AxisX.IsEnabled)
            {
                var l = new Line
                {
                    Stroke = new SolidColorBrush {Color = AxisX.Color}, StrokeThickness = AxisX.Thickness, X1 = ToPlotArea(0, AxisTags.X), Y1 = ToPlotArea(Min.Y, AxisTags.Y), X2 = ToPlotArea(0, AxisTags.X), Y2 = ToPlotArea(Max.Y, AxisTags.Y)
                };
                Canvas.Children.Add(l);
                Shapes.Add(l);
            }
        }

        protected virtual void LoadLegend(ChartLegend legend)
        {
            legend.Series = Series.Select(x => new SeriesStandin
            {
                Fill = x.Fill,
                Stroke = x.Stroke,
                Title = x.Title
            });
            legend.Orientation = LegendLocation == LegendLocation.Bottom || LegendLocation == LegendLocation.Top
                ? Orientation.Horizontal
                : Orientation.Vertical;
        }

        internal virtual void DataMouseEnter(object sender, MouseEventArgs e)
        {
            if (DataToolTip == null || !Hoverable) return;

            DataToolTip.Visibility = Visibility.Visible;
            TooltipTimer.Stop();

            var senderShape = HoverableShapes.FirstOrDefault(s => Equals(s.Shape, sender));
            if (senderShape == null) return;
            var sibilings = Invert ? HoverableShapes.Where(s => Math.Abs(s.Value.Y - senderShape.Value.Y) < S.Y*.01).ToList() : HoverableShapes.Where(s => Math.Abs(s.Value.X - senderShape.Value.X) < S.X*.01).ToList();

            var first = sibilings.Count > 0 ? sibilings[0] : null;
            var labels = Invert ? (AxisY.Labels != null ? AxisY.Labels.ToArray() : null) : (AxisX.Labels != null ? AxisX.Labels.ToArray() : null);
            var vx = first != null ? (Invert ? first.Value.Y : first.Value.X) : 0;

            foreach (var sibiling in sibilings)
            {
                if (ShapeHoverBehavior == ShapeHoverBehavior.Dot)
                {
                    sibiling.Target.Stroke = sibiling.Series.Stroke;
                    sibiling.Target.Fill = new SolidColorBrush {Color = PointHoverColor};
                }
                else sibiling.Target.Opacity = .8;
            }

            var indexedToolTip = DataToolTip as IndexedTooltip;
            if (indexedToolTip != null)
            {
                var fh = GetFormatter(Invert ? AxisY : AxisX);
                var fs = GetFormatter(Invert ? AxisX : AxisY);
                indexedToolTip.Header = fh(vx);
                indexedToolTip.Data = sibilings.Select(x => new IndexedTooltipData
                {
                    Index = Series.IndexOf(x.Series), Series = x.Series, Stroke = x.Series.Stroke, Fill = x.Series.Fill, Point = x.Value, Value = fs(Invert ? x.Value.X : x.Value.Y)
                }).ToArray();
            }

            var p = GetToolTipPosition(senderShape, sibilings);

            DataToolTip.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation
            {
                To = p.X, Duration = TimeSpan.FromMilliseconds(200)
            });
            DataToolTip.BeginAnimation(Canvas.TopProperty, new DoubleAnimation
            {
                To = p.Y, Duration = TimeSpan.FromMilliseconds(200)
            });
        }

        internal virtual void DataMouseLeave(object sender, MouseEventArgs e)
        {
            if (!Hoverable) return;

            var s = sender as Shape;
            if (s == null) return;

            var shape = HoverableShapes.FirstOrDefault(x => Equals(x.Shape, s));
            if (shape == null) return;

            var sibilings = HoverableShapes.Where(x => Math.Abs(x.Value.X - shape.Value.X) < .001*S.X).ToList();

            foreach (var p in sibilings)
            {
                if (ShapeHoverBehavior == ShapeHoverBehavior.Dot)
                {
                    p.Target.Fill = p.Series.Stroke;
                    p.Target.Stroke = new SolidColorBrush {Color = PointHoverColor};
                }
                else
                {
                    p.Target.Opacity = 1;
                }
            }
            TooltipTimer.Stop();
            TooltipTimer.Start();
        }

        internal virtual void DataMouseDown(object sender, MouseEventArgs e)
        {
            var shape = HoverableShapes.FirstOrDefault(s => Equals(s.Shape, sender));
            if (shape == null) return;
            if (DataClick != null) DataClick.Invoke(shape.Value);
        }

        protected virtual Point GetToolTipPosition(HoverableShape sender, List<HoverableShape> sibilings)
        {
            DataToolTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var x = sender.Value.X > (Min.X + Max.X)/2 ? ToPlotArea(sender.Value.X, AxisTags.X) - 10 - DataToolTip.DesiredSize.Width : ToPlotArea(sender.Value.X, AxisTags.X) + 10;
            var y = ToPlotArea(sibilings.Select(s => s.Value.Y).DefaultIfEmpty(0).Sum()/sibilings.Count, AxisTags.Y);
            y = y + DataToolTip.DesiredSize.Height > ActualHeight ? y - (y + DataToolTip.DesiredSize.Height - ActualHeight) - 5 : y;
            return new Point(x, y);
        }

        #endregion

        #region Internal Methods

        internal void OnDataSeriesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _serieValuesChanged.Stop();
            _serieValuesChanged.Start();
        }

        #endregion

        #region Private Methods

        private void DrawAxis(Axis axis, Point longestLabel)
        {
            var isX = Equals(axis, AxisX);
            var max = isX ? Max.X : Max.Y;
            var min = isX ? Min.X : Min.Y;
            var s = isX ? S.X : S.Y;

            var maxval = axis.Separator.IsEnabled || axis.ShowLabels ? max + (axis.IgnoresLastLabel ? -1 : 0) : min - 1;

            var formatter = GetFormatter(axis);

            for (var i = min; i <= maxval; i += s)
            {
                if (axis.Separator.IsEnabled)
                {
                    var l = new Line
                    {
                        Stroke = new SolidColorBrush {Color = axis.Separator.Color}, StrokeThickness = axis.Separator.Thickness
                    };
                    if (isX)
                    {
                        var x = ToPlotArea(i, AxisTags.X);
                        l.X1 = x;
                        l.X2 = x;
                        l.Y1 = ToPlotArea(Max.Y, AxisTags.Y);
                        l.Y2 = ToPlotArea(Min.Y, AxisTags.Y);
                    }
                    else
                    {
                        var y = ToPlotArea(i, AxisTags.Y);
                        l.X1 = ToPlotArea(Min.X, AxisTags.X);
                        l.X2 = ToPlotArea(Max.X, AxisTags.X);
                        l.Y1 = y;
                        l.Y2 = y;
                    }

                    Canvas.Children.Add(l);
                    Shapes.Add(l);
                }

                if (axis.ShowLabels)
                {
                    var text = formatter(i);
                    var label = axis.BuildATextBlock(0);
                    label.Text = text;
                    var fl = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(axis.FontFamily, axis.FontStyle, axis.FontWeight, axis.FontStretch), axis.FontSize, Brushes.Black);
                    Canvas.Children.Add(label);
                    Shapes.Add(label);

                    var top = 0;
                    var left = 0;

                    if (isX)
                    {
                        Canvas.SetLeft(label, ToPlotArea(i, AxisTags.X) - fl.Width*.5 + XOffset);
                        Canvas.SetTop(label, PlotArea.Y + PlotArea.Height + 5);
                    }
                    else
                    {
                        Canvas.SetLeft(label, PlotArea.X - fl.Width -5);
                        Canvas.SetTop(label, ToPlotArea(i, AxisTags.Y) - longestLabel.Y*.5 + YOffset);
                    }
                }
            }
        }

        internal Func<double, string> GetFormatter(Axis axis)
        {
            var labels = axis.Labels != null ? axis.Labels : null;

            return x => labels == null
                ? (axis.LabelFormatter == null ? x.ToString(CultureInfo.InvariantCulture) : axis.LabelFormatter(x))
                : (labels.Count > x && x >= 0 ? labels[(int) x] : "");
        }

        private void ForceRedrawNow()
        {
            PrepareCanvas();
            UpdateSeries(null, null);
        }

        private void PrepareCanvas(bool animate = false)
        {
            if (Series == null) return;
            if (!SeriesInitialized) InitializeSeries(this);

            if (AxisY.Parent == null) Canvas.Children.Add(AxisY);
            if (AxisX.Parent == null) Canvas.Children.Add(AxisX);

            foreach (var series in Series)
            {
                Canvas.Children.Remove(series);
                Canvas.Children.Add(series);
                EraseSerieBuffer.Add(series);
                series.RequiresAnimation = animate;
                series.RequiresPlot = true;
            }

            Canvas.Width = ActualWidth*CurrentScale;
            Canvas.Height = ActualHeight*CurrentScale;
            PlotArea = new Rect(0, 0, ActualWidth*CurrentScale, ActualHeight*CurrentScale);
            RequiresScale = true;
        }

        protected void InitializeComponents()
        {
            Series.Chart = this;
            Series.Configuration.Chart = this;
            foreach (var series in Series)
            {
                series.Collection = Series;
                if (series.Values == null) continue;
                if (series.Configuration != null) series.Configuration.Chart = this;
                series.Values.Series = series;
                series.Values.GetLimits();
            }
        }

        private void PreventPlotAreaToBeVisible()
        {
            var tt = Canvas.RenderTransform as TranslateTransform;
            if (tt == null) return;
            var eX = tt.X;
            var eY = tt.Y;
            var xOverflow = -tt.X + ActualWidth - Canvas.Width;
            var yOverflow = -tt.Y + ActualHeight - Canvas.Height;

            if (eX > 0)
            {
                //Cant understand why with I cant animate this...
                //Pan stops working when I do animation on overflow

                //var y = new DoubleAnimation(tt.Y, 0, TimeSpan.FromMilliseconds(150));
                //var x = new DoubleAnimation(tt.X, 0, TimeSpan.FromMilliseconds(150));

                //I even try this... but nope
                //y.Completed += (o, args) => { tt.Y = 0; };
                //x.Completed += (o, args) => { tt.X = 0; };

                //Canvas.RenderTransform.BeginAnimation(TranslateTransform.YProperty, y);
                //Canvas.RenderTransform.BeginAnimation(TranslateTransform.XProperty, x);
                tt.X = 0;
            }

            if (eY > 0)
            {
                tt.Y = 0;
            }

            if (xOverflow > 0)
            {
                tt.X = tt.X + xOverflow;
            }

            if (yOverflow > 0)
            {
                tt.Y = tt.Y + yOverflow;
            }
        }

        private void Chart_OnsizeChanged(object sender, SizeChangedEventArgs e)
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private static void SeriesChangedCallback(DependencyObject o, DependencyPropertyChangedEventArgs eventArgs)
        {
            var chart = o as Chart;

            if (chart == null || chart.Series == null) return;
            if (chart.Series.Any(x => x == null)) return;

            if (chart.Series.Count > 0 && !chart.HasInvalidArea) chart.Scale();
        }

        private void InitializeSeries(Chart chart)
        {
#if DEBUG
            Trace.WriteLine("Chart was initialized (" + DateTime.Now.ToLongTimeString() + ")");
#endif
            chart.SeriesInitialized = true;
            foreach (var series in chart.Series)
            {
                var index = _colorIndexer++;
                series.Chart = chart;
                series.Collection = Series;
                series.Stroke = series.Stroke ?? new SolidColorBrush(Colors[(int) (index - Colors.Count*Math.Truncate(index/(decimal) Colors.Count))]);
                series.Fill = series.Fill ?? new SolidColorBrush(Colors[(int) (index - Colors.Count*Math.Truncate(index/(decimal) Colors.Count))])
                {
                    Opacity = DefaultFillOpacity
                };
                series.RequiresPlot = true;
                series.RequiresAnimation = true;
                var observable = series.Values as INotifyCollectionChanged;
                if (observable != null)
                    observable.CollectionChanged += chart.OnDataSeriesChanged;
            }

            chart.ClearAndPlot();
            var anim = new DoubleAnimation
            {
                From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(1000)
            };
            if (!chart.DisableAnimation) chart.Canvas.BeginAnimation(OpacityProperty, anim);

            chart.Series.CollectionChanged += (sender, args) =>
            {
                chart._seriesChanged.Stop();
                chart._seriesChanged.Start();

                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    chart.Canvas.Children.Clear();
                    chart.Shapes.Clear();
                    chart.HoverableShapes.Clear();
                }

                if (args.OldItems != null)
                    foreach (var series in args.OldItems.Cast<Series>())
                    {
                        chart.EraseSerieBuffer.Add(series);
                    }

                var newElements = args.NewItems != null ? args.NewItems.Cast<Series>() : new List<Series>();

                chart.RequiresScale = true;
                foreach (var serie in chart.Series.Where(x => !newElements.Contains(x)))
                {
                    chart.EraseSerieBuffer.Add(serie);
                    serie.RequiresPlot = true;
                }

                if (args.NewItems != null)
                    foreach (var series in newElements)
                    {
                        var index = _colorIndexer++;
                        series.Chart = chart;
                        series.Collection = Series;
                        series.Stroke = series.Stroke ?? new SolidColorBrush(Colors[(int) (index - Colors.Count*Math.Truncate(index/(decimal) Colors.Count))]);
                        series.Fill = series.Fill ?? new SolidColorBrush(Colors[(int) (index - Colors.Count*Math.Truncate(index/(decimal) Colors.Count))])
                        {
                            Opacity = DefaultFillOpacity
                        };
                        series.RequiresPlot = true;
                        series.RequiresAnimation = true;
                        var observable = series.Values as INotifyCollectionChanged;
                        if (observable != null)
                            observable.CollectionChanged += chart.OnDataSeriesChanged;
#if DEBUG
                        if (observable == null) Trace.WriteLine("series do not implements INotifyCollectionChanged");
#endif
                    }
            };
        }

        private void UpdateSeries(object sender, EventArgs e)
        {
            _seriesChanged.Stop();

            EreaseSeries();

            if (Series == null || Series.Count == 0) return;
            if (HasInvalidArea) return;

            foreach (var shape in Shapes) Canvas.Children.Remove(shape);
            foreach (var shape in HoverableShapes.Select(x => x.Shape).ToList()) Canvas.Children.Remove(shape);
            HoverableShapes = new List<HoverableShape>();
            Shapes = new List<FrameworkElement>();

            if (RequiresScale)
            {
                Scale();
                RequiresScale = false;
            }

            var toPlot = Series.Where(x => x.RequiresPlot);
            foreach (var series in toPlot)
            {
                if (series.Values.Count > 0) series.Plot(series.RequiresAnimation);
                series.RequiresPlot = false;
                series.RequiresAnimation = false;
            }

            if (Plot != null) Plot(this);
#if DEBUG
            Trace.WriteLine("Series Updated (" + DateTime.Now.ToLongTimeString() + ")");
#endif
        }

        private void EreaseSeries()
        {
            foreach (var serie in EraseSerieBuffer.GroupBy(x => x)) serie.First().Erase();
            EraseSerieBuffer.Clear();
        }

        private void UpdateModifiedDataSeries(object sender, EventArgs e)
        {
#if DEBUG
            Trace.WriteLine("Primary Values Updated (" + DateTime.Now.ToLongTimeString() + ")");
#endif
            _serieValuesChanged.Stop();
            Scale();
            foreach (var serie in Series)
            {
                serie.Erase();
                serie.Plot(AnimatesNewPoints);
            }
        }

        private void MouseWheelOnRoll(object sender, MouseWheelEventArgs e)
        {
            if (ZoomingAxis == AxisTags.None) return;
            e.Handled = true;
            if (e.Delta > 0) ZoomIn(e.GetPosition(this));
            else ZoomOut(e.GetPosition(this));
        }

        private void MouseDownForPan(object sender, MouseEventArgs e)
        {
            if (ZoomingAxis == AxisTags.None) return;
            _panOrigin = e.GetPosition(this);
            _isDragging = true;
        }

        private void MouseMoveForPan(object sender, MouseEventArgs e)
        {
            //Panning is disabled for now

            //if (!_isDragging) return;

            //var movePoint = e.GetPosition(this);
            //var dif = _panOrigin - movePoint;

            //Min.X = Min.X - dif.X;
            //Min.Y = Min.Y - dif.Y;

            //foreach (var series in Series)
            //    series.Values.RequiresEvaluation = true;

            //UpdateSeries(null, null);

            //_panOrigin = movePoint;
        }

        private void MouseUpForPan(object sender, MouseEventArgs e)
        {
            if (ZoomingAxis == AxisTags.None) return;
            _isDragging = false;
            PreventPlotAreaToBeVisible();
        }

        private void TooltipTimerOnTick(object sender, EventArgs e)
        {
            DataToolTip.Visibility = Visibility.Hidden;
        }

        #endregion
    }
}