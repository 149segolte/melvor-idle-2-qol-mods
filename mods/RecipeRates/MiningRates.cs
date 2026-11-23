using System;
using System.Collections.Generic;
using GamesByMalcs.Melvor2.Core.TickTimer;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Mining;

namespace RecipeRates;

static class MiningRates
{
    const double FullProspecting = 0.9999;

    public static Rates? For(MiningRock rock, MiningActionComponent action)
    {
        MiningActionStats stats = rock.Stats;
        double interval = stats.Interval.Value;
        double breakChance = stats.RockBreakChance.Value;
        double prospecting = stats.MinimumProspecting.Value;
        if (!double.IsFinite(interval) || interval <= 0 || !double.IsFinite(breakChance) || !double.IsFinite(prospecting))
            return null;

        double breaksPerHit = BreaksPerHit(breakChance, prospecting);
        double shatter = Rates.Chance(stats.ShatterChance);
        double secondsPerHit = TickTimer.TicksToMS(Math.Max(1, TickTimer.MSToTicks(interval))) / 1000;
        var rates = new Rates
        {
            SecondsPerReward = secondsPerHit,
            TimingSummary = breaksPerHit > 0
                ? $"One break every {secondsPerHit / breaksPerHit:0.##}s"
                : "0 breaks per hour",
        };
        rates.XpPerHour = stats.Experience.XpToAdd.Value * (1 + shatter) * breaksPerHit * rates.RewardsPerHour;

        // Express all output per strike so bonus drops still count on a rock with zero break chance.
        var products = new Dictionary<Item, double>();
        Add(rock.Recipe.Product.Item, (stats.ProductQuantity.Value + Rates.Chance(stats.AdditionalProductQuantityChance))
            * (1 + 2 * shatter) * breaksPerHit);
        if (rock.Recipe.BarItem is { } bar) Add(bar, Rates.Chance(stats.BarItemChance) * breaksPerHit);
        foreach (var node in stats.OnHitItemChances.Values)
        {
            var drop = node.Value;
            Add(drop.Item, drop.Quantity * Math.Clamp(drop.Chance, 0, 1));
        }
        double stoneChance = Rates.Chance(stats.StoneItemChance);
        foreach (Item stone in action.StoneItems) Add(stone, stoneChance);
        double essenceChance = Rates.Chance(stats.EssenceItemChance);
        foreach (Item essence in action.EssenceItems) Add(essence, essenceChance);

        double doubling = 1 + Rates.Chance(stats.DoublingChance);
        foreach (var (item, quantity) in products)
            rates.AddProduct(item, quantity * doubling);
        return rates;

        void Add(Item item, double quantity)
        {
            if (quantity > 0) products[item] = products.GetValueOrDefault(item) + quantity;
        }
    }

    internal static double BreaksPerHit(double chance, double start)
    {
        if (chance >= 1 || start + chance >= FullProspecting) return 1;
        if (chance <= 0) return 0;

        // A failed roll adds the break chance to prospecting. The last hit therefore always breaks
        // the rock. Summing the chance of needing each hit gives a capped geometric mean.
        double progressLeft = FullProspecting - start;
        double lastHit = Math.Ceiling(progressLeft / chance);
        double progressAtCap = double.IsFinite(lastHit) ? lastHit * chance : progressLeft;

        // Normalize the log by the rounded miss probability to retain precision for tiny chances.
        double miss = 1 - chance;
        double logSurvival = progressAtCap * (miss == 1 ? -1 : Math.Log(miss) / (1 - miss));
        double cappedProbability = logSurvival > -1e-7
            ? -logSurvival * (1 + logSurvival / 2)
            : 1 - Math.Exp(logSurvival);
        return chance / cappedProbability;
    }
}
