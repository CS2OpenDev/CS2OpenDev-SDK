#region

using CS2SchemaGen.Emitters;

#endregion

namespace CS2SchemaGen.SchemaLens;

// Replays migrations, in file order, op order, into a LensState.
//
// Replay is strict: an op that does not apply cleanly — a duplicate add, a
// rename of a field that is not there — throws rather than best-effortsing.
// The migration history is the audit trail for every name the Lens has ever
// served, and an audit trail that tolerates entries that "probably meant"
// something is not one. The exporter reports these throws under CS2_GEN_013.
//
// After each migration the canonical hash of the accumulated state is computed
// and recorded against the hash the file declares. Recorded, not enforced,
// here: the placeholder authoring flow needs the computed value delivered to
// the author, and that is the exporter's job, not this class's.
internal static class LensReplay
{
    internal static LensReplayResult Replay(IReadOnlyList<LensMigration> migrations)
    {
        LensState state = new();
        List<LensHashCheck> hashChecks = [];
        List<LensRenameRecord> renames = [];

        foreach (LensMigration migration in migrations)
        {
            foreach (LensOp op in migration.Changes)
            {
                Apply(state, op, migration, renames);
            }

            string computed = LensCanonicalForm.Hash(state);
            bool isPlaceholder = string.Equals(
                migration.StateHash, LensMigration.PlaceholderHash, StringComparison.Ordinal);
            hashChecks.Add(new LensHashCheck(migration.Id, migration.StateHash, computed, isPlaceholder));
        }

        return new LensReplayResult
        {
            State = state,
            HashChecks = hashChecks,
            Renames = renames
        };
    }

    // The mechanical netName rule: strip a leading 'C' when it is followed by
    // another uppercase letter (CCSPlayerPawn → CSPlayerPawn, CHEGrenadeProjectile
    // → HEGrenadeProjectile). Only that. Prefixes that merely look like acronym
    // starts are left alone — the rule never guesses at word boundaries, and a
    // name it gets wrong is exactly what an explicit netName override is for.
    internal static string DeriveNetName(string engineClass) =>
        engineClass.Length >= 2 && engineClass[0] == 'C' && char.IsUpper(engineClass[1])
            ? engineClass.Substring(1)
            : engineClass;

    // The mechanical targetProperty rule: take the last segment of the dotted
    // path and run it through the same fold every emitted property name takes
    // (Hungarian strip + PascalCase + word split). Reusing NameHelpers.ToPropName
    // rather than a local copy is load-bearing: the fold funnels through
    // WordSplitter, so a derived Lens name obeys the vocabulary and the name
    // lock exactly like the SDK's own properties — one naming authority, not two.
    internal static string DeriveTargetProperty(string fieldPath)
    {
        int lastDot = fieldPath.LastIndexOf('.');
        string segment = lastDot >= 0 ? fieldPath.Substring(lastDot + 1) : fieldPath;
        return NameHelpers.ToPropName(segment);
    }

