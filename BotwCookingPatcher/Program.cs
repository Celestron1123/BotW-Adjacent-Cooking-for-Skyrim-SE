// This is a simple mod that makes Skyrim's cooking system more 
// like Breath of the Wild's by generating new food items and recipes 
// based on the core ingredients. It uses Mutagen to read the existing 
// game data and create a new plugin with the generated content.
//
// Author: Elijah Potter
// Date: May 29, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Noggog;

namespace BotwCookingPatcher;

// Data Types
public enum IngredientType
{
    Filler, // Meat, just adds HP
    Buffer, // Adds special effects
    Catalyst // Flour; lengthens the duration of buffers
}

// An enum that categorizes ingredients into broad culinary types for more flavorful meal name generation
public enum CulinaryTag
{
    Meat, Seafood, Fruit, Vegetable, Tuber, Dairy, Aromatic, Grain, Drink, Monster
}

// A simple struct to hold the relevant data for each ingredient/food item we want to use in the combinations
public struct CookingItem
{
    public string Name;
    public FormKey Id;
    public IReadOnlyList<IEffectGetter> Effects;
    public IngredientType Type;
    public Model? ModelData;
    public CulinaryTag Tag;
    public uint Value;
}

// A simple struct to hold the relevant data for the effects we want to apply to the generated food items
public struct BufferEffectData
{
    public FormKey EffectId;
    public float Magnitude;
    public int Duration;
}

