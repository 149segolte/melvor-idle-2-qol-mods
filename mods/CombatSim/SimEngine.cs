using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Loadouts;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Saves;
using Godot;

namespace CombatSim;

/// <summary>
/// Simulates fights without re-implementing the game's combat: a copy of the character is loaded into a
/// second, invisible <see cref="Game"/> and the game's own ticks are run on it, on a background thread.
/// Every fight starts from a fresh copy of the same snapshot, so results do not leak into each other, and no
/// copy is ever saved.
/// </summary>
sealed class SimEngine
{
    public const string Gold = "MelvorBase:GoldPieces";
    const int TicksPerSecond = 60;
    // How often the fight is looked at (restarted after a death, supplies handed back).
    const int CheckEvery = 64;

    static readonly string[] CombatSkills =
    {
        "MelvorBase:Attack", "MelvorBase:Strength", "MelvorBase:Defence", "MelvorBase:Hitpoints",
        "MelvorBase:Ranged", "MelvorBase:Magic", "MelvorBase:Prayer", "MelvorBase:Slayer",
    };

    // SaveManager is a static class, which an [UnsafeAccessor] cannot name: one reflection call per copy.
    static readonly MethodInfo? s_loadState = typeof(SaveManager).GetMethod("LoadGameStateFromStream",
        BindingFlags.NonPublic | BindingFlags.Static, [typeof(Stream), typeof(Game)]);

    // The entities the game advances on every tick.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_TickingEntities")]
    static extern List<ITickingEntity> TickingEntitiesOf(Game game);

    readonly Action<string> _log;
    CancellationTokenSource? _cancel;

    /// <summary>Filled by the background thread; the mod drains it on the game's own thread.</summary>
    public readonly ConcurrentQueue<SimResult> Finished = new();
    public volatile bool Running;
    public volatile int Total;
    public volatile string Current = "";
    int _done;
    public int Done => Volatile.Read(ref _done);

    /// <summary>
    /// Fights simulated side by side. Every fight is a game of its own, so they do not touch each other; half
    /// of the processor is left to the game that is being played.
    /// </summary>
    public static int DefaultWorkers => Math.Clamp(System.Environment.ProcessorCount / 2, 1, 4);

    public SimEngine(Action<string> log) => _log = log;
#if DEBUG
    static WeakReference<Game>? s_lastSim;
#endif

    public static bool IsSupported => s_loadState != null;

