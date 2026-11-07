using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentList = System.Windows.Documents.List;

namespace AnchorFeeds.Services;

public static class ArticleContentFormatter
{
    private static readonly Regex TokenRegex = new(@"<!--[\s\S]*?-->|<![^>]*>|</?[A-Za-z][^>]*>", RegexOptions.Compiled);
    private static readonly Regex TagNameRegex = new(@"^<\s*(?<close>/)?\s*(?<name>[A-Za-z][A-Za-z0-9:-]*)", RegexOptions.Compiled);
    private static readonly Regex StyleAttributeRegex = new(
        """(?:^|\s)style\s*=\s*(?:"(?<double>[^"]*)"|'(?<single>[^']*)'|(?<unquoted>[^\s>]+))""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };
    private static readonly HashSet<string> HiddenElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "button", "canvas", "embed", "form", "head", "iframe", "input", "object", "script", "style",
        "svg", "template", "textarea", "video", "audio", "noscript"
    };
    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "div", "dl", "figure", "figcaption", "footer",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "ol", "p", "pre",
        "section", "table", "ul"
    };

    public static FlowDocument CreateDocument(
        string? html,
        string? fallbackText,
        string fontFamily = "Segoe UI",
        double fontSize = 14)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily(fontFamily),
            FontSize = 14,
            ColumnWidth = double.PositiveInfinity
        };

        if (!string.IsNullOrWhiteSpace(html))
        {
            var root = Parse(html);
            AppendBlocks(root.Children, document.Blocks);
        }

        if (!HasVisibleText(document))
        {
            document.Blocks.Clear();
            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                var paragraph = CreateParagraph();
                paragraph.Inlines.Add(new Run(fallbackText));
                document.Blocks.Add(paragraph);
            }
        }

        ApplyTypography(document, new FontFamily(fontFamily), fontSize);
        return document;
    }

    public static string CreatePlainText(string? html, string? fallbackText)
    {
        var document = CreateDocument(html, fallbackText);
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    private static void ApplyTypography(FlowDocument document, FontFamily fontFamily, double fontSize)
    {
        var applyFontFamily = !fontFamily.Source.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase);
        var fontSizeScale = fontSize / 14d;
        var applyFontSize = !fontSizeScale.Equals(1d);
        if (!applyFontFamily && !applyFontSize)
        {
            return;
        }

        var elements = new List<(TextElement Element, double FontSize)>();
        for (var position = document.ContentStart;
             position is not null;
             position = position.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                position.GetAdjacentElement(LogicalDirection.Forward) is TextElement element)
            {
                elements.Add((element, element.FontSize));
            }
        }

        document.FontFamily = fontFamily;
        document.FontSize = fontSize;
        foreach (var (element, originalFontSize) in elements)
        {
            if (applyFontFamily)
            {
                element.FontFamily = fontFamily;
            }

            if (applyFontSize)
            {
                element.FontSize = originalFontSize * fontSizeScale;
            }
        }
    }

    private static HtmlNode Parse(string html)
    {
        var root = new HtmlNode("root");
        var stack = new Stack<HtmlNode>();
        stack.Push(root);
        var position = 0;

        foreach (Match match in TokenRegex.Matches(html))
        {
            AddText(stack.Peek(), html[position..match.Index]);
            position = match.Index + match.Length;
            if (match.Value.StartsWith("<!--", StringComparison.Ordinal) ||
                match.Value.StartsWith("<!", StringComparison.Ordinal))
            {
                continue;
            }

            var tagMatch = TagNameRegex.Match(match.Value);
            if (!tagMatch.Success)
            {
                AddText(stack.Peek(), match.Value);
                continue;
            }

            var qualifiedName = tagMatch.Groups["name"].Value.ToLowerInvariant();
            var name = qualifiedName[(qualifiedName.LastIndexOf(':') + 1)..];
            if (tagMatch.Groups["close"].Success)
            {
                while (stack.Count > 1)
                {
                    var popped = stack.Pop();
                    if (popped.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                continue;
            }

            if (BlockElements.Contains(name))
            {
                CloseOpenElement(stack, "p");
            }
            if (name == "li")
            {
                CloseOpenElement(stack, "li");
            }
            if (HeadingLevel(name) > 0)
            {
                CloseOpenElement(stack, name);
            }

            var styleMatch = StyleAttributeRegex.Match(match.Value);
            var style = styleMatch.Success
                ? WebUtility.HtmlDecode(
                    styleMatch.Groups["double"].Success ? styleMatch.Groups["double"].Value :
                    styleMatch.Groups["single"].Success ? styleMatch.Groups["single"].Value :
                    styleMatch.Groups["unquoted"].Value)
                : string.Empty;
            var node = new HtmlNode(name, style: style);
            stack.Peek().Children.Add(node);
            if (!match.Value.EndsWith("/>", StringComparison.Ordinal) && !VoidElements.Contains(name))
            {
                stack.Push(node);
            }
        }

        AddText(stack.Peek(), html[position..]);
        return root;
    }

    private static void AddText(HtmlNode parent, string value)
    {
        if (value.Length > 0)
        {
            parent.Children.Add(new HtmlNode(WebUtility.HtmlDecode(value), isText: true));
        }
    }

    private static void AppendBlocks(IEnumerable<HtmlNode> nodes, BlockCollection blocks)
    {
        Paragraph? paragraph = null;
        foreach (var node in nodes)
        {
            if (node.IsText)
            {
                if (string.IsNullOrWhiteSpace(node.Name))
                {
                    continue;
                }

                var text = NormalizeWhitespace(node.Name);
                if (text.Length == 0)
                {
                    continue;
                }

                paragraph ??= CreateParagraph();
                paragraph.Inlines.Add(new Run(text));
                continue;
            }

            if (HiddenElements.Contains(node.Name))
            {
                continue;
            }

            if (!BlockElements.Contains(node.Name))
            {
                paragraph ??= CreateParagraph();
                AppendInline(node, paragraph.Inlines);
                continue;
            }

            FlushParagraph(ref paragraph, blocks);
            AppendBlock(node, blocks);
        }

        FlushParagraph(ref paragraph, blocks);
    }

    private static void AppendBlock(HtmlNode node, BlockCollection blocks)
    {
        if (node.Name is "ul" or "ol")
        {
            var list = new DocumentList
            {
                MarkerStyle = node.Name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(24, 0, 0, 0)
            };
            foreach (var item in node.Children.Where(child => !child.IsText || !string.IsNullOrWhiteSpace(child.Name)))
            {
                if (item.Name == "li")
                {
                    var listItem = new ListItem();
                    AppendBlocks(item.Children, listItem.Blocks);
                    if (listItem.Blocks.Count == 0)
                    {
                        listItem.Blocks.Add(CreateParagraph());
                    }
                    list.ListItems.Add(listItem);
                }
                else if (item.IsText)
                {
                    var listItem = new ListItem();
                    var paragraph = CreateParagraph();
                    paragraph.Inlines.Add(new Run(NormalizeWhitespace(item.Name)));
                    listItem.Blocks.Add(paragraph);
                    list.ListItems.Add(listItem);
                }
                else
                {
                    var listItem = new ListItem();
                    AppendBlock(item, listItem.Blocks);
                    if (listItem.Blocks.Count > 0)
                    {
                        list.ListItems.Add(listItem);
                    }
                }
            }

            blocks.Add(list);
            return;
        }

        if (node.Name is "div" or "section" or "article" or "aside" or "main" or "header" or "footer" or "figure" or "dl")
        {
            if (node.Children.Any(child => !child.IsText && BlockElements.Contains(child.Name)))
            {
                var section = new Section { Margin = new Thickness(0, 0, 0, 8) };
                ApplyStyle(section, node.Style);
                AppendBlocks(node.Children, section.Blocks);
                if (section.Blocks.Count > 0)
                {
                    blocks.Add(section);
                }
            }
            else
            {
                var paragraph = CreateParagraph();
                ApplyStyle(paragraph, node.Style);
                AppendInlineChildren(node.Children, paragraph.Inlines);
                AddIfNotEmpty(blocks, paragraph);
            }
            return;
        }

        if (node.Name == "hr")
        {
            var separator = CreateParagraph();
            separator.BorderThickness = new Thickness(0, 0, 0, 1);
            separator.BorderBrush = SystemColors.GrayTextBrush;
            separator.Margin = new Thickness(0, 5, 0, 11);
            blocks.Add(separator);
            return;
        }

        if (node.Name == "table")
        {
            var rows = Descendants(node, "tr").ToList();
            foreach (var row in rows)
            {
                var paragraph = CreateParagraph();
                paragraph.Margin = new Thickness(0, 0, 0, 5);
                var cells = row.Children.Where(child => child.Name is "td" or "th").ToList();
                for (var i = 0; i < cells.Count; i++)
                {
                    if (i > 0)
                    {
                        paragraph.Inlines.Add(new Run("    |    "));
                    }

                    var content = new Span();
                    AppendInlineChildren(cells[i].Children, content.Inlines);
                    if (cells[i].Name == "th")
                    {
                        content.FontWeight = FontWeights.SemiBold;
                    }
                    paragraph.Inlines.Add(content);
                }
                AddIfNotEmpty(blocks, paragraph);
            }
            return;
        }

        var headingLevel = HeadingLevel(node.Name);
        if (headingLevel > 0)
        {
            var size = headingLevel switch
            {
                1 => 24,
                2 => 20,
                3 => 17,
                4 => 15,
                5 => 14,
                _ => 13
            };
            var heading = CreateParagraph(new Thickness(0, headingLevel < 3 ? 10 : 7, 0, 6));
            heading.FontSize = size;
            heading.FontWeight = FontWeights.SemiBold;
            ApplyStyle(heading, node.Style);
            AppendInlineChildren(node.Children, heading.Inlines);
            AddIfNotEmpty(blocks, heading);
            return;
        }

        var block = CreateParagraph();
        if (node.Name == "blockquote")
        {
            block.Margin = new Thickness(18, 3, 0, 10);
            block.FontStyle = FontStyles.Italic;
        }
        else if (node.Name == "pre")
        {
            block.Margin = new Thickness(0, 3, 0, 10);
            block.FontFamily = new FontFamily("Consolas");
            block.FontSize = 13;
        }
        else if (node.Name == "address")
        {
            block.FontStyle = FontStyles.Italic;
        }

        ApplyStyle(block, node.Style);
        AppendInlineChildren(node.Children, block.Inlines);
        AddIfNotEmpty(blocks, block);
    }

    private static void AppendInlineChildren(IEnumerable<HtmlNode> nodes, InlineCollection inlines)
    {
        foreach (var node in nodes)
        {
            AppendInline(node, inlines);
        }
    }

    private static void AppendInline(HtmlNode node, InlineCollection inlines)
    {
        if (node.IsText)
        {
            var text = NormalizeWhitespace(node.Name);
            if (text.Length > 0)
            {
                inlines.Add(new Run(text));
            }
            return;
        }

        if (node.Name == "br")
        {
            inlines.Add(new LineBreak());
            return;
        }

        if (node.Name == "a")
        {
            if (IsAllCapsLinkText(GetVisibleText(node)))
            {
                return;
            }

            AppendInlineChildren(node.Children, inlines);
            return;
        }

        if (HiddenElements.Contains(node.Name) || VoidElements.Contains(node.Name))
        {
            return;
        }

        var span = new Span();
        switch (node.Name)
        {
            case "b":
            case "strong":
                span.FontWeight = FontWeights.SemiBold;
                break;
            case "i":
            case "em":
            case "cite":
                span.FontStyle = FontStyles.Italic;
                break;
            case "u":
            case "ins":
                span.TextDecorations = TextDecorations.Underline;
                break;
            case "s":
            case "strike":
            case "del":
                span.TextDecorations = TextDecorations.Strikethrough;
                break;
            case "sup":
                span.BaselineAlignment = BaselineAlignment.Superscript;
                span.FontSize = 11;
                break;
            case "sub":
                span.BaselineAlignment = BaselineAlignment.Subscript;
                span.FontSize = 11;
                break;
            case "code":
            case "kbd":
            case "samp":
                span.FontFamily = new FontFamily("Consolas");
                span.FontSize = 13;
                break;
            case "small":
                span.FontSize = 12;
                break;
            case "mark":
                span.Background = SystemColors.InfoBrush;
                break;
        }

        ApplyStyle(span, node.Style);
        AppendInlineChildren(node.Children, span.Inlines);
        if (span.Inlines.Count > 0)
        {
            inlines.Add(span);
        }
    }

    private static string GetVisibleText(HtmlNode node)
    {
        if (node.IsText)
        {
            return node.Name;
        }

        if (node.Name == "br")
        {
            return " ";
        }

        if (HiddenElements.Contains(node.Name))
        {
            return string.Empty;
        }

        return string.Concat(node.Children.Select(GetVisibleText));
    }

    private static bool IsAllCapsLinkText(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsUpper);
    }

    private static IEnumerable<HtmlNode> Descendants(HtmlNode node, string name)
    {
        foreach (var child in node.Children)
        {
            if (child.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }

            if (!child.IsText)
            {
                foreach (var descendant in Descendants(child, name))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void CloseOpenElement(Stack<HtmlNode> stack, string name)
    {
        var openElements = stack.ToArray();
        if (!openElements.Any(node => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        while (stack.Count > 1)
        {
            var popped = stack.Pop();
            if (popped.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    private static int HeadingLevel(string name) =>
        name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6'
            ? name[1] - '0'
            : 0;

    private static void ApplyStyle(TextElement element, string style)
    {
        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = declaration[..separator].Trim();
            var value = declaration[(separator + 1)..].Trim();
            switch (name.ToLowerInvariant())
            {
                case "font-weight":
                    if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    {
                        element.FontWeight = FontWeights.Normal;
                    }
                    else if (value.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                             value.Equals("bolder", StringComparison.OrdinalIgnoreCase))
                    {
                        element.FontWeight = FontWeights.SemiBold;
                    }
                    else if (int.TryParse(value, out var weight))
                    {
                        element.FontWeight = weight >= 600 ? FontWeights.SemiBold : FontWeights.Normal;
                    }
                    break;
                case "font-style":
                    if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    {
                        element.FontStyle = FontStyles.Normal;
                    }
                    else if (value.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                             value.Equals("oblique", StringComparison.OrdinalIgnoreCase))
                    {
                        element.FontStyle = FontStyles.Italic;
                    }
                    break;
                case "font-size":
                    if (TryParseFontSize(value, out var fontSize))
                    {
                        element.FontSize = fontSize;
                    }
                    break;
                case "text-align" when element is Paragraph paragraph:
                    paragraph.TextAlignment = value.ToLowerInvariant() switch
                    {
                        "center" => TextAlignment.Center,
                        "right" => TextAlignment.Right,
                        "justify" => TextAlignment.Justify,
                        _ => TextAlignment.Left
                    };
                    break;
                case "text-decoration" when element is Inline inline:
                    if (value.Contains("underline", StringComparison.OrdinalIgnoreCase))
                    {
                        inline.TextDecorations = TextDecorations.Underline;
                    }
                    else if (value.Contains("line-through", StringComparison.OrdinalIgnoreCase))
                    {
                        inline.TextDecorations = TextDecorations.Strikethrough;
                    }
                    else if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        inline.TextDecorations = new TextDecorationCollection();
                    }
                    break;
            }
        }
    }

    private static bool TryParseFontSize(string value, out double fontSize)
    {
        var scale = value.EndsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? 4d / 3d
            : value.EndsWith("em", StringComparison.OrdinalIgnoreCase) ||
              value.EndsWith("rem", StringComparison.OrdinalIgnoreCase)
                ? 14d
                : value.EndsWith('%')
                    ? 0.14d
                    : 1d;
        var number = Regex.Match(value, @"[-+]?(?:\d+\.?\d*|\.\d+)");
        if (!number.Success ||
            !double.TryParse(number.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var size))
        {
            fontSize = 14;
            return false;
        }

        fontSize = Math.Clamp(size * scale, 9, 36);
        return true;
    }

    private static Paragraph CreateParagraph(Thickness? margin = null) => new()
    {
        Margin = margin ?? new Thickness(0, 0, 0, 10)
    };

    private static void AddIfNotEmpty(BlockCollection blocks, Paragraph paragraph)
    {
        if (paragraph.Inlines.Count > 0)
        {
            blocks.Add(paragraph);
        }
    }

    private static void FlushParagraph(ref Paragraph? paragraph, BlockCollection blocks)
    {
        if (paragraph is not null)
        {
            AddIfNotEmpty(blocks, paragraph);
            paragraph = null;
        }
    }

    private static bool HasVisibleText(FlowDocument document) =>
        !string.IsNullOrWhiteSpace(new TextRange(document.ContentStart, document.ContentEnd).Text);

    private static string NormalizeWhitespace(string text) => WhitespaceRegex.Replace(text, " ");

    private sealed class HtmlNode(string name, bool isText = false, string style = "")
    {
        public string Name { get; } = name;
        public string Style { get; } = style;
        public List<HtmlNode> Children { get; } = [];
        public bool IsText { get; } = isText;
    }
}
