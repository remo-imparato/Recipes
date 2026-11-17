using RecipeSiteBuilder;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/RecipeSiteBuilder -- <repository-root>");
    return 1;
}

try
{
    var result = SiteBuilder.Build(args[0]);
    Console.WriteLine($"Built {result.RecipeCount} recipes in {result.CategoryCount} categories: {result.OutputDirectory}");
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"Recipe website build failed: {exception.Message}");
    return 1;
}
