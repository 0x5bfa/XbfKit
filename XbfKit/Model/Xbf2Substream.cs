using XbfKit.IO;

namespace XbfKit;

/// <summary>Stores one XBF2 object-writer instruction stream and its line-information stream.</summary>
public sealed class Xbf2Substream
{
    /// <summary>Gets or sets the encoded object-writer instructions.</summary>
    public byte[] NodeBytes { get; set; } = [];

    /// <summary>Gets or sets the encoded line records.</summary>
    public byte[] LineBytes { get; set; } = [];

    /// <summary>Decodes this substream into editable instructions and line records.</summary>
    /// <returns>The decoded substream.</returns>
    public Xbf2DecodedSubstream Decode()
    {
        return Xbf2InstructionDecoder.Decode(this);
    }

    /// <summary>Encodes editable instructions and line records into an XBF2 substream.</summary>
    /// <param name="decoded">The decoded substream to encode.</param>
    /// <returns>The encoded substream.</returns>
    public static Xbf2Substream Encode(Xbf2DecodedSubstream decoded)
    {
        return Xbf2InstructionEncoder.Encode(decoded);
    }
}
