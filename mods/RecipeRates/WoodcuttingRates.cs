using System;
using System.Collections.Generic;
using GamesByMalcs.Melvor2.Core.TickTimer;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Woodcutting;
using GamesByMalcs.Melvor2.Stats.Nodes;

namespace RecipeRates;

static class WoodcuttingRates
{
    public static Rates? For(WoodcuttingTree tree)
    {
        WoodcuttingActionStats stats = tree.Stats;
        if (!double.IsFinite(stats.Interval.Value) || stats.Interval.Value <= 0 || stats.MaxHealth.Value <= 0)
            return null;

        double hitsPerCut = ExpectedHits(stats.MaxHealth.Value, Chance(stats.InstantCutChance),
            Chance(stats.WeakSpotChance), Chance(stats.DoubleDamageChance));
        int ticksPerHit = Math.Max(1, TickTimer.MSToTicks(stats.Interval.Value));
        var rates = new Rates { SecondsPerReward = hitsPerCut * TickTimer.TicksToMS(ticksPerHit) / 1000 };
        rates.XpPerHour = stats.Experience.XpToAdd.Value * rates.RewardsPerHour;

        // Hits and completed cuts can grant the same item. Show its combined estimate once.
        var products = new Dictionary<Item, double> { [tree.Recipe.Product.Item] = stats.ProductQuantity.Value };
        AddDrops(stats.OnHitItemChances, hitsPerCut);
        AddDrops(stats.OnCutItemChances, 1);
        double doubling = 1 + Chance(stats.DoublingChance);
        foreach (var (item, amount) in products)
            rates.AddProduct(item, amount * doubling);
        return rates;

        void AddDrops(ValuesStatNode<ItemQuantityChance> drops, double rolls)
        {
            foreach (var node in drops.Values)
            {
                var drop = node.Value;
                products[drop.Item] = products.GetValueOrDefault(drop.Item)
                    + rolls * drop.Quantity * Math.Clamp(drop.Chance, 0, 1);
            }
        }
    }

    static double Chance(ModifierStatNodes nodes) => Math.Clamp(nodes.Total.Value, 0, 1);

    internal static double ExpectedHits(int health, double instantCut, double weakSpot, double doubleDamage)
    {
        if (instantCut == 1) return 1;
        if (weakSpot == 0 && doubleDamage == 0 && instantCut == 0) return health;

        // Each hit lowers health. Add up the probabilities of visiting each positive health state,
        // since visiting a state means taking one more swing. Instant cuts go directly to zero.
        var reach = new double[health + 1];
        reach[health] = 1;
        int weakDamage = 3 + (int)(health * 0.1);
        double expected = 0;
        for (int remaining = health; remaining > 0; remaining--)
        {
            double probability = reach[remaining];
            expected += probability;
            if (remaining == 1 || probability == 0) continue;

            double ordinary = probability * (1 - instantCut) * (1 - weakSpot);
            double critical = probability * (1 - instantCut) * weakSpot;
            AddTransition(1, ordinary * (1 - doubleDamage));
            AddTransition(2, ordinary * doubleDamage);
            AddTransition(weakDamage, critical * (1 - doubleDamage));
            AddTransition(weakDamage * 2, critical * doubleDamage);

            void AddTransition(int damage, double weight)
            {
                if (remaining > damage) reach[remaining - damage] += weight;
            }
        }
        return expected;
    }
}
