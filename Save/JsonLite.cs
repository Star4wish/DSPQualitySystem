using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DSPQualitySystem.Save
{
    /// <summary>
    /// Minimal dependency-free JSON reader (spec section 4.1). Unity ships neither
    /// System.Web.Extensions nor Newtonsoft.Json, so the save file reader is a small
    /// recursive-descent parser limited to the schema written by QualitySave.
    /// Parsed values: Dictionary&lt;string, object&gt; (object), List&lt;object&gt; (array),
    /// string, long, double, bool, null.
    /// </summary>
    public static class JsonLite
    {
        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            var parser = new Parser(text);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw new FormatException("trailing characters at offset " + parser.Position);
            }
            return value;
        }

        /// <summary>Append value as a JSON string literal (including the surrounding quotes).</summary>
        public static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                            {
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _pos;

            public Parser(string text)
            {
                _text = text;
                _pos = 0;
            }

            public int Position
            {
                get { return _pos; }
            }

            public bool AtEnd
            {
                get { return _pos >= _text.Length; }
            }

            public void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw new FormatException("unexpected end of json");
                }

                char c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                _pos++; // {
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == '}')
                {
                    _pos++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != ':')
                    {
                        throw new FormatException("expected ':' at offset " + _pos);
                    }
                    _pos++;
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new FormatException("unexpected end of object");
                    }
                    char c = _text[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == '}')
                    {
                        _pos++;
                        return result;
                    }
                    throw new FormatException("expected ',' or '}' at offset " + _pos);
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                _pos++; // [
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == ']')
                {
                    _pos++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new FormatException("unexpected end of array");
                    }
                    char c = _text[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == ']')
                    {
                        _pos++;
                        return result;
                    }
                    throw new FormatException("expected ',' or ']' at offset " + _pos);
                }
            }

            private string ParseString()
            {
                if (AtEnd || _text[_pos] != '"')
                {
                    throw new FormatException("expected string at offset " + _pos);
                }
                _pos++;

                var sb = new StringBuilder();
                while (!AtEnd)
                {
                    char c = _text[_pos++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        break;
                    }
                    char escape = _text[_pos++];
                    switch (escape)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length)
                            {
                                throw new FormatException("truncated \\u escape at offset " + _pos);
                            }
                            sb.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default:
                            throw new FormatException("invalid escape at offset " + (_pos - 1));
                    }
                }

                throw new FormatException("unterminated string");
            }

            private object ParseNumber()
            {
                int start = _pos;
                if (!AtEnd && _text[_pos] == '-')
                {
                    _pos++;
                }
                while (!AtEnd && char.IsDigit(_text[_pos]))
                {
                    _pos++;
                }
                bool isInteger = true;
                if (!AtEnd && _text[_pos] == '.')
                {
                    isInteger = false;
                    _pos++;
                    while (!AtEnd && char.IsDigit(_text[_pos]))
                    {
                        _pos++;
                    }
                }
                if (!AtEnd && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    isInteger = false;
                    _pos++;
                    if (!AtEnd && (_text[_pos] == '+' || _text[_pos] == '-'))
                    {
                        _pos++;
                    }
                    while (!AtEnd && char.IsDigit(_text[_pos]))
                    {
                        _pos++;
                    }
                }

                if (_pos == start)
                {
                    throw new FormatException("invalid value at offset " + start);
                }

                string token = _text.Substring(start, _pos - start);
                long integer;
                if (isInteger && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    return integer;
                }
                return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void Expect(string literal)
            {
                if (_pos + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException("expected '" + literal + "' at offset " + _pos);
                }
                _pos += literal.Length;
            }
        }
    }
}