    /// <summary>
    /// Call on the game's thread. Has the game write its save the way its autosave does, takes that file as
    /// the snapshot and starts simulating the targets one after the other.
    /// </summary>
    public bool Start(Game real, IReadOnlyList<SimTarget> targets, SimOptions options, int workers, out string error)
    {
        error = "";
        if (Running) { error = "A simulation is already running."; return false; }
        if (!IsSupported) { error = "This game version loads saves differently: the simulator needs an update."; return false; }
        byte[] save;
        try
        {
            real.LocalSave();
            string? file = NewestSave();
            if (file == null) { error = "The character's save file was not found."; return false; }
            save = File.ReadAllBytes(file);
        }
        catch (Exception e) { error = "Could not take a snapshot of the character: " + e.Message; return false; }

        while (Finished.TryDequeue(out _)) { }
        Volatile.Write(ref _done, 0);
        Total = targets.Count;
        Current = "";
        Running = true;
        var cancel = _cancel = new CancellationTokenSource();
        var waiting = new ConcurrentQueue<SimTarget>(targets);
        workers = Math.Clamp(workers, 1, Math.Max(1, targets.Count));
        Task.Run(() =>
        {
            try
            {
                Task.WaitAll(Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
                {
                    while (!cancel.IsCancellationRequested && waiting.TryDequeue(out SimTarget? target))
                    {
                        Current = target.Name;
                        Finished.Enqueue(RunOne(save, target, options, cancel.Token));
                        Interlocked.Increment(ref _done);
                    }
                })).ToArray());
            }
            catch (Exception e) { _log("The simulation stopped: " + e); }
            finally
            {
#if DEBUG
                long before = GC.GetTotalMemory(false);
                long ws = Process.GetCurrentProcess().WorkingSet64;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                _log($"Dev: managed {before >> 20:N0} MB -> {GC.GetTotalMemory(false) >> 20:N0} MB after a full collection; process {ws >> 20:N0} MB -> {Process.GetCurrentProcess().WorkingSet64 >> 20:N0} MB; server GC {System.Runtime.GCSettings.IsServerGC}, latency {System.Runtime.GCSettings.LatencyMode}");
                if (s_lastSim != null && s_lastSim.TryGetTarget(out Game? leaked))
                {
                    var clock = Stopwatch.StartNew();
                    _log("Dev: the last copy is still alive; " + LeakProbe.Find(leaked) + $" ({clock.Elapsed.TotalSeconds:0.0}s)");
                }
                else _log("Dev: the last copy was collected.");
#endif
                Running = false;
            }
        });
        return true;
    }

    public void Cancel() => _cancel?.Cancel();

    static string? NewestSave()
    {
        string root = Path.Combine(OS.GetUserDataDir(), "steam");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "save_*.sav", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    static void LoadState(Stream stream, Game game)
    {
        try { s_loadState!.Invoke(null, [stream, game]); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
    }

    SimResult RunOne(byte[] save, SimTarget target, SimOptions options, CancellationToken cancel)
    {
        var result = new SimResult { Target = target, SetupKey = options.SetupKey, SetupLabel = options.SetupLabel };
        var clock = Stopwatch.StartNew();
        Game? sim = null;
        try
        {
            // A Game cannot take a save twice, so every fight gets a copy of its own (about a second).
            sim = new Game();
            sim.LoadForSave();
            using (var stream = new MemoryStream(save)) LoadState(stream, sim);
            Fight(sim, target, options, result, cancel);
        }
        catch (Exception e)
        {
            result.Failed = true;
            result.Note = "The simulation failed: " + e.Message;
            _log($"{target.Name}: {e}");
        }
        finally
        {
            // The game releases its own Game objects the same way when a character is closed.
            try { sim?.Dispose(); } catch (Exception e) { _log("Releasing the copy failed: " + e.Message); }
            try { if (sim != null) Unhook(sim); } catch (Exception e) { LogOnce("Letting go of the copy failed (the game was updated?): " + e.Message); }
#if DEBUG
            if (sim != null) s_lastSim = new WeakReference<Game>(sim);
#endif
            result.RealMilliseconds = clock.Elapsed.TotalMilliseconds;
        }
        return result;
    }

    static void Fight(Game sim, SimTarget target, SimOptions options, SimResult result, CancellationToken cancel)
    {
        if (!sim.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? ce) || ce is not CombatEntity combat ||
            !sim.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? be) || be is not BankEntity bank)
        { result.Failed = true; result.Note = "The copy has no combat or bank."; return; }

        BaseCombatArea? area = combat.Areas.ActiveAreas.Keys.FirstOrDefault(a => a.Id == target.AreaId);
        Enemy? enemy = target.EnemyId == null ? null : (area as CombatArea)?.Enemies.FirstOrDefault(x => x.Id == target.EnemyId);
        if (area == null || (target.EnemyId != null && enemy == null) || (target.EnemyId == null && area is not Dungeon))
        { result.Failed = true; result.Note = "This fight does not exist in the copy."; return; }

        // Start from a clean slate: nothing running, the chosen equipment on, full health.
        sim.ActionManager.TryStopActive(true);
        StopPassiveSkills(sim);
        var notes = new List<string>();
        Wear(sim, combat, bank, options, notes);
        if (target.StanceId != null) Stand(combat, target, notes);
        Cast(combat, bank, options, notes);
        combat.Player.GlobalHealth.Set(combat.Player.GlobalHealth.Max);
        result.MaxHp = combat.Player.Health.Max;
        result.LowestHp = combat.Player.Health.Current;

        var xpBefore = sim.LeveledEntities.Where(s => CombatSkills.Contains(s.Id)).ToDictionary(s => s.Id, s => (double)s.Experience.Xp);
        var loot = new Dictionary<string, Amount>();
        var used = new Dictionary<string, Amount>();
        // What was used since the last look, to hand back when supplies are unlimited.
        var bankDebt = new Dictionary<Item, int>();
        var ammoDebt = new Dictionary<Item, int>();
        var walletDebt = new Dictionary<Currency, long>();
        bool refilling = false;
        long ticks = 0;

        void Add(Dictionary<string, Amount> into, string id, string name, double quantity, double sellsFor, bool isCurrency) =>
            into[id] = into.TryGetValue(id, out Amount? old) ? old with { Quantity = old.Quantity + quantity } : new Amount(id, name, quantity, sellsFor, isCurrency);
        static double Price(Item item) => item.SellsFor is { Currency.Id: Gold } price ? price.Quantity : 0;
        static string ItemName(Item item) => item.DisplayName.GetPluralText(2);

        combat.Enemy.Killed += (_, _) => result.Kills++;
        combat.Areas.DungeonCompleted += (_, _) => result.DungeonRuns++;
        combat.Action.PlayerDied += (_, _) =>
        {
            result.Deaths++;
            if (result.FirstDeathSeconds < 0) result.FirstDeathSeconds = ticks / (double)TicksPerSecond;
        };
        combat.Food.FoodEaten += (_, e) => { result.FoodEaten += e.Quantity; result.Healed += e.Healing; };
        combat.Player.Health.CurrentChanged += (_, e) =>
        {
            if (e.NewValue < result.LowestHp) result.LowestHp = e.NewValue;
            if (e.NewValue < e.OldValue) result.DamageTaken += e.OldValue - e.NewValue;
        };
        combat.Loot.EnemyLooted += (_, e) =>
        {
            foreach (ItemQuantity item in e.Items) Add(loot, item.Item.Id, ItemName(item.Item), item.Quantity, Price(item.Item), false);
        };
        combat.Loot.DungeonLooted += (_, e) =>
        {
            foreach (ItemQuantity item in e.Items) Add(loot, item.Item.Id, ItemName(item.Item), item.Quantity, Price(item.Item), false);
        };
        // Items leaving the bank during a fight are supplies: food eaten, runes cast.
        bank.Inventory.ItemQuantityChanged += (_, e) =>
        {
            if (refilling || e.QuantityChange >= 0) return;
            Add(used, e.Item.Id, ItemName(e.Item), -e.QuantityChange, Price(e.Item), false);
            bankDebt[e.Item] = bankDebt.GetValueOrDefault(e.Item) - e.QuantityChange;
        };
        // Ammo is used from the equipment slot, not from the bank.
        combat.Equipment.AmmoConsumed += (_, e) =>
        {
            if (e.Item == null) return;
            Add(used, e.Item.Id, ItemName(e.Item), 1, Price(e.Item), false);
            ammoDebt[e.Item] = ammoDebt.GetValueOrDefault(e.Item) + 1;
        };
        // Money in is income (coins from drops, slayer coins); money out is a supply (prayer points).
        bank.Wallet.CurrencyQuantityChanged += (_, e) =>
        {
            if (refilling) return;
            long change = e.NewValue - e.OldValue;
            if (change > 0) Add(loot, e.Currency.Id, e.Currency.DisplayName.Text, change, 0, true);
            else if (change < 0)
            {
                Add(used, e.Currency.Id, e.Currency.DisplayName.Text, -change, 0, true);
                walletDebt[e.Currency] = walletDebt.GetValueOrDefault(e.Currency) - change;
            }
        };

        void Refill()
        {
            if (bankDebt.Count + ammoDebt.Count + walletDebt.Count == 0) return;
            refilling = true;
            try
            {
                foreach (var (item, quantity) in bankDebt) bank.Inventory.AddQuantity(item, quantity, AddItemFlags.None);
                foreach (var (item, quantity) in ammoDebt) combat.Equipment.TryAddQuantity(item, quantity);
                foreach (var (currency, quantity) in walletDebt) bank.Wallet.AddQuantity(currency, quantity);
            }
            finally { refilling = false; }
            bankDebt.Clear(); ammoDebt.Clear(); walletDebt.Clear();
        }

        bool Begin()
        {
            if (enemy != null) combat.Areas.SelectEnemy((CombatArea)area, enemy);
            else combat.Areas.SelectDungeon((Dungeon)area);
            // Selecting what is already selected switches it off, the way the game's button does.
            if (!sim.ActionManager.HasActiveAction)
            {
                if (enemy != null) combat.Areas.SelectEnemy((CombatArea)area, enemy);
                else combat.Areas.SelectDungeon((Dungeon)area);
            }
            return sim.ActionManager.HasActiveAction;
        }

        if (!Begin()) { result.Failed = true; result.Note = "The fight could not be started (requirements not met?)."; return; }
        if (notes.Count > 0) result.Note = string.Join(" ", notes);

        long total = (long)(options.Hours * 3600 * TicksPerSecond);
        int deathsSeen = 0, restarts = 0;
        for (; ticks < total; ticks++)
        {
            sim.PropagateTick();
            if (ticks % CheckEvery != 0) continue;
            if (options.UnlimitedSupplies) Refill();
            if ((ticks & 0xFFF) == 0 && cancel.IsCancellationRequested) { notes.Add("Cancelled."); result.Note = string.Join(" ", notes); break; }
            if (sim.ActionManager.HasActiveAction) continue;

            // The fight ended. After a death it goes on, to learn how often that happens; anything else
            // (out of ammo, runes, food) ends it here.
            bool died = result.Deaths > deathsSeen;
            deathsSeen = result.Deaths;
            if (!died)
            {
                Amount? empty = used.Values.FirstOrDefault(u => !u.IsCurrency && sim.Data.GetDataRegistryOutput<Item>().TryGetObjectById(u.Id, out Item? item)
                                                                && bank.Inventory.GetQuantity(item) == 0 && combat.Equipment.GetEquippedQuantity(item) == 0);
                notes.Add(empty != null ? $"Ran out of {empty.Name} after {Clock(ticks)}." : $"The fight stopped after {Clock(ticks)}.");
                result.Note = string.Join(" ", notes);
                break;
            }
            if (++restarts > 5000 || !Begin()) { notes.Add($"Could not go on after dying at {Clock(ticks)}."); result.Note = string.Join(" ", notes); break; }
        }

        result.Hours = ticks / (3600.0 * TicksPerSecond);
        foreach (var skill in sim.LeveledEntities)
            if (xpBefore.TryGetValue(skill.Id, out double before) && skill.Experience.Xp - before > 0)
                result.Xp[skill.Id] = skill.Experience.Xp - before;
        result.Loot = loot.Values.OrderByDescending(l => l.IsCurrency ? double.MaxValue : l.Quantity * Math.Max(l.SellsFor, 0.001)).ToList();
        result.Used = used.Values.OrderByDescending(u => u.Quantity).ToList();
    }

    // Puts the chosen loadout on, then the single items that were swapped in. An item the character does
    // not own is handed to the copy first; the game's own equip call still decides whether it can be worn.
    static void Wear(Game sim, CombatEntity combat, BankEntity bank, SimOptions options, List<string> notes)
    {
        if (options.LoadoutId != null && combat.Loadouts.CurrentLoadout?.Data.Id != options.LoadoutId)
        {
            LoadoutSlot? loadout = combat.Loadouts.ActiveLoadouts.Keys.FirstOrDefault(l => l.Id == options.LoadoutId);
            if (loadout == null) notes.Add("The loadout no longer exists.");
            else combat.Loadouts.SwapToLoadout(loadout);
        }
        if (options.Overrides.Count == 0) return;
        var slots = sim.Data.GetDataRegistryOutput<EquipmentSlot>();
        var items = sim.Data.GetDataRegistryOutput<Item>();
        foreach (var (slotId, itemId) in options.Overrides)
        {
            if (!slots.TryGetObjectById(slotId, out EquipmentSlot? slot)) continue;
            if (itemId == "") { combat.ItemEquipping.UnequipSlot(slot); continue; }
            if (!items.TryGetObjectById(itemId, out Item? item) || !item.Capabilities.TryGet(out EquipmentCapability? wearable)) continue;
            // Things worn in numbers (arrows, runes on a staff...) get a pile; everything else one.
            int quantity = wearable.CanEquipQuantity ? 100_000 : 1;
            int missing = quantity - bank.Inventory.GetQuantity(item);
            if (missing > 0) bank.Inventory.AddQuantity(item, missing, AddItemFlags.None);
            combat.ItemEquipping.EquipItem(item, quantity, slot);
            if (!combat.Equipment.IsItemEquipped(item))
                notes.Add($"{item.DisplayName.GetPluralText(1)} could not be equipped (requirements not met?).");
        }
    }

    // Selects one of the attack stances of the weapon that is worn (they decide which skills get the XP).
    static void Stand(CombatEntity combat, SimTarget target, List<string> notes)
    {
        WeaponType? weapon = (combat.Equipment.CurrentWeapon ?? combat.Equipment.DefaultWeapon)?.Weapon?.Type;
        AttackStance? stance = weapon?.Stances.FirstOrDefault(s => s.Id == target.StanceId);
        if (weapon == null || stance == null) { notes.Add($"The stance {target.StanceName} is not available with this weapon."); return; }
        combat.Player.SetSelectedStance(weapon, stance);
    }

    // Selects the spell to fight with. With unlimited supplies the copy is handed its runes, so a spell the
    // character cannot pay for right now can still be tried.
    static void Cast(CombatEntity combat, BankEntity bank, SimOptions options, List<string> notes)
    {
        if (options.SpellId != null)
        {
            MagicSpell? spell = combat.MagicSpells.ActiveSpells.Keys.FirstOrDefault(s => s.Id == options.SpellId);
            if (spell == null) { notes.Add("The spell no longer exists."); return; }
            combat.MagicSpells.SelectSpell(spell);
            if (combat.MagicSpells.SelectedSpell?.Data.Id != options.SpellId)
            {
                notes.Add($"{spell.DisplayName.Text} cannot be cast with this weapon.");
                return;
            }
        }
        if (!options.UnlimitedSupplies || combat.MagicSpells.SelectedSpell is not { } casting) return;
        foreach (ItemQuantity rune in casting.RuneCosts)
        {
            int missing = rune.Quantity * 1000 - bank.Inventory.GetQuantity(rune.Item);
            if (missing > 0) bank.Inventory.AddQuantity(rune.Item, missing, AddItemFlags.None);
        }
    }

    // ---- letting a copy go ---------------------------------------------------------------------------------

    // The game's settings live as long as the program, and the parts of a Game subscribe to their change
    // events without ever unsubscribing. That is harmless for the one game that is played, but every copy made
    // here would stay in memory for good (about 120 MB each). So the copy's handlers are taken off again.
    const string SettingsNamespace = "GamesByMalcs.Melvor2.Settings";
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    static List<FieldInfo>? s_settingsRoots;
    static readonly ConcurrentDictionary<Type, FieldInfo[]> s_settingsFields = new();
    int _logged;

    void LogOnce(string message)
    {
        if (Interlocked.Exchange(ref _logged, 1) == 0) _log(message);
    }

    static bool IsSettingsType(Type type) => type.Namespace is { } ns && ns.StartsWith(SettingsNamespace, StringComparison.Ordinal);

    static FieldInfo[] SettingsFields(Type type) => s_settingsFields.GetOrAdd(type, static t =>
    {
        var fields = new List<FieldInfo>();
        for (Type? at = t; at != null && at != typeof(object); at = at.BaseType)
            fields.AddRange(at.GetFields(Any | BindingFlags.Instance).Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType) || IsSettingsType(f.FieldType)));
        return fields.ToArray();
    });

    /// <summary>Removes the copy's handlers from the events of the game's settings. Returns how many.</summary>
    static int Unhook(Game sim)
    {
        s_settingsRoots ??= typeof(Game).Assembly.GetTypes()
            .Where(t => IsSettingsType(t) && !t.ContainsGenericParameters)
            .SelectMany(t => t.GetFields(Any | BindingFlags.Static))
            .Where(f => !f.IsLiteral && IsSettingsType(f.FieldType))
            .ToList();

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>();
        foreach (FieldInfo root in s_settingsRoots)
            if (root.GetValue(null) is { } value && seen.Add(value)) queue.Enqueue(value);

        int removed = 0;
        while (queue.Count > 0)
        {
            object settings = queue.Dequeue();
            foreach (FieldInfo field in SettingsFields(settings.GetType()))
            {
                object? value = field.GetValue(settings);
                if (value is Delegate handlers)
                {
                    // The event's own "remove" is used: it is safe against another thread subscribing meanwhile.
                    MethodInfo? remove = field.DeclaringType?.GetEvent(field.Name, Any | BindingFlags.Instance)?.GetRemoveMethod(true);
                    if (remove == null) continue;
                    foreach (Delegate handler in handlers.GetInvocationList())
                        if (BelongsTo(handler.Target, sim)) { remove.Invoke(settings, [handler]); removed++; }
                }
                else if (value != null && seen.Add(value)) queue.Enqueue(value);
            }
        }
        return removed;
    }

    // A handler is the copy's when it sits on the copy itself, on one of its entities, or on a part of one.
    static bool BelongsTo(object? target, Game sim)
    {
        if (target == null) return false;
        if (ReferenceEquals(target, sim)) return true;
        GameEntity? entity = target as GameEntity ?? target.GetType().GetProperty("Entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) as GameEntity;
        return entity != null && sim.Entities.TryGetObjectById(entity.Id, out GameEntity? mine) && ReferenceEquals(mine, entity);
    }

    // Farming and Ranching run on their own beside combat and use items from the same bank: left running,
    // their wine and seeds would show up as supplies of the fight.
    static void StopPassiveSkills(Game sim)
    {
        try
        {
            var passive = new HashSet<object>(sim.PassiveActionManager.PassiveActions.Select(p => (object)p.Entity), ReferenceEqualityComparer.Instance);
            TickingEntitiesOf(sim).RemoveAll(entity => passive.Contains(entity));
        }
        catch (MissingMethodException) { }   // a game update moved the list: the fight is still right, "Used" is noisier
    }

    static string Clock(long ticks)
    {
        var t = TimeSpan.FromSeconds(ticks / (double)TicksPerSecond);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s" : $"{t.Seconds}s";
    }
}
