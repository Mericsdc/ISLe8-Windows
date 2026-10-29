using ISLe8.Agents;
using ISLe8.Core.Agents;
using ISLe8.Localization;
using ISLe8.Shell;
using ISLe8.UI;
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
