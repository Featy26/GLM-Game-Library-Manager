using System.Text;

namespace GameTransfer.Core.Helpers;

public class VdfNode
{
    public string? Value { get; set; }
    public Dictionary<string, VdfNode> Children { get; set; } = new();

    public string? this[string key] => Children.TryGetValue(key, out var node) ? node.Value : null;
}

public static class VdfParser
{
    public static VdfNode Parse(string content)
    {
        var tokens = Tokenize(content);
        var index = 0;
        var root = new VdfNode();
        ParseChildren(tokens, ref index, root);
        return root;
    }

    private static List<string> Tokenize(string content)
    {
        var tokens = new List<string>();
        var i = 0;

        while (i < content.Length)
        {
            var c = content[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Line comments
            if (c == '/' && i + 1 < content.Length && content[i + 1] == '/')
            {
                while (i < content.Length && content[i] != '\n')
                    i++;
                continue;
            }

            if (c == '{' || c == '}')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            if (c == '"')
            {
                i++; // skip opening quote
                var sb = new StringBuilder();
                while (i < content.Length && content[i] != '"')
                {
                    if (content[i] == '\\' && i + 1 < content.Length)
                    {
                        sb.Append(content[i + 1] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            _ => content[i + 1]
                        });
                        i += 2;
                    }
                    else
                    {
                        sb.Append(content[i]);
                        i++;
                    }
                }
                if (i < content.Length) i++; // skip closing quote
                tokens.Add(sb.ToString());
                continue;
            }

            // Unquoted token
            {
                var sb = new StringBuilder();
                while (i < content.Length && !char.IsWhiteSpace(content[i]) && content[i] != '{' && content[i] != '}' && content[i] != '"')
                {
                    sb.Append(content[i]);
                    i++;
                }
                tokens.Add(sb.ToString());
            }
        }

        return tokens;
    }

    private static void ParseChildren(List<string> tokens, ref int index, VdfNode parent)
    {
        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (token == "}")
                return;

            // token is a key
            var key = token;
            index++;

            if (index >= tokens.Count)
                break;

            if (tokens[index] == "{")
            {
                index++; // skip '{'
                var child = new VdfNode();
                ParseChildren(tokens, ref index, child);
                if (index < tokens.Count && tokens[index] == "}")
                    index++; // skip '}'
                parent.Children[key] = child;
            }
            else
            {
                // value
                parent.Children[key] = new VdfNode { Value = tokens[index] };
                index++;
            }
        }
    }

    public static string Serialize(VdfNode node, int indent = 0)
    {
        var sb = new StringBuilder();
        var tabs = new string('\t', indent);

        foreach (var (key, child) in node.Children)
        {
            if (child.Value is not null)
            {
                sb.AppendLine($"{tabs}\"{EscapeString(key)}\"\t\t\"{EscapeString(child.Value)}\"");
            }
            else
            {
                sb.AppendLine($"{tabs}\"{EscapeString(key)}\"");
                sb.AppendLine($"{tabs}{{");
                sb.Append(Serialize(child, indent + 1));
                sb.AppendLine($"{tabs}}}");
            }
        }

        return sb.ToString();
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }
}
