using System;
using System.Collections.Generic;
using System.Linq;
using Jxqy.Domain.Presentation;
using Jxqy.Domain.Simulation;

namespace Jxqy.UnityAdapters
{
    /// <summary>
    /// A converted legacy text file addressed relative to a Mod's text root.
    /// </summary>
    public sealed class JxqyLegacyTextSource
    {
        public JxqyLegacyTextSource(
            string relativeDirectory,
            string fileName)
        {
            RelativeDirectory = RequireRelativePath(
                relativeDirectory,
                nameof(relativeDirectory));
            FileName = RequireFileName(fileName, nameof(fileName));
        }

        public string RelativeDirectory { get; }
        public string FileName { get; }

        private static string RequireRelativePath(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Relative directory is required.",
                    parameterName);
            string result = value.Trim().Replace('\\', '/').Trim('/');
            if (result.Length == 0 ||
                result.Split('/').Any(
                    segment => segment.Length == 0 ||
                               segment == "." ||
                               segment == ".."))
            {
                throw new ArgumentException(
                    "Relative directory contains an invalid segment.",
                    parameterName);
            }
            return result;
        }

        private static string RequireFileName(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "File name is required.",
                    parameterName);
            string result = value.Trim().Replace('\\', '/');
            if (result.IndexOf('/') >= 0 ||
                result == "." ||
                result == "..")
            {
                throw new ArgumentException(
                    "File name must not contain a directory.",
                    parameterName);
            }
            return result;
        }
    }

    /// <summary>
    /// Maps one semantic UI event to the original INI field that owns its
    /// sound. The runtime never needs to know a title-specific WAV name.
    /// </summary>
    public sealed class JxqyUiSoundSource
    {
        public JxqyUiSoundSource(
            JxqyUiSound sound,
            JxqyLegacyTextSource text,
            string section,
            string key)
        {
            Sound = sound;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Section = RequireText(section, nameof(section));
            Key = RequireText(key, nameof(key));
        }

        public JxqyUiSound Sound { get; }
        public JxqyLegacyTextSource Text { get; }
        public string Section { get; }
        public string Key { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }

    /// <summary>
    /// Title-specific legacy conventions supplied at the Mod adapter boundary.
    /// Shared runtime code consumes this profile and contains no XinJianXia
    /// resource names or package roots.
    /// </summary>
    public sealed class JxqyRuntimeContentProfile
    {
        public JxqyRuntimeContentProfile(
            JxqyLegacyTextSource talkIndex,
            JxqyLegacyTextSource newGameTraps,
            JxqyLegacyTextSource npcLevels,
            JxqyLegacyTextSource mapNames,
            JxqyLegacyTextSource portraits,
            string creditsScriptFileName,
            IReadOnlyDictionary<JxqyStatusKind, string>
                statusDeathAnimationFiles,
            JxqyDeathExperienceProfile deathExperience,
            JxqyDropContentProfile dropContent,
            IEnumerable<JxqyUiSoundSource> uiSoundSources,
            string materialRootRelativeAddress,
            string waterDisplacementRelativeAddress,
            string littleMapRootRelativeAddress,
            string littleMapFileExtension,
            string combatFloatTextPrefabRelativeAddress,
            string sceneCatalogRelativeAddress,
            string rainTextureRelativeAddress,
            IEnumerable<string> snowTextureRelativeAddresses,
            string whiteTextureRelativeAddress,
            bool combinedCharacterSheet)
        {
            TalkIndex = talkIndex ??
                        throw new ArgumentNullException(nameof(talkIndex));
            NewGameTraps = newGameTraps ??
                           throw new ArgumentNullException(nameof(newGameTraps));
            NpcLevels = npcLevels ??
                        throw new ArgumentNullException(nameof(npcLevels));
            MapNames = mapNames ??
                       throw new ArgumentNullException(nameof(mapNames));
            Portraits = portraits ??
                        throw new ArgumentNullException(nameof(portraits));
            CreditsScriptFileName = RequireFileName(
                creditsScriptFileName,
                nameof(creditsScriptFileName));
            StatusDeathAnimationFiles =
                new Dictionary<JxqyStatusKind, string>(
                    statusDeathAnimationFiles ??
                    throw new ArgumentNullException(
                        nameof(statusDeathAnimationFiles)));
            DeathExperience = deathExperience ??
                              throw new ArgumentNullException(
                                  nameof(deathExperience));
            DropContent = dropContent ??
                          throw new ArgumentNullException(nameof(dropContent));
            UiSoundSources = (uiSoundSources ??
                              throw new ArgumentNullException(
                                  nameof(uiSoundSources)))
                .ToArray();
            if (UiSoundSources
                .GroupBy(source => source.Sound)
                .Any(group => group.Count() > 1))
            {
                throw new ArgumentException(
                    "UI sound configuration contains a duplicate event.",
                    nameof(uiSoundSources));
            }
            MaterialRootRelativeAddress = RequireRelativeAddress(
                materialRootRelativeAddress,
                nameof(materialRootRelativeAddress));
            WaterDisplacementRelativeAddress = RequireRelativeAddress(
                waterDisplacementRelativeAddress,
                nameof(waterDisplacementRelativeAddress));
            LittleMapRootRelativeAddress = RequireRelativeAddress(
                littleMapRootRelativeAddress,
                nameof(littleMapRootRelativeAddress));
            LittleMapFileExtension = RequireFileExtension(
                littleMapFileExtension,
                nameof(littleMapFileExtension));
            CombatFloatTextPrefabRelativeAddress = RequireRelativeAddress(
                combatFloatTextPrefabRelativeAddress,
                nameof(combatFloatTextPrefabRelativeAddress));
            SceneCatalogRelativeAddress = RequireRelativeAddress(
                sceneCatalogRelativeAddress,
                nameof(sceneCatalogRelativeAddress));
            RainTextureRelativeAddress = RequireRelativeAddress(
                rainTextureRelativeAddress,
                nameof(rainTextureRelativeAddress));
            SnowTextureRelativeAddresses =
                (snowTextureRelativeAddresses ??
                 throw new ArgumentNullException(
                     nameof(snowTextureRelativeAddresses)))
                .Select((value, index) => RequireRelativeAddress(
                    value,
                    $"{nameof(snowTextureRelativeAddresses)}[{index}]"))
                .ToArray();
            WhiteTextureRelativeAddress = RequireRelativeAddress(
                whiteTextureRelativeAddress,
                nameof(whiteTextureRelativeAddress));
            CombinedCharacterSheet = combinedCharacterSheet;
        }

        public JxqyLegacyTextSource TalkIndex { get; }
        public JxqyLegacyTextSource NewGameTraps { get; }
        public JxqyLegacyTextSource NpcLevels { get; }
        public JxqyLegacyTextSource MapNames { get; }
        public JxqyLegacyTextSource Portraits { get; }
        public string CreditsScriptFileName { get; }
        public IReadOnlyDictionary<JxqyStatusKind, string>
            StatusDeathAnimationFiles { get; }
        public JxqyDeathExperienceProfile DeathExperience { get; }
        public JxqyDropContentProfile DropContent { get; }
        public IReadOnlyList<JxqyUiSoundSource> UiSoundSources { get; }
        public string MaterialRootRelativeAddress { get; }
        public string WaterDisplacementRelativeAddress { get; }
        public string LittleMapRootRelativeAddress { get; }
        public string LittleMapFileExtension { get; }
        public string CombatFloatTextPrefabRelativeAddress { get; }
        public string SceneCatalogRelativeAddress { get; }
        public string RainTextureRelativeAddress { get; }
        public IReadOnlyList<string> SnowTextureRelativeAddresses { get; }
        public string WhiteTextureRelativeAddress { get; }
        public bool CombinedCharacterSheet { get; }

        public static JxqyRuntimeContentProfile XinJianXia { get; } =
            CreateXinJianXia();

        private static JxqyRuntimeContentProfile CreateXinJianXia()
        {
            var commonSounds = new JxqyLegacyTextSource(
                "ini/ui/sound",
                "sound.ini");
            return new JxqyRuntimeContentProfile(
                new JxqyLegacyTextSource("content", "talkindex.txt"),
                new JxqyLegacyTextSource("ini/save", "traps.ini"),
                new JxqyLegacyTextSource("ini/level", "level-npc.ini"),
                new JxqyLegacyTextSource("ini/map", "mapname.ini"),
                new JxqyLegacyTextSource(
                    "ini/ui/dialog",
                    "headfile.ini"),
                "team.txt",
                new Dictionary<JxqyStatusKind, string>
                {
                    [JxqyStatusKind.Frozen] = "die-冰.asf",
                    [JxqyStatusKind.Poisoned] = "die-毒.asf",
                    [JxqyStatusKind.Petrified] = "die-石.asf",
                },
                JxqyDeathExperienceProfile.XinJianXia,
                JxqyDropContentProfile.XinJianXia,
                new[]
                {
                    new JxqyUiSoundSource(
                        JxqyUiSound.DragUp,
                        commonSounds,
                        "Init",
                        "DragUp"),
                    new JxqyUiSoundSource(
                        JxqyUiSound.DragDrop,
                        commonSounds,
                        "Init",
                        "DragDrop"),
                    new JxqyUiSoundSource(
                        JxqyUiSound.WindowOpen,
                        commonSounds,
                        "Init",
                        "WindowOpen"),
                    new JxqyUiSoundSource(
                        JxqyUiSound.WindowClose,
                        commonSounds,
                        "Init",
                        "WindowClose"),
                    new JxqyUiSoundSource(
                        JxqyUiSound.UseGoods,
                        commonSounds,
                        "Init",
                        "UseGoods"),
                    new JxqyUiSoundSource(
                        JxqyUiSound.BuyGoods,
                        commonSounds,
                        "Init",
                        "BuyGoods"),
                    IniSound(
                        JxqyUiSound.LargeButton,
                        "ini/ui/system",
                        "Option.ini"),
                    IniSound(
                        JxqyUiSound.Button,
                        "ini/ui/option",
                        "CBMusic.ini"),
                    IniSound(
                        JxqyUiSound.Browse,
                        "ini/ui/saveload",
                        "ListBox.ini"),
                    IniSound(
                        JxqyUiSound.MainMenu,
                        "ini/ui/title",
                        "InitBtn.ini"),
                    IniSound(
                        JxqyUiSound.GambleChoice,
                        "ini/ui/littlegame",
                        "GambleBig.ini"),
                },
                "materials",
                "shaderinputs/jxqycontent/effect/waterfall.jpg",
                "images/map/bmp",
                ".bmp",
                "ui/prefabs/jxqycombatfloattextview.prefab",
                "scenes/map-scene-catalog.json",
                "shared/weather/rain",
                new[]
                {
                    "shared/weather/snow-0",
                    "shared/weather/snow-1",
                    "shared/weather/snow-2",
                    "shared/weather/snow-3",
                },
                "shared/builtin/white",
                combinedCharacterSheet: true);
        }

        private static JxqyUiSoundSource IniSound(
            JxqyUiSound sound,
            string relativeDirectory,
            string fileName) =>
            new(
                sound,
                new JxqyLegacyTextSource(relativeDirectory, fileName),
                "Init",
                "Sound");

        private static string RequireFileName(
            string value,
            string parameterName)
        {
            string result = RequireRelativeAddress(value, parameterName);
            if (result.IndexOf('/') >= 0)
                throw new ArgumentException(
                    "File name must not contain a directory.",
                    parameterName);
            return result;
        }

        private static string RequireFileExtension(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "File extension is required.",
                    parameterName);
            string result = value.Trim().ToLowerInvariant();
            if (result.Length < 2 ||
                result[0] != '.' ||
                result.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
                result.IndexOf('.', 1) >= 0)
            {
                throw new ArgumentException(
                    "File extension must be a single extension.",
                    parameterName);
            }
            return result;
        }

        private static string RequireRelativeAddress(
            string value,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "Relative content address is required.",
                    parameterName);
            string result = value.Trim().Replace('\\', '/').Trim('/');
            if (result.Length == 0 ||
                result.Split('/').Any(
                    segment => segment.Length == 0 ||
                               segment == "." ||
                               segment == ".."))
            {
                throw new ArgumentException(
                    "Relative content address contains an invalid segment.",
                    parameterName);
            }
            return result.ToLowerInvariant();
        }
    }
}
