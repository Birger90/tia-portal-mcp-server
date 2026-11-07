using System.Globalization;
using System.Text;

namespace TiaMcpServer.Contracts.Shared.ClaudeInbox;

/// <summary>
/// A small, dependency-free JSON writer and reader for the "Claude in TIA" inbox and outbox files. It is shared source:
/// the TIA Portal Add-In (net48, running in TIA Portal's add-in sandbox) compiles the same file, so it uses nothing but
/// the base class library (no System.Text.Json, no serializer reflection). Values read are <see cref="string"/>,
/// <see cref="double"/>, <see cref="bool"/>, <c>null</c>, <see cref="List{T}"/> of values and
/// <see cref="Dictionary{TKey, TValue}"/> (ordinal keys; a repeated key is refused).
/// </summary>
public static class MiniJson
{
    /// <summary>The deepest nesting the reader accepts.</summary>
    public const int MaxDepth = 32;

    public static string Quote(string? value)
    {
        if (value is null) return "null";
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Parses one JSON document; throws <see cref="FormatException"/> for anything that is not exactly one value.</summary>
    public static object? Parse(string text)
    {
        if (text is null) throw new FormatException("No JSON text.");
        var reader = new Reader(text);
        reader.SkipWhitespace();
        var value = reader.ReadValue(0);
        reader.SkipWhitespace();
        if (!reader.AtEnd) throw new FormatException("Unexpected text after the JSON value.");
        return value;
    }

    private sealed class Reader
    {
        private readonly string _text;
        private int _position;

        public Reader(string text)
        {
            _text = text;
            if (_text.Length > 0 && _text[0] == '﻿') _position = 1;
        }

        public bool AtEnd => _position >= _text.Length;

        public void SkipWhitespace()
        {
            while (!AtEnd && (_text[_position] == ' ' || _text[_position] == '\t' || _text[_position] == '\n' || _text[_position] == '\r'))
                _position++;
        }

        public object? ReadValue(int depth)
        {
            if (depth > MaxDepth) throw new FormatException("The JSON document is nested too deeply.");
            if (AtEnd) throw new FormatException("Unexpected end of the JSON text.");
            var c = _text[_position];
            switch (c)
            {
                case '{': return ReadObject(depth);
                case '[': return ReadArray(depth);
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                    throw new FormatException($"Unexpected character at position {_position}.");
            }
        }

        private Dictionary<string, object?> ReadObject(int depth)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            _position++;
            SkipWhitespace();
            if (!AtEnd && _text[_position] == '}')
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (AtEnd || _text[_position] != '"') throw new FormatException("An object member name was expected.");
                var name = ReadString();
                SkipWhitespace();
                if (AtEnd || _text[_position] != ':') throw new FormatException("':' was expected.");
                _position++;
                SkipWhitespace();
                var value = ReadValue(depth + 1);
                if (result.ContainsKey(name)) throw new FormatException($"The member \"{name}\" is repeated.");
                result.Add(name, value);
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end inside an object.");
                if (_text[_position] == ',')
                {
                    _position++;
                    continue;
                }

                if (_text[_position] == '}')
                {
                    _position++;
                    return result;
                }

                throw new FormatException("',' or '}' was expected.");
            }
        }

        private List<object?> ReadArray(int depth)
        {
            var result = new List<object?>();
            _position++;
            SkipWhitespace();
            if (!AtEnd && _text[_position] == ']')
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                result.Add(ReadValue(depth + 1));
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end inside an array.");
                if (_text[_position] == ',')
                {
                    _position++;
                    continue;
                }

                if (_text[_position] == ']')
                {
                    _position++;
                    return result;
                }

                throw new FormatException("',' or ']' was expected.");
            }
        }

        private string ReadString()
        {
            _position++;
            var builder = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw new FormatException("Unterminated string.");
                var c = _text[_position++];
                if (c == '"') return builder.ToString();
                if (c < 0x20) throw new FormatException("A control character inside a string must be escaped.");
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (AtEnd) throw new FormatException("Unterminated escape.");
                var e = _text[_position++];
                switch (e)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (_position + 4 > _text.Length) throw new FormatException("Incomplete \\u escape.");
                        if (!int.TryParse(_text.Substring(_position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            throw new FormatException("Invalid \\u escape.");
                        builder.Append((char)code);
                        _position += 4;
                        break;
                    default: throw new FormatException("Invalid escape.");
                }
            }
        }

        private double ReadNumber()
        {
            var start = _position;
            if (_text[_position] == '-') _position++;
            while (!AtEnd && "0123456789.eE+-".IndexOf(_text[_position]) >= 0) _position++;
            var token = _text.Substring(start, _position - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException("Invalid number.");
            return value;
        }

        private void Expect(string literal)
        {
            if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0) throw new FormatException("Invalid literal.");
            _position += literal.Length;
        }
    }
}

/// <summary>Builds one JSON object member by member (shared source with the Add-In, like <see cref="MiniJson"/>).</summary>
public sealed class MiniJsonObjectWriter
{
    private readonly StringBuilder _builder = new();
    private readonly string _indent;
    private bool _first = true;

    public MiniJsonObjectWriter(int level = 0)
    {
        _indent = new string(' ', (level + 1) * 2);
        _builder.Append('{');
    }

    public MiniJsonObjectWriter String(string name, string? value) => Raw(name, MiniJson.Quote(value));

    public MiniJsonObjectWriter Number(string name, int value) => Raw(name, value.ToString(CultureInfo.InvariantCulture));

    public MiniJsonObjectWriter Bool(string name, bool value) => Raw(name, value ? "true" : "false");

    /// <summary>A member whose value is already JSON text.</summary>
    public MiniJsonObjectWriter Raw(string name, string json)
    {
        _builder.Append(_first ? "\n" : ",\n").Append(_indent).Append(MiniJson.Quote(name)).Append(": ").Append(json);
        _first = false;
        return this;
    }

    public static string Array(IEnumerable<string> jsonValues, int level)
    {
        var items = jsonValues.ToList();
        if (items.Count == 0) return "[]";
        var indent = new string(' ', (level + 1) * 2);
        return "[\n" + string.Join(",\n", items.Select(item => indent + item)) + "\n" + new string(' ', level * 2) + "]";
    }

    public override string ToString()
        => _first ? "{}" : _builder.ToString() + "\n" + new string(' ', _indent.Length - 2) + "}";
}
