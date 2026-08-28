using System;

namespace Jxqy.Editor.Animation
{
    public static class JxqyAnimationDecoder
    {
        public static JxqyDecodedAnimation Decode(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (JxqyAsfDecoder.HasSupportedSignature(data))
            {
                return JxqyAsfDecoder.Decode(data);
            }

            if (data.Length >= 12)
            {
                string signature = System.Text.Encoding.ASCII.GetString(data, 0, 12);
                if (string.Equals(signature, "MPC File Ver", StringComparison.Ordinal))
                    return JxqyMpcDecoder.Decode(data);
                if (string.Equals(signature, "SHD File Ver", StringComparison.Ordinal))
                    return JxqyMpcDecoder.DecodeShd(data);
            }

            throw new JxqyAnimationFormatException("Unknown animation file signature.", 0);
        }
    }
}
