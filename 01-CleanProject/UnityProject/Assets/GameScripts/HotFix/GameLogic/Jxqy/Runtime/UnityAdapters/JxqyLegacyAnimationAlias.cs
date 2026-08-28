using System;

namespace Jxqy.UnityAdapters
{
    public readonly struct JxqyLegacyAnimationAlias
    {
        public JxqyLegacyAnimationAlias(
            string category,
            string fileName,
            string metadataAddress)
        {
            Category = Require(category, nameof(category));
            FileName = Require(fileName, nameof(fileName));
            MetadataAddress = Require(
                metadataAddress,
                nameof(metadataAddress));
        }

        public string Category { get; }
        public string FileName { get; }
        public string MetadataAddress { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", parameterName);
            return value.Trim().Replace('\\', '/').ToLowerInvariant();
        }
    }
}
