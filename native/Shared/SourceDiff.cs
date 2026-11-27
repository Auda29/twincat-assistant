using System;
using System.Collections.Generic;

namespace TwinCatAssistant.Native
{
    internal enum SourceDiffKind { Unchanged, Removed, Added, Modified }

    internal sealed class SourceDiffRow
    {
        internal SourceDiffKind Kind { get; }
        internal int? LeftLineNumber { get; }
        internal int? RightLineNumber { get; }
        internal string LeftText { get; }
        internal string RightText { get; }
        internal int HighlightStart { get; }
        internal int LeftHighlightLength { get; }
        internal int RightHighlightLength { get; }

        internal SourceDiffRow(SourceDiffKind kind, int? left, string leftText, int? right, string rightText)
        {
            Kind = kind;
            LeftLineNumber = left;
            RightLineNumber = right;
            LeftText = leftText;
            RightText = rightText;
            if (kind != SourceDiffKind.Modified)
            {
                if (kind == SourceDiffKind.Removed) LeftHighlightLength = leftText.Length;
                if (kind == SourceDiffKind.Added) RightHighlightLength = rightText.Length;
                return;
            }

            int prefix = 0, suffix = 0;
            int commonLength = Math.Min(leftText.Length, rightText.Length);
            while (prefix < commonLength && leftText[prefix] == rightText[prefix]) prefix++;
            while (suffix < commonLength - prefix
                && leftText[leftText.Length - 1 - suffix] == rightText[rightText.Length - 1 - suffix]) suffix++;
            // Keep UTF-16 surrogate pairs together at either highlight boundary.
            if (prefix > 0 && char.IsHighSurrogate(leftText[prefix - 1])) prefix--;
            if (suffix > 0 && char.IsLowSurrogate(leftText[leftText.Length - suffix])) suffix--;
            HighlightStart = prefix;
            LeftHighlightLength = leftText.Length - prefix - suffix;
            RightHighlightLength = rightText.Length - prefix - suffix;
        }
    }

    internal sealed class SourceDiff
    {
        private const int MaxEditDistance = 512;
        private const int MaxMatchedLineVisits = 2_000_000;
        internal IReadOnlyList<SourceDiffRow> Rows { get; }
        internal IReadOnlyList<int> HunkRowIndices { get; }
        internal int? FirstChangedRowIndex => HunkRowIndices.Count == 0 ? (int?)null : HunkRowIndices[0];
        internal int AddedLines { get; }
        internal int RemovedLines { get; }
        internal int ModifiedLines { get; }

        private SourceDiff(List<SourceDiffRow> rows)
        {
            Rows = rows.AsReadOnly();
            var hunks = new List<int>();
            var inHunk = false;
            for (int i = 0; i < rows.Count; i++)
            {
                var kind = rows[i].Kind;
                if (kind == SourceDiffKind.Added) AddedLines++;
                if (kind == SourceDiffKind.Removed) RemovedLines++;
                if (kind == SourceDiffKind.Modified) ModifiedLines++;
                if (kind != SourceDiffKind.Unchanged && !inHunk) hunks.Add(i);
                inHunk = kind != SourceDiffKind.Unchanged;
            }
            HunkRowIndices = hunks.AsReadOnly();
        }

