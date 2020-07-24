using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AdvancedTooltip.Settings;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;

namespace AdvancedTooltip
{
    //it shows the suffix/refix tier directly near mod on hover item
    public class FastModsModule
    {
        private readonly Graphics _graphics;
        private readonly ItemModsSettings _modsSettings;
        private long _lastItemAddress;
        private Element _regularModsElement;
        private List<ModTierInfo> _mods = new List<ModTierInfo>();
        private Element _tooltip;

        public FastModsModule(Graphics graphics, ItemModsSettings modsSettings)
        {
            _graphics = graphics;
            _modsSettings = modsSettings;
        }

        public void DrawUiHoverFastMods(Element tooltip, Vector2 fixDrawPos)
        {
            try
            {
                InitializeElements(tooltip);

                if (_regularModsElement == null || !_regularModsElement.IsVisibleLocal)
                    return;

                var rect = _regularModsElement.GetClientRectCache;
                var drawPos = new Vector2(tooltip.GetClientRectCache.X - 3, rect.Top);
                var height = rect.Height / _mods.Count;

                if (fixDrawPos != Vector2.Zero)
                {
                    var offset = tooltip.GetClientRectCache.TopLeft - _regularModsElement.GetClientRectCache.TopLeft;
                    drawPos = fixDrawPos - offset;
                }

                for (var i = 0; i < _mods.Count; i++)
                {
                    var modTierInfo = _mods[i];
                    var boxHeight = height * modTierInfo.ModLines;

                    var textSize = _graphics.DrawText(modTierInfo.DisplayName,
                        drawPos.Translate(0, boxHeight / 2), modTierInfo.Color,
                        FontAlign.Right | FontAlign.VerticalCenter);

                    var rectangleF = new RectangleF(drawPos.X - textSize.X - 3, drawPos.Y, textSize.X + 6,
                        height * modTierInfo.ModLines);
                    _graphics.DrawBox(rectangleF, Color.Black);
                    _graphics.DrawFrame(rectangleF, Color.Gray, 1);

                    drawPos.Y += boxHeight;
                    i += modTierInfo.ModLines - 1;
                }
            }
            catch (Exception e)
            {
                //ignored   
            }
        }

        private void InitializeElements(Element tooltip)
        {
            _tooltip = tooltip;
            _regularModsElement = null;

            var modsRoot = tooltip.GetChildAtIndex(1);

            if (modsRoot == null)
                return;

            Element extendedModsElement = null;
            for (var i = modsRoot.Children.Count - 1; i >= 0; i--)
            {
                var element = modsRoot.Children[i];
                if (!string.IsNullOrEmpty(element.Text) && element.Text.StartsWith("<smaller"))
                {
                    extendedModsElement = element;
                    _regularModsElement = modsRoot.Children[i - 1];
                    break;
                }
            }

            if (_regularModsElement == null)
                return;
            if (_lastItemAddress != tooltip.Address)
            {
                _lastItemAddress = tooltip.Address;
                ParseItemHover(tooltip, extendedModsElement);
            }
        }

        private void ParseItemHover(Element tooltip, Element extendedModsElement)
        {
            _mods.Clear();
            var extendedModsStr =
                NativeStringReader.ReadString(extendedModsElement.Address + 0x2E8, tooltip.M, 5000);
            var extendedModsLines = extendedModsStr.Replace("\r\n", "\n").Split('\n');

            var regularModsStr =
                NativeStringReader.ReadString(_regularModsElement.Address + 0x2E8, tooltip.M, 5000);
            var regularModsLines = regularModsStr.Replace("\r\n", "\n").Split('\n');


            ModTierInfo currentModTierInfo = null;

            var modsDict = new Dictionary<string, ModTierInfo>();

            foreach (var extendedModsLine in extendedModsLines)
            {
                if (extendedModsLine.StartsWith("<italic>"))
                {
                    continue;
                }

                if (extendedModsLine.StartsWith("<smaller>") || extendedModsLine.StartsWith("<crafted>"))
                {
                    var isPrefix = extendedModsLine.Contains("Prefix");
                    var isSuffix = extendedModsLine.Contains("Suffix");

                    if (!isPrefix && !isSuffix)
                    {
                        DebugWindow.LogMsg($"Cannot extract Affix type from mod text: {extendedModsLine}", 4);
                        return;
                    }

                    var affix = isPrefix ? "P" : "S";
                    var color = isPrefix ? _modsSettings.PrefixColor : _modsSettings.SuffixColor;

                    var isRank = false;
                    const string TIER = "(Tier: ";
                    var tierPos = extendedModsLine.IndexOf(TIER);
                    if (tierPos != -1)
                    {
                        tierPos += TIER.Length;
                    }
                    else
                    {
                        const string RANK = "(Rank: ";
                        tierPos = extendedModsLine.IndexOf(RANK);

                        if (tierPos != -1)
                        {
                            tierPos += RANK.Length;
                            isRank = true;
                        }
                    }

                    if (tierPos != -1 && int.TryParse(extendedModsLine.Substring(tierPos, 1), out var tier))
                    {
                        if (isRank)
                            affix += $" Rank{tier}";
                        else
                            affix += tier;

                        if (tier == 1)
                            color = _modsSettings.T1Color.Value;
                        else if (tier == 2)
                            color = _modsSettings.T2Color.Value;
                        else if (tier == 3)
                            color = _modsSettings.T3Color.Value;
                    }


                    currentModTierInfo = new ModTierInfo(affix, color);
                    continue;
                }


                if (extendedModsLine.StartsWith("<") && !char.IsLetterOrDigit(extendedModsLine[0]))
                {
                    currentModTierInfo = null;
                    continue;
                }

                if (currentModTierInfo != null)
                {
                    var modLine = Regex.Replace(extendedModsLine, @"\([\d-.]+\)", string.Empty);
                    modLine = Regex.Replace(modLine, @"\s\([\d]+% Increased\)", string.Empty);

                    if (!modsDict.ContainsKey(modLine))
                    {
                        modsDict[modLine] = currentModTierInfo;
                    }
                }
            }

            var modTierInfos = new List<ModTierInfo>();
            foreach (var regularModsLine in regularModsLines)
            {
                var modFixed = regularModsLine;
                if (modFixed.StartsWith("+"))
                    modFixed = modFixed.Substring(1);

                var found = false;
                foreach (var keyValuePair in modsDict)
                {
                    if (regularModsLine.Contains(keyValuePair.Key))
                    {
                        found = true;
                        modTierInfos.Add(keyValuePair.Value);
                        break;
                    }
                }

                if (!found)
                {
                    DebugWindow.LogMsg($"Cannot extract mod from parsed mods: {modFixed}", 4);
                    var modTierInfo = new ModTierInfo("?", Color.Gray);
                    modTierInfos.Add(modTierInfo);
                    //return;
                }
            }

            if (modTierInfos.Count > 1)
            {
                for (var i = 1; i < modTierInfos.Count; i++)
                {
                    var info = modTierInfos[i];
                    var prevInfo = modTierInfos[i - 1];

                    if (info == prevInfo)
                    {
                        info.ModLines++;
                    }
                }
            }

            _mods = modTierInfos;
        }

        private class ModTierInfo
        {
            public ModTierInfo(string displayName, Color color)
            {
                DisplayName = displayName;
                Color = color;
            }

            public string DisplayName { get; }
            public Color Color { get; }

            /// <summary>
            /// Mean twinned mod
            /// </summary>
            public int ModLines { get; set; } = 1;
        }
    }
}