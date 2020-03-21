using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CO2Sensor
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// 折れ線グラフパーツ
    /// XAMLで全てのプロパティを設定しておくこと
    /// </summary>
    public class GraphPart : Canvas
    {
        public int UnitWidth { get; set; }
        public Color Co2DotColor { get; set; }
        public Color Co2AxisColor { get; set; }
        public Color ChargeDotColor { get; set; }
        public Color ChargeAxisColor { get; set; }
        public Color HorizontalAxisColor { get; set; }
        public bool ShowChargeLevel { get; set; }

        /// <summary>
        /// 0を真ん中にしたい  ⇒ 0.5
        /// 0を下限にしたい    ⇒ 1.0
        /// 0を上限にしたい    ⇒ 0.0
        /// </summary>
        public double VerticalLevel { get; set; }

        private List<Point> co2Points = new List<Point>();
        private List<Point> chargePoints = new List<Point>();
        private double co2MinY = 0.0;
        private double co2MaxY = 0.0;
        private double co2StepY = 0.0;
        private double chargeMinY = 0.0;
        private double chargeMaxY = 0.0;
        private double chargeStepY = 0.0;
        private double widthPixels;
        private int co2TextY;
        private int chargeTextY;

        public void Initialize()
        {
            Co2DotColor         = Colors.DarkGray;
            ChargeDotColor      = Colors.Magenta;
            Co2AxisColor        = Colors.Gray;
            ChargeAxisColor     = Colors.LightPink;
            HorizontalAxisColor = Colors.SandyBrown;

            ShowChargeLevel = false;

            co2MinY = 0.0;
            co2MaxY = 2500.0;
            co2StepY = 500.0D;
            co2TextY = (int)(co2MaxY + co2StepY / (co2MaxY / co2StepY)); 

            chargeMinY = 0.0;
            chargeMaxY = 100.0;
            chargeStepY = 20.0D;
            chargeTextY = (int)(chargeMaxY + chargeStepY / (chargeMaxY / chargeStepY)); 

            UnitWidth = 60;
            VerticalLevel = 1.0;
        }

        public void Clear()
        {
            Children.Clear();
            co2Points.Clear();
            chargePoints.Clear();
        }

        // need modify
        public void AdjustDrawArea()
        {
            widthPixels = 1 / (ActualWidth * UnitWidth);
        }

        // need modify
        //public void AdjustDrawArea(double unitWidth)
        //{
        //    UnitWidth = unitWidth;
        //    AdjustDrawArea();
        //}

        public void AddCo2Point(DateTime t, double value)
        {
            Point p = new Point();
            p.Y = value;
            p.X = t.Hour * 60 * 60 + t.Minute * 60 + t.Second;

            //if (minY > p.Y)
            //{
            //    minY = p.Y;
            //}
            //if (maxY < p.Y)
            //{
            //    maxY = p.Y;
            //}
            if (co2Points.Count > 0)
            {
                if ((p.X - co2Points[0].X) / UnitWidth >= ActualWidth)
                {
                    co2Points.RemoveAt(0);
                }
            }
            co2Points.Add(p);
        }

        public void AddChargePoint(DateTime t, double value)
        {
            Point p = new Point();
            p.Y = value;
            p.X = t.Hour * 60 * 60 + t.Minute * 60 + t.Second;

            //if (minY > p.Y)
            //{
            //    minY = p.Y;
            //}
            //if (maxY < p.Y)
            //{
            //    maxY = p.Y;
            //}
            if (chargePoints.Count > 0)
            {
                if ((p.X - chargePoints[0].X) / UnitWidth >= ActualWidth)
                {
                    chargePoints.RemoveAt(0);
                }
            }
            chargePoints.Add(p);
        }

        public void DrawGraph()
        {
            Children.Clear();

            //if (points.Count == 0)
            //{
            //    return;
            //}
            
            DateTime now = DateTime.Now;
            const int oneDayUnit = 24 * 60 * 60;
            int nowData = now.Hour * 60 * 60 + now.Minute * 60 + now.Second;
            int sinceData = nowData - (int)ActualWidth * UnitWidth;
            double co2UnitHeight = ActualHeight / co2MaxY;
            double chargeUnitHeight = ActualHeight / chargeMaxY;

            Line axis;

            // |
            // |
            axis = new Line();
            axis.X1 = 0;
            axis.Y1 = 0; // ActualHeight * VerticalLevel; // 330 * 1.0 
            axis.X2 = 0; // ActualWidth; // 330
            axis.Y2 = ActualHeight * VerticalLevel;
            axis.StrokeThickness = 1;
            axis.Stroke = new SolidColorBrush(Co2AxisColor);
            Children.Add(axis);

            // --------
            axis = new Line();
            axis.X1 = 0;
            axis.Y1 = ActualHeight * VerticalLevel; // 330 * 1.0 
            axis.X2 = ActualWidth; // 330
            axis.Y2 = axis.Y1;
            axis.StrokeThickness = 1;
            
            axis.Stroke = new SolidColorBrush(ShowChargeLevel ? HorizontalAxisColor: Co2AxisColor);
            Children.Add(axis);

            if (ShowChargeLevel)
            {
                // |
                // |
                axis = new Line();
                axis.X1 = ActualWidth; // 330
                axis.Y1 = 0; // ActualHeight * VerticalLevel; // 330 * 1.0 
                axis.X2 = ActualWidth; // 330
                axis.Y2 = ActualHeight * VerticalLevel;
                axis.StrokeThickness = 1;
                axis.Stroke = new SolidColorBrush(ChargeAxisColor);
                Children.Add(axis);
            }

            TextBlock minTb = new TextBlock();
            minTb.FontSize = 16;

            TextBlock tb;
            int textY;

            for (double d = co2MinY; d <= co2MaxY; d += co2StepY)
            {
                tb = new TextBlock();
                tb.FontSize = 16;
                tb.Text = String.Format("{0:0}", d);
                Canvas.SetLeft(tb, d == 0 ? -20 : d < 999 ? -40 : -50);
                //Debug.WriteLine(ActualHeight * VerticalLevel - (d * unitHeight) - 10);
                Canvas.SetTop(tb, ActualHeight * VerticalLevel - (d * co2UnitHeight) - 10);
                Children.Add(tb);
            }

            tb = new TextBlock();
            tb.FontSize = 16;
            tb.Text = "ppm";
            textY = (int)(co2MaxY + co2StepY / (co2MaxY / co2StepY)); ///
            Canvas.SetLeft(tb, -45);
            Canvas.SetTop(tb, ActualHeight * VerticalLevel - (co2TextY * co2UnitHeight) - 15);
            Children.Add(tb);

            for (double d = 0; d < 7; d++)
            {
                tb = new TextBlock();
                tb.FontSize = 16;
                tb.Text = String.Format("{0:0}", d);
                Canvas.SetTop(tb, ActualHeight * VerticalLevel + 10);
                Canvas.SetLeft(tb, ActualWidth - d * UnitWidth - 15);
                Children.Add(tb);
            }

            tb = new TextBlock();
            tb.FontSize = 16;
            tb.Text = "時間";
            Canvas.SetTop(tb, ActualHeight * VerticalLevel + 10);
            //Canvas.SetLeft(tb, ActualWidth + 10); // old
            Canvas.SetLeft(tb, -10);
            Children.Add(tb);

            if (ShowChargeLevel)
            {
                tb = new TextBlock();
                tb.FontSize = 16;
                tb.Text = "%";
                Canvas.SetLeft(tb, ActualWidth + 10);
                Canvas.SetTop(tb, ActualHeight * VerticalLevel - (chargeTextY * chargeUnitHeight) - 15);
                Children.Add(tb);

                for (double d = chargeMinY; d <= chargeMaxY; d += chargeStepY)
                {
                    tb = new TextBlock();
                    tb.FontSize = 16;
                    tb.Text = String.Format("{0:0}", d);
                    Canvas.SetLeft(tb, ActualWidth + 10);
                    //Debug.WriteLine(ActualHeight * VerticalLevel - (d * unitHeight) - 10);
                    Canvas.SetTop(tb, ActualHeight * VerticalLevel - (d * chargeUnitHeight) - 10);
                    Children.Add(tb);
                }
            }

            if (co2Points.Count == 0)
            {
                return;
            }

            double valueX;
            double valueX2;
            int i = 0;
            do
            {
                valueX = ((co2Points[i].X - sinceData) % oneDayUnit) / UnitWidth; ////
                i++;
            }
            while (i < co2Points.Count && (valueX > ActualWidth || valueX < 0));

            double co2ValueY = ActualHeight * VerticalLevel - co2Points[0].Y * co2UnitHeight;
            double chargeValueY = ActualHeight * VerticalLevel - chargePoints[0].Y * chargeUnitHeight;

            for (; i < co2Points.Count; i++)
            {
                if (co2Points[i].X >= sinceData)
                {
                    Line line = new Line();
                    if (valueX < 0)
                        valueX = 0;
                    line.X1 = valueX;
                    line.Y1 = co2ValueY;
                    valueX2 = ((co2Points[i].X - sinceData) % oneDayUnit) / UnitWidth;
                    co2ValueY = ActualHeight * VerticalLevel - co2Points[i].Y * co2UnitHeight;
                    line.X2 = valueX2;
                    line.Y2 = co2ValueY;
                    line.StrokeThickness = 3; // 4;
                    line.Stroke = new SolidColorBrush(Co2DotColor);
                    Children.Add(line);

                    if (ShowChargeLevel)
                    {
                        line = new Line();

                        line.X1 = valueX;
                        line.Y1 = chargeValueY;
                        valueX = ((co2Points[i].X - sinceData) % oneDayUnit) / UnitWidth; 
                        
                        chargeValueY = ActualHeight * VerticalLevel - chargePoints[i].Y * chargeUnitHeight;
                        line.X2 = valueX;
                        line.Y2 = chargeValueY;
                        line.StrokeThickness = 3; // 4;
                        line.Stroke = new SolidColorBrush(ChargeDotColor);
                        Children.Add(line);
                    }
                    valueX = valueX2;
                }
            }
        }
    }
}
