using System;
using System.Collections.Generic;
using System.Linq;

namespace JxNewMod.Domain
{
    public interface IModCatalog
    {
        IReadOnlyList<ModDescriptor> Mods { get; }
        bool TryGet(ModId id, out ModDescriptor descriptor);
    }

    public sealed class OfficialModCatalog : IModCatalog
    {
        private readonly IReadOnlyList<ModDescriptor> _mods;
        private readonly IReadOnlyDictionary<ModId, ModDescriptor> _byId;

        public OfficialModCatalog(IEnumerable<ModDescriptor> mods)
        {
            if (mods == null)
                throw new ArgumentNullException(nameof(mods));

            List<ModDescriptor> ordered = mods
                .Where(mod => mod != null)
                .OrderBy(mod => mod.SortOrder)
                .ThenBy(mod => mod.Id)
                .ToList();
            if (ordered.Count == 0)
                throw new ArgumentException(
                    "At least one official Mod is required.",
                    nameof(mods));

            EnsureUnique(ordered, mod => mod.Id.Value, "Mod id");
            EnsureUnique(ordered, mod => mod.PackageName, "Package name");
            EnsureUnique(ordered, mod => mod.SaveNamespace, "Save namespace");

            _mods = ordered.AsReadOnly();
            _byId = ordered.ToDictionary(mod => mod.Id);
        }

        public IReadOnlyList<ModDescriptor> Mods => _mods;

        public bool TryGet(ModId id, out ModDescriptor descriptor) =>
            _byId.TryGetValue(id, out descriptor);

        public static OfficialModCatalog CreateBuiltIn()
        {
            ModResourcePackage sharedOnDemand = SharedBase(
                "JxShared_XinJianXiaBase",
                ModPackageLoadPolicy.OnFirstUse);
            ModResourcePackage sharedRequired = SharedBase(
                "JxShared_XinJianXiaBase",
                ModPackageLoadPolicy.RequiredOnActivation);
            ModResourcePackage daoJianRequired = SharedBase(
                "JxShared_DaoJian543Base",
                ModPackageLoadPolicy.RequiredOnActivation);
            return new OfficialModCatalog(new[]
            {
                new ModDescriptor(
                    ModId.XinJianXia,
                    "新剑侠情缘（正式）",
                    "新剑侠情缘正式版",
                    "JxMod_XinJianXia",
                    "jxnewmod.xinjianxia.v1",
                    "xin-jian-xia-original",
                    new ModContentAddresses(
                        "jxqy/manifests/preload-manifest.json",
                        "jxqy/manifests/script-catalog.json",
                        "jxqy/ui/ui-catalog.json",
                        "jxqy/text/ini/save/player0.ini/content.txt",
                        "jxqy/text/script/common/newgame.txt/content.txt",
                        "map:map/map001_衡山.map"),
                    sortOrder: 10,
                    uiAnimationAliases: CreateXinJianXiaUiAliases(),
                    fallbackPackages: new[] { sharedOnDemand },
                    scriptDialectId: "xin-jian-xia-original"),
                new ModDescriptor(
                    ModId.LengJianHanMei,
                    "MG-冷剑寒梅 V1.0.3",
                    "冷剑寒梅独立 Mod",
                    "JxMod_LengJianHanMei",
                    "jxnewmod.lengjianhanmei.v1",
                    "dao-jian-5.4.3",
                    CreateJxqyContentAddresses(
                        "save/rpg0",
                        "ini/save"),
                    sortOrder: 20,
                    isEnabled: true,
                    uiAnimationAliases: CreateXinJianXiaUiAliases(),
                    fallbackPackages: new[]
                    {
                        sharedRequired,
                        daoJianRequired,
                    },
                    scriptDialectId: "dao-jian-5.4.3"),
                new ModDescriptor(
                    ModId.MengLiHuiMou,
                    "梦里回眸三张琳心传 1.011",
                    "梦里回眸三张琳心传独立 Mod",
                    "JxMod_MengLiHuiMou",
                    "jxnewmod.menglihuimou.v1",
                    "dao-jian-5.4.3",
                    CreateJxqyContentAddresses("ini/save"),
                    sortOrder: 30,
                    isEnabled: true,
                    uiAnimationAliases: CreateXinJianXiaUiAliases(),
                    fallbackPackages: new[]
                    {
                        sharedRequired,
                        daoJianRequired,
                    },
                    scriptDialectId: "dao-jian-5.4.3")
            });
        }

        private static ModContentAddresses CreateJxqyContentAddresses(
            string saveTemplateRelativeDirectory,
            string snapshotTemplateRelativeDirectory = null)
        {
            return new ModContentAddresses(
                "jxqy/manifests/preload-manifest.json",
                "jxqy/manifests/script-catalog.json",
                "jxqy/ui/ui-catalog.json",
                $"jxqy/text/{saveTemplateRelativeDirectory}/" +
                "player0.ini/content.txt",
                "jxqy/text/script/common/newgame.txt/content.txt",
                "map:map/map001_衡山.map",
                snapshotTemplateRelativeDirectory);
        }

        private static ModResourcePackage SharedBase(
            string packageName,
            ModPackageLoadPolicy loadPolicy)
        {
            return new ModResourcePackage(
                packageName,
                loadPolicy);
        }

        private static IReadOnlyList<ModUiAnimationAlias>
            CreateXinJianXiaUiAliases()
        {
            return new[]
            {
                Alias("bottom/window.asf", "bottom/window-bottom.asf"),
                Alias("column/panel9.asf", "column/window-column.asf"),
                Alias("dialog/panel.asf", "dialog/window-dialog.asf"),
                Alias("littlemap/panel.asf", "littlemap/window-littlemap.asf"),
                Alias("saveload/panel.asf", "saveload/window-saveload.asf"),
                Alias("timer/window.asf", "timer/window-timer.asf"),
                Alias("top/window.asf", "top/window-top.asf")
            };
        }

        private static ModUiAnimationAlias Alias(
            string requested,
            string actual)
        {
            return new ModUiAnimationAlias(requested, actual);
        }

        private static void EnsureUnique(
            IEnumerable<ModDescriptor> mods,
            Func<ModDescriptor, string> selector,
            string label)
        {
            string duplicate = mods
                .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicate != null)
                throw new ArgumentException(
                    $"{label} '{duplicate}' is duplicated.",
                    nameof(mods));
        }
    }
}
