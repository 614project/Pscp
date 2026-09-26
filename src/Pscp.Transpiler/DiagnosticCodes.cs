namespace Pscp.Transpiler;

// Stable diagnostic codes (language server guide appendix B). The severity is a property of each code.
public static class DiagnosticCodes
{
    // Lexical and syntax.
    public const string Lexical = "PSCP1000";
    public const string Syntax = "PSCP1001";
    public const string TrailingAssignment = "PSCP1101";
    public const string UnsupportedStatement = "PSCP1102";
    public const string IfExpressionWithoutElse = "PSCP1103";
    public const string MixedPipeDirections = "PSCP1104";
    public const string RemovedSectionLabel = "PSCP1105";

    // Control flow, names, declarations.
    public const string JumpOutsideLoop = "PSCP2008";
    public const string ReservedIdentifier = "PSCP2101";
    public const string VariableFunctionConflict = "PSCP2102";
    public const string UndefinedName = "PSCP2103";
    public const string UninitializedImmutable = "PSCP2104";
    public const string MixedDestructuring = "PSCP2105";
    public const string NotTypeNorCallable = "PSCP2106";
    public const string ReadBeforeInitialization = "PSCP2107";
    public const string ImmutableMutation = "PSCP2108";
    public const string TypeReferencesProgramScope = "PSCP2109";

    // Functions, returns, calls, operators.
    public const string MissingRec = "PSCP2201";
    public const string MissingReturnValue = "PSCP2202";
    public const string NotCallable = "PSCP2203";
    public const string SubtractFromFunction = "PSCP2204";
    public const string UnusedUnaryStatement = "PSCP2207";
    public const string UnnecessaryRec = "PSCP2208";
    public const string AssignmentInExpression = "PSCP2209";
    public const string NotOnComparisonLeft = "PSCP2210";

    // Ranges, indexing, collection expressions, array creation.
    public const string ZeroRangeStep = "PSCP2301";
    public const string MixedCharRange = "PSCP2302";
    public const string AmbiguousSliceBound = "PSCP2303";
    public const string SteppedSlice = "PSCP2304";
    public const string UnsupportedSliceTarget = "PSCP2305";
    public const string EmptyCollectionWithoutType = "PSCP2306";
    public const string NoCommonElementType = "PSCP2307";
    public const string ExclusiveRangeInCollection = "PSCP2308";
    public const string NewArrayWithoutTarget = "PSCP2309";
    public const string AutoConstructWithoutConstructor = "PSCP2310";

    // Input and output.
    public const string UntypedInput = "PSCP2401";
    public const string NotTokenReadable = "PSCP2402";
    public const string DeepCollectionRendering = "PSCP2403";
    public const string UnknownIoMember = "PSCP2404";

    // Intrinsics, ordering, data-structure rewrites.
    public const string ScalarMinMax = "PSCP2501";
    public const string NoDefaultOrder = "PSCP2502";
    public const string RewriteOperandMismatch = "PSCP2503";
    public const string PostfixOnKnownCollection = "PSCP2504";
    public const string DiscardedHelperResult = "PSCP2505";
    public const string CultureOrderedStrings = "PSCP2506";

    // Types and members, deprecation, transition.
    public const string SurfaceTypeError = "PSCP2600";
    public const string UnassignedImmutableField = "PSCP2601";
    public const string Deprecated = "PSCP2900";
    public const string GenericWarning = "PSCP3001";
    public const string GenericError = "PSCP3002";

    // Editor-only information.
    public const string ShadowsIntrinsic = "PSCP5001";
    public const string LowersToConst = "PSCP5101";
    public const string FusedGenerator = "PSCP5102";

    public static bool IsDeprecation(string code) => code == Deprecated;

    public static bool IsUnnecessary(string code) => code is UnusedUnaryStatement or UnnecessaryRec;
}
