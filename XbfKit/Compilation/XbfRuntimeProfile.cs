namespace XbfKit.Compilation;

/// <summary>Contains the target-dependent schema limits and custom runtime-data versions used by one compilation.</summary>
internal sealed class XbfRuntimeProfile
{
    private static readonly Version WindowsBlue = new(6, 3, 9600, 0);
    private static readonly Version Windows10Threshold1 = new(10, 0, 10240, 0);
    private static readonly Version Windows10Threshold2 = new(10, 0, 10586, 0);
    private static readonly Version Windows10Redstone1 = new(10, 0, 14393, 0);
    private static readonly Version Windows10Redstone2 = new(10, 0, 15063, 0);
    private static readonly Version Windows10Redstone3 = new(10, 0, 16299, 0);
    private static readonly Version Windows10Redstone4 = new(10, 0, 17134, 0);
    private static readonly Version Windows10Redstone5 = new(10, 0, 17763, 0);
    private static readonly Version Windows10Version1903 = new(10, 0, 18362, 0);
    private static readonly (Version MinimumVersion, int TypeCount, int PropertyCount)[] StableSchemas =
    [
        (Windows10Threshold1, 798, 1749),
        (Windows10Threshold2, 807, 1769),
        (Windows10Redstone1, 840, 1879),
        (Windows10Redstone2, 882, 1972),
        (Windows10Redstone3, 901, 2013),
        (Windows10Redstone4, 941, 2135),
        (Windows10Redstone5, 970, 2357),
        (Windows10Version1903, 978, 2456),
    ];

    private XbfRuntimeProfile(
        XbfDialect dialect,
        Version? targetRuntimeVersion,
        int stableTypeCount,
        int stablePropertyCount,
        Xbf2CustomRuntimeDataType resourceDictionaryRuntimeDataType,
        Xbf2CustomRuntimeDataType styleRuntimeDataType,
        Xbf2CustomRuntimeDataType deferredElementRuntimeDataType,
        Xbf2CustomRuntimeDataType visualStateRuntimeDataType,
        bool supportsConditionalXaml)
    {
        Dialect = dialect;
        TargetRuntimeVersion = targetRuntimeVersion;
        StableTypeCount = stableTypeCount;
        StablePropertyCount = stablePropertyCount;
        ResourceDictionaryRuntimeDataType = resourceDictionaryRuntimeDataType;
        StyleRuntimeDataType = styleRuntimeDataType;
        DeferredElementRuntimeDataType = deferredElementRuntimeDataType;
        VisualStateRuntimeDataType = visualStateRuntimeDataType;
        SupportsConditionalXaml = supportsConditionalXaml;
    }

    /// <summary>Gets the selected XAML framework dialect.</summary>
    internal XbfDialect Dialect { get; }

    /// <summary>Gets the normalized WUX target runtime, or <see langword="null"/> for MUX.</summary>
    internal Version? TargetRuntimeVersion { get; }

    /// <summary>Gets the exclusive upper bound for stable WUX type identifiers.</summary>
    internal int StableTypeCount { get; }

    /// <summary>Gets the exclusive upper bound for stable WUX property identifiers.</summary>
    internal int StablePropertyCount { get; }

    /// <summary>Gets the target resource-dictionary custom runtime-data kind.</summary>
    internal Xbf2CustomRuntimeDataType ResourceDictionaryRuntimeDataType { get; }

    /// <summary>Gets the target style custom runtime-data kind.</summary>
    internal Xbf2CustomRuntimeDataType StyleRuntimeDataType { get; }

    /// <summary>Gets the target deferred-element custom runtime-data kind.</summary>
    internal Xbf2CustomRuntimeDataType DeferredElementRuntimeDataType { get; }

    /// <summary>Gets the target visual-state custom runtime-data kind.</summary>
    internal Xbf2CustomRuntimeDataType VisualStateRuntimeDataType { get; }

    /// <summary>Gets whether conditional XAML can be represented for this target.</summary>
    internal bool SupportsConditionalXaml { get; }

    /// <summary>Creates and validates a runtime profile from public compilation options.</summary>
    internal static XbfRuntimeProfile Create(XbfCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Version.IsSupported)
        {
            throw new NotSupportedException($"XBF version {options.Version} is not supported.");
        }

