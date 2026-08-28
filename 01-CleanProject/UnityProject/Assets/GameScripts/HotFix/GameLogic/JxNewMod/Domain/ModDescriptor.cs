using System;
using System.Collections.Generic;
using System.Linq;

namespace JxNewMod.Domain
{
    public sealed class ModDescriptor
    {
        public ModDescriptor(
            ModId id,
            string displayName,
            string description,
            string packageName,
            string saveNamespace,
            string ruleProfileId,
            ModContentAddresses content,
            int sortOrder,
            bool isEnabled = true,
            IEnumerable<ModUiAnimationAlias> uiAnimationAliases = null,
            IEnumerable<ModResourcePackage> fallbackPackages = null,
            string scriptDialectId = null)
        {
            if (!id.IsValid)
                throw new ArgumentException("A valid Mod id is required.", nameof(id));
            Id = id;
            DisplayName = RequireText(displayName, nameof(displayName));
            Description = RequireText(description, nameof(description));
            PackageName = ModResourcePackage.RequirePackageName(packageName);
            SaveNamespace = RequireNamespace(saveNamespace);
            RuleProfileId = RequireText(ruleProfileId, nameof(ruleProfileId));
            ScriptDialectId = string.IsNullOrWhiteSpace(scriptDialectId)
                ? RuleProfileId
                : RequireText(scriptDialectId, nameof(scriptDialectId));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            SortOrder = sortOrder;
            IsEnabled = isEnabled;
            ModUiAnimationAlias[] aliases = (uiAnimationAliases ??
                                             Enumerable.Empty<
                                                 ModUiAnimationAlias>())
                .Where(alias => alias != null)
                .ToArray();
            string duplicateAlias = aliases
                .GroupBy(
                    alias => alias.RequestedRelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicateAlias != null)
                throw new ArgumentException(
                    $"UI animation alias '{duplicateAlias}' is duplicated.",
                    nameof(uiAnimationAliases));
            UiAnimationAliases = aliases;

            ModResourcePackage[] fallbacks = (fallbackPackages ??
                                              Enumerable.Empty<
                                                  ModResourcePackage>())
                .Where(package => package != null)
                .ToArray();
            string duplicatePackage = fallbacks
                .Select(package => package.PackageName)
                .Append(PackageName)
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicatePackage != null)
                throw new ArgumentException(
                    $"Resource package '{duplicatePackage}' is duplicated in " +
                    $"Mod '{id}'.",
                    nameof(fallbackPackages));
            ResourcePackages = new[]
                {
                    new ModResourcePackage(
                        PackageName,
                        ModPackageLoadPolicy.RequiredOnActivation),
                }
                .Concat(fallbacks)
                .ToArray();
        }

        public ModId Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string PackageName { get; }
        public string SaveNamespace { get; }
        public string RuleProfileId { get; }
        public string ScriptDialectId { get; }
        public ModContentAddresses Content { get; }
        public int SortOrder { get; }
        public bool IsEnabled { get; }
        public IReadOnlyList<ModUiAnimationAlias> UiAnimationAliases { get; }
        public IReadOnlyList<ModResourcePackage> ResourcePackages { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }

        private static string RequireNamespace(string value)
        {
            string saveNamespace = RequireText(value, nameof(value));
            foreach (char character in saveNamespace)
            {
                bool allowed = char.IsLetterOrDigit(character) ||
                               character == '.' ||
                               character == '-' ||
                               character == '_';
                if (!allowed)
                    throw new ArgumentException(
                        "Save namespace contains an invalid character.",
                        nameof(value));
            }

            return saveNamespace;
        }
    }
}
