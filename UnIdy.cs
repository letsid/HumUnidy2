using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared;
using ExileCore2.Shared.Enums;
using InputHumanizer.Input;

namespace UnIdy
{
    public class UnIdy : BaseSettingsPlugin<Settings>
    {
        private IngameState _ingameState;
        private Vector2 _windowOffset;
        private SyncTask<bool> _currentOperation;

        public UnIdy()
        {
        }

        public override bool Initialise()
        {
            base.Initialise();
            Name = "UnIdy";

            _ingameState = GameController.Game.IngameState;
            var windowRectangle = GameController.Window.GetWindowRectangle();
            _windowOffset = new Vector2(windowRectangle.TopLeft.X, windowRectangle.TopLeft.Y);
            return true;
        }

        public override void Render()
        {
            try
            {
                base.Render();

                if (_currentOperation != null)
                {
                    TaskUtils.RunOrRestart(ref _currentOperation, () => null);
                    return;
                }

                var inventoryPanel = _ingameState?.IngameUi?.InventoryPanel;
                if (inventoryPanel != null && inventoryPanel.IsVisible &&
                    Input.IsKeyDown(Settings.HotKey.Value))
                {
                    LogMessage("Hotkey pressed", 5);
                    _currentOperation = Identify();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in Render: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async SyncTask<bool> Identify()
        {
            try
            {
                var inventoryPanel = _ingameState?.IngameUi?.InventoryPanel;
                if (inventoryPanel == null)
                {
                    LogError("Identify called but InventoryPanel is null.");
                    return false;
                }

                var playerInventory = inventoryPanel[InventoryIndex.PlayerInventory];
                var scrollOfWisdom = GetItemWithBaseName(
                    "Metadata/Items/Currency/CurrencyIdentification",
                    playerInventory.VisibleInventoryItems);
                
                if (scrollOfWisdom == null)
                {
                    LogError("Scroll of Wisdom not found.");
                    return false;
                }

                LogMessage(scrollOfWisdom.Text, 1);

                var normalInventoryItems = playerInventory.VisibleInventoryItems?.ToList();
                if (normalInventoryItems == null)
                {
                    LogError("Normal Inventory Items are null.");
                    return false;
                }

                if (Settings.IdentifyVisibleTabItems.Value && _ingameState.IngameUi.StashElement.IsVisible)
                {
                    var stashItems = _ingameState.IngameUi.StashElement.VisibleStash?.VisibleInventoryItems;
                    if (stashItems != null)
                    {
                        normalInventoryItems.AddRange(stashItems);
                    }
                }

                var itemsToIdentify = GetItemsToIdentify(normalInventoryItems);
                if (itemsToIdentify.Count == 0)
                {
                    return false;
                }

                var tryGetInputController = GameController.PluginBridge.GetMethod<Func<string, IInputController>>("InputHumanizer.TryGetInputController");
                if (tryGetInputController == null)
                {
                    LogError("InputHumanizer method not registered.");
                    return false;
                }

                // Process items in smaller chunks to prevent holding the input controller too long
                foreach (var chunk in itemsToIdentify.Chunk(5))
                {
                    var inputController = tryGetInputController(this.Name);
                    if (inputController == null)
                    {
                        LogError("Failed to get input controller for chunk.");
                        continue;
                    }

                    using (inputController)
                    {
                        var scrollCenter = scrollOfWisdom.GetClientRect().Center;
                        await inputController.Click(MouseButtons.Right, new Vector2(scrollCenter.X, scrollCenter.Y) + _windowOffset);
                        await inputController.KeyDown(Keys.LShiftKey);

                        foreach (var item in chunk)
                        {
                            if (Settings.Debug.Value)
                            {
                                Graphics.DrawFrame(item.GetClientRect(), System.Drawing.Color.AliceBlue, 2);
                            }

                            var itemCenter = item.GetClientRect().Center;
                            await inputController.Click(MouseButtons.Left, new Vector2(itemCenter.X, itemCenter.Y) + _windowOffset);
                        }

                        await inputController.KeyUp(Keys.LShiftKey);
                    }

                    // Brief delay between chunks to allow other plugins access
                    await Task.Delay(50);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error in Identify: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private List<NormalInventoryItem> GetItemsToIdentify(List<NormalInventoryItem> normalInventoryItems)
        {
            var listOfNormalInventoryItemsToIdentify = new List<NormalInventoryItem>();

            foreach (var normalInventoryItem in normalInventoryItems)
            {
                if (normalInventoryItem?.Item == null || !normalInventoryItem.Item.HasComponent<Mods>())
                    continue;

                var mods = normalInventoryItem.Item.GetComponent<Mods>();

                if (mods.Identified)
                    continue;

                switch (mods.ItemRarity)
                {
                    case ItemRarity.Unique when !Settings.IdentifyUniques.Value:
                    case ItemRarity.Rare when !Settings.IdentifyRares.Value:
                    case ItemRarity.Magic when !Settings.IdentifyMagicItems.Value:
                    case ItemRarity.Normal:
                        continue;
                }

                var sockets = normalInventoryItem.Item.GetComponent<Sockets>();
                if (!Settings.IdentifySixSockets.Value && sockets?.NumberOfSockets == 6)
                    continue;

                var itemIsMap = normalInventoryItem.Item.HasComponent<Map>();
                if (!Settings.IdentifyMaps.Value && itemIsMap)
                    continue;

                listOfNormalInventoryItemsToIdentify.Add(normalInventoryItem);
            }

            return listOfNormalInventoryItemsToIdentify;
        }

        private NormalInventoryItem GetItemWithBaseName(string path,
            IEnumerable<NormalInventoryItem> normalInventoryItems)
        {
            try
            {
                return normalInventoryItems?
                    .FirstOrDefault(normalInventoryItem =>
                        normalInventoryItem?.Item?.Path == path);
            }
            catch (Exception ex)
            {
                LogError($"Error in GetItemWithBaseName: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }
    }
}
