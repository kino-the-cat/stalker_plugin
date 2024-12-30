using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using StalkerPlugin.Windows;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Collections.Generic;
using System;
using System.Text;

namespace StalkerPlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;

    private const string CommandName = "/stalk";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Stalker");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Dictionary<ulong, HashSet<String>> accounts = new Dictionary<ulong, HashSet<String>>();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // you might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "something something stalk something"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        Framework.Update += AutoSnoop;

        // DutyState.DutyStarted += StopAutoSnoop;
        // DutyState.DutyCompleted += StartAutoSnoop;

        Restore();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        Dump("stalk.csv");
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();


    private void StartAutoSnoop(object? sender, ushort dunno)
    {
        Framework.Update += AutoSnoop;
    }

    private void StopAutoSnoop(object? sender, ushort dunno)
    {
        Framework.Update -= AutoSnoop;
    }

    public int stalk_frame_counter = 0;
    public int save_frame_coutner = 0;
    private void AutoSnoop(IFramework framework)
    {
        if (stalk_frame_counter++ == 300)
        {
            Snoop();

            if (save_frame_coutner++ == 12)
            {
                Dump("stalk.csv");
                Log.Information("DUMP");
                save_frame_coutner = 0;
            }

            stalk_frame_counter = 0;
        }
    }

    public void Snoop()
    {
        foreach (IGameObject obj in Objects)
        {
            if (obj is null)
            {
                continue;
            }

            if (obj!.ObjectKind == ObjectKind.Player)
            {
                IPlayerCharacter character = (IPlayerCharacter)obj!;
                ulong accountId = character.GetAccountId();
                Log.Verbose($"Character: {character}, AccountID: {accountId}");

                AddCharacter(accountId, $"{character.Name}@{character.HomeWorld.Value.Name}");
            }
        }
    }

    public void AddCharacter(ulong accountId, String name)
    {
        if (accounts.ContainsKey(accountId))
        {
            accounts[accountId].Add(name);
        }
        else
        {
            accounts.Add(accountId, new HashSet<string>());
            accounts[accountId].Add(name);
        }

    }

    public void Restore()
    {
        var dbPath = Path.Combine(PluginInterface.ConfigDirectory.FullName!, "stalk.csv");
        if (!File.Exists(dbPath)) {
            accounts = new Dictionary<ulong, HashSet<string>>();
            return;
        }
        var csv = File.ReadAllLines(dbPath);

        accounts.Clear();

        foreach (var line in csv)
        {
            var values = line.Split(",");
            ulong accountID = (ulong)Decimal.Parse(values[0]);

            HashSet<String> names = new HashSet<String>();
            for (int i = 1; i < values.Length; ++i)
            {
                names.Add(values[i]);
            }

            accounts.Add(accountID, names);
        }
    }

    public void Dump(String filename)
    {
        var dbPath = Path.Combine(PluginInterface.ConfigDirectory.FullName!, filename);
        var csv = new StringBuilder();
        foreach (KeyValuePair<ulong, HashSet<String>> account in accounts)
        {
            csv.Append(account.Key);
            foreach (var value in account.Value)
            {
                csv.Append(",");
                csv.Append(value);
            }
            csv.AppendLine();
        }
        File.WriteAllText(dbPath, csv.ToString());
    }

    public void Destroy()
    {
        accounts.Clear();
    }


}

public static class UnsafeHelper
{
    public static unsafe ulong GetAccountId(this IPlayerCharacter character) => ((Character*)character.Address)->AccountId;
}