        bool validCombination = options.Version.Major switch
        {
            1 => options.Dialect == XbfDialect.WUX,
            2 => options.Dialect is XbfDialect.WUX or XbfDialect.MUX,
            _ => false,
        };
        if (!validCombination)
        {
            throw new ArgumentException($"XBF version {options.Version} cannot use the {options.Dialect} dialect.", nameof(options));
        }

        return options.Dialect switch
        {
            XbfDialect.WUX => CreateWux(options),
            XbfDialect.MUX => CreateMux(options),
            _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unknown XBF dialect {options.Dialect}."),
        };
    }

    /// <summary>Determines whether a WUX stable type identifier is available to this target.</summary>
    internal bool SupportsStableType(ushort id)
    {
        return Dialect != XbfDialect.WUX || id < StableTypeCount;
    }

    /// <summary>Determines whether a WUX stable property identifier is available to this target.</summary>
    internal bool SupportsStableProperty(ushort id)
    {
        return Dialect != XbfDialect.WUX || id < StablePropertyCount;
    }

    private static XbfRuntimeProfile CreateWux(XbfCompilationOptions options)
    {
        Version target = NormalizeVersion(options.TargetRuntimeVersion ?? (options.Version.Major == 1 ? WindowsBlue : Windows10Version1903));
        if (options.Version.Major == 1 && target >= Windows10Threshold1)
        {
            throw new ArgumentException($"WUX target {target} uses XBF2; XBF {options.Version} requires a target older than Windows 10 build 10240.", nameof(options));
        }

        if (options.Version.Major == 2 && target < Windows10Threshold1)
        {
            throw new ArgumentException($"WUX target {target} uses XBF1; XBF {options.Version} requires Windows 10 build 10240 or newer.", nameof(options));
        }

        (int stableTypeCount, int stablePropertyCount) = StableSchemaCounts(target);
        Xbf2CustomRuntimeDataType resourceDictionaryType = target < Windows10Redstone1
            ? Xbf2CustomRuntimeDataType.ResourceDictionaryV1
            : target < Windows10Redstone2
                ? Xbf2CustomRuntimeDataType.ResourceDictionaryV2
                : Xbf2CustomRuntimeDataType.ResourceDictionaryV3;
        Xbf2CustomRuntimeDataType styleType = target < Windows10Redstone1
            ? Xbf2CustomRuntimeDataType.StyleV1
            : Xbf2CustomRuntimeDataType.StyleV2;
        Xbf2CustomRuntimeDataType deferredType = target < Windows10Redstone2
            ? Xbf2CustomRuntimeDataType.DeferredElementV2
            : Xbf2CustomRuntimeDataType.DeferredElementV3;

        return new(
            XbfDialect.WUX,
            target,
            stableTypeCount,
            stablePropertyCount,
            resourceDictionaryType,
            styleType,
            deferredType,
            Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5,
            supportsConditionalXaml: target >= Windows10Redstone2);
    }

    private static XbfRuntimeProfile CreateMux(XbfCompilationOptions options)
    {
        if (options.TargetRuntimeVersion is not null)
        {
            throw new ArgumentException("TargetRuntimeVersion currently applies only to the WUX dialect.", nameof(options));
        }

        return new(
            XbfDialect.MUX,
            targetRuntimeVersion: null,
            stableTypeCount: ushort.MaxValue + 1,
            stablePropertyCount: ushort.MaxValue + 1,
            Xbf2CustomRuntimeDataType.ResourceDictionaryV3,
            Xbf2CustomRuntimeDataType.StyleV3,
            Xbf2CustomRuntimeDataType.DeferredElementV3,
            Xbf2CustomRuntimeDataType.VisualStateGroupCollectionV5,
            supportsConditionalXaml: true);
    }

    private static (int TypeCount, int PropertyCount) StableSchemaCounts(Version target)
    {
        var schema = StableSchemas.LastOrDefault(value => target >= value.MinimumVersion);
        return (schema.TypeCount, schema.PropertyCount);
    }

    private static Version NormalizeVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Major > ushort.MaxValue || version.Minor > ushort.MaxValue || version.Build is < 0 or > ushort.MaxValue || version.Revision is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A WUX target runtime version must contain four unsigned 16-bit components.");
        }

        return new Version(version.Major, version.Minor, version.Build, version.Revision);
    }
}
