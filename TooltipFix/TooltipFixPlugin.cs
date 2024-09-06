using BepInEx;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2.UI;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace TooltipFix
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(R2API.R2API.PluginGUID)]
    public class TooltipFixPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Gorakh";
        public const string PluginName = "TooltipFix";
        public const string PluginVersion = "1.0.0";

        internal static TooltipFixPlugin Instance { get; private set; }

        readonly List<IDetour> _hooks = [];

        void Awake()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            Log.Init(Logger);

            Instance = SingletonHelper.Assign(Instance, this);

            IL.RoR2.UI.TooltipController.SetTooltip += TooltipController_SetTooltip_FixEmptyCheck;

            MethodInfo tooltipIsAvailableGetter = AccessTools.DeclaredPropertyGetter(typeof(TooltipProvider), nameof(TooltipProvider.tooltipIsAvailable));
            if (tooltipIsAvailableGetter != null)
            {
                _hooks.Add(new Hook(tooltipIsAvailableGetter, TooltipProvider_get_tooltipIsAvailable_CheckEmpty));
            }
            else
            {
                Log.Error($"Failed to find getter method for {nameof(TooltipProvider.tooltipIsAvailable)}");
            }

            stopwatch.Stop();
            Log.Message_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
        }

        void OnDestroy()
        {
            IL.RoR2.UI.TooltipController.SetTooltip -= TooltipController_SetTooltip_FixEmptyCheck;

            foreach (IDetour detour in _hooks)
            {
                detour?.Dispose();
            }

            _hooks.Clear();

            Instance = SingletonHelper.Unassign(Instance, this);
        }

        static void TooltipController_SetTooltip_FixEmptyCheck(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            // Loop back to last instruction
            c.Index--;

            MethodInfo titleTextGetter = AccessTools.DeclaredPropertyGetter(typeof(TooltipProvider), nameof(TooltipProvider.titleText));
            MethodInfo bodyTextGetter = AccessTools.DeclaredPropertyGetter(typeof(TooltipProvider), nameof(TooltipProvider.bodyText));

            if (c.TryGotoPrev(x => x.MatchCallOrCallvirt(titleTextGetter) || x.MatchCallOrCallvirt(bodyTextGetter)))
            {
                ILLabel afterIfLabel = null;

                if (c.TryGotoNext(MoveType.After, x => x.MatchBrfalse(out afterIfLabel)))
                {
                    ILLabel skipTextCheckLabel = c.MarkLabel();

                    c.Index = 0;

                    if (c.TryGotoNext(x => x.MatchCallOrCallvirt(titleTextGetter) || x.MatchCallOrCallvirt(bodyTextGetter)))
                    {
                        if (c.TryGotoPrev(MoveType.After, x => x.MatchBrfalse(out ILLabel lbl) && lbl.Target == afterIfLabel.Target))
                        {
                            c.Emit(OpCodes.Br, skipTextCheckLabel);
                        }
                        else
                        {
                            Log.Error("Failed to find matching branch instruction");
                        }
                    }
                    else
                    {
                        Log.Error("Failed to find title of body text get (from front)");
                    }
                }
                else
                {
                    Log.Error("Failed to find branch instruction");
                }
            }
            else
            {
                Log.Error("Failed to find title of body text get (from back)");
            }
        }

        delegate bool orig_TooltipProvider_get_tooltipIsAvailable(TooltipProvider self);
        static bool TooltipProvider_get_tooltipIsAvailable_CheckEmpty(orig_TooltipProvider_get_tooltipIsAvailable orig, TooltipProvider self)
        {
            return orig(self) && (!string.IsNullOrEmpty(self.titleText) || !string.IsNullOrEmpty(self.bodyText));
        }
    }
}
