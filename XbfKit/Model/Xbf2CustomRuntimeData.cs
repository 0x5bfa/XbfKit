namespace XbfKit;

/// <summary>Identifies a versioned XBF2 custom runtime-data payload.</summary>
public enum Xbf2CustomRuntimeDataType : uint
{
    /// <summary>Indicates an unknown or unsupported payload kind.</summary>
    Unknown = 0,

    /// <summary>Identifies visual-state-group collection payload version 2.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    VisualStateGroupCollectionV2 = 1,

    /// <summary>Identifies style payload version 1.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    StyleV1 = 2,

    /// <summary>Identifies visual-state-group collection payload version 3.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    VisualStateGroupCollectionV3 = 3,

    /// <summary>Identifies visual-state-group collection payload version 4.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    VisualStateGroupCollectionV4 = 4,

    /// <summary>Identifies visual-state-group collection payload version 5.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    VisualStateGroupCollectionV5 = 5,

    /// <summary>Identifies deferred-element payload version 2.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    DeferredElementV2 = 6,

    /// <summary>Identifies resource-dictionary payload version 2.</summary>
    /// <remarks>Added in Windows 10 Redstone 1.</remarks>
    ResourceDictionaryV2 = 7,

    /// <summary>Identifies style payload version 2.</summary>
    /// <remarks>Added in Windows 10 Redstone 1.</remarks>
    StyleV2 = 8,

    /// <summary>Identifies deferred-element payload version 3.</summary>
    /// <remarks>Added in Windows 10 Redstone 2.</remarks>
    DeferredElementV3 = 9,

    /// <summary>Identifies resource-dictionary payload version 3.</summary>
    /// <remarks>Added in Windows 10 Redstone 2.</remarks>
    ResourceDictionaryV3 = 10,

    /// <summary>Identifies style payload version 3.</summary>
    /// <remarks>Added in Windows App SDK 1.7.</remarks>
    StyleV3 = 11,

    /// <summary>Identifies resource-dictionary payload version 4.</summary>
    /// <remarks>Added in Windows App SDK 2.x.</remarks>
    ResourceDictionaryV4 = 12,

    /// <summary>Identifies legacy resource-dictionary payload version 1.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    ResourceDictionaryV1 = 371,

    /// <summary>Identifies legacy visual-state-group collection payload version 1.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    VisualStateGroupCollectionV1 = 420,

    /// <summary>Identifies legacy deferred-element payload version 1.</summary>
    /// <remarks>Added in Windows 10 Threshold 1.</remarks>
    DeferredElementV1 = 746,
}

/// <summary>Provides the base type for decoded XBF2 custom runtime data.</summary>
public abstract class Xbf2CustomRuntimeData
{
    /// <summary>Gets the serialized payload kind.</summary>
    public required Xbf2CustomRuntimeDataType Type { get; init; }
}

/// <summary>Contains runtime data for an element whose realization is deferred.</summary>
public sealed class Xbf2DeferredRuntimeData : Xbf2CustomRuntimeData
{
    /// <summary>Gets the optional x:Name string reference.</summary>
    public Xbf2Reference Name { get; init; }

    /// <summary>Gets properties that must be assigned before deferred realization.</summary>
    public List<(Xbf2Reference Property, Xbf2Constant Value)> NonDeferredProperties { get; } = [];

    /// <summary>Gets whether the deferred element is realized immediately.</summary>
    public bool Realize { get; init; }
}

/// <summary>Identifies which payload fields are present in a serialized style setter.</summary>
[Flags]
public enum Xbf2StyleSetterFlags : uint
{
    /// <summary>Indicates that no setter flags are set.</summary>
    None = 0,

    /// <summary>Indicates that the value is a theme-resource reference.</summary>
    HasThemeResourceValue = 0x01,

    /// <summary>Indicates that the value is a static-resource reference.</summary>
    HasStaticResourceValue = 0x02,

    /// <summary>Indicates that a string value is present.</summary>
    HasStringValue = 0x04,

    /// <summary>Indicates that a deferred object value is present.</summary>
    HasObjectValue = 0x08,

