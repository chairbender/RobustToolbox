﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.Maths;

namespace Robust.Client.UserInterface.Controls
{
    /// <summary>
    ///     A container that lays out its children in a grid. Can define specific amount of
    ///     rows or specific amount of columns (not both), and will grow to fill in additional rows/columns within
    ///     that limit.
    /// </summary>
    public class GridContainer : Container
    {
        // limit - depending on mode, this is either rows or columns
        private int _limitedDimensionAmount = 1;

        private Dimension _limitDimension = Dimension.Column;

        /// <summary>
        /// Indicates whether row or column amount has been specified, and thus
        /// how items will fill them out as they are added.
        /// This is set depending on whether you have specified Columns or Rows.
        /// </summary>
        public Dimension LimitedDimension => _limitDimension;
        /// <summary>
        /// Opposite dimension of LimitedDimension
        /// </summary>
        public Dimension UnlimitedDimension => _limitDimension == Dimension.Column ? Dimension.Row : Dimension.Column;
        
        /// <summary>
        /// The "normal" direction of expansion when the defined row or column limit is met
        /// is right (for row-limited) and down (for column-limited),
        /// this inverts that so the container expands in the opposite direction as elements are added.
        /// </summary>
        public bool ExpandBackwards
        {
            get => _expandBackwards;
            set
            {
                _expandBackwards = value;
                UpdateLayout();
            }
        }

        /// <summary>
        ///     The amount of columns to organize the children into. Setting this puts this grid
        ///     into LimitMode.LimitColumns - items will be added to fill up the entire row, up to the defined
        ///     limit of columns, and then create a second row.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if the value assigned is less than or equal to 0.
        /// </exception>
        /// <returns>specified limit if LimitMode.LimitColums, otherwise the amount
        /// of columns being used for the current amount of children.</returns>
        public int Columns
        {
            get => GetAmount(Dimension.Column);
            set => SetAmount(Dimension.Column, value);
        }

        /// <summary>
        ///     The amount of rows to organize the children into. Setting this puts this grid
        ///     into LimitMode.LimitRows - items will be added to fill up the entire column, up to the defined
        ///     limit of rows, and then create a second column.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if the value assigned is less than or equal to 0.
        /// </exception>
        /// <returns>specified limit if LimitMode.LimitRows, otherwise the amount
        /// of rows being used for the current amount of children.</returns>
        public int Rows
        {
            get => GetAmount(Dimension.Row);
            set => SetAmount(Dimension.Row, value);
        }

        private int? _vSeparationOverride;

        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        public int? VSeparationOverride
        {
            get => _vSeparationOverride;
            set => _vSeparationOverride = value;
        }

        private int? _hSeparationOverride;
        private bool _expandBackwards;

        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        public int? HSeparationOverride
        {
            get => _hSeparationOverride;
            set => _hSeparationOverride = value;
        }

        private Vector2i Separations => (_hSeparationOverride ?? 4, _vSeparationOverride ?? 4);

        private int GetAmount(Dimension forDimension)
        {
            if (forDimension == _limitDimension) return _limitedDimensionAmount;
            if (ChildCount == 0)
            {
                return 1;
            }

            var divisor = (_limitDimension == Dimension.Column ? Columns : Rows);
            var div = ChildCount / divisor;
            if (ChildCount % divisor != 0)
            {
                div += 1;
            }

            return div;
        }

        private void SetAmount(Dimension forDimension, int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be greater than zero.");
            }

            _limitDimension = forDimension;

            _limitedDimensionAmount = value;
            MinimumSizeChanged();
            UpdateLayout();
        }

