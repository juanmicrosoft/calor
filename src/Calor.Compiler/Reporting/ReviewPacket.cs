namespace Calor.Compiler.Reporting;

/// <summary>
/// Review packet v1 (wedge plan v0.11 D-W3.3, strategy §4 2a item 3): a
/// per-change report over the seven-status vocabulary that LEADS with the
/// unproven remainder. Scope honesty: this is deliberately less than the
/// strategy's full packet — index-backed blast radius and invalidated-proof
/// sets ride the persisted semantic index, which is deferred (wedge plan §9);
/// v1 ships what the per-build analyses support (per-declaration proof
/// statuses, assumption lists, vacuity flags, interop/permissive fractions,
/// in-memory caller impact).
/// </summary>
public sealed class ReviewPacket
{
    /// <summary>ISO 8601 generation timestamp.</summary>
    public string GeneratedAt { get; init; } = "";

    /// <summary>
    /// Explicit waiver disclosures (strategy §5.2). When non-empty these are
    /// the FIRST LINE of every rendering: <c>--permissive-effects</c> voids
    /// the effect guarantee; <c>--contract-mode off</c> voids the contract
    /// guarantee.
    /// </summary>
    public List<string> Waivers { get; init; } = [];

    /// <summary>
    /// Standing honesty notes. Always includes the #782 line: refinement
    /// types are NOT runtime-enforced — refinement obligations are
    /// compile-time analysis only.
    /// </summary>
    public List<string> HonestyNotes { get; init; } = [];

    /// <summary>Seven-status counts plus the unproven remainder size.</summary>
    public ReviewPacketSummary Summary { get; init; } = new();

    /// <summary>
    /// The unproven remainder — every contract whose status is not a clean
    /// non-vacuous <c>proven</c>, listed first. Vacuous proofs are unproven
    /// remainder: a vacuous §Q means the function is uncallable.
    /// </summary>
    public List<ContractRecord> UnprovenRemainder { get; init; } = [];

    /// <summary>Cleanly proven contracts (non-vacuous), for completeness.</summary>
    public List<ContractRecord> Proven { get; init; } = [];

    /// <summary>Per-module interop/permissive disclosure.</summary>
    public List<ModuleDisclosure> Modules { get; init; } = [];

    /// <summary>Declarations treated as changed (explicit ids or git-diff derived).</summary>
    public List<string> ChangedDeclarations { get; init; } = [];

    /// <summary>Direct callers of each changed declaration (in-memory call graph, per module).</summary>
    public List<CallerImpactRecord> CallerImpact { get; init; } = [];

    /// <summary>The git ref the change set was diffed against, when one was given.</summary>
    public string? BaselineRef { get; init; }
}

/// <summary>Seven-status contract counts for the packet.</summary>
public sealed class ReviewPacketSummary
{
    public int TotalContracts { get; set; }

    /// <summary>Clean, non-vacuous proven contracts only (m2): a vacuous
    /// proof never counts here — see <see cref="ProvenVacuous"/>.</summary>
    public int Proven { get; set; }

    /// <summary>Vacuously proven contracts (unsatisfiable §Q set) — part of
    /// the unproven remainder, never of <see cref="Proven"/>.</summary>
    public int ProvenVacuous { get; set; }
    public int Refuted { get; set; }
    public int Assumed { get; set; }
    public int Unknown { get; set; }
    public int Timeout { get; set; }
    public int Unsupported { get; set; }
    public int Unavailable { get; set; }
    public int Vacuous { get; set; }

    /// <summary>Everything that is not a clean non-vacuous proven.</summary>
    public int UnprovenRemainder { get; set; }
}

/// <summary>One contract's proof record.</summary>
public sealed record ContractRecord(
    string File,
    string Module,
    string FunctionId,
    string FunctionName,
    string ContractKind,
    int Index,
    string? Text,
    string Status,
    string? Reason,
    bool Vacuous,
    IReadOnlyList<string> Assumptions,
    string? Counterexample);

/// <summary>
/// Per-module interop and waiver fractions: interop blocks over total members
/// (module-level and class-level members both counted), plus which waivers
/// applied to the build that produced this packet.
/// </summary>
public sealed record ModuleDisclosure(
    string Module,
    string File,
    int InteropBlocks,
    int TotalMembers,
    double InteropFraction,
    bool PermissiveEffects,
    bool ContractChecksOff,
    bool UsesRefinementTypes);

/// <summary>Direct callers of a changed declaration.</summary>
public sealed record CallerImpactRecord(
    string ChangedId,
    string ChangedName,
    string Module,
    IReadOnlyList<CallerRef> Callers);

/// <summary>A calling declaration.</summary>
public sealed record CallerRef(string Id, string Name);