    /// <summary>Indicates that the property is stored as a resolved reference.</summary>
    IsPropertyResolved = 0x10,

    /// <summary>Indicates that an inline constant container is present.</summary>
    HasContainerValue = 0x20,

    /// <summary>Indicates that the setter has a token for its own deferred object.</summary>
    HasTokenForSelf = 0x40,

    /// <summary>Indicates that the setter can be changed at runtime.</summary>
    IsMutable = 0x80,
}

/// <summary>Contains one setter decoded from style custom runtime data.</summary>
public sealed class Xbf2StyleSetter
{
    /// <summary>Gets the fields and value representation present in this setter.</summary>
    public Xbf2StyleSetterFlags Flags { get; init; }

    /// <summary>Gets the resolved property reference.</summary>
    public Xbf2Reference? Property { get; init; }

    /// <summary>Gets the unresolved property-name string reference.</summary>
    public Xbf2Reference? PropertyName { get; init; }

    /// <summary>Gets the declaring type used with an unresolved property name.</summary>
    public Xbf2Reference? DeclaringType { get; init; }

    /// <summary>Gets the string value reference.</summary>
    public Xbf2Reference? StringValue { get; init; }

    /// <summary>Gets the inline constant value.</summary>
    public Xbf2Constant? ConstantValue { get; init; }

    /// <summary>Gets the deferred object token.</summary>
    public uint? Token { get; init; }
}

/// <summary>Associates a deferred object token with its conditional predicates.</summary>
/// <param name="Token">The deferred object token.</param>
/// <param name="Predicates">The predicates that guard the object.</param>
public readonly record struct Xbf2ConditionalObject(uint Token, IReadOnlyList<Xbf2Predicate> Predicates);

/// <summary>Contains compactly encoded style setters and conditional objects.</summary>
public sealed class Xbf2StyleRuntimeData : Xbf2CustomRuntimeData
{
    /// <summary>Gets the compact setter records.</summary>
    public List<Xbf2StyleSetter> Setters { get; } = [];

    /// <summary>Gets conditionally declared deferred objects.</summary>
    public List<Xbf2ConditionalObject> ConditionalObjects { get; } = [];
}

/// <summary>Contains a resource key reference and its optional packed key metadata.</summary>
/// <param name="String">The key string or type reference.</param>
/// <param name="HashAndIsKeyType">The optional packed key hash and type-key marker.</param>
public readonly record struct Xbf2ResourceKey(Xbf2Reference String, ulong? HashAndIsKeyType = null);

/// <summary>Associates one resource key with deferred object tokens.</summary>
/// <param name="Key">The compact resource key.</param>
/// <param name="Tokens">The deferred object tokens associated with the key.</param>
public readonly record struct Xbf2ResourceEntry(Xbf2ResourceKey Key, IReadOnlyList<uint> Tokens);

/// <summary>Contains the compact resource indexes stored for a resource dictionary.</summary>
public sealed class Xbf2ResourceDictionaryRuntimeData : Xbf2CustomRuntimeData
{
    /// <summary>
    /// Gets resources declared with an explicit string key.
    /// </summary>
    public List<Xbf2ResourceEntry> Resources { get; } = [];

    /// <summary>
    /// Gets resources whose key is inferred from the resource type, such as an implicit Style.
    /// </summary>
    public List<Xbf2ResourceEntry> ImplicitResources { get; } = [];

    /// <summary>Gets resource-name string references.</summary>
    public List<Xbf2Reference> ResourcesWithNames { get; } = [];

    /// <summary>Gets explicitly keyed conditional resources.</summary>
    public List<Xbf2ResourceEntry> ConditionalResources { get; } = [];

    /// <summary>
    /// Gets conditionally declared resources whose key is inferred from the resource type.
    /// </summary>
    public List<Xbf2ResourceEntry> ConditionalImplicitResources { get; } = [];

    /// <summary>Gets legacy implicit data-template keys.</summary>
    public List<Xbf2Reference> LegacyImplicitDataTemplateKeys { get; } = [];

