using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private bool show_everything = false;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin, string goatImagePath)
        : base("MATUNO IS A STALKER##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        GoatImagePath = goatImagePath;
        Plugin = plugin;
    }

    public void Dispose() { }
        String search_text = "";

    public override void Draw()
    {
        if (ImGui.Button("DEW IT"))
        {
            Plugin.Snoop();
        }
        ImGui.SameLine();
        if (Plugin.accounts.Count == 0)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button("DUMP IT"))
        {
            Plugin.Dump("stalk_backup.csv");
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (Plugin.accounts.Count != 0)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button("RESTORE IT"))
        {
            Plugin.Restore();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("deleet"))
        {
            ImGui.OpenPopup("DeletingWindow");
        }
        if (ImGui.BeginPopup("DeletingWindow"))
        {
            ImGui.Button("NO");
            ImGui.Button("NO");
            if (ImGui.Button("yes"))
            {
                Plugin.Destroy();
            }
            ImGui.Button("NO");
            ImGui.Button("NO");
            ImGui.Button("NO");

            ImGui.EndPopup();
        }
        ImGui.Checkbox("SHOW EVERYTHING", ref show_everything);

        ImGui.Text($"SNOOPED ACCOUNTS: {Plugin.accounts.Count}");
        ImGui.SameLine();
        ImGui.Text($"REFRESH IN: {(300 - Plugin.stalk_frame_counter) / 60} (SAVE IN: {12 - Plugin.save_frame_coutner})");

        ImGui.InputText("NAME", ref search_text, 32);

        ImGui.BeginChild("table", ImGuiHelpers.ScaledVector2(0, 0), true, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (ImGui.BeginTable("accounts", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("ACCOUNTID", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("NAMES");
            ImGui.TableHeadersRow();

            foreach (KeyValuePair<ulong, HashSet<String>> account in Plugin.accounts)
            {
                if (show_everything || account.Value.Count > 1)
                {
                    string joined = $"{String.Join(", ", account.Value)}";
                    if (joined.IndexOf(search_text, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text($"{account.Key}");
                        ImGui.TableNextColumn();
                        ImGui.Text(joined);
                    }
                }

            }
        }
        ImGui.EndTable();
        ImGui.EndChild();
    }
}