        protected override Vector2 CalculateMinimumSize()
        {
            // to make it easier to read and visualize, we're just going to use the terms "x" and "y", width, and height,
            // rows and cols,
            // but at the start of the method here we'll set those to what they actually are based
            // on the limited dimension, which might involve swapping them.
            // For the below convention, we pretend that columns have a limit defined, thus
            // the amount of rows is not limited (unlimited).

            var rows = GetAmount(UnlimitedDimension);
            var cols = _limitedDimensionAmount;

            Span<int> minColWidth = stackalloc int[cols];
            Span<int> minRowHeight = stackalloc int[rows];

            var index = 0;
            foreach (var child in Children)
            {
                if (!child.Visible)
                {
                    index--;
                    continue;
                }

                var column = index % cols;
                var row = index / cols;

                // also converting here to our "pretend" scenario where columns have a limit defined
                var (minSizeXActual, minSizeYActual) = child.CombinedPixelMinimumSize;
                var minSizeX = _limitDimension == Dimension.Column ? minSizeXActual : minSizeYActual;
                var minSizeY = _limitDimension == Dimension.Column ? minSizeYActual : minSizeXActual;
                minColWidth[column] = Math.Max(minSizeX, minColWidth[column]);
                minRowHeight[row] = Math.Max(minSizeY, minRowHeight[row]);

                index += 1;
            }

            // converting here to our "pretend" scenario where columns have a limit defined
            var (wSepActual, hSepActual) = (Vector2i) (Separations * UIScale);
            var wSep = _limitDimension == Dimension.Column ? wSepActual : hSepActual;
            var hSep = _limitDimension == Dimension.Column ? hSepActual : wSepActual;
            var minWidth = AccumSizes(minColWidth, wSep);
            var minHeight = AccumSizes(minRowHeight, hSep);

            // converting back from our pretend scenario where columns are limited
            return new Vector2(
                _limitDimension == Dimension.Column ? minWidth : minHeight,
                _limitDimension == Dimension.Column ? minHeight : minWidth) / UIScale;
        }

        private static int AccumSizes(Span<int> sizes, int separator)
        {
            var totalSize = 0;
            var first = true;

            foreach (var size in sizes)
            {
                totalSize += size;

                if (first)
                {
                    first = false;
                }
                else
                {
                    totalSize += separator;
                }
            }

            return totalSize;
        }

