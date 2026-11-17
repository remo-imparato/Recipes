using System.Text.RegularExpressions;
using RecipeSiteBuilder;

namespace RecipeSiteBuilder.Tests;

[TestClass]
public sealed class SiteBuilderTests
{
    private string root = "";

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "RecipeSiteTests-" + Guid.NewGuid().ToString("N"));
        Write("website/index.template.html", "{{NAVIGATION}}{{CATEGORY_TABS}}{{RECIPE_COUNT}}|{{CATEGORY_COUNT}}|{{RECIPE_CARDS}}");
        Write("website/site.css", "/* fixture */");
        Write("website/site.js", "// fixture");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public void DiscoversCategoriesAndNestedRecipesWhileIgnoringInfrastructure()
    {
        Write("Apps/Starter.md", "# Starter\n\n## Ingredients\n- Salt\n");
        Write("Dessert/nested/Sweet.MD", "# Sweet\n");
        Write("Breakfast/Eggs.md", "# Eggs\n");
        foreach (var ignored in new[] { "README.md", "archive/old.md", "tools/example.md", "tests/example.md", "_site/stale.md", ".private/secret.md", "Meals/.private/secret.md", "Meals/bin/ignored.md" })
            Write(ignored, "not a recipe");
        Write("Meals/Word only.docx", "not a recipe");

        var result = SiteBuilder.Build(root);
        var html = Output();

        Assert.AreEqual(3, result.RecipeCount);
        Assert.AreEqual(3, result.CategoryCount);
        StringAssert.Contains(html, "data-category=\"Appetizers\"");
        StringAssert.Contains(html, "data-category=\"Desserts\"");
        StringAssert.Contains(html, "data-category=\"Breakfast\"");
        Assert.IsLessThan(html.IndexOf("id=\"recipe-eggs\"", StringComparison.Ordinal), html.IndexOf("id=\"recipe-sweet\"", StringComparison.Ordinal));
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "stale.md")));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "_site", "archive")));
    }

    [TestMethod]
    public void AddEditDeleteAndRepeatBuildsHaveNoStaleRecipes()
    {
        Write("Meals/one.md", "# One\n\nOriginal");
        SiteBuilder.Build(root);
        var first = Output();
        SiteBuilder.Build(root);
        Assert.AreEqual(first, Output());
        Write("Meals/one.md", "# One\n\nUpdated");
        Write("New chapter/two.md", "# Two\n");
        Assert.AreEqual(2, SiteBuilder.Build(root).RecipeCount);
        StringAssert.Contains(Output(), "Updated");
        Assert.IsFalse(Output().Contains("Original", StringComparison.Ordinal));
        File.Delete(Path.Combine(root, "Meals", "one.md"));
        Assert.AreEqual(1, SiteBuilder.Build(root).RecipeCount);
        Assert.IsFalse(Output().Contains("recipe-one", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PreservesRichSectionsAndMarksOnlySemanticIngredientLists()
    {
        Write("Meals/meal.md", """
            # Café & *Cake*

            Intro **important**.

            ## Ingredients
            - flour
              - optional detail

            ## Directions
            1. Mix.

            ## Variations
            ### Ingredients
            - cocoa

            ### Directions
            1. Stir.

            > Family note.

            ## Source
            [Original](https://example.com/recipe)
            """);

        SiteBuilder.Build(root);
        var html = Output();

        StringAssert.Contains(html, "id=\"recipe-cafe-cake\"");
        StringAssert.Contains(html, "Caf&#233; &amp; Cake");
        StringAssert.Contains(html, "<strong>important</strong>");
        StringAssert.Contains(html, "Variations");
        StringAssert.Contains(html, "Family note.");
        StringAssert.Contains(html, "<blockquote>");
        StringAssert.Contains(html, "https://example.com/recipe");
        Assert.HasCount(2, Regex.Matches(html, "class=\"recipe-ingredient-items\""));
        StringAssert.Contains(html, "<ol>");
        Assert.IsFalse(html.Contains("<h1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EscapesRawHtmlAndKeepsRecipeTextOutOfTemplateProcessing()
    {
        Write("Meals/safe.md", """
            # Safe "title"

            <script>alert('unsafe')</script>

            {{RECIPE_COUNT}} {{UNKNOWN}}
            """);
        SiteBuilder.Build(root);
        var html = Output();
        Assert.IsFalse(html.Contains("<script>", StringComparison.Ordinal));
        StringAssert.Contains(html, "data-title=\"Safe &quot;title&quot;\"");
        StringAssert.Contains(html, "{{RECIPE_COUNT}} {{UNKNOWN}}");
    }

    [TestMethod]
    public void PreservesLegacyLinksForMatchingStandaloneRecipes()
    {
        Write("Meals/pot.md", "# Pot Roast \u2013 In a Crock Pot");
        Write("Drinks/coffee.md", "# Coffee - French Press Cold Brew");
        Write("Drinks/whipped.md", "# Coffee - Instant - Whipped COffee");
        Write("Dessert/pie.md", "# Pumpkin Pie");
        Write("Sauces/gravy.md", "# Simple Brown Gravy");
        SiteBuilder.Build(root);
        foreach (var id in new[] { "pot-roast-in-a-crock-pot", "coffee-french-press-cold-brew", "coffee-instant-whipped-coffee", "pumpkin-pie-remo", "gravy" })
            StringAssert.Contains(Output(), $"id=\"recipe-{id}\"");
    }

    [TestMethod]
    public void IngredientSubsectionsGetCheckboxListsButNestedDetailsDoNot()
    {
        Write("Dessert/pie.md", """
            # Pie

            ## Ingredients
            ### Filling
            - fruit
              - optional detail
            ### Crust
            - flour

            ## Directions
            - Mix.
            """);
        SiteBuilder.Build(root);
        Assert.HasCount(2, Regex.Matches(Output(), "class=\"recipe-ingredient-items\""));
    }

    [TestMethod]
    [DataRow("[bad](javascript:alert%281%29)")]
    [DataRow("[bad](data:text/html,test)")]
    [DataRow("![bad](file:///private.png)")]
    [DataRow("[bad](//example.com/test)")]
    [DataRow("[bad](../../outside.pdf)")]
    [DataRow("[bad](missing.pdf)")]
    [DataRow("[bad](#missing)")]
    public void RejectsUnsafeOrBrokenLinks(string markdown)
    {
        Write("Meals/bad.md", "# Bad\n\n" + markdown);
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "bad.md");
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "index.html")));
    }

    [TestMethod]
    public void ResolvesRelativeRecipeSectionsAndCopiesOnlyReferencedAssets()
    {
        Write("Meals/First dish.md", """
            # First

            [Next](../Dessert/Second%20dish.md#directions)
            [Top](#first)
            ![Photo](pictures/meal%20photo.png)
            [Notes](pictures/notes.pdf)
            """);
        Write("Dessert/Second dish.md", "# Second\n\n## Directions\n1. Serve.\n");
        Write("Meals/pictures/meal photo.png", "fixture image bytes");
        Write("Meals/pictures/notes.pdf", "fixture PDF bytes");
        Write("Meals/pictures/not-used.png", "not copied");
        SiteBuilder.Build(root);

        StringAssert.Contains(Output(), "href=\"#recipe-second-directions\"");
        StringAssert.Contains(Output(), "href=\"#recipe-first\"");
        StringAssert.Contains(Output(), "src=\"assets/Meals/pictures/meal%20photo.png\"");
        Assert.AreEqual("fixture image bytes", File.ReadAllText(Path.Combine(root, "_site", "assets", "Meals", "pictures", "meal photo.png")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "assets", "Meals", "pictures", "not-used.png")));
        File.Delete(Path.Combine(root, "Meals", "First dish.md"));
        SiteBuilder.Build(root);
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "assets", "Meals", "pictures", "meal photo.png")));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("# ")]
    [DataRow("## Missing title")]
    public void ReportsMissingTitlesWithSourcePath(string markdown)
    {
        Write("Meals/broken.md", markdown);
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "broken.md");
    }

    [TestMethod]
    public void RejectsCollidingRecipeLinksAndCategoryLabels()
    {
        Write("Meals/one.md", "# Same!");
        Write("Meals/two.md", "# Same");
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "conflicts");
        File.Delete(Path.Combine(root, "Meals", "two.md"));
        Write("Apps/a.md", "# A");
        Write("Appetizers/b.md", "# B");
        exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "Category label");
    }

    [TestMethod]
    public void FailedBuildPreservesPreviouslyGeneratedSite()
    {
        Write("Meals/one.md", "# One\n\nGood content");
        SiteBuilder.Build(root);
        var original = Output();
        Write("Meals/broken.md", "missing heading");
        Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        Assert.AreEqual(original, Output());
    }

    [TestMethod]
    public void RejectsUnknownOrMissingTemplatePlaceholders()
    {
        Write("Meals/one.md", "# One");
        Write("website/index.template.html", "{{NAVIGATION}}{{CATEGORY_TABS}}{{RECIPE_COUNT}}{{CATEGORY_COUNT}}{{RECIPE_CARDS}}{{UNKNOWN}}");
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "UNKNOWN");
        Write("website/index.template.html", "{{NAVIGATION}}{{CATEGORY_TABS}}{{RECIPE_COUNT}}{{CATEGORY_COUNT}}{{RECIPE_CARDS}}{{unknown}}");
        exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "unknown");
        Write("website/index.template.html", "no placeholders");
        exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "missing required placeholder");
    }

    private string Output() => File.ReadAllText(Path.Combine(root, "_site", "index.html"));

    private void Write(string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
