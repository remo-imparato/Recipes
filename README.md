# The Family Table

A family recipe collection, maintained as individual Markdown files.

**Website:** [remo-imparato.github.io/Recipes](https://remo-imparato.github.io/Recipes/)

The website is generated automatically after changes are committed and pushed to `main`. The address becomes available after the first successful GitHub Pages deployment.

## Editing recipes

Edit the individual `.md` files in the category folders. Start each recipe with one title:

```markdown
# Recipe name

## Ingredients
- First ingredient
- Second ingredient

## Directions
1. First step.
2. Next step. 

## Notes
Optional variations, sources, or family notes.
```

Additional sections and nested headings are supported. Ingredient checkboxes are added to unordered lists within an `Ingredients` section, including its subsections, but not nested detail lists. Keep recipe titles distinct: punctuation and accents are normalized when generating recipe links. Existing shared recipe links are preserved.

- **Add:** save a Markdown recipe in a category folder.
- **Edit:** change its Markdown file.
- **Delete:** remove its Markdown file.
- **Rename:** a file can be renamed without changing its link if its title stays the same. Changing the title can change its website link.
- **New category:** create a top-level folder containing Markdown recipes. Its name becomes the category label.

Subfolders within a category are supported. `Apps` displays as **Appetizers** and `Dessert` as **Desserts**. The original category order is preserved; new categories follow alphabetically. Do not create a second folder with the same displayed category label or name a category `all`.

Root-level Markdown files are not recipe inputs. Hidden folders and these reserved folders are excluded at every level: `archive`, `website`, `tools`, `tests`, `_site`, `bin`, `obj`, `TestResults`, and `node_modules`.

Only standalone recipe Markdown is authoritative. Original combined documents are preserved in [archive](archive/), but are not published or synchronized. All Word documents remain unchanged; Word-only recipes must be converted to Markdown to appear on the website. Do not edit the generated website to update a recipe.

### Links and pictures

Use ordinary Markdown links. Relative links to another discovered recipe become website recipe links; section links are supported:

```markdown
[Another recipe](../Dessert/Apple%20Crisp.md)
[Directions](#directions)
![Recipe photo](pictures/my%20photo.jpg)
```

Referenced PNG, JPEG, GIF, WebP, AVIF, and PDF files inside eligible category folders are copied into the published site. Other files are not copied. Use forward slashes in Markdown URLs and URL-encode special characters in filenames.

HTTP/HTTPS links and email links are supported; images may use HTTP/HTTPS. Raw HTML is displayed as text, not executed. Unsafe URLs, missing local targets, and invalid recipe links fail the build with the source filename. Use Markdown rather than embedded HTML.

## Publishing

One-time repository setup:

1. In GitHub, open **Settings → Pages**.
2. Set **Build and deployment → Source** to **GitHub Actions**.
3. Commit and push the implementation to `main`.
4. Open **Actions → Build and publish recipe website** and check that build and deployment succeed. Approve the `github-pages` environment if required.
5. Open the published URL above.

After setup, edit/add/delete recipe files, commit, and push to `main`. GitHub automatically tests, rebuilds, and publishes the website. Saving a file only on your computer does not publish it.

Use **Actions → Build and publish recipe website → Run workflow** to rebuild manually. A failed build does not replace the live site. Read the failed step's log for the filename and error; fix the source and push again. Duplicate recipe titles, missing title headings, broken local links, and unsafe URLs are reported explicitly.

The published artifact contains only generated HTML, CSS, JavaScript, and referenced images/PDFs. It excludes archives, source Word documents, build tools, and tests. The original source files may still be visible on GitHub if this repository is public; this is not a private sharing system.

## Printing and saving PDF

- Open a recipe and choose **Print this recipe**.
- Choose **Print selected recipes (N)** to print the recipes matching your current category and search, in the current sort order.
- To print the entire cookbook, clear filters first.
- Select **Save as PDF** in your browser's print dialog to create a shareable PDF.

Collapsed recipes are expanded for printing; navigation and controls are omitted. Checked ingredients still print normally. Printing or cancelling restores the previous expanded cards and filters. Browser print uses the current filtered selection too.

Without JavaScript, recipes remain readable and can be expanded manually; interactive search, checkboxes, and print buttons require JavaScript.

## Local build and tests

Install the .NET 10 SDK selected by [global.json](global.json). No Node.js, database, or web server is required to build.

From the repository root in PowerShell:

```powershell
dotnet restore tests\RecipeSiteBuilder.Tests\RecipeSiteBuilder.Tests.csproj --locked-mode
dotnet test tests\RecipeSiteBuilder.Tests\RecipeSiteBuilder.Tests.csproj --configuration Release --no-restore
dotnet run --project tools\RecipeSiteBuilder --configuration Release --no-restore -- .
Start-Process .\_site\index.html
```

The builder creates `_site`, which is ignored by Git and regenerated on each successful build. Do not put manually maintained files there. Dependencies are locked in committed NuGet lock files.

The static output also works beneath the GitHub Pages `/Recipes/` path. Website layout and behavior live in [website](website/); the Markdown renderer uses the build-only Markdig dependency. Builder regression tests live in [tests/RecipeSiteBuilder.Tests](tests/RecipeSiteBuilder.Tests/).
