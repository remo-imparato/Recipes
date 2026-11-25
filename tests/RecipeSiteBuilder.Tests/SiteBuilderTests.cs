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
        Write("recipes/Appetizers/Starter.md", "# Starter\n\n## Ingredients\n- Salt\n");
        Write("recipes/Desserts/nested/Sweet.MD", "# Sweet\n");
        Write("recipes/Breakfast/Eggs.md", "# Eggs\n");
        Write("Meals/root-level.md", "# Ignored");
        foreach (var ignored in new[] { "README.md", "archive/old.md", "tools/example.md", "tests/example.md", "_site/stale.md", ".private/secret.md", "recipes/.private/secret.md", "recipes/.hidden.md", "recipes/Meals/.private/secret.md" })
            Write(ignored, "not a recipe");
        Write("recipes/Meals/Word only.docx", "not a recipe");

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
    public void UsesFolderNamesAsCategoryLabelsWithoutAliases()
    {
        Write("recipes/Apps/legacy.md", "# Legacy appetizer");
        Write("recipes/Appetizers/starter.md", "# Starter");
        Write("recipes/Dessert/legacy.md", "# Legacy dessert");
        Write("recipes/Desserts/sweet.md", "# Sweet");
        Write("recipes/Sauces/sauce.md", "# Sauce");
        Write("recipes/Spices/spice.md", "# Spice");

        var result = SiteBuilder.Build(root);
        var html = Output();

        Assert.AreEqual(6, result.CategoryCount);
        foreach (var category in new[] { "Apps", "Appetizers", "Dessert", "Desserts", "Sauces", "Spices" })
        {
            StringAssert.Contains(html, $"data-category=\"{category}\"");
            StringAssert.Contains(html, $"data-chapter=\"{category}\"");
        }
        Assert.IsFalse(html.Contains("Sauces", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UsesCategorySpecificArtworkAndNeutralArtworkForUnknownCategories()
    {
        foreach (var category in new[] { "Appetizers", "Meals", "Desserts", "Drinks", "Sauces", "Soups", "Spices", "Breakfast" })
            Write($"recipes/{category}/recipe.md", $"# {category} recipe");

        SiteBuilder.Build(root);
        var html = Output().Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (var category in new[] { "Appetizers", "Meals", "Desserts", "Drinks", "Sauces", "Soups", "Spices", "Breakfast" })
        {
            var art = category == "Breakfast" ? "general" : category.ToLowerInvariant();
            StringAssert.Contains(html, $"data-category=\"{category}\">\n  <summary>\n    <div class=\"card-art\" data-art=\"{art}\"");
            StringAssert.Contains(html, $"href=\"#art-{art}\"");
        }
    }

    [TestMethod]
    public void DiscoversAllOrdinaryFoldersAndKeepsNestedRecipesInTopLevelCategories()
    {
        Write("recipes/archive/old.md", "# Old\n\n![Photo](../Meals/bin/photo.png)");
        Write("recipes/tools/deep/nested/recipe.MD", "# Nested");
        Write("recipes/Meals/bin/meal.md", "# Meal");
        Write("recipes/tests/recipe.md", "# Test");
        Write("recipes/Meals/bin/photo.png", "fixture image bytes");

        var result = SiteBuilder.Build(root);
        var html = Output();

        Assert.AreEqual(4, result.RecipeCount);
        Assert.AreEqual(4, result.CategoryCount);
        StringAssert.Contains(html, "id=\"recipe-old\" data-title=\"Old\" data-category=\"archive\"");
        StringAssert.Contains(html, "id=\"recipe-nested\" data-title=\"Nested\" data-category=\"tools\"");
        StringAssert.Contains(html, "id=\"recipe-meal\" data-title=\"Meal\" data-category=\"Meals\"");
        StringAssert.Contains(html, "id=\"recipe-test\" data-title=\"Test\" data-category=\"tests\"");
        StringAssert.Contains(html, "src=\"assets/Meals/bin/photo.png\"");
    }

    [TestMethod]
    public void DiscoversRootRecipesAndResolvesTheirLinksAndAssets()
    {
        Write("recipes/first.MD", "# First\n\n[Next](Meals/second.md)\n![Photo](photo.png)");
        Write("recipes/photo.png", "fixture image bytes");
        Write("recipes/Meals/second.md", "# Second\n\n[First](../first.MD)\n![Photo](../photo.png)");

        var result = SiteBuilder.Build(root);
        var html = Output();

        Assert.AreEqual(2, result.RecipeCount);
        Assert.AreEqual(2, result.CategoryCount);
        StringAssert.Contains(html, "id=\"recipe-first\" data-title=\"First\" data-category=\"General\"");
        StringAssert.Contains(html, "href=\"#recipe-first\"");
        StringAssert.Contains(html, "href=\"#recipe-second\"");
        StringAssert.Contains(html, "src=\"assets/photo.png\"");
        Assert.AreEqual("fixture image bytes", File.ReadAllText(Path.Combine(root, "_site", "assets", "photo.png")));
    }

    [TestMethod]
    public void BuildsRootRecipesWithoutAnyCategoryFolders()
    {
        Write("recipes/only.md", "# Only");

        var result = SiteBuilder.Build(root);

        Assert.AreEqual(1, result.RecipeCount);
        Assert.AreEqual(1, result.CategoryCount);
        StringAssert.Contains(Output(), "data-category=\"General\"");
    }

    [TestMethod]
    public void RejectsExistingAssetsOutsideRecipes()
    {
        Write("recipes/only.md", "# Only\n\n![Photo](../photo.png)");
        Write("photo.png", "outside image bytes");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));

        StringAssert.Contains(exception.Message, "outside recipes");
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "index.html")));
    }

    [TestMethod]
    public void AddEditDeleteAndRepeatBuildsHaveNoStaleRecipes()
    {
        Write("recipes/Meals/one.md", "# One\n\nOriginal");
        SiteBuilder.Build(root);
        var first = Output();
        SiteBuilder.Build(root);
        Assert.AreEqual(first, Output());
        Write("recipes/Meals/one.md", "# One\n\nUpdated");
        Write("recipes/New chapter/deep/two.md", "# Two\n");
        Write("recipes/three.md", "# Three");
        var result = SiteBuilder.Build(root);
        Assert.AreEqual(3, result.RecipeCount);
        Assert.AreEqual(3, result.CategoryCount);
        StringAssert.Contains(Output(), "data-category=\"New chapter\"");
        StringAssert.Contains(Output(), "id=\"recipe-two\"");
        StringAssert.Contains(Output(), "data-category=\"General\"");
        StringAssert.Contains(Output(), "Updated");
        Assert.IsFalse(Output().Contains("Original", StringComparison.Ordinal));
        File.Delete(Path.Combine(root, "recipes", "Meals", "one.md"));
        Assert.AreEqual(2, SiteBuilder.Build(root).RecipeCount);
        Assert.IsFalse(Output().Contains("recipe-one", StringComparison.Ordinal));
        Assert.IsFalse(Output().Contains("data-category=\"Meals\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PreservesRichSectionsAndMarksOnlySemanticIngredientLists()
    {
        Write("recipes/Meals/meal.md", """
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
        Write("recipes/Meals/safe.md", """
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
        Write("recipes/Meals/pot.md", "# Pot Roast \u2013 In a Crock Pot");
        Write("recipes/Drinks/coffee.md", "# Coffee - French Press Cold Brew");
        Write("recipes/Drinks/whipped.md", "# Coffee - Instant - Whipped COffee");
        Write("recipes/Dessert/pie.md", "# Pumpkin Pie");
        Write("recipes/Sauces/gravy.md", "# Simple Brown Gravy");
        SiteBuilder.Build(root);
        foreach (var id in new[] { "pot-roast-in-a-crock-pot", "coffee-french-press-cold-brew", "coffee-instant-whipped-coffee", "pumpkin-pie-remo", "gravy" })
            StringAssert.Contains(Output(), $"id=\"recipe-{id}\"");
    }

    [TestMethod]
    public void IngredientSubsectionsGetCheckboxListsButNestedDetailsDoNot()
    {
        Write("recipes/Dessert/pie.md", """
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
        Write("recipes/Meals/bad.md", "# Bad\n\n" + markdown);
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "bad.md");
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "index.html")));
    }

    [TestMethod]
    public void ResolvesRelativeRecipeSectionsAndCopiesOnlyReferencedAssets()
    {
        Write("recipes/Meals/First dish.md", """
            # First

            [Next](../Dessert/Second%20dish.md#directions)
            [Top](#first)
            ![Photo](pictures/meal%20photo.png)
            [Notes](pictures/notes.pdf)
            """);
        Write("recipes/Dessert/Second dish.md", "# Second\n\n## Directions\n1. Serve.\n");
        Write("recipes/Meals/pictures/meal photo.png", "fixture image bytes");
        Write("recipes/Meals/pictures/notes.pdf", "fixture PDF bytes");
        Write("recipes/Meals/pictures/not-used.png", "not copied");
        SiteBuilder.Build(root);

        StringAssert.Contains(Output(), "href=\"#recipe-second-directions\"");
        StringAssert.Contains(Output(), "href=\"#recipe-first\"");
        StringAssert.Contains(Output(), "src=\"assets/Meals/pictures/meal%20photo.png\"");
        Assert.AreEqual("fixture image bytes", File.ReadAllText(Path.Combine(root, "_site", "assets", "Meals", "pictures", "meal photo.png")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "assets", "Meals", "pictures", "not-used.png")));
        File.Delete(Path.Combine(root, "recipes", "Meals", "First dish.md"));
        SiteBuilder.Build(root);
        Assert.IsFalse(File.Exists(Path.Combine(root, "_site", "assets", "Meals", "pictures", "meal photo.png")));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("# ")]
    [DataRow("## Missing title")]
    public void ReportsMissingTitlesWithSourcePath(string markdown)
    {
        Write("recipes/Meals/broken.md", markdown);
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "broken.md");
    }

    [TestMethod]
    public void RejectsCollidingRecipeLinksAndCategoryLabels()
    {
        Write("recipes/Meals/one.md", "# Same!");
        Write("recipes/Meals/two.md", "# Same");
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "conflicts");
        File.Delete(Path.Combine(root, "recipes", "Meals", "two.md"));
        Write("recipes/General/a.md", "# A");
        Write("recipes/b.md", "# B");
        exception = Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        StringAssert.Contains(exception.Message, "Category label");
    }

    [TestMethod]
    public void FailedBuildPreservesPreviouslyGeneratedSite()
    {
        Write("recipes/Meals/one.md", "# One\n\nGood content");
        SiteBuilder.Build(root);
        var original = Output();
        Write("recipes/Meals/broken.md", "missing heading");
        Assert.ThrowsExactly<InvalidOperationException>(() => SiteBuilder.Build(root));
        Assert.AreEqual(original, Output());
    }

    [TestMethod]
    public void RejectsUnknownOrMissingTemplatePlaceholders()
    {
        Write("recipes/Meals/one.md", "# One");
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
