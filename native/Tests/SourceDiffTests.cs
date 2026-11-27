using System.Diagnostics;
using TwinCatAssistant.Native;

internal static class SourceDiffTests
{
    internal static int Run()
    {
        int passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++;
            Console.WriteLine("PASS " + name);
        }

        var empty = SourceDiff.Compute("", "");
        Check(empty.Rows.Count == 0 && empty.FirstChangedRowIndex == null, "empty diff");
        var identical = SourceDiff.Compute("A\r\nB\nC", "A\nB\r\nC");
        Check(identical.Rows.Count == 3 && identical.Rows.All(row => row.Kind == SourceDiffKind.Unchanged)
            && identical.HunkRowIndices.Count == 0, "LF and CRLF match");
        var newline = SourceDiff.Compute("END_IF", "END_IF\r\n");
        Check(newline.AddedLines == 1 && newline.Rows[1].RightText == "" && newline.Rows[1].LeftLineNumber == null
            && newline.FirstChangedRowIndex == 1, "trailing newline difference");
        var inserted = SourceDiff.Compute("", "X");
        var removed = SourceDiff.Compute("X", "");
        Check(inserted.AddedLines == 1 && inserted.Rows[0].RightLineNumber == 1 && inserted.Rows[0].RightHighlightLength == 1
            && removed.RemovedLines == 1 && removed.Rows[0].LeftLineNumber == 1 && removed.Rows[0].LeftHighlightLength == 1,
            "one-sided source and highlights");

        var diff = SourceDiff.Compute("VAR\nx := 1;\nEND_VAR\nkeep\nremove\nlast",
            "VAR\nx := 200;\ny := 3;\nEND_VAR\nkeep\nlast");
        var modified = diff.Rows[1];
        Check(diff.AddedLines == 1 && diff.RemovedLines == 1 && diff.ModifiedLines == 1
            && diff.HunkRowIndices.SequenceEqual(new[] { 1, 5 }), "hunks separate changes across unchanged rows");
        Check(modified.HighlightStart == 5 && modified.LeftHighlightLength == 1 && modified.RightHighlightLength == 3
            && diff.Rows[2].LeftLineNumber == null && diff.Rows[3].LeftLineNumber == 3 && diff.Rows[3].RightLineNumber == 4
            && diff.Rows[5].RightLineNumber == null, "aligned lines and exact inline highlights");
        var unicode = SourceDiff.Compute("(* 😀 *)", "(* 😁 *)").Rows[0];
        Check(unicode.HighlightStart == 3 && unicode.LeftHighlightLength == 2 && unicode.RightHighlightLength == 2,
            "inline highlight keeps surrogate pairs together");
        var inlineInsertion = SourceDiff.Compute("x := 1;", "x := 10;").Rows[0];
        Check(inlineInsertion.HighlightStart == 6 && inlineInsertion.LeftHighlightLength == 0 && inlineInsertion.RightHighlightLength == 1,
            "inline insertion preserves common prefix and suffix");
        var reordering = SourceDiff.Compute("a\nb\na\nc", "a\na\nb\nc");
        Check(reordering.Rows.Select(row => row.Kind).SequenceEqual(new[] { SourceDiffKind.Unchanged,
            SourceDiffKind.Removed, SourceDiffKind.Unchanged, SourceDiffKind.Added, SourceDiffKind.Unchanged }),
            "repeated line alignment and tie breaking are deterministic");
        Check(SmallInputsAlign(), "repeated short inputs retain all lines and find a shortest edit path");

        string oldSource = string.Join("\n", Enumerable.Repeat("OutputA := Input; ", 3600));
        string newSource = string.Join("\n", Enumerable.Repeat("OutputB := Input; ", 3600));
        var timer = Stopwatch.StartNew();
        var large = SourceDiff.Compute(oldSource, newSource);
        Check(large.ModifiedLines == 3600 && large.HunkRowIndices.SequenceEqual(new[] { 0 })
            && timer.Elapsed < TimeSpan.FromSeconds(2), "large high-distance ST diff is bounded");
        var lines = Enumerable.Range(0, 3000).Select(i => $"A[{i}] := 1;").ToArray();
        var sparse = SourceDiff.Compute(string.Join("\n", lines),
            string.Join("\n", lines.Take(500).Append("B := 1;").Concat(lines.Skip(500).Take(2000)).Concat(lines.Skip(2501))));
        Check(sparse.AddedLines == 1 && sparse.RemovedLines == 1 && sparse.ModifiedLines == 0
            && sparse.HunkRowIndices.SequenceEqual(new[] { 500, 2501 }), "large sparse ST diff aligns distant edits");
        return passed;
    }

    private static bool SmallInputsAlign()
    {
        var inputs = Enumerable.Range(1, 63).Select(bits => Convert.ToString(bits, 2).Skip(1)
            .Select(bit => bit.ToString()).ToArray()).ToArray();
        foreach (var left in inputs)
            foreach (var right in inputs)
            {
                var diff = SourceDiff.Compute(string.Join("\n", left), string.Join("\n", right));
                var common = new int[left.Length + 1, right.Length + 1];
                for (int i = 0; i < left.Length; i++)
                    for (int j = 0; j < right.Length; j++)
                        common[i + 1, j + 1] = left[i] == right[j] ? common[i, j] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
                if (diff.Rows.Count(row => row.Kind == SourceDiffKind.Unchanged) != common[left.Length, right.Length]
                    || !diff.Rows.Where(row => row.LeftLineNumber.HasValue).Select(row => row.LeftText).SequenceEqual(left)
                    || !diff.Rows.Where(row => row.RightLineNumber.HasValue).Select(row => row.RightText).SequenceEqual(right)) return false;
            }
        return true;
    }
}
