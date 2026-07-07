using System;
using System.Collections.Generic;
using System.Linq;
using DeadEditor.Helpers;
using DeadEditor.Models;
using Xunit;

namespace DeadEditor.Tests;

/// <summary>
/// Tests for <see cref="SetlistPositionRemap"/> — the pure, atomic slice-4 position-remap component
/// (setlist-extras-writeback-spec.md §3.3 / D9a). This helper guards the whole extras arc's data
/// integrity: an insert / removal / reorder on the flattened setlist axis must remap every
/// position-indexed consumer (alias covered indices, ClaimedPositions, per-track ClaimedSetlistPosition)
/// coherently or leave everything unchanged. Covered indices and claims are 0-based on the one flattened
/// axis (alias-setlists-spec.md §3.2).
/// </summary>
public class SetlistPositionRemapTests
{
    // ===== Single-position mapping: insert at head / middle / tail =====

    [Fact]
    public void Insert_AtHead_ShiftsAllPositionsUpByOne()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 0);
        Assert.Equal(4, r.NewCount);
        Assert.Equal(1, r.MapPosition(0));
        Assert.Equal(2, r.MapPosition(1));
        Assert.Equal(3, r.MapPosition(2));
    }

    [Fact]
    public void Insert_InMiddle_ShiftsAtAndAfter_LeavesBefore()
    {
        // Insert at 2: positions 0,1 unchanged; 2,3 shift to 3,4.
        var r = SetlistPositionRemap.ForInsert(oldCount: 4, insertAt: 2);
        Assert.Equal(0, r.MapPosition(0));
        Assert.Equal(1, r.MapPosition(1));
        Assert.Equal(3, r.MapPosition(2)); // at the shift point → shifts
        Assert.Equal(4, r.MapPosition(3));
    }

    [Fact]
    public void Insert_AtTail_LeavesEveryExistingPositionUnchanged()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 3); // append
        Assert.Equal(0, r.MapPosition(0));
        Assert.Equal(1, r.MapPosition(1));
        Assert.Equal(2, r.MapPosition(2));
        Assert.Equal(4, r.NewCount);
    }

    // ===== Single-position mapping: remove at head / middle / tail =====

    [Fact]
    public void Remove_AtHead_MapsRemovedToNull_ShiftsRestDown()
    {
        var r = SetlistPositionRemap.ForRemove(oldCount: 3, removeAt: 0);
        Assert.Null(r.MapPosition(0)); // removed
        Assert.Equal(0, r.MapPosition(1));
        Assert.Equal(1, r.MapPosition(2));
        Assert.Equal(2, r.NewCount);
    }

    [Fact]
    public void Remove_InMiddle_ShiftsAfterDown_LeavesBefore()
    {
        var r = SetlistPositionRemap.ForRemove(oldCount: 4, removeAt: 1);
        Assert.Equal(0, r.MapPosition(0));
        Assert.Null(r.MapPosition(1));     // removed
        Assert.Equal(1, r.MapPosition(2)); // shifts down
        Assert.Equal(2, r.MapPosition(3));
    }

    [Fact]
    public void Remove_AtTail_MapsRemovedToNull_LeavesRest()
    {
        var r = SetlistPositionRemap.ForRemove(oldCount: 3, removeAt: 2);
        Assert.Equal(0, r.MapPosition(0));
        Assert.Equal(1, r.MapPosition(1));
        Assert.Null(r.MapPosition(2));
    }

    // ===== Identity (no-op edit) =====

    [Fact]
    public void Identity_MapsEveryPositionToItself()
    {
        var r = SetlistPositionRemap.Identity(3);
        Assert.Equal(0, r.MapPosition(0));
        Assert.Equal(1, r.MapPosition(1));
        Assert.Equal(2, r.MapPosition(2));
        Assert.Equal(3, r.OldCount);
        Assert.Equal(3, r.NewCount);
    }

    [Fact]
    public void Identity_ClaimsAndAlias_ByteIdenticalState()
    {
        // A no-op edit leaves claims and alias coverage logically identical (same indices).
        var r = SetlistPositionRemap.Identity(4);
        var claims = new HashSet<int> { 0, 2, 3 };
        var aliases = new List<AliasSetlist>
        {
            new AliasSetlist { Id = "a", Label = "L", Entries = { new AliasEntry { CoveredOfficialIndices = new List<int> { 1, 2 } } } },
        };

        var result = r.ApplyTo(aliases, claims);

        Assert.Equal(new HashSet<int> { 0, 2, 3 }, result.ClaimedPositions);
        Assert.Equal(new List<int> { 1, 2 }, result.AliasSetlists[0].Entries[0].CoveredOfficialIndices);
        Assert.Equal("a", result.AliasSetlists[0].Id);
        Assert.Equal("L", result.AliasSetlists[0].Label);
    }

    // ===== Alias coverage: insert before / after / spanning =====

    [Fact]
    public void Alias_InsertBeforeCoveredRun_ShiftsIndices()
    {
        // Covered [2,3]; insert at 0 → covered becomes [3,4].
        var r = SetlistPositionRemap.ForInsert(oldCount: 5, insertAt: 0);
        var mapped = r.MapCoveredIndices(new List<int> { 2, 3 });
        Assert.Equal(new List<int> { 3, 4 }, mapped);
    }

    [Fact]
    public void Alias_InsertAfterCoveredRun_LeavesIndicesUnchanged()
    {
        // Covered [0,1]; insert at 3 → covered unchanged [0,1].
        var r = SetlistPositionRemap.ForInsert(oldCount: 5, insertAt: 3);
        var mapped = r.MapCoveredIndices(new List<int> { 0, 1 });
        Assert.Equal(new List<int> { 0, 1 }, mapped);
    }

    [Fact]
    public void Alias_InsertSpanningTheRun_ShiftsOnlyIndicesAtOrAfterInsertion()
    {
        // Covered [1,2]; insert at 2 → 1 stays, 2 shifts to 3 → [1,3].
        // (Contiguity is broken by an interleaving insert; the component remaps faithfully — see report note.)
        var r = SetlistPositionRemap.ForInsert(oldCount: 4, insertAt: 2);
        var mapped = r.MapCoveredIndices(new List<int> { 1, 2 });
        Assert.Equal(new List<int> { 1, 3 }, mapped);
    }

    [Fact]
    public void Alias_RemoveBeforeCoveredRun_ShiftsIndicesDown()
    {
        // Covered [2,3]; remove at 0 → [1,2].
        var r = SetlistPositionRemap.ForRemove(oldCount: 5, removeAt: 0);
        var mapped = r.MapCoveredIndices(new List<int> { 2, 3 });
        Assert.Equal(new List<int> { 1, 2 }, mapped);
    }

    [Fact]
    public void Alias_RemoveAfterCoveredRun_LeavesIndicesUnchanged()
    {
        var r = SetlistPositionRemap.ForRemove(oldCount: 5, removeAt: 4);
        var mapped = r.MapCoveredIndices(new List<int> { 0, 1 });
        Assert.Equal(new List<int> { 0, 1 }, mapped);
    }

    // ===== Alias coverage: STOP-point — removed-covered entry =====

    [Fact]
    public void Alias_RemoveCoveredEntryItself_Throws_StopPoint()
    {
        // Covered [1,2]; remove position 2 (a covered index) → unspecified → refuse.
        var r = SetlistPositionRemap.ForRemove(oldCount: 4, removeAt: 2);
        var ex = Assert.Throws<SetlistRemapUnsupportedException>(
            () => r.MapCoveredIndices(new List<int> { 1, 2 }));
        Assert.Equal(2, ex.RemovedCoveredIndex);
    }

    [Fact]
    public void Alias_RemoveCoveredEntry_LeavesInputListUnmutated()
    {
        // Atomicity at the entry level: a refused remap must not have touched the source list.
        var r = SetlistPositionRemap.ForRemove(oldCount: 4, removeAt: 1);
        var covered = new List<int> { 1, 2 };
        Assert.Throws<SetlistRemapUnsupportedException>(() => r.MapCoveredIndices(covered));
        Assert.Equal(new List<int> { 1, 2 }, covered); // unchanged
    }

    // ===== Multi-run alias =====

    [Fact]
    public void MapAliasSetlists_MultipleRuns_AllRemapped_IdLabelPreserved()
    {
        // Two combine runs on one alias; insert at 3 shifts only the second run.
        var r = SetlistPositionRemap.ForInsert(oldCount: 6, insertAt: 3);
        var aliases = new List<AliasSetlist>
        {
            new AliasSetlist
            {
                Id = "vault", Label = "One From The Vault",
                Entries =
                {
                    new AliasEntry { CoveredOfficialIndices = new List<int> { 0, 1 } }, // before insert → unchanged
                    new AliasEntry { CoveredOfficialIndices = new List<int> { 3, 4 } }, // at/after → [4,5]
                },
            },
        };

        var mapped = r.MapAliasSetlists(aliases);

        Assert.Equal("vault", mapped[0].Id);
        Assert.Equal("One From The Vault", mapped[0].Label);
        Assert.Equal(new List<int> { 0, 1 }, mapped[0].Entries[0].CoveredOfficialIndices);
        Assert.Equal(new List<int> { 4, 5 }, mapped[0].Entries[1].CoveredOfficialIndices);
    }

    [Fact]
    public void MapAliasSetlists_ReturnsFreshObjects_InputUntouched()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 4, insertAt: 0);
        var entry = new AliasEntry { CoveredOfficialIndices = new List<int> { 1, 2 } };
        var aliases = new List<AliasSetlist> { new AliasSetlist { Entries = { entry } } };

        var mapped = r.MapAliasSetlists(aliases);

        Assert.NotSame(aliases[0], mapped[0]);
        Assert.NotSame(aliases[0].Entries[0], mapped[0].Entries[0]);
        Assert.Equal(new List<int> { 1, 2 }, entry.CoveredOfficialIndices);        // source unmutated
        Assert.Equal(new List<int> { 2, 3 }, mapped[0].Entries[0].CoveredOfficialIndices);
    }

    // ===== Claim state: ClaimedPositions (set) =====

    [Fact]
    public void MapClaims_Insert_ShiftsAtAndAfter()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 5, insertAt: 2);
        var claims = new HashSet<int> { 0, 2, 4 };
        Assert.Equal(new HashSet<int> { 0, 3, 5 }, r.MapClaims(claims));
    }

    [Fact]
    public void MapClaims_Remove_DropsRemovedPosition_ShiftsRest()
    {
        var r = SetlistPositionRemap.ForRemove(oldCount: 5, removeAt: 2);
        var claims = new HashSet<int> { 1, 2, 4 };      // 2 is the removed entry
        Assert.Equal(new HashSet<int> { 1, 3 }, r.MapClaims(claims)); // 2 dropped, 4→3
    }

    [Fact]
    public void MapClaims_OutOfRange_DroppedTolerantly()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 0);
        var claims = new HashSet<int> { 0, 9, -1 };     // 9 and -1 are stale/invalid
        Assert.Equal(new HashSet<int> { 1 }, r.MapClaims(claims));
    }

    [Fact]
    public void MapClaims_ReturnsNewSet_InputUntouched()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 0);
        var claims = new HashSet<int> { 0, 1 };
        var mapped = r.MapClaims(claims);
        Assert.NotSame(claims, mapped);
        Assert.Equal(new HashSet<int> { 0, 1 }, claims); // unchanged
    }

    // ===== Claim state: per-track ClaimedSetlistPosition (nullable) =====

    [Fact]
    public void MapClaim_Null_StaysNull()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 0);
        Assert.Null(r.MapClaim(null));
    }

    [Fact]
    public void MapClaim_Shifted_ReturnsNewPosition()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 1);
        Assert.Equal(2, r.MapClaim(1)); // 1 shifts to 2
        Assert.Equal(0, r.MapClaim(0)); // before insert → unchanged
    }

    [Fact]
    public void MapClaim_OnRemovedPosition_BecomesNull()
    {
        var r = SetlistPositionRemap.ForRemove(oldCount: 4, removeAt: 2);
        Assert.Null(r.MapClaim(2));     // its entry no longer exists
        Assert.Equal(2, r.MapClaim(3)); // 3 shifts down to 2
    }

    [Fact]
    public void MapClaim_OutOfRange_BecomesNull_Tolerant()
    {
        var r = SetlistPositionRemap.ForRemove(oldCount: 3, removeAt: 0);
        Assert.Null(r.MapClaim(99));
        Assert.Null(r.MapClaim(-5));
    }

    // ===== Reorder =====

    [Fact]
    public void Reorder_MapsEachOldPositionToItsNewIndex()
    {
        // newOrder[newPos] = oldPos. Reverse of 3: new [2,1,0] → old 0→2, 1→1, 2→0.
        var r = SetlistPositionRemap.ForReorder(new List<int> { 2, 1, 0 });
        Assert.Equal(2, r.MapPosition(0));
        Assert.Equal(1, r.MapPosition(1));
        Assert.Equal(0, r.MapPosition(2));
    }

    [Fact]
    public void Reorder_RemapsClaimsAndContiguousCoveredRun()
    {
        // Move old tail (3) to the front: newOrder = [3,0,1,2] → old 0→1,1→2,2→3,3→0.
        var r = SetlistPositionRemap.ForReorder(new List<int> { 3, 0, 1, 2 });
        Assert.Equal(new HashSet<int> { 1, 2 }, r.MapClaims(new HashSet<int> { 0, 1 }));
        // Covered [1,2] → old 1→2, 2→3 → [2,3] (still contiguous, sorted ascending).
        Assert.Equal(new List<int> { 2, 3 }, r.MapCoveredIndices(new List<int> { 1, 2 }));
    }

    [Fact]
    public void Reorder_NonPermutation_Throws_NothingBuilt()
    {
        Assert.Throws<ArgumentException>(() => SetlistPositionRemap.ForReorder(new List<int> { 0, 0, 2 })); // dup
        Assert.Throws<ArgumentException>(() => SetlistPositionRemap.ForReorder(new List<int> { 0, 3, 1 })); // out of range
    }

    // ===== Multiple sequential operations =====

    [Fact]
    public void Sequential_InsertThenRemove_ComposesCoherently()
    {
        // Start: 4 entries, claim {1,3}, alias covered [1,2].
        // Op1: insert at 0 (→5 entries). Op2 on the new axis: remove at 4.
        var claims0 = new HashSet<int> { 1, 3 };
        var covered0 = new List<int> { 1, 2 };

        var op1 = SetlistPositionRemap.ForInsert(oldCount: 4, insertAt: 0);
        var claims1 = op1.MapClaims(claims0);                 // {2,4}
        var covered1 = op1.MapCoveredIndices(covered0);       // [2,3]
        Assert.Equal(new HashSet<int> { 2, 4 }, claims1);
        Assert.Equal(new List<int> { 2, 3 }, covered1);

        var op2 = SetlistPositionRemap.ForRemove(oldCount: op1.NewCount, removeAt: 4);
        var claims2 = op2.MapClaims(claims1);                 // 4 removed → {2}
        var covered2 = op2.MapCoveredIndices(covered1);       // [2,3] both < 4 → unchanged
        Assert.Equal(new HashSet<int> { 2 }, claims2);
        Assert.Equal(new List<int> { 2, 3 }, covered2);
    }

    // ===== Round-trip coherence: claim + alias point at the same logical entry after a shift =====

    [Fact]
    public void RoundTrip_ClaimAndAlias_FollowTheSameLogicalEntryThroughAnInsert()
    {
        // Logical entry originally at position 3 is claimed AND is the start of a covered run [3,4].
        // Insert one extra at 2 (ahead of it). Both the claim and the covered index must now name 4/5.
        var r = SetlistPositionRemap.ForInsert(oldCount: 6, insertAt: 2);

        int? claimForEntry3 = r.MapClaim(3);
        var coveredRun = r.MapCoveredIndices(new List<int> { 3, 4 });

        Assert.Equal(4, claimForEntry3);                       // entry-3 now lives at 4
        Assert.Equal(4, coveredRun[0]);                        // same entry, same new index
        Assert.Equal(new List<int> { 4, 5 }, coveredRun);
    }

    // ===== ApplyTo atomicity across both consumers =====

    [Fact]
    public void ApplyTo_RemovedCovered_ThrowsBeforeAdopting_InputsUnchanged()
    {
        // Remove a covered index → ApplyTo must throw (alias remap runs first) and leave inputs intact.
        var r = SetlistPositionRemap.ForRemove(oldCount: 5, removeAt: 2);
        var claims = new HashSet<int> { 0, 4 };
        var aliases = new List<AliasSetlist>
        {
            new AliasSetlist { Entries = { new AliasEntry { CoveredOfficialIndices = new List<int> { 2, 3 } } } },
        };

        Assert.Throws<SetlistRemapUnsupportedException>(() => r.ApplyTo(aliases, claims));

        // Nothing mutated on failure.
        Assert.Equal(new HashSet<int> { 0, 4 }, claims);
        Assert.Equal(new List<int> { 2, 3 }, aliases[0].Entries[0].CoveredOfficialIndices);
    }

    [Fact]
    public void ApplyTo_ValidEdit_RemapsBothConsumers()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 5, insertAt: 1);
        var claims = new HashSet<int> { 0, 3 };
        var aliases = new List<AliasSetlist>
        {
            new AliasSetlist { Entries = { new AliasEntry { CoveredOfficialIndices = new List<int> { 3, 4 } } } },
        };

        var result = r.ApplyTo(aliases, claims);

        Assert.Equal(new HashSet<int> { 0, 4 }, result.ClaimedPositions);          // 0 stays, 3→4
        Assert.Equal(new List<int> { 4, 5 }, result.AliasSetlists[0].Entries[0].CoveredOfficialIndices);
    }

    // ===== FromSurvivingOrder: the composite at-save map =====

    [Fact]
    public void FromSurvivingOrder_Identity_WhenOrderUnchanged()
    {
        // 4 rows, none moved/inserted/removed → identity.
        var r = SetlistPositionRemap.FromSurvivingOrder(4, new int?[] { 0, 1, 2, 3 });
        Assert.Equal(0, r.MapPosition(0));
        Assert.Equal(3, r.MapPosition(3));
        Assert.Equal(4, r.NewCount);
    }

    [Fact]
    public void FromSurvivingOrder_InsertMoveRemove_ComposedInOneSession()
    {
        // Old axis: 0,1,2,3,4. Session: remove old-2; move old-4 to the front; insert a new row
        // in the middle. New order (origins): [4, 0, null, 1, 3].
        var r = SetlistPositionRemap.FromSurvivingOrder(5, new int?[] { 4, 0, null, 1, 3 });

        Assert.Equal(1, r.MapPosition(0)); // old-0 now at new index 1
        Assert.Equal(3, r.MapPosition(1)); // old-1 → 3
        Assert.Null(r.MapPosition(2));     // old-2 removed
        Assert.Equal(4, r.MapPosition(3)); // old-3 → 4
        Assert.Equal(0, r.MapPosition(4)); // old-4 moved to front
        Assert.Equal(5, r.NewCount);       // 4 survivors + 1 inserted
    }

    [Fact]
    public void FromSurvivingOrder_AllRemoved_EveryOldMapsToNull()
    {
        var r = SetlistPositionRemap.FromSurvivingOrder(3, new int?[] { });
        Assert.Null(r.MapPosition(0));
        Assert.Null(r.MapPosition(1));
        Assert.Null(r.MapPosition(2));
        Assert.Equal(0, r.NewCount);
    }

    [Fact]
    public void FromSurvivingOrder_AllInserted_FromEmptyOld()
    {
        // Started empty (oldCount 0), three new rows inserted.
        var r = SetlistPositionRemap.FromSurvivingOrder(0, new int?[] { null, null, null });
        Assert.Equal(0, r.OldCount);
        Assert.Equal(3, r.NewCount);
        // A brand-new setlist has no aliases/claims to remap; MapClaims over empty is empty.
        Assert.Empty(r.MapClaims(new HashSet<int>()));
    }

    [Fact]
    public void FromSurvivingOrder_DuplicateOrigin_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => SetlistPositionRemap.FromSurvivingOrder(3, new int?[] { 0, 1, 0 }));
    }

    [Fact]
    public void FromSurvivingOrder_OutOfRangeOrigin_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => SetlistPositionRemap.FromSurvivingOrder(3, new int?[] { 0, 5 }));
        Assert.Throws<ArgumentException>(
            () => SetlistPositionRemap.FromSurvivingOrder(3, new int?[] { 0, -1 }));
    }

    [Fact]
    public void FromSurvivingOrder_InsertBeforeCombine_RemapsCoveredRun()
    {
        // Old axis 0..3 with a combine covering [1,2]. Insert a new row at the front:
        // new order origins = [null, 0, 1, 2, 3] → covered [1,2] must become [2,3].
        var r = SetlistPositionRemap.FromSurvivingOrder(4, new int?[] { null, 0, 1, 2, 3 });
        Assert.Equal(new List<int> { 2, 3 }, r.MapCoveredIndices(new List<int> { 1, 2 }));
    }

    // ===== Factory argument validation (out-of-range robustness) =====

    [Fact]
    public void Factories_RejectOutOfRangeArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SetlistPositionRemap.ForRemove(oldCount: 3, removeAt: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => SetlistPositionRemap.ForRemove(oldCount: 0, removeAt: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SetlistPositionRemap.Identity(-1));
    }

    [Fact]
    public void MapPosition_OutOfRange_ThrowsStrict()
    {
        var r = SetlistPositionRemap.ForInsert(oldCount: 3, insertAt: 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.MapPosition(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.MapPosition(-1));
    }

    [Fact]
    public void MapCoveredIndices_OutOfRangeCovered_ThrowsStrict()
    {
        // Corrupt alias data (covered index beyond the setlist) surfaces, never silently dropped.
        var r = SetlistPositionRemap.Identity(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.MapCoveredIndices(new List<int> { 0, 5 }));
    }
}