    private static void Apply(LensState state, LensOp op, LensMigration migration, List<LensRenameRecord> renames)
    {
        switch (op)
        {
            case AddClassOp add:
            {
                if (state.Classes.ContainsKey(add.Class))
                {
                    throw Error(migration, $"addClass '{add.Class}' — the class is already covered.");
                }

                state.Classes.Add(add.Class, new LensClassState
                {
                    NetName = add.NetName ?? DeriveNetName(add.Class),
                    ModulePin = add.Module
                });
                break;
            }

            case RemoveClassOp remove:
            {
                if (!state.Classes.Remove(remove.Class))
                {
                    throw Error(migration, $"removeClass '{remove.Class}' — the class is not covered.");
                }

                break;
            }

            case AddFieldOp add:
            {
                LensClassState cls = RequireClass(state, add.Class, migration);
                if (cls.Fields.ContainsKey(add.Field))
                {
                    throw Error(migration, $"addField '{add.Class}.{add.Field}' — the field is already tracked.");
                }

                if (cls.Aliases.TryGetValue(add.Field, out string? aliasTarget))
                {
                    throw Error(migration,
                        $"addField '{add.Class}.{add.Field}' — the name is already an alias of "
                        + $"'{aliasTarget}'. Canonical names and aliases share one namespace.");
                }

                // Tracking supersedes acknowledgment: a field parked with
                // ignoreField in an earlier migration can be promoted to
                // tracked later without a ceremony op in between.
                cls.Ignored.Remove(add.Field);

                cls.Fields.Add(add.Field, new LensFieldEntry
                {
                    TargetProperty = add.TargetProperty ?? DeriveTargetProperty(add.Field),
                    FirstSeenBuild = migration.Build
                });
                break;
            }

            case RemoveFieldOp remove:
            {
                LensClassState cls = RequireClass(state, remove.Class, migration);
                if (!cls.Fields.Remove(remove.Field))
                {
                    throw Error(migration, $"removeField '{remove.Class}.{remove.Field}' — the field is not tracked.");
                }

                // A removed canonical takes its aliases with it. An alias whose
                // target is gone answers lookups with a name that no longer
                // means anything, which is the exact failure the Lens exists
                // to prevent.
                foreach (string alias in cls.Aliases
                             .Where(kv => string.Equals(kv.Value, remove.Field, StringComparison.Ordinal))
                             .Select(kv => kv.Key)
                             .ToList())
                {
                    cls.Aliases.Remove(alias);
                }

                break;
            }

            case RenameOp rename:
            {
                ApplyMove(state, rename.Class, rename.From, rename.To, "rename", migration, renames);
                break;
            }

            case MoveSubServiceOp move:
            {
                ApplyMove(state, move.Class, move.From, move.To, "moveSubService", migration, renames);
                break;
            }

            case AddAliasOp addAlias:
            {
                LensClassState cls = RequireClass(state, addAlias.Class, migration);
                if (!cls.Fields.ContainsKey(addAlias.Canonical))
                {
                    throw Error(migration,
                        $"addAlias '{addAlias.Alias}' — canonical '{addAlias.Class}.{addAlias.Canonical}' is not tracked.");
                }

                if (cls.Fields.ContainsKey(addAlias.Alias))
                {
                    throw Error(migration,
                        $"addAlias '{addAlias.Alias}' on '{addAlias.Class}' — the alias collides with a "
                        + "canonical field name. Only a rename may map a canonical name onto itself.");
                }

                if (cls.Aliases.TryGetValue(addAlias.Alias, out string? existingTarget))
                {
                    throw Error(migration,
                        $"addAlias '{addAlias.Alias}' on '{addAlias.Class}' — the alias already exists, "
                        + $"pointing at '{existingTarget}'.");
                }

                cls.Aliases.Add(addAlias.Alias, addAlias.Canonical);
                break;
            }

            case TypeShiftOp shift:
            {
                LensClassState cls = RequireClass(state, shift.Class, migration);
                if (!cls.Fields.TryGetValue(shift.Field, out LensFieldEntry? entry))
                {
                    throw Error(migration, $"typeShift '{shift.Class}.{shift.Field}' — the field is not tracked.");
                }

                entry.TypeHistory.Add(new LensTypeShift(migration.Build, shift.FromType, shift.ToType));
                break;
            }

            case IgnoreFieldOp ignore:
            {
                LensClassState cls = RequireClass(state, ignore.Class, migration);
                if (cls.Fields.ContainsKey(ignore.Field))
                {
                    throw Error(migration,
                        $"ignoreField '{ignore.Class}.{ignore.Field}' — the field is tracked. A field cannot "
                        + "be both served and disowned; use removeField first if the intent is to stop tracking it.");
                }

                if (!cls.Ignored.Add(ignore.Field))
                {
                    throw Error(migration, $"ignoreField '{ignore.Class}.{ignore.Field}' — already ignored.");
                }

                break;
            }

            default:
                throw Error(migration, $"unhandled op type '{op.GetType().Name}'.");
        }
    }

    // Shared by rename and moveSubService — the mechanics are the same, and
    // sharing the implementation is what keeps "same mechanics" true by
    // construction rather than by two blocks staying in step.
    private static void ApplyMove(
        LensState state,
        string className,
        string from,
        string to,
        string opName,
        LensMigration migration,
        List<LensRenameRecord> renames)
    {
        LensClassState cls = RequireClass(state, className, migration);
        if (!cls.Fields.Remove(from, out LensFieldEntry? entry))
        {
            throw Error(migration, $"{opName} '{className}.{from}' — the field is not tracked.");
        }

        if (cls.Fields.ContainsKey(to))
        {
            throw Error(migration, $"{opName} '{className}.{from}' → '{to}' — the target is already tracked.");
        }

        if (cls.Aliases.TryGetValue(to, out string? existing)
            && !string.Equals(existing, from, StringComparison.Ordinal))
        {
            throw Error(migration,
                $"{opName} '{className}.{from}' → '{to}' — the target is an alias of '{existing}'.");
        }

        // The entry moves wholesale: target property, first-seen build and type
        // history all belong to the field, not to the spelling it happens to
        // have this build.
        cls.Fields.Add(to, entry);

        // Every name that used to reach `from` must now reach `to`, and both
        // `from` and `to` themselves must resolve through the alias table — the
        // self-alias is what makes lookup-by-any-historical-name total.
        foreach (string alias in cls.Aliases
                     .Where(kv => string.Equals(kv.Value, from, StringComparison.Ordinal))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            cls.Aliases[alias] = to;
        }

        cls.Aliases[from] = to;
        cls.Aliases[to] = to;

        renames.Add(new LensRenameRecord(className, from, to, opName, migration.Id));
    }

    private static LensClassState RequireClass(LensState state, string className, LensMigration migration)
    {
        if (state.Classes.TryGetValue(className, out LensClassState? cls))
        {
            return cls;
        }

        throw Error(migration, $"class '{className}' is not covered — addClass must precede any op that names it.");
    }

    private static InvalidOperationException Error(LensMigration migration, string message) =>
        new($"'{migration.Id}': {message}");
}
