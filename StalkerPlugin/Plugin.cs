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
using Dalamud.Utility.Signatures;
using Dalamud.Hooking;
using Serilog;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Dalamud.Memory;
using System.Linq;

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
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

    private const string CommandName = "/stalk";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Stalker");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public SortedDictionary<ulong, HashSet<String>> accounts = new SortedDictionary<ulong, HashSet<String>>();

    GameHooks hook;

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

        hook = new GameHooks(GameInteropProvider, (uint account, String name, String world) =>
        {
            AddCharacter(account, $"{name}@{world}");
        });
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
        if (!accounts.ContainsKey(accountId))
        {
            accounts.Add(accountId, new HashSet<string>());
        }
        var found_account = accounts[accountId];

        var split = name.Split("@");
        var char_name = split[0];
        var world_name = split[1];

        // I used @Search when I didn't know I can get home world from search, smh
        // Should be probably removed at some point
        if (world_name == "Search")
        {
            if (!found_account.Any(saved_name => saved_name.Split("@")[0] == char_name))
            {
                found_account.Add(name);
            }
        }
        else
        {
            if (found_account.Contains($"{char_name}@Search"))
            {
                found_account.Remove($"{char_name}@Search");
            }
            found_account.Add(name);
        }
    }

    public void Restore()
    {
        var dbPath = Path.Combine(PluginInterface.ConfigDirectory.FullName!, "stalk.csv");
        if (!File.Exists(dbPath))
        {
            accounts = new SortedDictionary<ulong, HashSet<string>>();
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

internal sealed class PlayerMapping
{
    public required ulong? AccountId { get; init; }
    public required ulong ContentId { get; init; }
    public required string HomeWorld { get; init; } = string.Empty;
    public required string PlayerName { get; init; } = string.Empty;
}


// Mostly stolen from RetainerTrack
internal sealed unsafe class GameHooks : IDisposable
{
    Action<uint, String, String> AddCB;
    public GameHooks(IGameInteropProvider GameInteropProvider, Action<uint, String, String> add_cb)
    {
        Log.Error("INIT");

        AddCB = add_cb;

        GameInteropProvider.InitializeFromAttributes(this);
        SocialListResultHook.Enable();

        Log.Error("AAA");
    }

    public void Dispose()
    {
        SocialListResultHook.Dispose();
    }

    private delegate nint SocialListResultDelegate(nint a1, nint dataPtr);
    [Signature("48 89 5C 24 10 56 48 83 EC 20 48 ?? ?? ?? ?? ?? ?? 48 8B F2 E8 ?? ?? ?? ?? 48 8B D8",
    DetourName = nameof(ProcessSocialListResult))]
    private Hook<SocialListResultDelegate> SocialListResultHook { get; init; } = null!;

    private string WorldNumberToString(short num)
    {
        switch (num)
        {
            case 80: return "Cerberus";
            case 83: return "Louisoix";
            case 71: return "Moogle";
            case 39: return "Omega";
            case 401: return "Phantom";
            case 97: return "Ragnarok";
            case 400: return "Sagittarius";
            case 85: return "Spriggan";
            case 402: return "Alpha";
            case 36: return "Lich";
            case 66: return "Odin";
            case 56: return "Phoenix";
            case 403: return "Raiden";
            case 67: return "Shiva";
            case 33: return "Twintania";
            case 42: return "Zodiark";
            default:
                return "HELP";
        }
    }

    private void PrintBytes(SocialListPlayer player)
    {
        int size = Marshal.SizeOf(player);
        byte[] arr = new byte[size];

        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(player, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        String re = "";
        String res = "";
        for (int i = 0; i < size; ++i)
        {
            if (arr[i] != 0)
            {
                res += ",   " + (char)arr[i];
            }
            else
            {
                res += ",   .";
            }

            if (arr[i] < 10)
            {
                re += ",   " + arr[i];

            }
            else if (arr[i] < 100)
            {
                re += ",  " + arr[i];
            }
            else
            {
                re += ", " + arr[i];
            }
        }
        Log.Error($"{re}");
        Log.Error($"{res}");
    }
    private nint ProcessSocialListResult(nint a1, nint dataPtr)
    {
        try
        {
            var result = Marshal.PtrToStructure<SocialListResultPage>(dataPtr);
            List<PlayerMapping> mappings = new();
            foreach (SocialListPlayer player in result.PlayerSpan)
            {
                if (player.ContentId == 0)
                    continue;

                var mapping = new PlayerMapping
                {
                    ContentId = player.ContentId,
                    AccountId = player.AccountId != 0 ? player.AccountId : null,
                    HomeWorld = WorldNumberToString(player.HomeWorld),
                    PlayerName = MemoryHelper.ReadString(new nint(player.CharacterName), Encoding.ASCII, 32),
                };

                if (!string.IsNullOrEmpty(mapping.PlayerName))
                {
                    Log.Debug("Content id {ContentId} belongs to '{Name}' ({AccountId})", mapping.ContentId,
                        mapping.PlayerName, mapping.AccountId);
                    mappings.Add(mapping);
                }
                else
                {
                    Log.Debug("Content id {ContentId} didn't resolve to a player name, ignoring",
                        mapping.ContentId);
                }
            }

            // if (mappings.Count > 0)
            //     Task.Run(() => _persistenceContext.HandleContentIdMapping(mappings));
            for (int i = 0; i < mappings.Count; ++i)
            {
                var a = mappings[i];
                Log.Information($"{a.PlayerName}@{a.HomeWorld}: {a.AccountId}");
                if (a.AccountId is not null)
                    AddCB((uint)a.AccountId, a.PlayerName, a.HomeWorld);
                else
                    Log.Error($"AAAAAAAAAAAAAAAAAAAAAA {a.PlayerName}");
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not process social list result");
        }

        return SocialListResultHook.Original(a1, dataPtr);
    }



    /// <summary>
    /// There are some caveats here, the social list includes a LOT of things with different types
    /// (we don't care for the result type in this plugin), see sapphire for which field is the type.
    ///
    /// 1 = party
    /// 2 = friend list
    /// 3 = link shell
    /// 4 = player search
    /// 5 = fc short list (first tab, with company board + actions + online members)
    /// 6 = fc long list (members tab)
    ///
    /// Both 1 and 2 are sent to you on login, unprompted.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 0x420)]
    internal struct SocialListResultPage
    {
        [FieldOffset(0x10)] private fixed byte Players[10 * 0x70];

        public Span<SocialListPlayer> PlayerSpan => new(Unsafe.AsPointer(ref Players[0]), 10);
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x70, Pack = 1)]
    internal struct SocialListPlayer
    {
        /// <summary>
        /// If this is set, it means there is a player present in this slot (even if no name can be retrieved),
        /// 0 if empty.
        /// </summary>
        [FieldOffset(0x00)] public readonly ulong ContentId;

        /// <summary>
        /// Only seems to be set for certain kind of social lists, e.g. friend list/FC members doesn't include any.
        /// </summary>
        [FieldOffset(0x18)] public readonly ulong AccountId;

        /// <summary>
        /// Maybe
        /// </summary>
        [FieldOffset(0x42)] public readonly short HomeWorld;

        /// <summary>
        /// This *can* be empty, e.g. if you're querying your friend list, the names are ONLY set for characters on the same world.
        /// </summary>
        [FieldOffset(0x44)] public fixed byte CharacterName[32];
    }

}

public static class UnsafeHelper
{
    public static unsafe ulong GetAccountId(this IPlayerCharacter character) => ((Character*)character.Address)->AccountId;
}