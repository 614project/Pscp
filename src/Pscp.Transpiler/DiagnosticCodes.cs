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

    public const string SpecUrl = "https://github.com/614project/Pscp/blob/main/docs/pscp_v_0_7_spec.md";

    // The spec section of each code (guide appendix B) and its heading anchor, for `codeDescription.href`.
    private static readonly IReadOnlyDictionary<string, (string Section, string Anchor)> SpecSections = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        [Lexical] = ("§3", "3-어휘-구조"),
        [Syntax] = ("부록 A", "부록-a-문법-요약"),
        [TrailingAssignment] = ("§4.3", "43-줄-이어짐이-아닌-것"),
        [UnsupportedStatement] = ("§6.3", "63-지원하지-않는-c-문장"),
        [IfExpressionWithoutElse] = ("§12.1", "121-if"),
        [MixedPipeDirections] = ("§13.3", "133-pipe"),
        [JumpOutsideLoop] = ("§12.5", "125-break-continue-return"),
        [ReservedIdentifier] = ("§3.2", "32-식별자"),
        [VariableFunctionConflict] = ("§5.1", "51-비한정-이름의-우선순위"),
        [UndefinedName] = ("§5.1", "51-비한정-이름의-우선순위"),
        [UninitializedImmutable] = ("§9.3", "93-초기화-없는-선언"),
        [MixedDestructuring] = ("§9.5", "95-구조-분해"),
        [NotTypeNorCallable] = ("§11.5", "115-선언인가-호출인가"),
        [ReadBeforeInitialization] = ("§7.3", "73-프로그램-scope"),
        [ImmutableMutation] = ("§9.2", "92-불변성의-의미와-진단"),
        [TypeReferencesProgramScope] = ("§7.4", "74-타입-선언에서의-가시성"),
        [MissingRec] = ("§10.3", "103-rec"),
        [MissingReturnValue] = ("§10.5", "105-꼬리-위치와-암묵적-반환"),
        [NotCallable] = ("§11.2", "112-space-call"),
        [SubtractFromFunction] = ("§11.3", "113-부호와-space-call"),
        [UnusedUnaryStatement] = ("§4.3", "43-줄-이어짐이-아닌-것"),
        [UnnecessaryRec] = ("§10.3", "103-rec"),
        [AssignmentInExpression] = ("§13.4", "134-와-"),
        [NotOnComparisonLeft] = ("§13.7", "137-not과-"),
        [ZeroRangeStep] = ("§14.3", "143-의미"),
        [MixedCharRange] = ("§14.2", "142-원소-타입"),
        [AmbiguousSliceBound] = ("§15.2", "152-슬라이스"),
        [SteppedSlice] = ("§15.2", "152-슬라이스"),
        [UnsupportedSliceTarget] = ("§15.3", "153-슬라이스-대상과-lowering"),
        [EmptyCollectionWithoutType] = ("§16.1", "161-materialized-collection"),
        [NoCommonElementType] = ("§16.1", "161-materialized-collection"),
        [ExclusiveRangeInCollection] = ("§16.1", "161-materialized-collection"),
        [NewArrayWithoutTarget] = ("§27.1", "271-newn-newnm"),
        [AutoConstructWithoutConstructor] = ("§27.2", "272-newn"),
        [UntypedInput] = ("§17.1", "171-선언-기반-입력-shorthand"),
        [NotTokenReadable] = ("§17.6", "176-token-readable-타입"),
        [DeepCollectionRendering] = ("§18.3", "183-렌더링-규칙"),
        [UnknownIoMember] = ("§17.2", "172-stdin-api"),
        [ScalarMinMax] = ("§22.2", "222-min-max"),
        [NoDefaultOrder] = ("§25.1", "251-기본-순서"),
        [RewriteOperandMismatch] = ("§26.2", "262-표"),
        [PostfixOnKnownCollection] = ("§26.2", "262-표"),
        [DiscardedHelperResult] = ("§24.4", "244-결과를-버리는-호출"),
        [CultureOrderedStrings] = ("§25.4", "254-net-정렬-컬렉션과-문자열"),
        [SurfaceTypeError] = ("§34", "34-적합성"),
        [UnassignedImmutableField] = ("§28.2", "282-멤버"),
        [Deprecated] = ("부록 C", "부록-c-폐기-예정-목록"),
    };

    public static string? GetSpecSection(string code)
        => SpecSections.TryGetValue(code, out (string Section, string Anchor) entry) ? entry.Section : null;

    public static string? GetSpecLink(string code)
        => SpecSections.TryGetValue(code, out (string Section, string Anchor) entry) ? $"{SpecUrl}#{Uri.EscapeDataString(entry.Anchor)}" : null;
}
