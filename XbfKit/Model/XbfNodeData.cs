namespace XbfKit;

/// <summary>Provides the base type for version-specific XBF node payloads.</summary>
public abstract class XbfNodeData;

/// <summary>Contains the linear node stream used by XBF1.</summary>
public sealed class Xbf1NodeData : XbfNodeData
{
    /// <summary>Gets the XBF1 nodes in persisted order.</summary>
    public List<Xbf1Node> Nodes { get; } = [];
}

/// <summary>Contains the independently addressable substreams used by XBF2.</summary>
public sealed class Xbf2NodeData : XbfNodeData
{
    /// <summary>Gets the XBF2 substreams in persisted order.</summary>
    public List<Xbf2Substream> Substreams { get; } = [];
}
