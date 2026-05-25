using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;

namespace BotwCookingPatcher;

// 1. Data Normalization Struct
public struct CookingItem
{
    public string Name;
    public FormKey Id;
    public IReadOnlyList<IEffectGetter> Effects;
}

public class Program
{
    public static void Main()
    {
        var dataPath = @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition\Data";
        var desktopPath = @"C:\Users\epot1\Desktop";
        var pluginsToLoad = new[] { "Skyrim.esm", "HearthFires.esm" };

        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ale", "Cabbage", "Carrot", "Charred Skeever Hide",
            "Chicken Breast", "Eidar Cheese Wheel", "Garlic", "Green Apple",
            "Horker Meat", "Horse Meat", "Leek", "Leg of Goat",
            "Mammoth Snout", "Mudcrab Legs", "Pheasant Breast", "Potato",
            "Raw Beef", "Raw Rabbit Leg", "Red Apple", "Sack of Flour",
            "Salmon Meat", "Tomato", "Venison"
        };

        var validIngredients = new List<CookingItem>();

        foreach (var pluginName in pluginsToLoad)
        {
            var pluginPath = Path.Combine(dataPath, pluginName);
            Console.WriteLine($"Parsing {pluginName}...");
            var mod = SkyrimMod.CreateFromBinary(pluginPath, SkyrimRelease.SkyrimSE);

            // Scan the Food table
            foreach (var food in mod.Ingestibles)
            {
                if (food.Name != null && targetNames.Contains(food.Name.String))
                {
                    validIngredients.Add(new CookingItem
                    {
                        Name = food.Name.String,
                        Id = food.FormKey,
                        Effects = food.Effects
                    });
                    targetNames.Remove(food.Name.String);
                    Console.WriteLine($"Ingested Food: {food.Name}");
                }
            }

            // Scan the Alchemy Ingredient table (for Garlic)
            foreach (var ingredient in mod.Ingredients)
            {
                if (ingredient.Name != null && targetNames.Contains(ingredient.Name.String))
                {
                    validIngredients.Add(new CookingItem
                    {
                        Name = ingredient.Name.String,
                        Id = ingredient.FormKey,
                        Effects = ingredient.Effects
                    });
                    targetNames.Remove(ingredient.Name.String);
                    Console.WriteLine($"Ingested Ingredient: {ingredient.Name}");
                }
            }
        }

        Console.WriteLine("-----------------------------------------");
        Console.WriteLine($"Found {validIngredients.Count} / 23 core ingredients.");

        if (targetNames.Count > 0)
        {
            Console.WriteLine("Still missing: " + string.Join(", ", targetNames));
            return; // Stop if we don't have all 23
        }

        // --- THE ALGORITHM ---
        Console.WriteLine("\nCalculating Combinations...");
        var allCombinations = new List<List<CookingItem>>();

        // Generate combinations of length 1, 2, and 3
        allCombinations.AddRange(GetCombinations(validIngredients, 0, 1));
        allCombinations.AddRange(GetCombinations(validIngredients, 0, 2));
        allCombinations.AddRange(GetCombinations(validIngredients, 0, 3));

        Console.WriteLine($"Generated {allCombinations.Count} unique recipe combinations!");

        // For demonstration, print 10 combinations after index 100
        Console.WriteLine("\nSample Combinations:");
        for (int i = 100; i < Math.Min(110, allCombinations.Count); i++)
        {
            Console.WriteLine($"  {string.Join(", ", allCombinations[i].Select(x => x.Name))}");
        }

        // Time to generate plugin records
        Console.WriteLine("\nGenerating Plugin Records...");

        // 1. Create the new Patch Plugin
        var patchMod = new SkyrimMod(ModKey.FromNameAndExtension("BotwCooking.esp"), SkyrimRelease.SkyrimSE);

        // 2. We need the static FormKey for the Cooking Pot and the Restore Health effect
        var cookingPotKeyword = new FormKey("Skyrim.esm", 0x0A5CB3);
        var restoreHealthEffect = new FormKey("Skyrim.esm", 0x03EB15);

        int recipeCount = 0;

        foreach (var combo in allCombinations)
        {
            if (combo.Count == 0) continue;

            recipeCount++;

            // --- CREATE THE NEW FOOD ITEM ---
            var newFood = patchMod.Ingestibles.AddNew();
            newFood.EditorID = $"BOTW_Food_{recipeCount}";
            newFood.Name = GenerateMealName(combo);
            newFood.Weight = combo.Count * 0.5f;
            newFood.Value = (uint)(combo.Count * 15);
            newFood.Model = new Model { File = "clutter\\food\\potatobaked.nif" };
            newFood.Flags |= Ingestible.Flag.FoodItem;

            // Add the dynamic Health Effect directly
            var effect = new Effect();
            effect.BaseEffect.SetTo(restoreHealthEffect);
            effect.Data = new EffectData()
            {
                Magnitude = 10 * combo.Count,
                Duration = 0
            };
            newFood.Effects.Add(effect); // Mutagen already initialized this list!


            // --- CREATE THE RECIPE ---
            var newRecipe = patchMod.ConstructibleObjects.AddNew();
            newRecipe.EditorID = $"BOTW_Recipe_{recipeCount}";
            newRecipe.CreatedObjectCount = 1;

            newRecipe.CreatedObject.SetTo(newFood);
            newRecipe.WorkbenchKeyword.SetTo(cookingPotKeyword);

            var ingredientCounts = combo.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Count());

            foreach (var kvp in ingredientCounts)
            {
                var reqItem = new ContainerEntry()
                {
                    Item = new ContainerItem()
                    {
                        // FIX: Assign a fresh FormLink directly instead of calling .SetTo() on a null property
                        Item = kvp.Key.ToLink<IItemGetter>(),
                        Count = kvp.Value
                    }
                };
                newRecipe.Items ??= new Noggog.ExtendedList<ContainerEntry>();
                newRecipe.Items.Add(reqItem);
            }
        }

        // 3. Write the mod to disk!
        var outputPath = Path.Combine(desktopPath, "BotwCooking.esp");
        Console.WriteLine($"Writing {recipeCount} recipes to {outputPath}...");
        patchMod.WriteToBinary(outputPath);

        Console.WriteLine("Done! Mod successfully created.");
    }

    // Recursive generator for Combinations with Replacement
    public static IEnumerable<List<CookingItem>> GetCombinations(List<CookingItem> input, int startIndex, int length)
    {
        if (length == 0)
        {
            yield return new List<CookingItem>();
            yield break;
        }

        for (int i = startIndex; i < input.Count; i++)
        {
            foreach (var combo in GetCombinations(input, i, length - 1))
            {
                var result = new List<CookingItem> { input[i] };
                result.AddRange(combo);
                yield return result;
            }
        }
    }

    // A simple helper to make the names look somewhat natural
    public static string GenerateMealName(List<CookingItem> ingredients)
    {
        if (ingredients.Count == 1)
            return $"Cooked {ingredients[0].Name}";

        var distinctNames = ingredients.Select(i => i.Name).Distinct().ToList();

        if (distinctNames.Count == 1)
            return $"Hearty {distinctNames[0]} Meal";

        if (distinctNames.Count == 2)
            return $"{distinctNames[0]} and {distinctNames[1]}";

        return "Dubious Skewer"; // The BOTW fallback!
    }
}
