using System.Text;
using System.Text.RegularExpressions;

namespace Froststrap.Utility
{
    /// <summary>
    /// Ranks how well a game name matches a user search query (token/word aware).
    /// </summary>
    internal static class GameNameMatch
    {
        private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

        public static int Score(string? name, string? query)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(query))
                return 0;

            string n = Normalize(name);
            string q = Normalize(query);
            if (n.Length == 0 || q.Length == 0)
                return 0;

            if (n == q)
                return 1000;
            if (n.StartsWith(q, StringComparison.Ordinal))
                return 900;
            if (n.Contains(q, StringComparison.Ordinal))
                return 750;

            string[] qTokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] nTokens = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (qTokens.Length == 0)
                return 0;

            int hits = 0;
            int prefixBonus = 0;
            foreach (string token in qTokens)
            {
                bool matched = false;
                foreach (string nt in nTokens)
                {
                    if (nt == token)
                    {
                        hits++;
                        prefixBonus += 40;
                        matched = true;
                        break;
                    }

                    if (nt.StartsWith(token, StringComparison.Ordinal) && token.Length >= 2)
                    {
                        hits++;
                        prefixBonus += 20;
                        matched = true;
                        break;
                    }

                    if (token.StartsWith(nt, StringComparison.Ordinal) && nt.Length >= 3)
                    {
                        hits++;
                        matched = true;
                        break;
                    }
                }

                if (!matched && n.Contains(token, StringComparison.Ordinal))
                    hits++;
            }

            if (hits == 0)
                return 0;

            int score = (int)Math.Round(520.0 * hits / qTokens.Length) + prefixBonus;
            if (hits == qTokens.Length)
                score += 140;

            // Prefer results where query tokens appear in order.
            int orderHits = 0;
            int searchFrom = 0;
            foreach (string token in qTokens)
            {
                int idx = Array.FindIndex(nTokens, searchFrom, nt =>
                    nt == token || (nt.StartsWith(token, StringComparison.Ordinal) && token.Length >= 2));
                if (idx < 0)
                    break;
                orderHits++;
                searchFrom = idx + 1;
            }

            if (orderHits == qTokens.Length)
                score += 80;

            return score;
        }

        public static string Normalize(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else
                    sb.Append(' ');
            }

            return MultiSpace.Replace(sb.ToString(), " ").Trim();
        }
    }
}
