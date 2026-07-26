using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Arranges children in a grid with configurable rows and columns.
    /// Track sizing: Fixed (pixels) → Auto (content) → Star (proportional).
    /// </summary>
    public class GridLayout : Widget
    {
        private readonly List<RowDefinition> _rowDefs = new();
        private readonly List<ColumnDefinition> _colDefs = new();

        // Cached track sizes from the most recent Measure pass (reused arrays)
        private float[] _rowHeights = Array.Empty<float>();
        private float[] _colWidths = Array.Empty<float>();

        public IReadOnlyList<RowDefinition> RowDefinitions => _rowDefs;
        public IReadOnlyList<ColumnDefinition> ColumnDefinitions => _colDefs;

        public void AddRow(RowDefinition row)
        {
            _rowDefs.Add(row);
            InvalidateMeasure();
        }

        public void AddColumn(ColumnDefinition col)
        {
            _colDefs.Add(col);
            InvalidateMeasure();
        }

        public void ClearRows()
        {
            _rowDefs.Clear();
            InvalidateMeasure();
        }

        public void ClearColumns()
        {
            _colDefs.Clear();
            InvalidateMeasure();
        }

        // ── Attached Properties ──────────────────────────────────────

        public const string PropRow = "Grid.Row";
        public const string PropColumn = "Grid.Column";
        public const string PropRowSpan = "Grid.RowSpan";
        public const string PropColumnSpan = "Grid.ColumnSpan";

        public static int GetRow(Widget w) => w.GetAttached<int>(PropRow);
        public static void SetRow(Widget w, int value) => w.SetAttached(PropRow, value);
        public static int GetColumn(Widget w) => w.GetAttached<int>(PropColumn);
        public static void SetColumn(Widget w, int value) => w.SetAttached(PropColumn, value);
        public static int GetRowSpan(Widget w) => Math.Max(1, w.GetAttached<int>(PropRowSpan));
        public static void SetRowSpan(Widget w, int value) => w.SetAttached(PropRowSpan, value);
        public static int GetColumnSpan(Widget w) => Math.Max(1, w.GetAttached<int>(PropColumnSpan));
        public static void SetColumnSpan(Widget w, int value) => w.SetAttached(PropColumnSpan, value);

        // ── Measure ──────────────────────────────────────────────────

        protected override Vector2 OnMeasure(Vector2 available)
        {
            int rowCount = Math.Max(1, _rowDefs.Count);
            int colCount = Math.Max(1, _colDefs.Count);

            EnsureTrackArrays(rowCount, colCount);

            // Compute available content size
            float availW = available.X;
            float availH = available.Y;

            // Step 1 & 2: Resolve track sizes
            ComputeTrackSizes(availW, availH, rowCount, colCount);

            // Step 3: Measure children with computed cell sizes
            MeasureChildren();

            // Desired size = sum of all track sizes
            float totalW = 0, totalH = 0;
            for (int c = 0; c < colCount; c++) totalW += _colWidths[c];
            for (int r = 0; r < rowCount; r++) totalH += _rowHeights[r];

            return new Vector2(totalW, totalH);
        }

        private void EnsureTrackArrays(int rows, int cols)
        {
            if (_rowHeights.Length != rows) _rowHeights = new float[rows];
            if (_colWidths.Length != cols) _colWidths = new float[cols];
        }

        /// <summary>
        /// Three-pass track sizing: Fixed → Auto → Star.
        /// </summary>
        private void ComputeTrackSizes(float availW, float availH,
            int rowCount, int colCount)
        {
            // Initialize: Fixed tracks get their value, others start at 0
            for (int c = 0; c < colCount; c++)
            {
                var def = GetColDef(c);
                _colWidths[c] = def.Width.IsFixed ? Math.Max(0, def.Width.Value) : 0;
            }
            for (int r = 0; r < rowCount; r++)
            {
                var def = GetRowDef(r);
                _rowHeights[r] = def.Height.IsFixed ? Math.Max(0, def.Height.Value) : 0;
            }

            // ── Auto tracks: measure non-spanning children to determine size ──
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                int col = GetColumn(child);
                int row = GetRow(child);
                int colSpan = GetColumnSpan(child);
                int rowSpan = GetRowSpan(child);

                // Clamp to valid range
                col = Math.Clamp(col, 0, colCount - 1);
                row = Math.Clamp(row, 0, rowCount - 1);
                colSpan = Math.Clamp(colSpan, 1, colCount - col);
                rowSpan = Math.Clamp(rowSpan, 1, rowCount - row);

                // Determine available for this child based on track types
                float childAvailW = GetColAvailable(col, colSpan, availW);
                float childAvailH = GetRowAvailable(row, rowSpan, availH);

                child.Measure(new Vector2(childAvailW, childAvailH));
                var ds = child.DesiredSize;

                // Update Auto tracks (only for non-spanning)
                if (colSpan == 1 && GetColDef(col).Width.IsAuto)
                    _colWidths[col] = MathF.Max(_colWidths[col], ds.X);
                if (rowSpan == 1 && GetRowDef(row).Height.IsAuto)
                    _rowHeights[row] = MathF.Max(_rowHeights[row], ds.Y);
            }

            // Clamp to Min/Max
            for (int c = 0; c < colCount; c++)
            {
                var def = GetColDef(c);
                if (!def.Width.IsFixed)
                    _colWidths[c] = Math.Clamp(_colWidths[c], def.MinWidth, def.MaxWidth);
            }
            for (int r = 0; r < rowCount; r++)
            {
                var def = GetRowDef(r);
                if (!def.Height.IsFixed)
                    _rowHeights[r] = Math.Clamp(_rowHeights[r], def.MinHeight, def.MaxHeight);
            }

            // ── Star tracks ──
            DistributeStarTracks(colCount, _colWidths, availW, isCol: true);
            DistributeStarTracks(rowCount, _rowHeights, availH, isCol: false);
        }

        // Safe accessors: return Auto default when no definitions exist
        private ColumnDefinition GetColDef(int index) =>
            index < _colDefs.Count ? _colDefs[index] : new ColumnDefinition(GridLength.Auto);

        private RowDefinition GetRowDef(int index) =>
            index < _rowDefs.Count ? _rowDefs[index] : new RowDefinition(GridLength.Auto);

        private float GetColAvailable(int start, int span, float containerAvail)
        {
            float total = 0;
            bool hasFlex = false;
            for (int i = start; i < start + span; i++)
            {
                var def = GetColDef(i);
                if (def.Width.IsFixed) total += def.Width.Value;
                else hasFlex = true;
            }
            return hasFlex ? float.PositiveInfinity : total;
        }

        private float GetRowAvailable(int start, int span, float containerAvail)
        {
            float total = 0;
            bool hasFlex = false;
            for (int i = start; i < start + span; i++)
            {
                var def = GetRowDef(i);
                if (def.Height.IsFixed) total += def.Height.Value;
                else hasFlex = true;
            }
            return hasFlex ? float.PositiveInfinity : total;
        }

        private void DistributeStarTracks(int count, float[] sizes,
            float containerAvail, bool isCol)
        {
            float fixedSum = 0, starWeightSum = 0;
            for (int i = 0; i < count; i++)
            {
                if (isCol)
                {
                    var d = GetColDef(i);
                    if (d.Width.IsStar) starWeightSum += d.Width.Value;
                    else fixedSum += sizes[i];
                }
                else
                {
                    var d = GetRowDef(i);
                    if (d.Height.IsStar) starWeightSum += d.Height.Value;
                    else fixedSum += sizes[i];
                }
            }

            if (starWeightSum <= 0) return;

            float remaining = containerAvail - fixedSum;
            if (float.IsInfinity(remaining) || remaining <= 0) return;

            float allocated = 0;
            int lastStarIdx = -1;
            for (int i = 0; i < count; i++)
            {
                float weight, min, max;
                if (isCol)
                {
                    var d = GetColDef(i);
                    if (!d.Width.IsStar) continue;
                    weight = d.Width.Value;
                    min = d.MinWidth;
                    max = d.MaxWidth;
                }
                else
                {
                    var d = GetRowDef(i);
                    if (!d.Height.IsStar) continue;
                    weight = d.Height.Value;
                    min = d.MinHeight;
                    max = d.MaxHeight;
                }

                float share = remaining * weight / starWeightSum;
                share = Math.Clamp(share, min, max);
                sizes[i] = share;
                allocated += share;
                lastStarIdx = i;
            }

            // Give rounding remainder to the last Star track
            float remainder = remaining - allocated;
            if (MathF.Abs(remainder) > 0.5f && lastStarIdx >= 0)
            {
                sizes[lastStarIdx] += remainder;
            }
        }

        private void MeasureChildren()
        {
            int colCount = Math.Max(1, _colDefs.Count);
            int rowCount = Math.Max(1, _rowDefs.Count);

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;

                int col = Math.Clamp(GetColumn(child), 0, colCount - 1);
                int row = Math.Clamp(GetRow(child), 0, rowCount - 1);
                int colSpan = Math.Clamp(GetColumnSpan(child), 1, colCount - col);
                int rowSpan = Math.Clamp(GetRowSpan(child), 1, rowCount - row);

                float cellW = 0;
                for (int c = col; c < col + colSpan; c++) cellW += _colWidths[c];
                float cellH = 0;
                for (int r = row; r < row + rowSpan; r++) cellH += _rowHeights[r];

                child.Measure(new Vector2(cellW, cellH));
            }
        }

        // ── Arrange ───────────────────────────────────────────────────

        protected override void OnArrange(Rectangle content)
        {
            int rowCount = Math.Max(1, _rowDefs.Count);
            int colCount = Math.Max(1, _colDefs.Count);

            // Recompute track sizes for the actual allocated space (Star tracks may resize)
            ComputeTrackSizes(content.Width, content.Height, rowCount, colCount);
            // Re-measure so children get correct available based on final track sizes
            MeasureChildren();

            // Compute row offsets
            float[] rowOffsets = new float[rowCount];
            float y = content.Y;
            for (int r = 0; r < rowCount; r++)
            {
                rowOffsets[r] = y;
                y += _rowHeights[r];
            }

            // Compute column offsets
            float[] colOffsets = new float[colCount];
            float x = content.X;
            for (int c = 0; c < colCount; c++)
            {
                colOffsets[c] = x;
                x += _colWidths[c];
            }

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;

                int col = Math.Clamp(GetColumn(child), 0, colCount - 1);
                int row = Math.Clamp(GetRow(child), 0, rowCount - 1);
                int colSpan = Math.Clamp(GetColumnSpan(child), 1, colCount - col);
                int rowSpan = Math.Clamp(GetRowSpan(child), 1, rowCount - row);

                float cellW = 0;
                for (int c = col; c < col + colSpan; c++) cellW += _colWidths[c];
                float cellH = 0;
                for (int r = row; r < row + rowSpan; r++) cellH += _rowHeights[r];

                var cellRect = new Rectangle(
                    (int)colOffsets[col], (int)rowOffsets[row],
                    (int)cellW, (int)cellH);

                child.Arrange(cellRect);
            }
        }
    }
}
