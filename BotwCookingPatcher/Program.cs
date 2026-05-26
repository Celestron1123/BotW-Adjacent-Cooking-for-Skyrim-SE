// This is a simple mod that makes Skyrim's cooking system more 
// like Breath of the Wild's by generating new food items and recipes 
// based on the core ingredients. It uses Mutagen to read the existing 
// game data and create a new plugin with the generated content.
//
// Author: Elijah Potter
// Date: May 24, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;

namespace BotwCookingPatcher;

// Data Types
public enum IngredientType
{
    Filler, // Meat, just adds HP
    Buffer, // Adds special effects
    Catalyst // Flour; lengthens the duration of buffers
}

public struct CookingItem
{
    public string Name;
    public FormKey Id;
    public IReadOnlyList<IEffectGetter> Effects;
    public IngredientType Type;
}

// Main Program
public class Program
{
    // Entry Point
    public static void Main()
    {
        // Define paths and target ingredients
        var dataPath = @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition\Data";
        var desktopPath = @"C:\Users\epot1\Desktop";
        var pluginsToLoad = new[] { "Skyrim.esm", "HearthFires.esm" };

        // Define the core fillers and buffers we want to use for the combinations
        var fillers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Chicken Breast", "Horse Meat", "Leg of Goat", "Pheasant Breast",
            "Potato", "Raw Beef", "Raw Rabbit Leg", "Red Apple", "Venison", "Mammoth Snout"
        };

