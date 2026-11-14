using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GlassRoom
{
    /// <summary>
    /// Reads JSON (as sent by the online board store) into dictionaries, lists, strings, numbers
    /// (double), booleans and null. Small and forgiving; enough for the board.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            try { return Value(json, ref i); }
            catch { return null; }
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return Object(s, ref i);
            if (c == '[') return Array(s, ref i);
            if (c == '"') return String(s, ref i);
            if (c == 't' && s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (c == 'f' && s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (c == 'n' && s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> Object(string s, ref int i)
        {
            var result = new Dictionary<string, object>();
            i++; // {
            while (true)
            {
                Skip(s, ref i);
                if (s[i] == '}') { i++; return result; }
                string key = String(s, ref i);
                Skip(s, ref i);
                i++; // :
                result[key] = Value(s, ref i);
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return result; }
            }
        }

        static List<object> Array(string s, ref int i)
        {
            var result = new List<object>();
            i++; // [
            while (true)
            {
                Skip(s, ref i);
                if (s[i] == ']') { i++; return result; }
                result.Add(Value(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return result; }
            }
        }

        static string String(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++; // closing quote
            return sb.ToString();
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