// Main Program
public class Program
{
    // The meal name prefix Dictionary
    private static readonly Dictionary<string, string> BufferPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Ale", "Invigorating" }, { "Cabbage", "Enduring" }, { "Carrot", "Keen-Eyed" },
        { "Charred Skeever Hide", "Gritty" }, { "Eidar Cheese Wheel", "Ironclad" },
        { "Garlic", "Revitalizing" }, { "Green Apple", "Tireless" }, { "Horker Meat", "Spicy" },
        { "Leek", "Mystic" }, { "Mudcrab Legs", "Mighty" }, { "Salmon Meat", "Amphibious" },
        { "Tomato", "Chilly" }
    };

    public static List<Model?> foodModels = [];

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
        var validIngredients = new List<CookingItem>();

        // Model form keys
        var stewFormKey = new FormKey("Skyrim.esm", 0x000EBA01);
        var horkerLoafFormKey = new FormKey("Skyrim.esm", 0x0007224E);
        var homeCookedMealFormKey = new FormKey("Skyrim.esm", 0x00064B43);
        var chickenDumplingFormKey = new FormKey("HearthFires.esm", 0x000117ff);
        var horseHaunchFormKey = new FormKey("Skyrim.esm", 0x000722B0);
        var tankardFormKey = new FormKey("Skyrim.esm", 0x000319E3);
        var bakedPotatoesFormKey = new FormKey("Skyrim.esm", 0x00064B3A);

        // The actual models
        Model? stew = null;
        Model? horkerLoaf = null;
        Model? homeCookedMeal = null;
        Model? chickenDumpling = null;
        Model? horseHaunch = null;
        Model? tankard = null;
        Model? bakedPotatoes = null;

        // add a dummy so the starting index is 1 LOL
        foodModels.Add(null);

        // --- DATA INGESTION ---
        // Load the plugins and extract the relevant ingredients and food items
        foreach (var pluginName in pluginsToLoad)
        {
            var pluginPath = Path.Combine(dataPath, pluginName);
            Console.WriteLine($"Parsing {pluginName}...");
            var skyrim = SkyrimMod.CreateFromBinary(pluginPath, SkyrimRelease.SkyrimSE);

            if (pluginName == "Skyrim.esm")
            {
                if (skyrim.Ingestibles.TryGetValue(stewFormKey, out var appleCabbageStew) && appleCabbageStew.Model != null)
                {
                    stew = appleCabbageStew.Model.DeepCopy();
                    foodModels.Add(stew);
                    Console.WriteLine("1 Successfully cached Apple Cabbage Stew model. Index: " + (foodModels.Count - 1));
                }
                if (skyrim.Ingestibles.TryGetValue(horkerLoafFormKey, out var horker) && horker.Model != null)
                {
                    horkerLoaf = horker.Model.DeepCopy();
                    foodModels.Add(horkerLoaf);
                    Console.WriteLine("2 Successfully cached Horker Loaf model. Index: " + (foodModels.Count - 1));
                }
                if (skyrim.Ingestibles.TryGetValue(homeCookedMealFormKey, out var home) && home.Model != null)
                {
                    homeCookedMeal = home.Model.DeepCopy();
                    foodModels.Add(homeCookedMeal);
                    Console.WriteLine("3 Successfully cached Home Cooked Meal model. Index: " + (foodModels.Count - 1));
                }
                if (skyrim.Ingestibles.TryGetValue(horseHaunchFormKey, out var hors) && hors.Model != null)
                {
                    horseHaunch = hors.Model.DeepCopy();
                    foodModels.Add(horseHaunch);
                    Console.WriteLine("4 Successfully cached horse haunch model. Index: " + (foodModels.Count - 1));
                }
                if (skyrim.MiscItems.TryGetValue(tankardFormKey, out var tank) && tank.Model != null)
                {
                    tankard = tank.Model.DeepCopy();
                    foodModels.Add(tankard);
                    Console.WriteLine("5 Successfully cached Tankard model. Index: " + (foodModels.Count - 1));
                }
                if (skyrim.Ingestibles.TryGetValue(bakedPotatoesFormKey, out var potat) && potat.Model != null)
                {
                    bakedPotatoes = potat.Model.DeepCopy();
                    foodModels.Add(bakedPotatoes);
                    Console.WriteLine("6 Successfully cached Potato model. Index: " + (foodModels.Count - 1));
                }
            }
            else if (pluginName == "HearthFires.esm")
            {
                if (skyrim.Ingestibles.TryGetValue(chickenDumplingFormKey, out var dump) && dump.Model != null)
                {
                    chickenDumpling = dump.Model.DeepCopy();
                    foodModels.Add(chickenDumpling);
                    Console.WriteLine("7 Successfully cached Chicken Dumpling model. Index: " + (foodModels.Count - 1));
                }
            }

            // Scan the Food table
            foreach (var food in skyrim.Ingestibles)
            {
                if (food.Name != null && targetNames.Remove(food.Name.String))
                {
                    validIngredients.Add(new CookingItem
                    {
                        Name = food.Name.String,
                        Id = food.FormKey,
                        Effects = food.Effects,
                        Type = DetermineType(food.Name.String, fillers, buffers),
                        ModelData = food.Model?.DeepCopy(),
                        Tag = DetermineTag(food.Name.String),
                        Value = food.Value
                    });
                    Console.WriteLine($"Ingested Food: {food.Name} ({validIngredients.Last().Type})");
                }
            }

            // Scan the Alchemy Ingredient table
            foreach (var ingredient in skyrim.Ingredients)
            {
                if (ingredient.Name != null && targetNames.Remove(ingredient.Name.String))
                {
                    validIngredients.Add(new CookingItem
                    {
                        Name = ingredient.Name.String,
                        Id = ingredient.FormKey,
                        Effects = ingredient.Effects,
                        Type = DetermineType(ingredient.Name.String, fillers, buffers),
                        ModelData = ingredient.Model?.DeepCopy(),
                        Tag = DetermineTag(ingredient.Name.String),
                        Value = ingredient.Value
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

        // Time to generate plugin records
        Console.WriteLine("\nGenerating Plugin Records...");
        var patchMod = new SkyrimMod(ModKey.FromNameAndExtension("BotwCooking.esp"), SkyrimRelease.SkyrimSE);

        // static FormKey for the Cooking Pot
        var cookingPotKeyword = new FormKey("Skyrim.esm", 0x0A5CB3);

        // static FormKey for the vanilla eating sound descriptor
        var eatSoundDescriptor = new FormKey("Skyrim.esm", 0xcaf94);

        // static FormKeys for all other effects via buffers
        var effectRestoreHealth = new FormKey("Skyrim.esm", 0x03EB15); // AlchRestoreHealth
        var effectStaminaRegen = new FormKey("Skyrim.esm", 0x03EB08); // AlchRegenStamina
        var effectCarryWeight = new FormKey("Skyrim.esm", 0x03EB01); // AlchFortifyCarryWeight
        var effectArcheryUp = new FormKey("Skyrim.esm", 0x03EB1B); // AlchFortifyMarksman
        var effectResistPoison = new FormKey("Skyrim.esm", 0x090041); // AlchResistPoison
        var effectFortifyArmor = new FormKey("Skyrim.esm", 0x03EB1E); // AlchFortifyHeavyArmor
        var effectHealthRegen = new FormKey("Skyrim.esm", 0x03EB06); // AlchRegenHealth
        var effectFortifyStam = new FormKey("Skyrim.esm", 0x03EAF9); // AlchFortifyStamina
        var effectResistFrost = new FormKey("Skyrim.esm", 0x03EAEB); // AlchResistFrost
        var effectFortifyMag = new FormKey("Skyrim.esm", 0x03EAF8); // AlchFortifyMagicka
        var effectFortifyMelee = new FormKey("Skyrim.esm", 0x03EB19); // AlchFortifyOneHanded
        var effectWaterbreath = new FormKey("Skyrim.esm", 0x03AC2D); // AlchWaterbreathing
        var effectResistFire = new FormKey("Skyrim.esm", 0x03EAEA); // AlchResistFire

        // CSV Output for Testing
        var effectNames = new Dictionary<FormKey, string>()
        {
            { effectRestoreHealth, "Restore Health" },
            { effectStaminaRegen, "Regenerate Stamina" },
            { effectCarryWeight, "Fortify Carry Weight" },
            { effectArcheryUp, "Fortify Marksman" },
            { effectResistPoison, "Resist Poison" },
            { effectFortifyArmor, "Fortify Heavy Armor" },
            { effectHealthRegen, "Regenerate Health" },
            { effectFortifyStam, "Fortify Stamina" },
            { effectResistFrost, "Resist Frost" },
            { effectFortifyMag, "Fortify Magicka" },
            { effectFortifyMelee, "Fortify One-Handed" },
            { effectWaterbreath, "Waterbreathing" },
            { effectResistFire, "Resist Fire" }
        };

        // A simple mapping of filler ingredients to their HP values for the Restore Health effect magnitude calculation
        var fillerHpValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Mammoth Snout", 60 },
            { "Raw Beef", 25 }, { "Venison", 25 },
            { "Horse Meat", 20 }, { "Leg of Goat", 20 },
            { "Chicken Breast", 15 }, { "Pheasant Breast", 15 }, { "Raw Rabbit Leg", 15 },
            { "Potato", 10 }, { "Red Apple", 10 }
        };

        // A mapping of buffer ingredients to their corresponding effects, magnitudes, and durations
        var bufferData = new Dictionary<string, BufferEffectData>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ale", new BufferEffectData                   { EffectId = effectStaminaRegen,    Magnitude = 15, Duration = 60 } },
            { "Cabbage", new BufferEffectData               { EffectId = effectCarryWeight,     Magnitude = 15, Duration = 90 } },
            { "Carrot", new BufferEffectData                { EffectId = effectArcheryUp,       Magnitude = 5,  Duration = 60 } },
            { "Charred Skeever Hide", new BufferEffectData  { EffectId = effectResistPoison,    Magnitude = 30, Duration = 60 } },
            { "Eidar Cheese Wheel", new BufferEffectData    { EffectId = effectFortifyArmor,    Magnitude = 15, Duration = 90 } },
            { "Garlic", new BufferEffectData                { EffectId = effectHealthRegen,     Magnitude = 15, Duration = 60 } },
            { "Green Apple", new BufferEffectData           { EffectId = effectFortifyStam,     Magnitude = 15, Duration = 60 } },
            { "Horker Meat", new BufferEffectData           { EffectId = effectResistFrost,     Magnitude = 15, Duration = 90 } },
            { "Leek", new BufferEffectData                  { EffectId = effectFortifyMag,      Magnitude = 15, Duration = 60 } },
            { "Mudcrab Legs", new BufferEffectData          { EffectId = effectFortifyMelee,    Magnitude = 15, Duration = 60 } },
            { "Salmon Meat", new BufferEffectData           { EffectId = effectWaterbreath,     Magnitude = 1,  Duration = 30 } },
            { "Tomato", new BufferEffectData                { EffectId = effectResistFire,      Magnitude = 15, Duration = 90 } }
        };

        // --- ADD CSV SETUP ---
        var csvPath = Path.Combine(desktopPath, "GeneratedMeals.csv");
        using var csvWriter = new StreamWriter(csvPath);
        csvWriter.WriteLine("MealName,Healing,Effect,EffectMagnitude,EffectDuration,Ingredient1,Ingredient2,Ingredient3,Weight,Price");

        int recipeCount = 0;

        foreach (var combo in filteredCombinations)
        {
            if (combo.Count == 0) continue;
            recipeCount++;

            // --- DYNAMIC EFFECT CALCULATION ---
            int totalHp = 0;
            float buffMagnitude = 0;
            int buffDuration = 0;
            FormKey? activeBuffId = null;
            int flourCount = 0;

            foreach (var item in combo)
            {
                if (item.Type == IngredientType.Filler)
                {
                    totalHp += fillerHpValues[item.Name];
                }
                else if (item.Type == IngredientType.Buffer)
                {
                    totalHp += 10; // Base HP for all buffers
                    if (bufferData.TryGetValue(item.Name, out var bData))
                    {
                        activeBuffId = bData.EffectId;
                        buffMagnitude += bData.Magnitude;
                        buffDuration += bData.Duration;
                    }
                }
                else if (item.Type == IngredientType.Catalyst)
                {
                    totalHp += 5;
                    flourCount++;
                }
            }

            // Apply the Flour Multiplier (2.5x per flour)
            if (flourCount > 0 && activeBuffId != null)
            {
                double multiplier = Math.Pow(2.5, flourCount);
                buffDuration = (int)(buffDuration * multiplier);
            }

            // --- CREATE THE NEW FOOD ITEM ---
            var newFood = patchMod.Ingestibles.AddNew();
            newFood.EditorID = $"BOTW_Food_{recipeCount}";
            newFood.ConsumeSound.SetTo(eatSoundDescriptor);
            newFood.Name = GenerateMealName(combo);
            newFood.Weight = combo.Count * 0.5f;
            uint baseValue = (uint)combo.Sum(i => i.Value);
            newFood.Value = (uint)(baseValue * 1.5);
            newFood.Model = DetermineModel(combo, newFood.Name.String);
            newFood.Flags |= Ingestible.Flag.FoodItem;
            newFood.Flags |= Ingestible.Flag.NoAutoCalc;

            // Attach Base Health Effect
            var hpEffect = new Effect();
            hpEffect.BaseEffect.SetTo(effectRestoreHealth);
            hpEffect.Data = new EffectData()
            {
                Magnitude = totalHp,
                Duration = 0
            };
            newFood.Effects.Add(hpEffect);

            // Attach Status Effect (if applicable)
            if (activeBuffId.HasValue)
            {
                var statusEffect = new Effect();
                statusEffect.BaseEffect.SetTo(activeBuffId.Value);
                statusEffect.Data = new EffectData()
                {
                    Magnitude = buffMagnitude,
                    Duration = buffDuration
                };
                newFood.Effects.Add(statusEffect);
            }

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

            // --- ADD CSV ROW EXPORT ---
            string mealName = newFood.Name?.String ?? "Unknown Meal";
            string effectName = activeBuffId.HasValue && effectNames.TryGetValue(activeBuffId.Value, out var eName) ? eName : "";
            string magnitudeStr = activeBuffId.HasValue ? buffMagnitude.ToString() : "";
            string durationStr = activeBuffId.HasValue ? buffDuration.ToString() : "";
            string ing1 = combo.Count > 0 ? combo[0].Name : "";
            string ing2 = combo.Count > 1 ? combo[1].Name : "";
            string ing3 = combo.Count > 2 ? combo[2].Name : "";

            csvWriter.WriteLine($"{mealName},{totalHp},{effectName},{magnitudeStr},{durationStr},{ing1},{ing2},{ing3},{newFood.Weight},{newFood.Value}");
        }

        // Write the mod to disk!
        var outputPath = Path.Combine(desktopPath, "BotwCooking.esp");
        Console.WriteLine($"Writing {recipeCount} recipes to {outputPath}...");
        patchMod.WriteToBinary(outputPath);

        Console.WriteLine("Done! Mod successfully created.");
    }

    // A helper to assign food models :)))
    private static Model? DetermineModel(List<CookingItem> ingredients, string mealName)
    {
        string nameLower = mealName.ToLower();

        // 1. Tankard (Pure Drinks)
        if (ingredients.All(x => x.Tag == CulinaryTag.Drink))
        {
            return foodModels[5]; // tankard
        }
        // 2. Skewer (or Horker Loaf)
        else if (nameLower.Contains("skewer"))
        {
            return foodModels[2]; // horker loaf
        }
        // 3. Rustic Pie (Grain + Meat or Fruit)
        else if (ingredients.Any(x => x.Tag == CulinaryTag.Grain) &&
                 ingredients.Any(x => x.Tag == CulinaryTag.Meat || x.Tag == CulinaryTag.Fruit))
        {
            return foodModels[3]; // pie
        }
        // 4. Dumpling / Bread Board (Grain WITHOUT Meat/Fruit - caught by the statement above)
        else if (ingredients.Any(x => x.Tag == CulinaryTag.Grain))
        {
            return foodModels[7]; // dumpling
        }
        // 5. Plated Roast
        else if ((nameLower.Contains("surf and turf") || nameLower.Contains("braised") || nameLower.Contains("garlic")) &&
                 ingredients.Any(x => x.Tag == CulinaryTag.Meat || x.Tag == CulinaryTag.Seafood))
        {
            return foodModels[4]; // roast
        }
        // 6. Side Plate
        else if (ingredients.All(x => x.Tag == CulinaryTag.Fruit || x.Tag == CulinaryTag.Vegetable || x.Tag == CulinaryTag.Tuber || x.Tag == CulinaryTag.Dairy) ||
                (ingredients.Count == 1 && ingredients[0].Tag != CulinaryTag.Meat))
        {
            return foodModels[6]; // side plate
        }

        // 7. Wooden Bowl (Stews, Chowders, Suspicious Stew, and Universal Fallback)
        return foodModels[1]; // stew
    }

    // A simple helper to determine the culinary tag based on the ingredient name for more flavorful meal name generation
    private static CulinaryTag DetermineTag(string name)
    {
        return name.ToLower() switch
        {
            "chicken breast" or "horse meat" or "leg of goat" or "pheasant breast" or
            "raw beef" or "raw rabbit leg" or "venison" or "mammoth snout" or "horker meat"
                => CulinaryTag.Meat,
            "mudcrab legs" or "salmon meat" => CulinaryTag.Seafood,
            "red apple" or "green apple" or "tomato" => CulinaryTag.Fruit,
            "cabbage" or "carrot" or "leek" => CulinaryTag.Vegetable,
            "potato" => CulinaryTag.Tuber,
            "eidar cheese wheel" => CulinaryTag.Dairy,
            "garlic" => CulinaryTag.Aromatic,
            "sack of flour" => CulinaryTag.Grain,
            "ale" => CulinaryTag.Drink,
            "charred skeever hide" => CulinaryTag.Monster,
            _ => CulinaryTag.Vegetable // Fallback
        };
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

    // A more flavorful meal name generator that uses the ingredient types and tags to create unique names for each meal combination
    public static string GenerateMealName(List<CookingItem> ingredients)
    {
        // 1. Calculate the Prefix
        string prefix = "Hearty"; // Default for pure-filler meals
        var bufferItem = ingredients.FirstOrDefault(i => i.Type == IngredientType.Buffer);

        if (bufferItem.Name != null && BufferPrefixes.TryGetValue(bufferItem.Name, out var matchedPrefix))
        {
            prefix = matchedPrefix;
        }

        // 2. Filter for The Skeever Exception (Monster Tag Override)
        if (ingredients.Any(i => i.Tag == CulinaryTag.Monster))
        {
            return $"{prefix} Suspicious Stew";
        }

        // 3. Filter for Single-Ingredient Meals (Just one item in the pot)
        if (ingredients.Count == 1)
        {
            var singleItem = ingredients[0];

            // If it's a standard Filler (No buffs)
            if (singleItem.Type == IngredientType.Filler)
            {
                if (singleItem.Tag == CulinaryTag.Meat)
                    return "Cooked Meat Skewer";

                return $"Cooked {singleItem.Name}"; // e.g., "Cooked Potato"
            }

            // If it's a single Buffer (Has a buff)
            return $"{prefix} Cooked {singleItem.Name}"; // e.g., "Chilling Cooked Tomato"
        }

        // 4. Filter for Purebreds (3 of the exact same item)
        if (ingredients.Count == 3 && ingredients.All(i => i.Name == ingredients[0].Name))
        {
            string pureName = ingredients[0].Name.ToLower();
            if (pureName == "potato") return $"{prefix} Mashed Potatoes";
            if (pureName.Contains("apple")) return $"{prefix} Simmered Compote";
            if (pureName == "tomato") return $"{prefix} Salsa";
            if (pureName.Contains("cheese")) return $"{prefix} Cheese Fondue";
            if (pureName == "ale") return $"{prefix} Mulled Ale";
            if (ingredients[0].Tag == CulinaryTag.Meat) return $"{prefix} Generous Meat Skewer";
        }

        // 5. Extract Distinct Tags for the Matrix
        var tags = ingredients.Select(i => i.Tag).Distinct().ToList();

        // 7. The Structural Modifiers (Flour overrides everything else)
        if (tags.Contains(CulinaryTag.Grain))
        {
            if (tags.Contains(CulinaryTag.Meat)) return $"{prefix} Meat Pie";
            if (tags.Contains(CulinaryTag.Fruit)) return $"{prefix} Pastry";
            if (tags.Contains(CulinaryTag.Vegetable) || tags.Contains(CulinaryTag.Tuber)) return $"{prefix} Dumplings";
            if (tags.Contains(CulinaryTag.Dairy)) return $"{prefix} Cheese Bread";
            return $"{prefix} Baked Goods"; // Fallback for weird flour combos
        }

        // 8. The Standard Meal Intersections
        if (tags.Contains(CulinaryTag.Meat) && tags.Contains(CulinaryTag.Seafood))
            return $"{prefix} Surf and Turf";

        if (tags.Contains(CulinaryTag.Seafood) && (tags.Contains(CulinaryTag.Vegetable) || tags.Contains(CulinaryTag.Fruit) || tags.Contains(CulinaryTag.Tuber)))
            return $"{prefix} Seafood Chowder";

        if (tags.Contains(CulinaryTag.Meat) && (tags.Contains(CulinaryTag.Vegetable) || tags.Contains(CulinaryTag.Fruit) || tags.Contains(CulinaryTag.Tuber)))
            return $"{prefix} Meat Stew";

        if (tags.Contains(CulinaryTag.Drink)) // Ale + Something else
        {
            if (tags.Contains(CulinaryTag.Meat)) return $"{prefix} Braised Meat";
            if (tags.Contains(CulinaryTag.Seafood)) return $"{prefix} Braised Seafood";
            return $"{prefix} Tavern Braise";
        }

        if (tags.Contains(CulinaryTag.Aromatic)) // Garlic + Something else
        {
            if (tags.Contains(CulinaryTag.Meat)) return $"{prefix} Garlic-Buttered Meat";
            if (tags.Contains(CulinaryTag.Seafood)) return $"{prefix} Garlic-Buttered Seafood";
            if (tags.Contains(CulinaryTag.Tuber)) return $"{prefix} Garlic-Roasted Potatoes";
        }

        // 7. Base Category Fallbacks (If it's just 2 of the same tag, or tags that didn't trigger a special intersection)
        if (tags.Contains(CulinaryTag.Meat)) return $"{prefix} Meat Skewer";
        if (tags.Contains(CulinaryTag.Seafood)) return $"{prefix} Seafood Skewer";
        if (tags.Contains(CulinaryTag.Dairy)) return $"{prefix} Cheesy Meal";
        if (tags.Contains(CulinaryTag.Vegetable) || tags.Contains(CulinaryTag.Tuber)) return $"{prefix} Veggie Medley";
        if (tags.Contains(CulinaryTag.Fruit)) return $"{prefix} Fruit Simmer";

        // Ultimate Fallback (Should rarely be hit with our 6 combo rules)
        return $"{prefix} Mixed Meal";
    }
}
