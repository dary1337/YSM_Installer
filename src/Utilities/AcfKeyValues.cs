using System;
using System.Collections.Generic;
using System.Text;

namespace YSMInstaller {
    // Minimal Valve KeyValues (VDF / Steam .acf) reader + writer. Parses into an ordered tree,
    // lets callers read/modify nested keys, and re-serializes in Steam's tab-indented format
    // (Steam re-reads a round-tripped manifest fine).
    internal sealed class AcfKeyValues {
        public string Key;
        public string? Value;                       // leaf value; null when this is a block
        public List<AcfKeyValues>? Children;        // block children; null when this is a leaf

        private AcfKeyValues(string key) {
            Key = key;
        }

        public static AcfKeyValues Parse(string text) {
            int pos = 0;
            string? rootKey = ReadString(text, ref pos);
            if (rootKey == null) {
                throw new FormatException("Empty KeyValues document.");
            }
            SkipTrivia(text, ref pos);
            if (pos >= text.Length || text[pos] != '{') {
                throw new FormatException("Expected '{' after root key.");
            }
            pos++;
            var root = new AcfKeyValues(rootKey);
            ParseBlock(text, ref pos, root);
            return root;
        }

        private static void ParseBlock(string text, ref int pos, AcfKeyValues node) {
            node.Children = new List<AcfKeyValues>();
            while (true) {
                SkipTrivia(text, ref pos);
                if (pos >= text.Length) {
                    throw new FormatException("Unexpected end of KeyValues.");
                }
                if (text[pos] == '}') {
                    pos++;
                    return;
                }
                string? key = ReadString(text, ref pos);
                if (key == null) {
                    throw new FormatException("Expected a key.");
                }
                SkipTrivia(text, ref pos);
                if (pos >= text.Length) {
                    throw new FormatException("Unexpected end after key '" + key + "'.");
                }
                if (text[pos] == '{') {
                    pos++;
                    var child = new AcfKeyValues(key);
                    ParseBlock(text, ref pos, child);
                    node.Children.Add(child);
                }
                else {
                    string? value = ReadString(text, ref pos);
                    if (value == null) {
                        throw new FormatException("Expected a value for key '" + key + "'.");
                    }
                    node.Children.Add(new AcfKeyValues(key) { Value = value });
                }
            }
        }

        private static void SkipTrivia(string text, ref int pos) {
            while (pos < text.Length) {
                char c = text[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') {
                    pos++;
                    continue;
                }
                if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/') {
                    while (pos < text.Length && text[pos] != '\n') {
                        pos++;
                    }
                    continue;
                }
                break;
            }
        }

        private static string? ReadString(string text, ref int pos) {
            SkipTrivia(text, ref pos);
            if (pos >= text.Length || text[pos] != '"') {
                return null;
            }
            pos++;
            var sb = new StringBuilder();
            while (pos < text.Length) {
                char c = text[pos++];
                if (c == '\\' && pos < text.Length) {
                    char next = text[pos++];
                    if (next == 'n') sb.Append('\n');
                    else if (next == 't') sb.Append('\t');
                    else sb.Append(next); // \\ and \" and anything else → the literal char
                    continue;
                }
                if (c == '"') {
                    return sb.ToString();
                }
                sb.Append(c);
            }
            throw new FormatException("Unterminated string.");
        }

        private AcfKeyValues? FindChild(string key) {
            if (Children == null) {
                return null;
            }
            foreach (AcfKeyValues child in Children) {
                if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase)) {
                    return child;
                }
            }
            return null;
        }

        public string? GetValue(params string[] path) {
            AcfKeyValues? node = this;
            foreach (string key in path) {
                node = node.FindChild(key);
                if (node == null) {
                    return null;
                }
            }
            return node.Value;
        }

        // Ensures the nested block path exists and sets the leaf value at the final key.
        public void SetValue(string value, params string[] path) {
            AcfKeyValues node = this;
            for (int i = 0; i < path.Length - 1; i++) {
                AcfKeyValues? next = node.FindChild(path[i]);
                if (next == null) {
                    next = new AcfKeyValues(path[i]) { Children = new List<AcfKeyValues>() };
                    if (node.Children == null) {
                        node.Children = new List<AcfKeyValues>();
                    }
                    node.Children.Add(next);
                }
                node = next;
            }
            string leafKey = path[path.Length - 1];
            AcfKeyValues? leaf = node.FindChild(leafKey);
            if (leaf != null) {
                leaf.Value = value;
                leaf.Children = null;
            }
            else {
                if (node.Children == null) {
                    node.Children = new List<AcfKeyValues>();
                }
                node.Children.Add(new AcfKeyValues(leafKey) { Value = value });
            }
        }

        public void Remove(params string[] path) {
            AcfKeyValues node = this;
            for (int i = 0; i < path.Length - 1; i++) {
                AcfKeyValues? next = node.FindChild(path[i]);
                if (next == null) {
                    return;
                }
                node = next;
            }
            AcfKeyValues? target = node.FindChild(path[path.Length - 1]);
            if (target != null && node.Children != null) {
                node.Children.Remove(target);
            }
        }

        public string Serialize() {
            var sb = new StringBuilder();
            Write(sb, 0);
            return sb.ToString();
        }

        private void Write(StringBuilder sb, int depth) {
            string indent = new string('\t', depth);
            if (Children != null) {
                sb.Append(indent).Append('"').Append(Escape(Key)).Append("\"\n");
                sb.Append(indent).Append("{\n");
                foreach (AcfKeyValues child in Children) {
                    child.Write(sb, depth + 1);
                }
                sb.Append(indent).Append("}\n");
            }
            else {
                sb.Append(indent).Append('"').Append(Escape(Key)).Append("\"\t\t\"")
                    .Append(Escape(Value ?? string.Empty)).Append("\"\n");
            }
        }

        private static string Escape(string s) {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }
}
