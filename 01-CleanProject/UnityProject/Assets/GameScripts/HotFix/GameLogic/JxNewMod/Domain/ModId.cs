using System;

namespace JxNewMod.Domain
{
    public readonly struct ModId : IEquatable<ModId>, IComparable<ModId>
    {
        public static readonly ModId XinJianXia = new("xin-jian-xia");
        public static readonly ModId LengJianHanMei =
            new("leng-jian-han-mei");
        public static readonly ModId MengLiHuiMou =
            new("meng-li-hui-mou");

        private readonly string _value;

        public ModId(string value)
        {
            string candidate = value?.Trim();
            if (!IsValidValue(candidate))
                throw new ArgumentException(
                    "Mod id must contain only lowercase ASCII letters, digits, or single hyphens.",
                    nameof(value));

            _value = candidate;
        }

        public string Value => _value ?? string.Empty;
        public bool IsValid => IsValidValue(_value);

        public static bool TryParse(string value, out ModId modId)
        {
            string candidate = value?.Trim();
            if (!IsValidValue(candidate))
            {
                modId = default;
                return false;
            }

            modId = new ModId(candidate);
            return true;
        }

        public bool Equals(ModId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is ModId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value);

        public int CompareTo(ModId other) =>
            string.Compare(Value, other.Value, StringComparison.Ordinal);

        public override string ToString() => Value;

        public static bool operator ==(ModId left, ModId right) =>
            left.Equals(right);

        public static bool operator !=(ModId left, ModId right) =>
            !left.Equals(right);

        private static bool IsValidValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
                return false;
            if (value[0] == '-' || value[value.Length - 1] == '-')
                return false;

            bool previousWasHyphen = false;
            foreach (char character in value)
            {
                bool isLetter = character >= 'a' && character <= 'z';
                bool isDigit = character >= '0' && character <= '9';
                bool isHyphen = character == '-';
                if (!isLetter && !isDigit && !isHyphen)
                    return false;
                if (isHyphen && previousWasHyphen)
                    return false;
                previousWasHyphen = isHyphen;
            }

            return true;
        }
    }
}
