using System.Text;

namespace Chitragupta.Infrastructure.Feeds.Csv;

/// <summary>A small RFC 4180 reader: quoted fields, doubled quotes, commas and line breaks inside quotes.</summary>
internal static class CsvText
{
    public static List<string[]> Read(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var letter = text[index];
            if (quoted)
            {
                if (letter != '"')
                {
                    field.Append(letter);
                }
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (letter)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(letter);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            EndRow();
        }

        return rows;

        void EndRow()
        {
            row.Add(field.ToString());
            field.Clear();
            if (row.Any(value => value.Trim().Length > 0))
            {
                rows.Add(row.Select(value => value.Trim()).ToArray());
            }

            row.Clear();
        }
    }
}
