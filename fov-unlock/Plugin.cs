using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GoldDigger;
using HarmonyLib;

namespace FovUnlock
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "espi.gmsde.fovunlock";
        public const string Name = "FOV Unlock";
        public const string Version = "1.0.0";

        // game: fov = 60 + 5 * optionIndex (QualitySettingsLoader.SetFOV)
        internal const int BaseFov = 60;
        internal const int Step = 5;

        internal static ConfigEntry<int> MaxFov;
        internal static ConfigEntry<float> FovOverride;
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            MaxFov = Config.Bind("General", "MaxFov", 120,
                new ConfigDescription("Highest FOV offered in the graphics menu (5 degree steps). Vanilla is 90.",
                    new AcceptableValueRange<int>(90, 150)));

            FovOverride = Config.Bind("General", "FovOverride", 0f,
                new ConfigDescription("Force this exact FOV, ignoring the menu. 0 = off.",
                    new AcceptableValueRange<float>(0f, 170f)));

            FovOverride.SettingChanged += (_, __) => Reapply();

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"Loaded. MaxFov={MaxFov.Value} FovOverride={FovOverride.Value}");
        }

        private static void Reapply()
        {
            var gs = DEGraphicSettings.instance;
            if (gs != null && gs.playerCamera != null)
                gs.UpdateFOV();
        }
    }

    [HarmonyPatch(typeof(MainMenuGraphicsSimple), nameof(MainMenuGraphicsSimple.Awake))]
    internal static class ExtendFovOptions
    {
        private static readonly Regex Num = new Regex(@"\d+");

        private static void Postfix(MainMenuGraphicsSimple __instance)
        {
            var bar = __instance.FOV;
            if (bar == null || bar.Options == null || bar.Options.Count == 0)
                return;

            // reuse vanilla label format (e.g. "90" or "90°") when it's a plain number
            string fmt = "{0}";
            if (!bar.Localized && Num.IsMatch(bar.Options[0]))
                fmt = Num.Replace(bar.Options[0].Replace("{", "{{").Replace("}", "}}"), "{0}", 1);

            int max = Plugin.MaxFov.Value;
            var opts = new List<string>();
            for (int fov = Plugin.BaseFov; fov <= max; fov += Plugin.Step)
                opts.Add(string.Format(fmt, fov));

            bar.Options = opts;
            bar.Localized = false;
            Plugin.Log.LogDebug($"FOV options: {opts[0]}..{opts[opts.Count - 1]}");
        }
    }

    [HarmonyPatch(typeof(CameraSettingsGetter), nameof(CameraSettingsGetter.SetFov))]
    internal static class OverrideFov
    {
        private static void Prefix(ref float fov)
        {
            float o = Plugin.FovOverride.Value;
            if (o > 0f)
                fov = o;
        }
    }
}
