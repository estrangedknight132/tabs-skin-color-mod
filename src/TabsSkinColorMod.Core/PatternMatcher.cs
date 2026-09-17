using System;
using System.Collections.Generic;

namespace TabsSkinColorMod.Core
{
    /// <summary>
    /// Matches material / renderer names against a configurable pattern list.
    ///
    /// Pattern syntax (comma-separated entries):
    ///   skin            substring match, case-insensitive
    ///   wob*  *body*    pattern containing '*' is a full wildcard match ('*' = any run of characters)
    ///   -team           negation: a name matching any negative pattern is rejected
    ///
    /// Semantics: a name matches when it matches at least one positive pattern AND
    /// no negative pattern. An empty positive list matches nothing.
    /// </summary>
    public sealed class PatternMatcher
    {
        private readonly List<string> _positive = new List<string>();
        private readonly List<string> _negative = new List<string>();

        public PatternMatcher(string patternList) => Reload(patternList);

        public void Reload(string patternList)
        {
            _positive.Clear();
            _negative.Clear();
            foreach (var raw in (patternList ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;
                if (p.Length == 1 && p[0] == '-') continue;
                if (p[0] == '-')
                {
                    var neg = p.Substring(1).Trim();
                    if (neg.Length > 0) _negative.Add(neg.ToLowerInvariant());
                }
                else
                {
                    _positive.Add(p.ToLowerInvariant());
                }
            }
        }

        public bool HasAnyPositive => _positive.Count > 0;

        public bool IsMatch(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            foreach (var neg in _negative)
                if (WildcardMatches(neg, n)) return false;
            foreach (var pos in _positive)
                if (WildcardMatches(pos, n)) return true;
            return false;
        }

        /// <summary>
        /// If the pattern contains '*', it is a full wildcard match against the text.
        /// Otherwise it is a substring (contains) match.
        /// </summary>
        public static bool WildcardMatches(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern) || text == null) return false;
            if (pattern.IndexOf('*') < 0) return text.Contains(pattern);

            int p = 0, t = 0, starP = -1, starT = -1;
            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '*' || pattern[p] == text[t]))
                {
                    if (pattern[p] == '*') { starP = p; starT = t; p++; }
                    else { p++; t++; }
                }
                else if (starP != -1)
                {
                    starT++;
                    t = starT;
                    p = starP + 1;
                }
                else
                {
                    return false;
                }
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }
}
