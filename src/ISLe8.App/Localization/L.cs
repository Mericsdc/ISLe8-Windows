using System.Globalization;
using ISLe8.Core.Diagnostics;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ISLe8.Localization;

/// <summary>
/// Strings from Strings/&lt;language&gt;/Resources.resw. "system" follows Windows' display language;
/// "tr-TR" or "en-US" pins one, whatever Windows uses.
/// </summary>
internal static class L
{
    private static ResourceManager? s_manager;
    private static ResourceContext? s_context;
    private static ResourceMap? s_map;

    public static string Language { get; private set; } = "system";

    /// <summary>Numbers are written the way the island's language writes them: "0,35" in Turkish, "0.35" in English.</summary>
    public static CultureInfo Culture => Language == "system" ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(Language);

    public static void Use(string language)
    {
        Language = language;
        try
        {
            s_manager ??= new ResourceManager();
            s_context = s_manager.CreateResourceContext();
            if (language != "system") s_context.QualifierValues["Language"] = language;
            s_map = s_manager.MainResourceMap.GetSubtree("Resources");
        }
        catch (Exception e)
        {
            Log.Error(e, "resources");
            s_map = null;
        }
    }

    /// <summary>The string for <paramref name="key"/>, or the key itself if it is missing.</summary>
    public static string Get(string key)
    {
        try
        {
            return s_map?.TryGetValue(key, s_context)?.ValueAsString ?? key;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args) =>
        string.Format(Culture, Get(key), args);
}