        protected override void LayoutUpdateOverride()
        {
            // to make it easier to read and visualize, we're just going to use the terms "x" and "y", width, and height,
            // rows and cols,
            // but at the start of the method here we'll set those to what they actually are based
            // on the limited dimension, which might involve swapping them.
            // For the below convention, we pretend that columns have a limit defined, thus
            // the amount of rows is not limited (unlimited).

            var rows = GetAmount(UnlimitedDimension);
            var cols = _limitedDimensionAmount;

            Span<int> minColWidth = stackalloc int[cols];
            // Minimum lateral size of the unlimited dimension
            // (i.e. width of columns, height of rows).
            Span<int> minRowHeight = stackalloc int[rows];
            // columns that are set to expand vertically
            Span<bool> colExpand = stackalloc bool[cols];
            // rows that are set to expand horizontally
            Span<bool> rowExpand = stackalloc bool[rows];

            // Get minSize and size flag expand of each column and row.
            // All we need to apply the same logic BoxContainer does.
            var index = 0;
            foreach (var child in Children)
            {
                if (!child.Visible)
                {
                    continue;
                }

                var column = index % cols;
                var row = index / cols;

                // converting here to our "pretend" scenario where columns have a limit defined
                var (minSizeXActual, minSizeYActual) = child.CombinedPixelMinimumSize;
                var minSizeX = _limitDimension == Dimension.Column ? minSizeXActual : minSizeYActual;
                var minSizeY = _limitDimension == Dimension.Column ? minSizeYActual : minSizeXActual;
                minColWidth[column] = Math.Max(minSizeX, minColWidth[column]);
                minRowHeight[row] = Math.Max(minSizeY, minRowHeight[row]);
                var colSizeFlag = _limitDimension == Dimension.Column
                    ? child.SizeFlagsHorizontal
                    : child.SizeFlagsVertical;
                var rowSizeFlag = UnlimitedDimension == Dimension.Column
                    ? child.SizeFlagsHorizontal
                    : child.SizeFlagsVertical;
                colExpand[column] = colExpand[column] || (colSizeFlag & SizeFlags.Expand) != 0;
                rowExpand[row] = rowExpand[row] || (rowSizeFlag & SizeFlags.Expand) != 0;

                index += 1;
            }

            // Basically now we just apply BoxContainer logic on rows and columns.
            var stretchMinX = 0;
            var stretchMinY = 0;
            // We do not use stretch ratios because Godot doesn't,
            // which makes sense since what happens if two things on the same column have a different stretch ratio?
            // Maybe there's an answer for that but I'm too lazy to think of a concrete solution
            // and it would make this code more complex so...
            // pass.
            var stretchCountX = 0;
            var stretchCountY = 0;

            for (var i = 0; i < minColWidth.Length; i++)
            {
                if (!colExpand[i])
                {
                    stretchMinX += minColWidth[i];
                }
                else
                {
                    stretchCountX++;
                }
            }

            for (var i = 0; i < minRowHeight.Length; i++)
            {
                if (!rowExpand[i])
                {
                    stretchMinY += minRowHeight[i];
                }
                else
                {
                    stretchCountY++;
                }
            }

            // converting here to our "pretend" scenario where columns have a limit defined
            var (vSepActual, hSepActual) = (Vector2i) (Separations * UIScale);
            var hSep = _limitDimension == Dimension.Column ? hSepActual : vSepActual;
            var vSep = _limitDimension == Dimension.Column ? vSepActual : hSepActual;
            var width = _limitDimension == Dimension.Column ? Width : Height;
            var height = _limitDimension == Dimension.Column ? Height : Width;

            var stretchMaxX = width - hSep * (cols - 1);
            var stretchMaxY = height - vSep * (rows - 1);

            var stretchAvailX = Math.Max(0, stretchMaxX - stretchMinX);
            var stretchAvailY = Math.Max(0, stretchMaxY - stretchMinY);

            for (var i = 0; i < minColWidth.Length; i++)
            {
                if (!colExpand[i])
                {
                    continue;
                }

                minColWidth[i] = (int) (stretchAvailX / stretchCountX);
            }

            for (var i = 0; i < minRowHeight.Length; i++)
            {
                if (!rowExpand[i])
                {
                    continue;
                }

                minRowHeight[i] = (int) (stretchAvailY / stretchCountY);
            }

            // Actually lay them out.
            // if inverted, (in our pretend "columns are limited" scenario) we must calculate the final
            // height (as height will vary depending on number of elements), and then
            // go backwards, starting from the bottom and filling elements in upwards
            var finalVOffset = 0;
            if (ExpandBackwards)
            {
                // we have to iterate through the elements first to determine the height each
                // row will end up having, as they can vary
                index = 0;
                for (var i = 0; i < ChildCount; i++, index++)
                {
                    var child = GetChild(i);
                    if (!child.Visible)
                    {
                        index--;
                        continue;
                    }

                    var column = index % cols;
                    var row = index / cols;

                    if (column == 0)
                    {
                        // Just started a new row/col.
                        if (row != 0)
                        {
                            finalVOffset += vSep + minRowHeight[row - 1];
                        }
                    }
                }
            }

            var hOffset = 0;
            var vOffset = ExpandBackwards ? finalVOffset : 0;
            index = 0;
            for (var i = 0; i < ChildCount; i++, index++)
            {
                var child = GetChild(i);
                if (!child.Visible)
                {
                    index--;
                    continue;
                }

                var column = index % cols;
                var row = index / cols;

                if (column == 0)
                {
                    // Just started a new row
                    hOffset = 0;
                    if (row != 0)
                    {
                        if (ExpandBackwards)
                        {
                            // every time we start a new row we actually decrease the voffset, we are filling
                            // in the up direction
                            vOffset -= vSep + minRowHeight[row - 1];
                        }
                        else
                        {
                            vOffset += vSep + minRowHeight[row - 1];
                        }

                    }
                }

                // converting back from our "pretend" scenario
                var left = _limitDimension == Dimension.Column ? hOffset : vOffset;
                var top = _limitDimension == Dimension.Column ? vOffset : hOffset;
                var boxWidth = _limitDimension == Dimension.Column ? minColWidth[column] : minRowHeight[row];
                var boxHeight = _limitDimension == Dimension.Column ? minRowHeight[row] : minColWidth[column];

                var box = UIBox2i.FromDimensions(left, top, boxWidth, boxHeight);
                FitChildInPixelBox(child, box);

                hOffset += minColWidth[column] + hSep;
            }
        }
    }

    public enum Dimension
    {
        Column,
        Row
    }
}
