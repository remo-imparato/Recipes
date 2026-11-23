# The Imparato Family Cookbook*

Mostly Accurate Recipes Since Whenever Grandma Wrote Them Down

A family recipe collection maintained as Markdown files.

**Website:** [remo-imparato.github.io/Recipes](https://remo-imparato.github.io/Recipes/)

## Recipes

Add or edit `.md` files under `recipes/`. Each recipe must start with a non-empty `# Recipe title` heading. For example:

```markdown
# Recipe name

## Ingredients
- First ingredient
- Second ingredient

## Directions
1. First step.
2. Next step.
```

The first folder under `recipes/` sets the category; nested folders stay in that category. For example, `recipes/Meals/Soups/soup.md` appears under **Meals**. `Apps` displays as **Appetizers** and `Dessert` as **Desserts**. Files directly under `recipes/` appear under **General**; do not also add a `General` folder containing recipes. New categories need no code changes. Category labels must be unique, and `all` is reserved.

Keep recipe titles distinct because punctuation and accents are normalized when creating links. Renaming a file without changing its title preserves its link; changing the title may change it. Ingredient lists under an `Ingredients` section get checkboxes automatically.

Only Markdown recipes under `recipes/` are published. The combined source documents in [archive](archive/) are not synchronized. Do not edit the generated `_site/` files.

Use ordinary Markdown links. Relative links to recipes and section headings are supported. Referenced PNG, JPEG, GIF, WebP, AVIF, and PDF files under `recipes/` are copied to the site; other local files are not. Use forward slashes in Markdown URLs and URL-encode special characters in filenames. Invalid or unsafe links and missing local targets fail the build.

### Submit a recipe through GitHub

Open **Issues → New issue → Add a recipe** and fill in the form. Keep the `[Recipe]` title prefix. Requests opened by someone with write access are assigned to GitHub Copilot automatically. For outside submissions, a maintainer should review the issue and apply `recipe-approved` to start Copilot. Do not approve requests that ask for existing files to be changed.

Copilot works on a branch from the default branch and prepares a draft pull request containing up to four new recipes. It will not make up missing recipe details or replace a duplicate. The [add-only check](.github/workflows/check-copilot-recipe.yml) flags any same-repository `copilot/` branch PR into `main` that changes anything other than one to four new Markdown files in existing recipe categories. Review the recipes and their diff before merging. Nothing merges automatically.

To enable this after merging the [assignment workflow](.github/workflows/assign-recipe-issue.yml) into `main`:

1. Enable GitHub Copilot cloud agent for the repository and create the `recipe-approved` issue label.
2. Create a fine-grained personal access token from an account with Copilot cloud agent and write access. Limit it to this repository, with **Metadata: Read** and **Actions, Contents, Issues, and Pull requests: Read and write**, as [required by GitHub](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/use-cloud-agent-via-the-api#assigning-an-existing-issue).
3. Store the token under **Settings → Secrets and variables → Actions → New repository secret** as `RECIPE_COPILOT_TOKEN`. Do not put it in an issue or a file.
4. Protect `main` with a rule requiring a pull request and human approval. On Copilot PRs, verify that the add-only check passed for the latest changes before approving the merge.

## Publishing

The [GitHub Actions workflow](.github/workflows/pages.yml) tests and builds the site on pushes to `main`; it can also be run manually from **Actions → Build and publish recipe website → Run workflow**. For first-time setup, set **Settings → Pages → Build and deployment → Source** to **GitHub Actions**. A failed build is not deployed.

## Local build and tests

Install the .NET 10 SDK specified in [global.json](global.json). From the repository root in PowerShell:

```powershell
dotnet restore Recipes.sln --locked-mode
dotnet build Recipes.sln --configuration Release --no-restore
dotnet test Recipes.sln --configuration Release --no-build --no-restore
Start-Process .\_site\index.html
```

The solution build generates the site in `_site/`. Build again after changing recipes or files in [website](website/). The output is ignored by Git and regenerated on each successful build; do not edit it manually. To generate the site without building the solution, run `dotnet run --project tools\RecipeSiteBuilder --configuration Release --no-restore -- .` after restoring dependencies.

Recipes can be printed individually or as a filtered selection using the site’s print controls. The print layout requests a 3.5-by-5-inch portrait page, with each recipe starting on a new card; longer recipes continue on additional cards. Choose **Save as PDF** in the browser’s print dialog to save a copy. Browser and printer settings may override the requested paper size; select 4 by 5 inches (or a custom size) and disable headers and footers if needed.
