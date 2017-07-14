﻿using Chem4Word.Model;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Chem4Word.View
{
    public class AtomGeometry
    {
        private System.Windows.Media.Geometry GetSymbolGeometry(out System.Windows.Media.Geometry definingGeometry, Atom parentAtom)
        {
            FormattedText formattedText = new FormattedText(
                parentAtom.SymbolText,
                CultureInfo.GetCultureInfo("en-us"),
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                24,
                Brushes.Black);
            //offset the text by half its width and height
            double xOffset = 0.0, yOffset = 0.0;
            xOffset = formattedText.Width / 2;
            yOffset = formattedText.Height / 2;
            Point startingPoint = new Point(parentAtom.Position.X - xOffset, parentAtom.Position.Y - yOffset);
            System.Windows.Media.Geometry textGeometry = formattedText.BuildGeometry(startingPoint);

            definingGeometry = textGeometry;
            return textGeometry;
        }
    }
}