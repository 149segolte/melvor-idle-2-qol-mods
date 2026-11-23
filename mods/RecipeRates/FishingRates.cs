using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2.Core.TickTimer;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Fishing;

namespace RecipeRates;

static class FishingRates
{
    public static Rates? For(ActiveFishingRecipe spot, FishingBaitComponent bait)
    {
        var stats = spot.Stats;
        var (min, max) = stats.Interval.Value;
        if (!double.IsFinite(min) || !double.IsFinite(max) || min < 0 || max < min) return null;

        double seconds = MeanCastSeconds(min, max);
        var rates = new Rates
        {
            SecondsPerReward = seconds,
            TimingSummary = $"One cast every {seconds:0.##}s",
        };
        rates.XpPerHour = stats.Experience.XpToAdd.Value * rates.RewardsPerHour;
        rates.BaitSummary = "Bait: None";
        if (bait.SelectedBait is { } selected)
        {
            int stock = bait.Inventory.GetQuantity(selected);
            string name = selected.DisplayName.GetPluralText(1);
            rates.BaitSummary = stock > 0 ? $"Bait: {name}" : $"Bait: None ({name} out of stock)";
            if (stock > 0)
            {
                // The current game build removes one bait even if it raises the preservation event.
                rates.Used.Add(new Rates.Line(selected.DisplayName.GetPluralText(2), -1, 1, selected, null)
                {
                    AvailableStock = stock,
                });
            }
        }

        var products = new Dictionary<Item, double>();
        double doubleChance = Rates.Chance(stats.DoublingChance);
        double fishChance = Math.Clamp(stats.FishChance.Value, 0, 1);
        var table = spot.Recipe.RandomProducts;

        // The priority roll can finish the main catch. Only the remaining casts roll for fish or junk.
        double priorityCatch = table.Items.Where(p => p.Item == spot.PriorityProduct).Sum(table.GetItemChance) * fishChance;
        double ordinaryFish = (1 - priorityCatch) * (fishChance + (1 - fishChance) * Rates.Chance(stats.JunkRerollChance));
        foreach (var fish in table.Items)
        {
            double catchChance = ordinaryFish + (fish.Item == spot.PriorityProduct ? fishChance : 0);
            double meanQuantity = (fish.MinQuantity + (double)fish.MaxQuantity) / 2;
            Add(fish.Item, catchChance * table.GetItemChance(fish) * meanQuantity * (1 + doubleChance));
        }

        if (spot.Recipe.JunkItem is { } junk)
        {
            double junkChance = Math.Max(0, 1 - priorityCatch - ordinaryFish);
            double conversion = stats.JunkToGP.Total.Value;
            if (conversion > 0 && junk.SellsFor is { } price)
            {
                double value = price.Quantity * conversion;
                double perCast = junkChance * ((1 - doubleChance) * Math.Floor(value) + doubleChance * Math.Floor(2 * value));
                if (perCast > 0)
                    rates.Made.Add(new Rates.Line(price.Currency.DisplayName.Text, perCast, 0, null, price.Currency));
            }
            else Add(junk, junkChance * (1 + doubleChance));
        }

        double stillRolling = 1;
        var special = new Dictionary<Item, double>();
        foreach (var node in stats.SpecialProductChances.Values.OrderBy(n => n.Value.Chance))
        {
            var drop = node.Value;
            double chance = Math.Clamp(drop.Chance, 0, 1);
            special[drop.Item] = special.GetValueOrDefault(drop.Item) + stillRolling * chance * drop.Quantity;
            stillRolling *= 1 - chance;
        }
        double specialRolls = 1 + (1 - stillRolling) * Rates.Chance(stats.SecondSpecialItemChance);
        foreach (var (item, quantity) in special) Add(item, quantity * specialRolls * (1 + doubleChance));

        foreach (var (item, quantity) in products) rates.AddProduct(item, quantity);
        return rates;

        void Add(Item item, double quantity)
        {
            if (quantity > 0) products[item] = products.GetValueOrDefault(item) + quantity;
        }
    }

    internal static double MeanCastSeconds(double min, double max)
    {
        int first = Math.Max(1, TickTimer.MSToTicks(min));
        int last = Math.Max(1, TickTimer.MSToTicks(max));
        double tick = TickTimer.TicksToMS(1);
        if (first == last) return first * tick / 1000;

        // Fishing rolls uniformly in milliseconds and the timer rounds to ticks. Only the two end buckets
        // can be partial. The full buckets in between average to the midpoint of the end tick counts.
        double lowBoundary = (first + 0.5) * tick;
        double highBoundary = (last - 0.5) * tick;
        double meanTicks = (first * (lowBoundary - min) + last * (max - highBoundary)
            + (first + (double)last) / 2 * (highBoundary - lowBoundary)) / (max - min);
        return meanTicks * tick / 1000;
    }
}
