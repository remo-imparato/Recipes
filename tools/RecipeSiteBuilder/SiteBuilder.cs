using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace RecipeSiteBuilder;

public sealed record BuildResult(int RecipeCount, int CategoryCount, string OutputDirectory);

public static partial class SiteBuilder
{
    private static readonly string[] CategoryOrder = ["Appetizers", "Meals", "Desserts", "Drinks", "Sauces"];
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".pdf"
    };
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record Recipe(string Path, string Category, string Title, string Id, MarkdownDocument Document);

    public static BuildResult Build(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        RejectLink(root);
        var recipesRoot = Path.Combine(root, "recipes");
        RejectLink(recipesRoot);
        var recipes = Discover(recipesRoot);
        if (recipes.Count == 0)
            throw new InvalidOperationException($"No Markdown recipes found under {recipesRoot}.");

        var byPath = new Dictionary<string, Recipe>(PathComparer);
        foreach (var recipe in recipes)
            byPath.Add(recipe.Path, recipe);

        var assets = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var cards = new StringBuilder();
        foreach (var recipe in recipes)
        {
            PrepareDocument(recipe, byPath, recipesRoot, assets);
            cards.Append(RenderCard(recipe));
        }

        var categories = new List<string>();
        foreach (var recipe in recipes)
            if (!categories.Contains(recipe.Category, StringComparer.Ordinal))
                categories.Add(recipe.Category);

        var navigation = new StringBuilder(Navigation("all", "All recipes", recipes.Count));
        var tabs = new StringBuilder("<button type=\"button\" class=\"category-tab\" data-category=\"all\" aria-pressed=\"true\">All recipes</button>");
        foreach (var category in categories)
        {
            var count = 0;
            foreach (var recipe in recipes)
                if (recipe.Category == category) count++;
            navigation.Append(Navigation(category, category, count));
            tabs.Append($"<button type=\"button\" class=\"category-tab\" data-category=\"{Encode(category)}\" aria-pressed=\"false\">{Encode(category)}</button>");
        }

        var templatePath = Path.Combine(root, "website", "index.template.html");
        var html = ReadFile(templatePath);
        var replacements = new Dictionary<string, string>
        {
            ["NAVIGATION"] = navigation.ToString(),
            ["CATEGORY_TABS"] = tabs.ToString(),
            ["RECIPE_CARDS"] = cards.ToString(),
            ["RECIPE_COUNT"] = recipes.Count.ToString(CultureInfo.InvariantCulture),
            ["CATEGORY_COUNT"] = categories.Count.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var name in replacements.Keys)
            if (!html.Contains("{{" + name + "}}", StringComparison.Ordinal))
                throw new InvalidOperationException($"{templatePath}: missing required placeholder {{{{{name}}}}}.");
        // Replace template tokens once, so recipe text can never become template instructions.
        html = TemplateToken().Replace(html, match =>
        {
            var name = match.Groups[1].Value;
            if (!replacements.TryGetValue(name, out var value))
                throw new InvalidOperationException($"{templatePath}: unresolved placeholder {match.Value}.");
            return value;
        });
        var css = ReadFile(Path.Combine(root, "website", "site.css"));
        var script = ReadFile(Path.Combine(root, "website", "site.js"));

        // Validate and render everything before touching output from the previous successful build.
        var output = Path.Combine(root, "_site");
        EnsureOutputDirectory(output, root);
        foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            RejectLink(file);
            File.Delete(file);
        }
        var oldDirectories = new List<string>(Directory.EnumerateDirectories(output, "*", SearchOption.AllDirectories));
        oldDirectories.Sort((left, right) => right.Length.CompareTo(left.Length));
        foreach (var directory in oldDirectories)
            Directory.Delete(directory);
        File.WriteAllText(Path.Combine(output, "index.html"), html, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "site.css"), css, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "site.js"), script, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, ".nojekyll"), "");
        foreach (var asset in assets)
        {
            var destination = Path.Combine(output, asset.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(asset.Value, destination, true);
        }
        return new BuildResult(recipes.Count, categories.Count, output);
    }

    private static List<Recipe> Discover(string root)
    {
        var folders = new List<string> { root };
        foreach (var folder in Directory.EnumerateDirectories(root))
            if (EligibleSourcePath(folder)) folders.Add(folder);
        folders.Sort((left, right) =>
        {
            var leftName = left == root ? "General" : Path.GetFileName(left);
            var rightName = right == root ? "General" : Path.GetFileName(right);
            var a = Array.IndexOf(CategoryOrder, leftName);
            var b = Array.IndexOf(CategoryOrder, rightName);
            if (a < 0) a = int.MaxValue;
            if (b < 0) b = int.MaxValue;
            var order = a.CompareTo(b);
            return order != 0 ? order : StringComparer.Ordinal.Compare(leftName, rightName);
        });

        var recipes = new List<Recipe>();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            var files = new List<string>();
            FindMarkdown(folder, files, recursive: folder != root);
            if (files.Count == 0) continue;
            var name = Path.GetFileName(folder);
            var category = folder == root ? "General" : name;
            if (labels.TryGetValue(category, out var otherFolder))
                throw new InvalidOperationException($"Category label '{category}' is shared by {otherFolder} and {folder}. Rename one folder.");
            if (category.Equals("all", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{folder}: category name 'all' is reserved for the all-recipes filter.");
            labels.Add(category, folder);
            var categoryRecipes = new List<Recipe>();
            files.Sort(StringComparer.Ordinal);
            foreach (var path in files)
            {
                var document = Markdown.Parse(ReadFile(path), Pipeline);
                if (document.Count == 0 || document[0] is not HeadingBlock { Level: 1 } heading)
                    throw new InvalidOperationException($"{path}: the first block must be a non-empty '# Recipe title'.");
                var title = PlainText(heading.Inline).Trim();
                if (title.Length == 0)
                    throw new InvalidOperationException($"{path}: empty recipe title.");
                var slug = Slug(title);
                if (slug.Length == 0)
                    throw new InvalidOperationException($"{path}: title must contain a letter or number for its recipe link.");
                // Keep the two original shared links whose snapshot titles differ from their source H1.
                var id = slug switch
                {
                    "pumpkin-pie" => "recipe-pumpkin-pie-remo",
                    "simple-brown-gravy" => "recipe-gravy",
                    _ => "recipe-" + slug
                };
                if (ids.TryGetValue(id, out var other))
                    throw new InvalidOperationException($"{path}: recipe link '{id}' conflicts with {other}. Use distinct recipe titles.");
                ids.Add(id, path);
                document.RemoveAt(0);
                categoryRecipes.Add(new Recipe(path, category, title, id, document));
            }
            categoryRecipes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title));
            recipes.AddRange(categoryRecipes);
        }
        return recipes;
    }

    private static void PrepareDocument(Recipe recipe, Dictionary<string, Recipe> byPath, string root, SortedDictionary<string, string> assets)
    {
        MarkIngredientLists(recipe.Document);
        var headingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var heading in recipe.Document.Descendants<HeadingBlock>())
        {
            var text = PlainText(heading.Inline);
            var slug = Slug(text);
            var id = recipe.Id + "-" + slug;
            var suffix = 2;
            while (!headingIds.Add(id))
                id = recipe.Id + "-" + slug + "-" + suffix++;
            heading.GetAttributes().Id = id;
            heading.Level = Math.Min(6, heading.Level + 1);
        }
        foreach (var link in recipe.Document.Descendants<LinkInline>())
            link.Url = ResolveUrl(link.Url ?? "", link.IsImage, recipe, byPath, root, assets);
        foreach (var link in recipe.Document.Descendants<AutolinkInline>())
            ValidateAbsoluteUrl(link.IsEmail ? "mailto:" + link.Url : link.Url, false, recipe.Path);
    }

    private static void MarkIngredientLists(ContainerBlock container, bool inherited = false)
    {
        var sections = new List<(int Level, bool Ingredients)>();
        foreach (var block in container)
        {
            if (block is HeadingBlock heading)
            {
                while (sections.Count > 0 && sections[^1].Level >= heading.Level)
                    sections.RemoveAt(sections.Count - 1);
                var parentIngredients = sections.Count == 0 ? inherited : sections[^1].Ingredients;
                sections.Add((heading.Level, parentIngredients ||
                    PlainText(heading.Inline).Equals("Ingredients", StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                var ingredients = sections.Count == 0 ? inherited : sections[^1].Ingredients;
                if (block is ListBlock list)
                {
                    if (ingredients && !list.IsOrdered) list.GetAttributes().AddClass("recipe-ingredient-items");
                }
                else if (block is ContainerBlock nested)
                {
                    MarkIngredientLists(nested, ingredients);
                }
            }
        }
    }

    private static string ResolveUrl(string url, bool isImage, Recipe recipe, Dictionary<string, Recipe> byPath, string root, SortedDictionary<string, string> assets)
    {
        if (url.StartsWith("//", StringComparison.Ordinal) || url.Contains('\\') || url.Any(char.IsControl))
            throw new InvalidOperationException($"{recipe.Path}: unsafe or unsupported URL '{url}'.");
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ValidateAbsoluteUrl(url, isImage, recipe.Path);
            return url;
        }
        if (url.StartsWith('#'))
            return RecipeFragment(recipe, Uri.UnescapeDataString(url[1..]), recipe.Path);
        var fragmentIndex = url.IndexOf('#');
        var fragment = fragmentIndex < 0 ? "" : url[(fragmentIndex + 1)..];
        var relative = fragmentIndex < 0 ? url : url[..fragmentIndex];
        if (relative.Contains('?') || relative.StartsWith('/') || relative.Length == 0)
            throw new InvalidOperationException($"{recipe.Path}: unsupported relative URL '{url}'.");
        var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(recipe.Path)!, Uri.UnescapeDataString(relative)));
        if (byPath.TryGetValue(target, out var linkedRecipe) && !isImage)
            return RecipeFragment(linkedRecipe, Uri.UnescapeDataString(fragment), recipe.Path);

        var withinRoot = Path.GetRelativePath(root, target);
        var parts = withinRoot.Split(Path.DirectorySeparatorChar);
        if (parts[0] == ".." ||
            !AssetExtensions.Contains(Path.GetExtension(target)) ||
            (isImage && Path.GetExtension(target).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{recipe.Path}: unsupported asset or asset outside recipes: '{url}'. Link to a discovered Markdown recipe or an image/PDF inside recipes.");
        var check = root;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.StartsWith('.'))
                throw new InvalidOperationException($"{recipe.Path}: asset URL uses a hidden path: {url}.");
            check = Path.Combine(check, part);
            RejectLink(check);
            if (Path.Exists(check) && (File.GetAttributes(check) & FileAttributes.Hidden) != 0)
                throw new InvalidOperationException($"{recipe.Path}: asset URL uses a hidden path: {url}.");
        }
        if (!File.Exists(target))
            throw new InvalidOperationException($"{recipe.Path}: linked asset does not exist: {url}.");
        var assetPath = Path.Combine("assets", withinRoot);
        assets[assetPath] = target;
        var encoded = new List<string> { "assets" };
        foreach (var part in parts) encoded.Add(Uri.EscapeDataString(part));
        return string.Join("/", encoded) + (fragment.Length == 0 ? "" : "#" + Uri.EscapeDataString(fragment));
    }

    private static void ValidateAbsoluteUrl(string url, bool isImage, string path)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http" && (isImage || uri.Scheme != "mailto")))
            throw new InvalidOperationException($"{path}: unsafe URL '{url}'. Use https, http, or mailto links.");
    }

    private static string RecipeFragment(Recipe target, string fragment, string sourcePath)
    {
        if (fragment.Length == 0 || Slug(fragment) == Slug(target.Title)) return "#" + target.Id;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var heading in target.Document.Descendants<HeadingBlock>())
        {
            var slug = Slug(PlainText(heading.Inline));
            var unique = slug;
            var suffix = 2;
            while (!ids.Add(unique)) unique = slug + "-" + suffix++;
            if (unique == Slug(fragment)) return "#" + target.Id + "-" + unique;
        }
        throw new InvalidOperationException($"{sourcePath}: missing section '{fragment}' in {target.Path}.");
    }

    private static string RenderCard(Recipe recipe)
    {
        var art = recipe.Category switch
        {
            "Appetizers" => "appetizers", "Desserts" => "desserts", "Drinks" => "drinks",
            "Sauces" => "sauces", _ => "meals"
        };
        var body = RenderBody(recipe.Document);
        return $"""
            <details class="recipe-card" id="{recipe.Id}" data-title="{Encode(recipe.Title)}" data-category="{Encode(recipe.Category)}">
              <summary>
                <div class="card-art" data-art="{art}" aria-hidden="true"><svg viewBox="0 0 160 125"><use href="#art-{art}"></use></svg></div>
                <div class="card-info"><span class="card-category">{Encode(recipe.Category)}</span>
                  <h3 class="recipe-title">{Encode(recipe.Title)}</h3>
                  <div class="card-bottom"><span>From our recipe box</span><span class="recipe-link">Open recipe</span></div>
                </div>
              </summary>
              <div class="recipe-content">{body}</div>
            </details>

            """;
    }

    private static string RenderBody(MarkdownDocument document)
    {
        if (document.Count > 0 && document[0] is HeadingBlock first &&
            PlainText(first.Inline).Equals("Ingredients", StringComparison.OrdinalIgnoreCase))
        {
            var end = 1;
            while (end < document.Count && (document[end] is not HeadingBlock next || next.Level > first.Level))
                end++;
            if (end < document.Count)
                return "<div class=\"recipe-columns\"><div class=\"recipe-ingredients\">" +
                    Render(document, 0, end) + "</div><div class=\"recipe-method\">" +
                    Render(document, end, document.Count) + "</div></div>";
        }
        return Render(document, 0, document.Count);
    }

    private static string Render(MarkdownDocument document, int start, int end)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        for (var index = start; index < end; index++)
            renderer.Render(document[index]);
        writer.Flush();
        return writer.ToString();
    }

    private static string Navigation(string category, string label, int count)
    {
        var icon = category switch
        {
            "all" => "&#9783;", "Meals" => "&#9832;", "Desserts" => "&#9825;",
            "Drinks" => "&#9749;", "Sauces" => "&#10087;", _ => "&#10035;"
        };
        return $"<li><a href=\"#recipes\" data-chapter=\"{Encode(category)}\"{(category == "all" ? " aria-current=\"page\"" : "")}><span class=\"nav-icon\" aria-hidden=\"true\">{icon}</span><span>{Encode(label)}</span><span class=\"nav-count\">{count}</span></a></li>";
    }

    private static string PlainText(ContainerInline? container)
    {
        var text = new StringBuilder();
        if (container is null) return "";
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal: text.Append(literal.Content); break;
                case CodeInline code: text.Append(code.Content); break;
                case LineBreakInline: text.Append(' '); break;
                case AutolinkInline link: text.Append(link.Url); break;
                case ContainerInline nested: text.Append(PlainText(nested)); break;
            }
        }
        return text.ToString();
    }

    private static string Slug(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character == '-')
                result.Append(character);
            else if (char.IsWhiteSpace(character))
                result.Append('-');
        }
        return DashRun().Replace(result.ToString(), "-").Trim('-');
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static bool EligibleSourcePath(string path)
    {
        if (Path.GetFileName(path).StartsWith('.')) return false;
        return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) == 0;
    }

    private static void FindMarkdown(string directory, List<string> files, bool recursive = true)
    {
        RejectLink(directory);
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) && EligibleSourcePath(path))
                files.Add(path);
        }
        if (!recursive) return;
        foreach (var child in Directory.EnumerateDirectories(directory))
            if (EligibleSourcePath(child)) FindMarkdown(child, files);
    }

    private static string ReadFile(string path)
    {
        RejectLink(path);
        return File.ReadAllText(path, new UTF8Encoding(false, true));
    }

    private static void RejectLink(string path)
    {
        if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Symbolic links and junctions are not supported: {path}.");
    }

    private static void EnsureOutputDirectory(string output, string root)
    {
        if (Path.GetDirectoryName(output) != root || Path.GetFileName(output) != "_site")
            throw new InvalidOperationException("Output must be the repository's _site directory.");
        RejectLink(output);
        Directory.CreateDirectory(output);
        ValidateOutputChildren(output);
    }

    private static void ValidateOutputChildren(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            RejectLink(child);
            ValidateOutputChildren(child);
        }
    }

    [GeneratedRegex(@"\{\{([^{}]*)\}\}")]
    private static partial Regex TemplateToken();

    [GeneratedRegex("-+")]
    private static partial Regex DashRun();
}