        internal static SourceDiff Compute(string before, string after)
        {
            var left = Lines(before);
            var right = Lines(after);
            var rows = new List<SourceDiffRow>(Math.Max(left.Length, right.Length));
            int start = 0, leftEnd = left.Length, rightEnd = right.Length;
            while (start < leftEnd && start < rightEnd && left[start] == right[start])
            {
                Align(rows, left, start, start + 1, right, start, start + 1);
                start++;
            }
            while (leftEnd > start && rightEnd > start && left[leftEnd - 1] == right[rightEnd - 1])
            {
                leftEnd--;
                rightEnd--;
            }

            int leftStart = start, rightStart = start;
            // Large unmatched ranges are aligned by position. Matching edges
            // remain visible and every line is retained even on the bounded path.
            if (leftEnd > start && rightEnd > start)
            {
                var matches = FindMatches(left, right, start, leftEnd - start, rightEnd - start);
                if (matches != null)
                {
                    foreach (var (x, y) in matches)
                    {
                        Align(rows, left, leftStart, x, right, rightStart, y);
                        rows.Add(new SourceDiffRow(SourceDiffKind.Unchanged, x + 1, left[x], y + 1, right[y]));
                        leftStart = x + 1;
                        rightStart = y + 1;
                    }
                }
            }
            Align(rows, left, leftStart, leftEnd, right, rightStart, rightEnd);
            Align(rows, left, leftEnd, left.Length, right, rightEnd, right.Length);
            return new SourceDiff(rows);
        }

        // Myers furthest-reaching diagonals. Cap both the edit trace and the
        // work spent following matching lines in highly repetitive sections.
        private static List<(int Left, int Right)> FindMatches(string[] left, string[] right, int start, int n, int m)
        {
            if (Math.Abs(n - m) > MaxEditDistance) return null;
            int limit = Math.Min(n + m, MaxEditDistance);
            int center = limit + 1;
            int remainingVisits = MaxMatchedLineVisits;
            var furthest = new int[center * 2 + 1];
            var trace = new List<int[]>(limit);
            for (int distance = 0; distance <= limit; distance++)
            {
                for (int diagonal = -distance; diagonal <= distance; diagonal += 2)
                {
                    int previous = PreviousDiagonal(furthest, center, distance, diagonal);
                    int x = furthest[center + previous] + (previous < diagonal ? 1 : 0);
                    int y = x - diagonal;
                    while (x < n && y < m && left[start + x] == right[start + y])
                    {
                        if (--remainingVisits == 0) return null;
                        x++;
                        y++;
                    }
                    if (x >= n && y >= m) return BacktrackMatches(trace, center, start, n, m);
                    furthest[center + diagonal] = x;
                }
                if (distance < limit) trace.Add((int[])furthest.Clone());
            }
            return null;
        }

        private static List<(int Left, int Right)> BacktrackMatches(List<int[]> trace, int center, int start, int x, int y)
        {
            var matches = new List<(int, int)>();
            for (int distance = trace.Count; distance > 0; distance--)
            {
                int diagonal = x - y;
                var step = trace[distance - 1];
                int previous = PreviousDiagonal(step, center, distance, diagonal);
                int previousX = step[center + previous];
                int previousY = previousX - previous;
                while (x > previousX && y > previousY) matches.Add((start + --x, start + --y));
                x = previousX;
                y = previousY;
            }
            while (x > 0 && y > 0) matches.Add((start + --x, start + --y));
            matches.Reverse();
            return matches;
        }

        private static int PreviousDiagonal(int[] furthest, int center, int distance, int diagonal)
        {
            return diagonal == -distance || (diagonal != distance && furthest[center + diagonal - 1] < furthest[center + diagonal + 1])
                ? diagonal + 1 : diagonal - 1;
        }

        private static string[] Lines(string value)
        {
            return string.IsNullOrEmpty(value) ? Array.Empty<string>()
                : value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static void Align(List<SourceDiffRow> rows, string[] left, int i, int leftEnd,
            string[] right, int j, int rightEnd)
        {
            while (i < leftEnd && j < rightEnd)
            {
                var kind = left[i] == right[j] ? SourceDiffKind.Unchanged : SourceDiffKind.Modified;
                rows.Add(new SourceDiffRow(kind, i + 1, left[i], j + 1, right[j]));
                i++;
                j++;
            }
            for (; i < leftEnd; i++) rows.Add(new SourceDiffRow(SourceDiffKind.Removed, i + 1, left[i], null, null));
            for (; j < rightEnd; j++) rows.Add(new SourceDiffRow(SourceDiffKind.Added, null, null, j + 1, right[j]));
        }
    }
}
