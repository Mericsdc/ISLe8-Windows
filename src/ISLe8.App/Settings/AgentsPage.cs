using ISLe8.Agents;
using ISLe8.Core.Agents;
using ISLe8.Core.Usage;
using ISLe8.Localization;
using ISLe8.Shell;
using ISLe8.UI;
using ISLe8.Usage;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Agents, after the Mac's (CustomizationSettings.swift, AgentSettings). Hooks go in only
/// when the user presses Connect: those files affect every session on this computer. The usage
/// limits section comes with the limits themselves (PORTING.md §3.13).
/// </summary>
internal static class AgentsPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Agents")));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Agents_Connections")));
        var problem = new TextBlock { Foreground = new SolidColorBrush(Colors.DarkOrange), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var command = AgentCommand.Path();
        page.Children.Add(HookCard(context, AgentKind.Claude, "Agents_ClaudeNote", command, problem));
        page.Children.Add(HookCard(context, AgentKind.Codex, "Agents_CodexNote", command, problem));
        if (command is null)
        {
            problem.Text = L.Get("Agents_NoCommand");
            problem.Visibility = Visibility.Visible;
        }
        page.Children.Add(problem);
        page.Children.Add(SettingsUi.Description(palette, L.Get("Agents_ConnectionsNote")));

        page.Children.Add(UsageSection(context));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Agents_Approvals")));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Agents_AskOnIsland"), SettingsUi.Switch(settings.AgentApprovalsOnIsland, on =>
        {
            settings.AgentApprovalsOnIsland = on;
            context.Save();
        }), L.Get("Agents_AskOnIslandNote")));
        page.Children.Add(WidgetsPage.Stepper(context, "Agents_Timeout", settings.AgentApprovalTimeout, 15, 300, 15, seconds =>
        {
            settings.AgentApprovalTimeout = seconds;
            context.Save();
        }));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Agents_DoneAlert"), SettingsUi.Switch(settings.AgentDoneAlert, on =>
        {
            settings.AgentDoneAlert = on;
            context.Save();
        })));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Agents_CommandLine")));
        foreach (var example in new[] { "isle8 activity start --id derleme --title \"Derleme\" --progress 0.4", "isle8 activity end --id derleme" })
        {
            page.Children.Add(SettingsUi.Card(palette, new TextBlock
            {
                Text = example,
                FontFamily = AgentLook.Mono,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            }, new Border()));
        }
        page.Children.Add(SettingsUi.Description(palette, L.Get(AppPackage.IsInstalled ? "Agents_CommandLineNote" : "Agents_CommandLineNoteZip")));
        return page;
    }

    /// <summary>Settings › Agents › Usage limits (CustomizationSettings.swift, UsageSettings): on or off, each service, saver mode.</summary>
    private static StackPanel UsageSection(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var section = new StackPanel();
        section.Children.Add(SettingsUi.SectionHeader(L.Get("Usage_Section")));
        var details = new StackPanel { Visibility = settings.UsageEnabled ? Visibility.Visible : Visibility.Collapsed };
        section.Children.Add(SettingsUi.Card(palette, L.Get("Usage_Show"), SettingsUi.Switch(settings.UsageEnabled, on =>
        {
            settings.UsageEnabled = on;
            details.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            context.Save();
            context.Island.ApplyUsageSettings();
        }), L.Get("Usage_ShowNote")));
        foreach (var vendor in UsageVendors.All)
        {
            var id = UsageVendors.Id(vendor);
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (UsageLook.Mark(vendor, 14, context.Palette.SecondaryText) is { } mark)
            {
                mark.VerticalAlignment = VerticalAlignment.Center;
                heading.Children.Add(mark);
            }
            heading.Children.Add(new TextBlock { Text = UsageVendors.Product(vendor), VerticalAlignment = VerticalAlignment.Center });
            heading.Children.Add(new TextBlock { Text = $"({UsageVendors.Cli(vendor)})", FontFamily = AgentLook.Mono, Foreground = palette.SecondaryText, VerticalAlignment = VerticalAlignment.Center });
            // Only the CLIs found show on the island; here each says whether it was, and why not.
            var (found, path) = UsageCli.Find(vendor);
            var where = SettingsUi.Description(palette, found switch
            {
                UsageCli.Found.Ok => L.Format("Usage_CliFound", path ?? ""),
                UsageCli.Found.Untrusted => L.Format("Usage_CliUntrusted", path ?? ""),
                _ => L.Format("Usage_CliMissing", UsageVendors.Cli(vendor)),
            });
            if (found != UsageCli.Found.Ok) where.Foreground = new SolidColorBrush(Colors.DarkOrange);
            details.Children.Add(SettingsUi.Card(palette, heading, SettingsUi.Switch(!settings.UsageDisabledVendors.Contains(id), on =>
            {
                settings.UsageDisabledVendors.Remove(id);
                if (!on) settings.UsageDisabledVendors.Add(id);
                context.Save();
                context.Island.ApplyUsageSettings();
            }), where));
        }
        details.Children.Add(SettingsUi.Card(palette, L.Get("Usage_Saver"), SettingsUi.Switch(settings.UsageSaverMode, on =>
        {
            settings.UsageSaverMode = on;
            context.Save();
            context.Island.ApplyUsageSettings();
        }), L.Get("Usage_SaverNote")));
        section.Children.Add(details);
        section.Children.Add(SettingsUi.Description(palette, L.Get("Usage_SectionNote")));
        return section;
    }

    /// <summary>One agent: its icon and name, Connected and Remove, or Connect, and where it goes.</summary>
    private static Border HookCard(SettingsContext context, AgentKind agent, string noteKey, string? command, TextBlock problem)
    {
        var file = AgentHooks.File(agent, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        void Fill()
        {
            controls.Children.Clear();
            bool installed;
            try
            {
                installed = AgentHooks.IsInstalled(file);
            }
            catch (InvalidDataException e)
            {
                installed = false;
                Show(e.Message);
            }
            if (installed)
            {
                controls.Children.Add(new TextBlock { Text = L.Get("Agents_Connected"), Foreground = context.Palette.SecondaryText, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Content = L.Get("Agents_Remove") };
                remove.Click += (_, _) => Change(() => AgentHooks.Uninstall(file));
                controls.Children.Add(remove);
            }
            else
            {
                var connect = new Button { Content = L.Get("Agents_Connect"), IsEnabled = command is not null };
                connect.Click += (_, _) => Change(() => AgentHooks.Install(agent, file, command!));
                controls.Children.Add(connect);
            }
        }
        void Show(string message)
        {
            problem.Text = L.Format("Agents_ChangeFailed", message);
            problem.Visibility = Visibility.Visible;
        }
        void Change(Action change)
        {
            try
            {
                change();
                problem.Visibility = Visibility.Collapsed;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Show(e.Message);
            }
            Fill();
        }
        Fill();
        var icon = AgentLook.Icon(agent, 20);
        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { icon, new TextBlock { Text = AgentKinds.Name(agent), VerticalAlignment = VerticalAlignment.Center } },
        };
        return SettingsUi.Card(context.Palette, heading, controls, SettingsUi.Description(context.Palette, L.Get(noteKey)));
    }
}

/// <summary>
/// The command the hooks run. The installed ISLe8 has the app execution alias, the same for every
/// version; the zip has isle8-cli.exe beside ISLe8.exe. Null when neither is there (a development build).
/// </summary>
internal static class AgentCommand
{
    public static string? Path()
    {
        if (AppPackage.IsInstalled)
        {
            var alias = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "isle8.exe");
            if (File.Exists(alias)) return alias;
        }
        var local = System.IO.Path.Combine(AppContext.BaseDirectory, "isle8-cli.exe");
        return File.Exists(local) ? local : null;
    }
}
