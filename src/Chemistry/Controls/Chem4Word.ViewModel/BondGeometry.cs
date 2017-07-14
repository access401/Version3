﻿using Chem4Word.Model.Enums;
using Chem4Word.Model.Geometry;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Chem4Word.View
{
    /// <summary>
    /// Static class to define bond geometries
    /// now uses StreamGeometry in preference to PathGeometry
    /// Old code is commented out
    /// </summary>
    public static class BondGeometry
    {
        public static System.Windows.Media.Geometry WedgeBondGeometry(Point startPoint, Point endPoint)
        {
            Vector bondVector = endPoint - startPoint;
            Vector perpVector = bondVector.Perpendicular();
            perpVector.Normalize();
            perpVector *= Globals.WedgeWidth / 2;
            Point point2 = endPoint + perpVector;
            Point point3 = endPoint - perpVector;

            StreamGeometry sg = new StreamGeometry();

            using (StreamGeometryContext sgc = sg.Open())
            {
                sgc.BeginFigure(startPoint, true, true);
                sgc.LineTo(point2, true, true);
                sgc.LineTo(point3, true, true);
                sgc.Close();
            }
            sg.Freeze();

            return sg;
        }

        /// <summary>
        /// Common to both edge and hatch bonds.  The filling of this shape
        /// is done purely in XAML through styles
        /// </summary>
        /// <param name="startPoint">Point object where the bond starts</param>
        /// <param name="angle">The angle from ScreenNorth:  clockwise +ve, anticlockwise -ve</param>
        /// <param name="bondlength">How long the bond is in pixels</param>
        /// <returns></returns>
        public static System.Windows.Media.Geometry WedgeBondGeometry(Point startPoint, double angle, double bondlength)
        {
            //List<PathSegment> wedgesegments = new List<PathSegment>(4);

            //get a right sized vector first
            Vector bondVector = BasicGeometry.ScreenNorth();
            bondVector.Normalize();
            bondVector = bondVector * bondlength;

            //then rotate it to the proper angle
            Matrix rotator = new Matrix();
            rotator.Rotate(angle);
            bondVector = bondVector * rotator;

            //then work out the points at the thick end of the wedge
            var perpVector = bondVector.Perpendicular();
            perpVector.Normalize();
            perpVector = perpVector * Globals.WedgeWidth / 2;

            Point point2 = startPoint + bondVector + perpVector;
            Point point3 = startPoint + bondVector - perpVector;
            //and draw it
            StreamGeometry sg = new StreamGeometry();

            using (StreamGeometryContext sgc = sg.Open())
            {
                sgc.BeginFigure(startPoint, true, true);
                sgc.LineTo(point2, true, true);
                sgc.LineTo(point3, true, true);
                sgc.Close();
            }
            sg.Freeze();

            return sg;
        }

        /// <summary>
        /// Defines the three parallel lines of a Triple bond.
        /// </summary>
        /// <param name="startPoint">Where the bond starts</param>
        /// <param name="endPoint">Where it ends</param>
        /// <param name="enclosingPoly"></param>
        /// <returns></returns>
        public static System.Windows.Media.Geometry TripleBondGeometry(Point startPoint, Point endPoint, ref List<Point> enclosingPoly)
        {
            Vector v = endPoint - startPoint;
            Vector normal = v.Perpendicular();
            normal.Normalize();

            double distance = Globals.Offset;
            Point point1 = startPoint + normal * distance;
            Point point2 = point1 + v;

            Point point3 = startPoint - normal * distance;
            Point point4 = point3 + v;

            enclosingPoly = new List<Point>() { point1, point2, point4, point3 };

            StreamGeometry sg = new StreamGeometry();
            using (StreamGeometryContext sgc = sg.Open())
            {
                sgc.BeginFigure(startPoint, false, false);
                sgc.LineTo(endPoint, true, false);
                sgc.BeginFigure(point1, false, false);
                sgc.LineTo(point2, true, false);
                sgc.BeginFigure(point3, false, false);
                sgc.LineTo(point4, true, false);
                sgc.Close();
            }
            sg.Freeze();

            return sg;
        }

        /// <summary>
        /// draws the two parallel lines of a double bond
        /// These bonds can either straddle the atom-atom line or fall to one or other side of it
        /// </summary>
        /// <param name="startPoint"></param>
        /// <param name="endPoint"></param>
        /// <param name="doubleBondPlacement"></param>
        /// <param name="ringCentroid"></param>
        /// <param name="enclosingPoly"></param>
        /// <returns></returns>
        public static System.Windows.Media.Geometry DoubleBondGeometry(Point startPoint, Point endPoint,
            BondDirection doubleBondPlacement, ref List<Point> enclosingPoly, Point? ringCentroid = null)
        //this routine is very complex because iof the essentially asymmetric nature of these bonds
        {
            Vector v = endPoint - startPoint;
            Vector normal = v.Perpendicular();
            normal.Normalize();

            Point point1, point2, point3, point4;
            Point? point3a, point4a;

            double distance = Globals.Offset;
            switch (doubleBondPlacement)
            {
                case BondDirection.None:

                    point1 = startPoint + normal * distance;
                    point2 = point1 + v;

                    point3 = startPoint - normal * distance;
                    point4 = point3 + v;

                    break;

                case BondDirection.Clockwise:
                    {
                        point1 = startPoint;

                        point2 = endPoint;
                        point3 = startPoint - normal * 2 * distance;
                        point4 = point3 + v;

                        break;
                    }

                case BondDirection.Anticlockwise:
                    point1 = startPoint;
                    point2 = endPoint;
                    point3 = startPoint + normal * 2 * distance;
                    point4 = point3 + v;
                    break;

                default:

                    point1 = startPoint + normal * distance;
                    point2 = point1 + v;

                    point3 = startPoint - normal * distance;
                    point4 = point3 + v;
                    break;
            }

            //capture  the enclosing polygone for hit testing later

            enclosingPoly = new List<Point>() { point1, point2, point4, point3 };

            //shorten the supporting bond if it's a ring bond
            if (ringCentroid != null)
            {
                point3a = BasicGeometry.LineSegmentsIntersect(startPoint, ringCentroid.Value, point3, point4);

                var temp_point3 = point3a ?? point3;

                point4a = BasicGeometry.LineSegmentsIntersect(endPoint, ringCentroid.Value, point3, point4);

                var temp_point4 = point4 = point4a ?? point4;

                point3 = temp_point3;
                point4 = temp_point4;
            }

          ;

            StreamGeometry sg = new StreamGeometry();
            using (StreamGeometryContext sgc = sg.Open())
            {
                sgc.BeginFigure(point1, false, false);
                sgc.LineTo(point2, true, false);
                sgc.BeginFigure(point3, false, false);
                sgc.LineTo(point4, true, false);
                sgc.Close();
            }
            sg.Freeze();
            return sg;
        }

        public static System.Windows.Media.Geometry SingleBondGeometry(Point startPoint, Point endPoint)
        {
            StreamGeometry sg = new StreamGeometry();
            using (StreamGeometryContext sgc = sg.Open())
            {
                sgc.BeginFigure(startPoint, false, false);
                sgc.LineTo(endPoint, true, false);
                sgc.Close();
            }
            sg.Freeze();
            return sg;
        }

        private static List<PathFigure> GetSingleBondSegment(Point startPoint, Point endPoint)
        {
            List<PathSegment> segments = new List<PathSegment> { new LineSegment(endPoint, false) };

            List<PathFigure> figures = new List<PathFigure>();
            PathFigure pf = new PathFigure(startPoint, segments, true);
            figures.Add(pf);
            return figures;
        }
    }
}