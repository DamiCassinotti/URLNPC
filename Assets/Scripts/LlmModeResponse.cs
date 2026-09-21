// What the model said, parsed (issue #130). The request constrains decoding to
// a JSON schema, but a constrained decode is not a guarantee across servers and
// versions, so this stays defensive: fences and prose around the object are
// stripped, a truncated object still yields its mode, and anything that names
// no mode fails rather than guessing one.
public struct LlmModeResponse
{
    public NpcMode Mode;
    public string Reason;

    public static bool TryParse(string text, out LlmModeResponse parsed)
    {
        parsed = default;
        parsed.Reason = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (TryReadStringValue(text, "mode", out string value))
        {
            // The key's value is the answer, whatever it says: falling back to
            // a scan of the rest would let a word in the reason ("hunt him
            // down") stand in for an out-of-vocabulary mode.
            if (!TryMatchMode(value, out parsed.Mode)) return false;
        }
        else if (!TryScanForMode(text, out parsed.Mode))
        {
            return false;
        }

        if (TryReadStringValue(text, "reason", out string reason)) parsed.Reason = reason.Trim();
        return true;
    }

    static bool TryMatchMode(string value, out NpcMode mode)
    {
        mode = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string trimmed = value.Trim();
        // TryParse alone accepts "3" and any other integer; the model's answer
        // is a name.
        foreach (NpcMode candidate in NpcModes.All)
        {
            if (string.Equals(trimmed, candidate.ToString(), System.StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }
        return false;
    }

    // No usable "mode" key: a bare name, or an object truncated inside the
    // value. Only when exactly one mode is named — free-text reasoning that
    // mentions two of them ("not Retreat, so Hunt") has no reading the order of
    // the words can be trusted for, and a wrong mode would be committed with
    // nothing to detect it by. Naming none is the same failure, and both are
    // worth a retry.
    static bool TryScanForMode(string text, out NpcMode mode)
    {
        mode = default;
        int found = 0;
        foreach (NpcMode candidate in NpcModes.All)
        {
            if (text.IndexOf(candidate.ToString(), System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            mode = candidate;
            found++;
        }
        return found == 1;
    }

    // "<key>" ... : ... "<value>", with JSON escapes undone. Deliberately not a
    // JSON parser: the object may be truncated or wrapped in prose, and every
    // key this reads has a string value.
    static bool TryReadStringValue(string text, string key, out string value)
    {
        value = null;
        string quoted = "\"" + key + "\"";
        int at = text.IndexOf(quoted, System.StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;
        int colon = text.IndexOf(':', at + quoted.Length);
        if (colon < 0) return false;

        int open = -1;
        for (int i = colon + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') { open = i; break; }
            // Anything but whitespace between the colon and the quote means the
            // value is not a string (a number, null, a nested object).
            if (!char.IsWhiteSpace(c)) return false;
        }
        if (open < 0) return false;

        var sb = new System.Text.StringBuilder();
        for (int i = open + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                char next = text[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    // A model writing prose reaches for an em-dash or an accent
                    // often enough, and the mangled text would land in the
                    // decisions file this exists to be debugged from.
                    case 'u' when i + 4 < text.Length
                        && ushort.TryParse(text.Substring(i + 1, 4),
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out ushort code):
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: sb.Append(next); break;
                }
                continue;
            }
            if (c == '"')
            {
                value = sb.ToString();
                return true;
            }
            sb.Append(c);
        }
        // Unterminated: the response was cut off mid-value, so there is no
        // value to trust.
        return false;
    }

    // The schema the server constrains decoding to, built off NpcModes.All so
    // the vocabulary can't drift from the enum.
    public static string Schema()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"enum\":[");
        for (int i = 0; i < NpcModes.All.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(NpcModes.All[i]).Append('"');
        }
        sb.Append("]},\"reason\":{\"type\":\"string\"}},")
          .Append("\"required\":[\"mode\",\"reason\"]}");
        return sb.ToString();
    }
}
