using System.Text;
using System.Text.RegularExpressions;

namespace OracleSqlPortal.Services
{
    /// <summary>
    /// Parses Oracle SQL*Plus migration scripts into individually executable statements.
    ///
    /// Rules:
    ///  - Strip  /* ... */  block comments (dotall, non-greedy)
    ///  - Strip  -- ...     line comments
    ///  - Skip   SET SCAN/DEFINE/SERVEROUTPUT/ECHO/VERIFY/FEEDBACK/... directives (with or without trailing ;)
    ///  - Skip   PROMPT / DEFINE / WHENEVER / @include / SELECT SYSDATE lines
    ///  - Skip   REM comment lines
    ///  - Skip   bare "/" lines (SQL*Plus block terminator)
    ///
    /// Splitting:
    ///  - PL/SQL blocks (DECLARE/BEGIN/CREATE PROCEDURE etc.) are kept whole,
    ///    terminated by a bare "/" line. The block is sent as-is to Oracle.
    ///  - Regular DML/DDL statements are split on ";".
    ///
    /// Fixes applied:
    ///  1. SetDirectiveRe now matches trailing ";" so "SET SCAN OFF;" is fully consumed.
    ///  2. Regular SQL collector stops before consuming a PL/SQL block-start line.
    ///  3. Segments produced by splitting on ";" are filtered for directive/skip lines
    ///     so any directive that leaked into a buffer is discarded before sending to Oracle.
    /// </summary>
    public static class SqlMigrationParser
    {
        // FIX 1: Added [^;]*;? at the end so "SET DEFINE OFF;" (with semicolon) is fully matched
        // and never leaks into a regular SQL buffer.
        private static readonly Regex SetDirectiveRe = new(
            @"^SET\s+(SCAN|DEFINE|SERVEROUTPUT|ECHO|VERIFY|FEEDBACK|HEADING|PAGESIZE|LINESIZE|TRIMOUT|TRIMSPOOL|NEWPAGE)\b[^;]*;?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BlockCommentRe =
            new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex LineCommentRe =
            new(@"--[^\n]*", RegexOptions.Compiled);

        private static readonly Regex SkipLineRe = new(
            @"^(PROMPT|DEFINE|WHENEVER|SELECT\s+SYSDATE)\b|^@",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RemRe =
            new(@"^\s*REM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Lines that start a PL/SQL block (terminated by bare "/")
        private static readonly Regex PlsqlStartRe = new(
            @"^\s*(DECLARE\b|BEGIN\b|CREATE\s+(OR\s+REPLACE\s+)?(PROCEDURE|FUNCTION|PACKAGE|TRIGGER|TYPE)\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<(string Statement, int ApproxLine)> ParseStatements(string sql)
        {
            // Normalise line endings
            sql = sql.Replace("\r\n", "\n").Replace("\r", "\n");
            // Strip block comments
            sql = BlockCommentRe.Replace(sql, " ");
            // Strip line comments
            sql = LineCommentRe.Replace(sql, "");

            var rawLines = sql.Split('\n');
            var result = new List<(string, int)>();
            int i = 0;

            while (i < rawLines.Length)
            {
                string line = rawLines[i].Trim();
                int lineNum = i + 1;

                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }
                if (line == "/") { i++; continue; }
                if (SetDirectiveRe.IsMatch(line)) { i++; continue; }
                if (SkipLineRe.IsMatch(line)) { i++; continue; }
                if (RemRe.IsMatch(line)) { i++; continue; }

                // PL/SQL block — collect until bare "/"
                if (PlsqlStartRe.IsMatch(line))
                {
                    var sb = new StringBuilder();
                    int start = i;
                    while (i < rawLines.Length)
                    {
                        string t = rawLines[i].Trim();
                        if (t == "/") { i++; break; }
                        sb.AppendLine(rawLines[i]);
                        i++;
                    }
                    string block = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(block))
                        result.Add((block, start + 1));
                    continue;
                }

                // Regular SQL — collect until line ending with ";" or bare "/"
                {
                    var sb = new StringBuilder();
                    int start = i;
                    while (i < rawLines.Length)
                    {
                        string raw = rawLines[i];
                        string t = raw.Trim();

                        if (t == "/") { i++; break; }

                        // Skip directive/comment lines even mid-buffer
                        if (SetDirectiveRe.IsMatch(t) || SkipLineRe.IsMatch(t) || RemRe.IsMatch(t))
                        { i++; continue; }

                        // FIX 2: If we have already collected something and the next line
                        // starts a PL/SQL block, stop WITHOUT consuming that line so the
                        // outer loop can route it to the PL/SQL collector.
                        if (sb.Length > 0 && PlsqlStartRe.IsMatch(t)) break;

                        sb.AppendLine(raw);
                        i++;
                        if (t.EndsWith(";")) break;
                    }

                    // Split on ";" for multi-statement lines
                    foreach (var seg in sb.ToString().Split(';'))
                    {
                        string s = seg.Trim();
                        if (string.IsNullOrWhiteSpace(s)) continue;

                        // FIX 3: Discard any directive fragment that leaked into the buffer
                        // (e.g. "SET SCAN OFF" that was part of a line ending with ";")
                        if (SetDirectiveRe.IsMatch(s)) continue;
                        if (SkipLineRe.IsMatch(s)) continue;
                        if (RemRe.IsMatch(s)) continue;

                        result.Add((s, start + 1));
                    }
                }
            }

            return result;
        }
    }
}