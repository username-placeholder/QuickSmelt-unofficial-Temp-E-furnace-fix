using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Quick Smelt", "misticos", "5.1.5")]
    [Description("Increases the speed of the furnace smelting")]
    class QuickSmelt : RustPlugin
    {
        #region Variables

        private static QuickSmelt _instance;

        private const string PermissionUse = "quicksmelt.use";

        #endregion

        #region Configuration

        private static Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Use Permission")]
            public bool UsePermission = true;

            [JsonProperty(PropertyName = "Speed Multipliers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> SpeedMultipliers = new Dictionary<string, float>
            {
                { "global", 1.0f },
                { "furnace.shortname", 1.0f }
            };

            [JsonProperty(PropertyName = "Fuel Usage Speed Multipliers",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> FuelSpeedMultipliers = new Dictionary<string, float>
            {
                { "global", 1.0f },
                { "furnace.shortname", 1.0f }
            };

            [JsonProperty(PropertyName = "Fuel Usage Multipliers",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> FuelUsageMultipliers = new Dictionary<string, int>
            {
                { "global", 1 },
                { "furnace.shortname", 1 }
            };

            [JsonProperty(PropertyName = "Output Multipliers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, Dictionary<string, float>> OutputMultipliers =
                new Dictionary<string, Dictionary<string, float>>
                {
                    {
                        "global", new Dictionary<string, float>
                        {
                            { "global", 1.0f }
                        }
                    },
                    {
                        "furnace.shortname", new Dictionary<string, float>
                        {
                            { "item.shortname", 1.0f }
                        }
                    }
                };

            [JsonProperty(PropertyName = "Whitelist", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> Whitelist = new Dictionary<string, List<string>>
            {
                {
                    "global", new List<string>
                    {
                        "item.shortname"
                    }
                },
                {
                    "furnace.shortname", new List<string>
                    {
                        "item.shortname"
                    }
                }
            };

            [JsonProperty(PropertyName = "Blacklist", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> Blacklist = new Dictionary<string, List<string>>
            {
                {
                    "global", new List<string>
                    {
                        "item.shortname"
                    }
                },
                {
                    "furnace.shortname", new List<string>
                    {
                        "item.shortname"
                    }
                }
            };

            [JsonProperty(PropertyName = "Smelting Frequencies (Smelt items every N smelting ticks)",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> SmeltingFrequencies = new Dictionary<string, int>
            {
                { "global", 1 },
                { "furnace.shortname", 1 }
            };

            [JsonProperty(PropertyName = "Debug")]
            public bool Debug = false;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Hooks

        private void Unload()
        {
            var ovens = UnityEngine.Object.FindObjectsOfType<BaseOven>();
            PrintDebug($"Processing BaseOven(s).. Amount: {ovens.Length}.");

            for (var i = 0; i < ovens.Length; i++)
            {
                var oven = ovens[i];
                var component = oven.GetComponent<FurnaceController>();

                if (oven.IsOn())
                {
                    PrintDebug("Oven is on. Restarted cooking");
                    oven.StartCooking();
                }

                UnityEngine.Object.Destroy(component);
            }

            PrintDebug("Done.");
        }

        private void OnServerInitialized()
        {
            _instance = this;
            permission.RegisterPermission(PermissionUse, this);

            var ovens = UnityEngine.Object.FindObjectsOfType<BaseOven>();
            PrintDebug($"Processing BaseOven(s).. Amount: {ovens.Length}.");

            for (var i = 0; i < ovens.Length; i++)
            {
                var oven = ovens[i];

                OnEntitySpawned(oven);
            }

            timer.Once(1f, () =>
            {
                for (var i = 0; i < ovens.Length; i++)
                {
                    var oven = ovens[i];
                    if (oven == null)
                    {
                        continue;
                    }

                    var component = oven.gameObject.GetComponent<FurnaceController>();

                    if (oven.IsDestroyed || !oven.IsOn() || !CanUse(oven.OwnerID))
                        continue;

                    component.StartCooking();
                }
            });
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var oven = entity as BaseOven;
            if (oven == null)
                return;

            oven.gameObject.AddComponent<FurnaceController>();
        }

        private object OnOvenToggle(StorageContainer oven, BasePlayer player)
        {
            if (oven is BaseFuelLightSource || oven.needsBuildingPrivilegeToUse && !player.CanBuild())
                return null;

            PrintDebug("OnOvenToggle called");
            var component = oven.gameObject.GetComponent<FurnaceController>();
            var canUse = CanUse(oven.OwnerID) || CanUse(player.userID);
            if (oven.IsOn())
            {
            }
            else
            {
                if (canUse)
                    component.StartCooking();
                else
                {
                    PrintDebug($"No permission ({player.userID})");
                    return null;
                }
            }

            return false;
        }

        #endregion

        #region Helpers

        private bool CanUse(ulong id) =>
            !_config.UsePermission || permission.UserHasPermission(id.ToString(), PermissionUse);

        private static void PrintDebug(string message)
        {
            if (_config.Debug)
                Debug.Log($"DEBUG ({_instance.Name}) > " + message);
        }

        #endregion

        #region Controller

        public class FurnaceController : FacepunchBehaviour
        {
            private int _ticks;

            private BaseOven _oven;

            private BaseOven Furnace
            {
                get
                {
                    if (_oven == null)
                        _oven = GetComponent<BaseOven>();

                    return _oven;
                }
            }

            private float _speedMultiplier;

            private float _fuelSpeedMultiplier;

            private int _fuelUsageMultiplier;

            private int _smeltingFrequency;

            private Dictionary<string, float> _outputModifiers;

            private float OutputMultiplier(string shortname)
            {
                float modifier;
                if (_outputModifiers == null || !_outputModifiers.TryGetValue(shortname, out modifier) &&
                    !_outputModifiers.TryGetValue("global", out modifier))
                    modifier = 1.0f;

                PrintDebug($"{shortname} modifier: {modifier}");
                return modifier;
            }

            private List<string> _blacklist;
            private List<string> _whitelist;

            private bool? IsAllowed(string shortname)
            {
                if (_blacklist != null && _blacklist.Contains(shortname))
                    return false;

                if (_whitelist != null && _whitelist.Contains(shortname))
                    return true;

                return null;
            }

            private void Awake()
            {
                // Well, sorry for my complicated code. But that should work faster! :)

                float modifierF; // float modifier
                int modifierI; // int modifier

                if (!_config.SpeedMultipliers.TryGetValue(Furnace.ShortPrefabName, out modifierF) &&
                    !_config.SpeedMultipliers.TryGetValue("global", out modifierF))
                    modifierF = 1.0f;

                _speedMultiplier = 0.5f / modifierF;

                if (!_config.FuelSpeedMultipliers.TryGetValue(Furnace.ShortPrefabName, out modifierF) &&
                    !_config.FuelSpeedMultipliers.TryGetValue("global", out modifierF))
                    modifierF = 1.0f;

                _fuelSpeedMultiplier = modifierF;

                if (!_config.FuelUsageMultipliers.TryGetValue(Furnace.ShortPrefabName, out modifierI) &&
                    !_config.FuelUsageMultipliers.TryGetValue("global", out modifierI))
                    modifierI = 1;

                _fuelUsageMultiplier = modifierI;

                if (!_config.SmeltingFrequencies.TryGetValue(Furnace.ShortPrefabName, out modifierI) &&
                    !_config.SmeltingFrequencies.TryGetValue("global", out modifierI))
                    modifierI = 1;

                _smeltingFrequency = modifierI;

                if (!_config.OutputMultipliers.TryGetValue(Furnace.ShortPrefabName, out _outputModifiers) &&
                    !_config.OutputMultipliers.TryGetValue("global", out _outputModifiers))
                {
                    // ignored
                }

                if ((!_config.Blacklist.TryGetValue(Furnace.ShortPrefabName, out _blacklist) &&
                     !_config.Blacklist.TryGetValue("global", out _blacklist)) &
                    (!_config.Whitelist.TryGetValue(Furnace.ShortPrefabName, out _whitelist) &&
                     !_config.Whitelist.TryGetValue("global", out _whitelist)))
                {
                    // ignored
                }
            }

            private Item FindBurnable()
            {
                if (Furnace.inventory == null)
                    return null;

                var burnable = Interface.Call<Item>("OnFindBurnable", _oven);
                if (burnable != null)
                {
                    return burnable;
                }

                foreach (var item in Furnace.inventory.itemList)
                {
                    if (!_oven.IsBurnableItem(item))
                        continue;

                    return item;
                }

                return null;
            }

            public void Cook()
            {
                var itemBurnable = FindBurnable();
                if (Interface.CallHook("OnOvenCook", this, itemBurnable) != null)
                {
                    return;
                }

                if (itemBurnable == null)
                {
                }

                SmeltItems();

                foreach (var itemCooking in _oven.inventory.itemList)
                {
                    if (itemCooking.position >= _oven._inputSlotIndex &&
                        itemCooking.position < _oven._inputSlotIndex + _oven.inputSlots &&
                        !itemCooking.HasFlag(global::Item.Flag.Cooking))
                    {
                        itemCooking.SetFlag(global::Item.Flag.Cooking, true);
                        itemCooking.MarkDirty();
                    }
                }

                var slot = Furnace.GetSlot(BaseEntity.Slot.FireMod);
                if (slot)
                {
                    slot.SendMessage("Cook", 0.5f, SendMessageOptions.DontRequireReceiver);
                }

                var burnable = itemBurnable.info.GetComponent<ItemModBurnable>();
                itemBurnable.fuel -= 0.5f * (Furnace.cookingTemperature / 200f) * _fuelSpeedMultiplier;

                if (!itemBurnable.HasFlag(global::Item.Flag.OnFire))
                {
                    itemBurnable.SetFlag(global::Item.Flag.OnFire, true);
                    itemBurnable.MarkDirty();
                }

                if (itemBurnable.fuel <= 0f)
                {
                    ConsumeFuel(itemBurnable, burnable);
                }

                _ticks++;

                Interface.CallHook("OnOvenCooked", this, itemBurnable, slot);
            }

            private void ConsumeFuel(Item fuel, ItemModBurnable burnable)
            {
                if (Interface.CallHook("OnFuelConsume", _oven, fuel, burnable) != null)
                {
                    return;
                }

                if (Furnace.allowByproductCreation && burnable.byproductItem != null &&
                    Random.Range(0f, 1f) > burnable.byproductChance)
                {
                    var def = burnable.byproductItem;
                    var item = ItemManager.Create(def,
                        (int)(burnable.byproductAmount * OutputMultiplier(def.shortname))); // It's fuel multiplier

                    if (!item.MoveToContainer(Furnace.inventory))
                    {
                        item.Drop(Furnace.inventory.dropPosition, Furnace.inventory.dropVelocity);
                    }
                }

                if (fuel.amount <= _fuelUsageMultiplier)
                {
                    fuel.Remove();
                    return;
                }

                fuel.UseItem(_fuelUsageMultiplier);
                fuel.fuel = burnable.fuelAmount;
                fuel.MarkDirty();

                Interface.CallHook("OnFuelConsumed", _oven, fuel, burnable);
            }

            private void SmeltItems()
            {
                if (_ticks % _smeltingFrequency != 0)
                    return;

                for (var i = 0; i < Furnace.inventory.itemList.Count; i++)
                {
                    var item = Furnace.inventory.itemList[i];
                    if (item == null || !item.IsValid())
                        continue;

                    var cookable = item.info.GetComponent<ItemModCookable>();
                    if (cookable == null)
                        continue;

                    var isAllowed = IsAllowed(item.info.shortname);
                    if (isAllowed != null &&
                        !isAllowed.Value) // Allowed is false? Okay, no problem. Don't cook this item
                        continue;

                    var temperature = item.temperature;
                    if (!cookable.CanBeCookedByAtTemperature(temperature) && isAllowed == null)
                    {
                        if (!cookable.setCookingFlag || !item.HasFlag(global::Item.Flag.Cooking))
                            continue;

                        item.SetFlag(global::Item.Flag.Cooking, false);
                        item.MarkDirty();
                        continue;
                    }

                    if (cookable.cookTime > 0 && _ticks * 1f / _smeltingFrequency % cookable.cookTime > 0)
                        continue;

                    if (cookable.setCookingFlag && !item.HasFlag(global::Item.Flag.Cooking))
                    {
                        item.SetFlag(global::Item.Flag.Cooking, true);
                        item.MarkDirty();
                    }

                    // var targetPosition = item.position + 1;
                    var amountConsumed = (int)_oven.GetSmeltingSpeed();
                    amountConsumed = Math.Min(amountConsumed, item.amount);
                    if (item.amount > amountConsumed)
                    {
                        item.amount -= amountConsumed;
                        item.MarkDirty();
                    }
                    else
                    {
                        item.Remove();
                    }

                    if (cookable.becomeOnCooked == null)
                        continue;

                    var itemProduced = ItemManager.Create(cookable.becomeOnCooked,
                        (int)(cookable.amountOfBecome * amountConsumed *
                              OutputMultiplier(cookable.becomeOnCooked.shortname)));

                    if (itemProduced == null || itemProduced.MoveToContainer(item.parent))
                        continue;

                    itemProduced.Drop(item.parent.dropPosition, item.parent.dropVelocity);

                }
            }

            public void StartCooking()
            {
                PrintDebug("Starting cooking..");
                Furnace.inventory.temperature = Furnace.cookingTemperature;
                Furnace.UpdateAttachmentTemperature();

                PrintDebug($"Speed Multiplier: {_speedMultiplier}");
                Furnace.InvokeRepeating(Cook, _speedMultiplier, _speedMultiplier);
                Furnace.SetFlag(BaseEntity.Flags.On, true);
            }
        }

        #endregion
    }
}