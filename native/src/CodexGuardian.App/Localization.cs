using System.Globalization;
using System.Windows;

namespace CodexGuardian.App;

public static class Localization
{
    private const string ResourcePrefix = "Resources/Strings.";

    public static string LanguageCode { get; private set; } = "en-US";

    public static void Apply(string languageCode)
    {
        LanguageCode = languageCode == "zh-CN" ? "zh-CN" : "en-US";
        var resources = System.Windows.Application.Current.Resources.MergedDictionaries;
        var existing = resources.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains(ResourcePrefix, StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"{ResourcePrefix}{LanguageCode}.xaml", UriKind.Relative),
        };
        if (existing is null) resources.Insert(0, replacement);
        else resources[resources.IndexOf(existing)] = replacement;

        var culture = CultureInfo.GetCultureInfo(LanguageCode);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static string Text(string key, params object[] arguments)
    {
        var template = System.Windows.Application.Current.TryFindResource(key) as string ?? key;
        return arguments.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, arguments);
    }
}