    /// <summary>Gets legacy implicit style keys.</summary>
    public List<Xbf2Reference> LegacyImplicitStyleKeys { get; } = [];

    /// <summary>Gets conditionally declared deferred objects.</summary>
    public List<Xbf2ConditionalObject> ConditionalObjects { get; } = [];
}

/// <summary>Contains compact visual states, groups, transitions, and deferred tokens.</summary>
public sealed class Xbf2VisualStateRuntimeData : Xbf2CustomRuntimeData
{
    /// <summary>Gets the visual-state index to group-index mapping.</summary>
    public List<uint> StateToGroupMap { get; } = [];

    /// <summary>Gets compact visual-state records.</summary>
    public List<Xbf2VisualState> States { get; } = [];

    /// <summary>Gets compact visual-state group records.</summary>
    public List<Xbf2VisualStateGroup> Groups { get; } = [];

    /// <summary>Gets compact visual-transition records.</summary>
    public List<Xbf2VisualTransition> Transitions { get; } = [];

    /// <summary>Gets whether unrecognized token data was encountered.</summary>
    public bool UnexpectedTokens { get; init; }

    /// <summary>Gets from/to-state transition lookup entries.</summary>
    public List<Xbf2TransitionLookupEntry> TransitionLookup { get; } = [];

    /// <summary>Gets the default transition index for each group.</summary>
    public List<uint> DefaultTransitionsByGroup { get; } = [];

    /// <summary>Gets the deferred token for the complete state collection.</summary>
    public uint EntireCollectionToken { get; init; }

    /// <summary>Gets x:Name string references observed while encoding states.</summary>
    public List<Xbf2Reference> SeenNameDirectives { get; } = [];
}

/// <summary>Contains one visual state's deferred storyboard, setters, and trigger tokens.</summary>
public sealed class Xbf2VisualState
{
    /// <summary>Gets the state-name string reference.</summary>
    public Xbf2Reference Name { get; init; }

    /// <summary>Gets the deferred storyboard token.</summary>
    public uint StoryboardToken { get; init; }

    /// <summary>Gets whether the state has a storyboard.</summary>
    public bool HasStoryboard { get; init; }

    /// <summary>Gets deferred setter tokens.</summary>
    public List<uint> DeferredSetterTokens { get; } = [];

    /// <summary>Gets serialized values for each state trigger.</summary>
    public List<List<uint>> StateTriggerValues { get; } = [];

    /// <summary>Gets deferred tokens for extensible triggers.</summary>
    public List<uint> ExtensibleTriggerTokens { get; } = [];

    /// <summary>Gets deferred tokens for trigger collections.</summary>
    public List<uint> TriggerCollectionTokens { get; } = [];

    /// <summary>Gets static-resource tokens referenced by triggers.</summary>
    public List<uint> StaticResourceTriggerTokens { get; } = [];
}

/// <summary>Contains one compact visual-state group entry.</summary>
/// <param name="Name">The group-name string reference.</param>
/// <param name="HasDynamicTimelines">Whether the group contains dynamic timelines.</param>
/// <param name="DeferredSelfToken">The deferred token for the group object.</param>
public readonly record struct Xbf2VisualStateGroup(Xbf2Reference Name, bool HasDynamicTimelines, uint DeferredSelfToken);

/// <summary>Contains one compact visual transition entry.</summary>
/// <param name="ToState">The destination state-name reference.</param>
/// <param name="FromState">The source state-name reference.</param>
/// <param name="DeferredSelfToken">The deferred token for the transition object.</param>
public readonly record struct Xbf2VisualTransition(Xbf2Reference ToState, Xbf2Reference FromState, uint DeferredSelfToken);

/// <summary>Maps a from/to state pair to a transition entry.</summary>
/// <param name="FromIndex">The source state index.</param>
/// <param name="ToIndex">The destination state index.</param>
/// <param name="TransitionIndex">The matching transition index.</param>
public readonly record struct Xbf2TransitionLookupEntry(uint FromIndex, uint ToIndex, uint TransitionIndex);
