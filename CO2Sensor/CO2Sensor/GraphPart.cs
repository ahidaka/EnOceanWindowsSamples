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
        public Color HumidDotColor { get; set; }
        public Color TempDotColor { get; set; }
        public Color HumidAxisColor { get; set; }
        public Color HorizontalAxisColor { get; set; }
        public bool ShowHumidLevel { get; set; }

        /// <summary>
        /// 0を真ん中にしたい  ⇒ 0.5
        /// 0を下限にしたい    ⇒ 1.0
        /// 0を上限にしたい    ⇒ 0.0
        /// </summary>
        public double VerticalLevel { get; set; }

        private List<Point> co2Points = new List<Point>();
        private List<Point> humidPoints = new List<Point>();
        private List<Point> tempPoints = new List<Point>();
        private double co2MinY = 0.0;
        private double co2MaxY = 0.0;
        private double co2StepY = 0.0;
        private double humidMinY = 0.0;
        private double humidMaxY = 0.0;
        private double humidStepY = 0.0;
        //private double tempMinY = 0.0;
        //private double tempMaxY = 0.0;
        //private double tempStepY = 0.0;
        private double widthPixels;
        private int co2TextY;
        private int humidTextY;

        public void Initialize()
        {
            Co2DotColor = Colors.DarkGray;
            HumidDotColor = Colors.Cyan;
            TempDotColor = Colors.Magenta;
            Co2AxisColor = Colors.Gray;
            HumidAxisColor = Colors.LightPink;
            HorizontalAxisColor = Colors.SandyBrown;

            ShowHumidLevel = false;

            co2MinY = 0.0;
            co2MaxY = 2500.0;
            co2StepY = 500.0D;
            co2TextY = (int)(co2MaxY + co2StepY / (co2MaxY / co2StepY)); 

            humidMinY = 0.0;
            humidMaxY = 100.0;
            humidStepY = 20.0D;
            humidTextY = (int)(humidMaxY + humidStepY / (humidMaxY / humidStepY)); 

            UnitWidth = 60;
            VerticalLevel = 1.0;
        }

        public void Clear()
        {
            Children.Clear();
            co2Points.Clear();
            humidPoints.Clear();
            tempPoints.Clear();
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

        public void AddHumidPoint(DateTime t, double value)
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
            if (humidPoints.Count > 0)
            {
                if ((p.X - humidPoints[0].X) / UnitWidth >= ActualWidth)
                {
                    humidPoints.RemoveAt(0);
                }
            }
            humidPoints.Add(p);
        }

        public void AddTempPoint(DateTime t, double value)
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
            if (tempPoints.Count > 0)
            {
                if ((p.X - tempPoints[0].X) / UnitWidth >= ActualWidth)
                {
                    tempPoints.RemoveAt(0);
                }
            }
            tempPoints.Add(p);
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
            double humidUnitHeight = ActualHeight / humidMaxY;

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
            
            axis.Stroke = new SolidColorBrush(ShowHumidLevel ? HorizontalAxisColor: Co2AxisColor);
            Children.Add(axis);

            if (ShowHumidLevel)
            {
                // |
                // |
                axis = new Line();
                axis.X1 = ActualWidth; // 330
                axis.Y1 = 0; // ActualHeight * VerticalLevel; // 330 * 1.0 
                axis.X2 = ActualWidth; // 330
                axis.Y2 = ActualHeight * VerticalLevel;
                axis.StrokeThickness = 1;
                axis.Stroke = new SolidColorBrush(HumidAxisColor);
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

            if (ShowHumidLevel)
            {
                tb = new TextBlock();
                tb.FontSize = 16;
                tb.Text = "℃ %";
                Canvas.SetLeft(tb, ActualWidth + 10);
                Canvas.SetTop(tb, ActualHeight * VerticalLevel - (humidTextY * humidUnitHeight) - 15);
                Children.Add(tb);

                for (double d = humidMinY; d <= humidMaxY; d += humidStepY)
                {
                    tb = new TextBlock();
                    tb.FontSize = 16;
                    tb.Text = String.Format("{0:0}", d);
                    Canvas.SetLeft(tb, ActualWidth + 10);
                    //Debug.WriteLine(ActualHeight * VerticalLevel - (d * unitHeight) - 10);
                    Canvas.SetTop(tb, ActualHeight * VerticalLevel - (d * humidUnitHeight) - 10);
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
            double humidValueY = ActualHeight * VerticalLevel - humidPoints[0].Y * humidUnitHeight;
            double tempValueY = ActualHeight * VerticalLevel - tempPoints[0].Y * humidUnitHeight;

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

                    if (ShowHumidLevel)
                    {
                        // Humidity
                        line = new Line();

                        line.X1 = valueX;
                        line.Y1 = humidValueY;
                        //valueX2 = ((humidPoints[i].X - sinceData) % oneDayUnit) / UnitWidth; 
                        
                        humidValueY = ActualHeight * VerticalLevel - humidPoints[i].Y * humidUnitHeight;
                        line.X2 = valueX2;
                        line.Y2 = humidValueY;
                        line.StrokeThickness = 3; // 4;
                        line.Stroke = new SolidColorBrush(HumidDotColor);
                        Children.Add(line);

                        // Temperature
                        line = new Line();
                        line.X1 = valueX;
                        line.Y1 = tempValueY;
                        //valueX2 = ((tempPoints[i].X - sinceData) % oneDayUnit) / UnitWidth;

                        tempValueY = ActualHeight * VerticalLevel - tempPoints[i].Y * humidUnitHeight;
                        line.X2 = valueX2;
                        line.Y2 = tempValueY;
                        line.StrokeThickness = 3; // 4;
                        line.Stroke = new SolidColorBrush(TempDotColor);
                        Children.Add(line);
                    }
                    valueX = valueX2;
                }
            }
        }
    }
}