        var buffers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ale", "Cabbage", "Carrot", "Charred Skeever Hide", "Eidar Cheese Wheel",
            "Garlic", "Green Apple", "Horker Meat", "Leek", "Mudcrab Legs", "Salmon Meat", "Tomato"
        };

        // Combine into a single set for easy lookup, and also add the catalyst (Flour)
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        targetNames.UnionWith(fillers);
        targetNames.UnionWith(buffers);
        targetNames.Add("Sack of Flour");

        // For reference, this is the original list/logic of 23 core ingredients I wanted to include.
        // var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        // {
        //     "Ale", "Cabbage", "Carrot", "Charred Skeever Hide",
        //     "Chicken Breast", "Eidar Cheese Wheel", "Garlic", "Green Apple",
        //     "Horker Meat", "Horse Meat", "Leek", "Leg of Goat",
        //     "Mammoth Snout", "Mudcrab Legs", "Pheasant Breast", "Potato",
        //     "Raw Beef", "Raw Rabbit Leg", "Red Apple", "Sack of Flour",
        //     "Salmon Meat", "Tomato", "Venison"
        // };

        var validIngredients = new List<CookingItem>();

        // --- DATA INGESTION ---
        // Load the plugins and extract the relevant ingredients and food items
        foreach (var pluginName in pluginsToLoad)
        {
            var pluginPath = Path.Combine(dataPath, pluginName);
            Console.WriteLine($"Parsing {pluginName}...");
            var mod = SkyrimMod.CreateFromBinary(pluginPath, SkyrimRelease.SkyrimSE);

            // Scan the Food table
            foreach (var food in mod.Ingestibles)
            {
                if (food.Name != null && targetNames.Remove(food.Name.String))
                {
                    validIngredients.Add(new CookingItem
                    {
                        Name = food.Name.String,
                        Id = food.FormKey,
                        Effects = food.Effects,
                        Type = DetermineType(food.Name.String, fillers, buffers)
                    });
                    Console.WriteLine($"Ingested Food: {food.Name} ({validIngredients.Last().Type})");
                }
            }

            // Scan the Alchemy Ingredient table
            foreach (var ingredient in mod.Ingredients)
            {
                if (ingredient.Name != null && targetNames.Remove(ingredient.Name.String))
                {
                    validIngredients.Add(new CookingItem
                    {
                        Name = ingredient.Name.String,
                        Id = ingredient.FormKey,
                        Effects = ingredient.Effects,
                        Type = DetermineType(ingredient.Name.String, fillers, buffers)
                    });
                    Console.WriteLine($"Ingested Ingredient: {ingredient.Name} ({validIngredients.Last().Type})");
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
        Console.WriteLine("\nCalculating Raw Permutations...");
        var rawCombinations = new List<List<CookingItem>>();

        // Generate combinations of length 1, 2, and 3
        rawCombinations.AddRange(GetCombinations(validIngredients, 0, 1));
        rawCombinations.AddRange(GetCombinations(validIngredients, 0, 2));
        rawCombinations.AddRange(GetCombinations(validIngredients, 0, 3));

        Console.WriteLine($"Raw pool size: {rawCombinations.Count}");
        Console.WriteLine("Filtering combinations through gameplay constraints...");

        // Filter out combinations based on the rules:
        // - You can't cook flour by itself
        // - You cannot cook meals with clashing ingredients (i.e. two different buffers)
        // - Flour can only be used with meals that have buffers
        var filteredCombinations = rawCombinations.Where(IsValidCombination).ToList();
        Console.WriteLine($"Filtered down to {filteredCombinations.Count} valid recipe combinations!");

        // For demonstration, print 10 combinations after index 100
        Console.WriteLine("\nSample Combinations:");
        for (int i = 100; i < Math.Min(110, filteredCombinations.Count); i++)
        {
            Console.WriteLine($"  {string.Join(", ", filteredCombinations[i].Select(x => x.Name))}");
        }

        // Time to generate plugin records
        Console.WriteLine("\nGenerating Plugin Records...");
        var patchMod = new SkyrimMod(ModKey.FromNameAndExtension("BotwCooking.esp"), SkyrimRelease.SkyrimSE);

        // static FormKeys for the Cooking Pot and the Restore Health effect
        var cookingPotKeyword = new FormKey("Skyrim.esm", 0x0A5CB3);
        var restoreHealthEffect = new FormKey("Skyrim.esm", 0x03EB15);

        int recipeCount = 0;

        foreach (var combo in filteredCombinations)
        {
            if (combo.Count == 0) continue;

            recipeCount++;

            // --- CREATE THE NEW FOOD ITEM ---
            var newFood = patchMod.Ingestibles.AddNew();
            newFood.EditorID = $"BOTW_Food_{recipeCount}";
            newFood.Name = GenerateMealName(combo);
            newFood.Weight = combo.Count * 0.5f;
            newFood.Value = (uint)(combo.Count * 15);
            // newFood.Model = new Model { File = "clutter\\food\\potatobaked.nif" }; //TODO: make these dynamic
            // TODO: check if this works lol - also look into sorting meals so that the model prefers Meat -> Veggies -> Flour
            var primeIngredientKey = combo[0].Id;
            if (patchMod.Ingestibles.TryGetValue(primeIngredientKey, out var vanillaFood) && vanillaFood.Model != null)
            {
                newFood.Model = vanillaFood.Model.DeepCopy();
            }
            // Fallback to a hardcoded baseline model if the ingredient is an alchemy item (like Garlic) lacking a standard food model
            else
            {
                newFood.Model = new Model { File = @"clutter\food\potatobaked.nif" };
            }
            newFood.Flags |= Ingestible.Flag.FoodItem;

            var effect = new Effect();
            effect.BaseEffect.SetTo(restoreHealthEffect);
            effect.Data = new EffectData()
            {
                Magnitude = 10 * combo.Count,
                Duration = 0
            };
            newFood.Effects.Add(effect);

            // --- CREATE THE RECIPE ---
            var newRecipe = patchMod.ConstructibleObjects.AddNew();
            newRecipe.EditorID = $"BOTW_Recipe_{recipeCount}";
            newRecipe.CreatedObjectCount = 1;

            newRecipe.CreatedObject.SetTo(newFood);
            newRecipe.WorkbenchKeyword.SetTo(cookingPotKeyword);

            // Count the occurrences of each ingredient in the combo to set the recipe requirements
            var ingredientCounts = combo.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Count());

            // Add the ingredients to the recipe
            foreach (var kvp in ingredientCounts)
            {
                var reqItem = new ContainerEntry()
                {
                    Item = new ContainerItem()
                    {
                        Item = kvp.Key.ToLink<IItemGetter>(),
                        Count = kvp.Value
                    }
                };
                newRecipe.Items ??= new Noggog.ExtendedList<ContainerEntry>();
                newRecipe.Items.Add(reqItem);
            }
        }

        // Write the mod to disk!
        var outputPath = Path.Combine(desktopPath, "BotwCooking.esp");
        Console.WriteLine($"Writing {recipeCount} recipes to {outputPath}...");
        patchMod.WriteToBinary(outputPath);

        Console.WriteLine("Done! Mod successfully created.");
    }

    // A simple helper to determine the ingredient type based on the name
    private static IngredientType DetermineType(string name, HashSet<string> fillers, HashSet<string> buffers)
    {
        if (fillers.Contains(name)) return IngredientType.Filler;
        if (buffers.Contains(name)) return IngredientType.Buffer;
        return IngredientType.Catalyst; // "Sack of Flour"
    }

    // Pipeline Filter Implementing the 3 Design Rules
    private static bool IsValidCombination(List<CookingItem> combo)
    {
        // Rule 1: Single-ingredient meals cannot be a Catalyst (flour)
        if (combo.Count == 1 && combo[0].Type == IngredientType.Catalyst)
            return false;

        // Extract internal profiles
        var bufferCount = combo.Count(x => x.Type == IngredientType.Buffer);
        var catalystCount = combo.Count(x => x.Type == IngredientType.Catalyst);

        // Rule 2: Flour can only be used with meals that have Buffers
        if (catalystCount > 0 && bufferCount == 0)
            return false;

        // Rule 3: You cannot cook meals with clashing ingredients
        var distinctBuffers = combo.Where(x => x.Type == IngredientType.Buffer)
                                   .Select(x => x.Id)
                                   .Distinct();

        if (distinctBuffers.Count() > 1)
            return false;

        return true;
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

        return "Mixed Skewer";
    }
}
