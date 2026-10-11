using System;
using Manhattan.Viewer;
using NUnit.Framework;
using Gate = Manhattan.Viewer.SourceReplacementGate;

public sealed class SourceReplacementGateTests
{
    private Gate gate;
    private Gate.SourceIdentity source;
    private Gate.LegacyBinding legacy;
    private Gate.PayloadIdentity body, trim, profile;
    private Gate.Request request;

    [SetUp]
    public void SetUp()
    {
        source = Source("building-42", "v2", '2');
        var baseline = Source("building-42", "legacy-v1", '1');
        legacy = new Gate.LegacyBinding(
            Payload(baseline, "fallback", 'a', Gate.PayloadKind.LegacyFallback),
            Payload(baseline, "body-map", 'b', Gate.PayloadKind.LegacyBodyMapping),
            Payload(baseline, "trim-map", 'c', Gate.PayloadKind.LegacyTrimMapping));
        body = Payload(source, "body", 'd', Gate.PayloadKind.Geometry);
        trim = Payload(source, "trim", 'e', Gate.PayloadKind.Geometry);
        profile = Payload(source, "optical-profile", 'f', Gate.PayloadKind.OpticalMaterialProfile);
        request = new Gate.Request(source, Gate.Semantics.ReplaceLegacy, body, trim, profile);
        gate = new Gate(source.SourceId, legacy);
    }

    [Test]
    public void PartialLoadNeverPublishesReplacementOrMasksLegacy()
    {
        var token = Begin();
        Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        ReadyLegacy(token);
        Ready(token, body);
        Ready(token, profile);
        Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        Ready(token, trim);
        var decision = gate.Evaluate(0);
        Assert.That(decision.Visibility, Is.EqualTo(Gate.Visibility.Replacement));
        Assert.That(decision.RetainedReplacement, Is.SameAs(token));
        Assert.That(gate.AcknowledgePublished(decision, 0), Is.True);
        Assert.That(gate.CanReleaseReplacement(token), Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void EveryPinnedFallbackAndExactBodyTrimMappingIsRequired(int missing)
    {
        var token = Begin();
        ReadyPayloads(token);
        var expected = new[] { legacy.Fallback, legacy.BodyMapping, legacy.TrimMapping };
        for (int i = 0; i < expected.Length; i++) if (i != missing) Ready(token, expected[i]);
        Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        Ready(token, expected[missing]);
        Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.Replacement));
    }

    [Test]
    public void MatchingCountsCannotSubstituteForeignPayloadIdentity()
    {
        var token = Begin();
        ReadyLegacy(token);
        Ready(token, body);
        Ready(token, profile);
        var foreign = Payload(source, "unexpected-trim", 'e', Gate.PayloadKind.Geometry);
        Assert.That(gate.ReportValidatedReady(token, foreign, foreign.ContentHash, 0), Is.False);
        AssertStopped(Gate.StopReason.IdentityMismatch);
        Assert.That(gate.ReportValidatedReady(token, trim, trim.ContentHash, 0), Is.False);
    }

    [TestCase("source")]
    [TestCase("version")]
    [TestCase("source-hash")]
    [TestCase("payload-hash")]
    [TestCase("kind")]
    [TestCase("verified-hash")]
    public void MismatchedEvidenceCannotSatisfyRequiredPayload(string mismatch)
    {
        var token = Begin();
        ReadyLegacy(token);
        var observedSource = Source(mismatch == "source" ? "building-99" : source.SourceId,
            mismatch == "version" ? "v3" : source.Version, mismatch == "source-hash" ? '3' : '2');
        var observed = Payload(observedSource, body.PayloadId, mismatch == "payload-hash" ? '0' : 'd',
            mismatch == "kind" ? Gate.PayloadKind.OpticalMaterialProfile : body.Kind);
        string verified = mismatch == "verified-hash" ? Hash('0') : observed.ContentHash;
        Assert.That(gate.ReportValidatedReady(token, observed, verified, 0), Is.False);
        AssertStopped(Gate.StopReason.IdentityMismatch);
    }

    [Test]
    public void MismatchedLegacyVersionOrMappingHashCannotRetireLegacy()
    {
        var token = Begin();
        ReadyPayloads(token);
        Ready(token, legacy.Fallback);
        Ready(token, legacy.BodyMapping);
        var changedMapping = Payload(legacy.TrimMapping.Source, legacy.TrimMapping.PayloadId, '0', legacy.TrimMapping.Kind);
        Assert.That(gate.ReportValidatedReady(token, changedMapping, changedMapping.ContentHash, 0), Is.False);
        Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        Assert.That(gate.Evaluate(0).Reason, Is.EqualTo(Gate.StopReason.IdentityMismatch));
    }

    [Test]
    public void StaleCompletionsAndForeignGateTokensAreIgnoredEvenWithFutureTime()
    {
        var stale = Begin();
        gate.Cancel(stale, 1);
        var current = gate.Begin(request, 2, 100);
        var otherGate = new Gate(source.SourceId, legacy);
        var foreign = otherGate.Begin(request, 0, 100);
        Assert.That(gate.ReportValidatedReady(stale, body, body.ContentHash, 999999), Is.False);
        Assert.That(gate.ReportValidatedReady(foreign, body, body.ContentHash, 999999), Is.False);
        Assert.That(gate.Cancel(stale, 999999), Is.False);
        Assert.That(gate.ReportFailure(stale, body, 999999), Is.False);
        Assert.That(gate.Evaluate(2).Reason, Is.EqualTo(Gate.StopReason.None));
        ReadyLegacy(current, 2);
        ReadyPayloads(current, 2);
        Assert.That(gate.Evaluate(2).Visibility, Is.EqualTo(Gate.Visibility.Replacement));
        Assert.That(gate.CanReleaseReplacement(stale), Is.True);
        Assert.That(gate.CanReleaseReplacement(foreign), Is.False);
    }

    [Test]
    public void TimeoutAtDeadlineIsTerminalAndLateCompletionCannotRevive()
    {
        var token = gate.Begin(request, 0, 10);
        ReadyLegacy(token);
        Ready(token, body);
        Ready(token, profile);
        Assert.That(gate.ReportValidatedReady(token, trim, trim.ContentHash, 10), Is.False);
        Assert.That(gate.Evaluate(10).Reason, Is.EqualTo(Gate.StopReason.TimedOut));
        Assert.That(gate.Evaluate(10).Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        Assert.That(gate.CanReleaseReplacement(token), Is.True);
    }

    [Test]
    public void CompletedReadinessDoesNotExpireAnActiveReplacement()
    {
        var token = gate.Begin(request, 0, 10);
        ReadyAll(token);
        var active = gate.Evaluate(9);
        Assert.That(gate.AcknowledgePublished(active, 9), Is.True);
        Assert.That(gate.Evaluate(10000).Visibility, Is.EqualTo(Gate.Visibility.Replacement));
    }

    [Test]
    public void CancelThenReenterRequiresNewPayloadReadiness()
    {
        var old = Begin();
        ReadyAll(old);
        gate.Cancel(old, 1);
        Assert.That(gate.ReportValidatedReady(old, body, body.ContentHash, 1), Is.False);
        Assert.That(gate.Evaluate(1).Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        var next = gate.Begin(request, 2, 100);
        Assert.That(next.Number, Is.GreaterThan(old.Number));
        Assert.That(gate.Evaluate(2).Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        ReadyAll(next, 2);
        Assert.That(gate.Evaluate(2).Visibility, Is.EqualTo(Gate.Visibility.Replacement));
    }

    [Test]
    public void NewVersionCannotPublishOverAnUnrestoredPreviousReplacement()
    {
        var old = Begin();
        ReadyAll(old);
        var active = gate.Evaluate(0);
        Assert.That(gate.AcknowledgePublished(active, 0), Is.True);
        var changed = Source(source.SourceId, "v3", '3');
        var newBody = Payload(changed, "body", '0', Gate.PayloadKind.Geometry);
        var newProfile = Payload(changed, "profile", '9', Gate.PayloadKind.OpticalMaterialProfile);
        var next = gate.Begin(new Gate.Request(changed, Gate.Semantics.ReplaceLegacy, newBody, newProfile), 1, 100);
        Ready(next, newBody, 1);
        Ready(next, newProfile, 1);
        Assert.That(gate.ReportValidatedReady(old, body, body.ContentHash, 1), Is.False);
        Assert.That(gate.AcknowledgePublished(active, 1), Is.False);
        var rollback = gate.Evaluate(1);
        Assert.That(rollback.Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        Assert.That(gate.CanReleaseReplacement(old), Is.False);
        Assert.That(gate.AcknowledgePublished(rollback, 1), Is.True);
        Assert.That(gate.CanReleaseReplacement(old), Is.True);
        var replacement = gate.Evaluate(1);
        Assert.That(replacement.Visibility, Is.EqualTo(Gate.Visibility.Replacement));
        Assert.That(replacement.Generation, Is.SameAs(next));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailureAfterOfferedOrActiveReplacementRequiresAcknowledgedFallback(bool acknowledgeFirst)
    {
        var token = Begin();
        ReadyAll(token);
        var offered = gate.Evaluate(0);
        if (acknowledgeFirst) Assert.That(gate.AcknowledgePublished(offered, 0), Is.True);
        Assert.That(gate.ReportFailure(token, trim, 1), Is.True);
        Assert.That(gate.AcknowledgePublished(offered, 1), Is.False);
        Assert.That(gate.CanReleaseReplacement(token), Is.False);
        var fallback = gate.Evaluate(1);
        Assert.That(fallback.Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        Assert.That(gate.AcknowledgePublished(fallback, 1), Is.True);
        Assert.That(gate.CanReleaseReplacement(token), Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void LostFallbackOrMappingHoldsReplacementUntilVerifiedRecovery(int failedIndex)
    {
        var token = Begin();
        ReadyAll(token);
        Assert.That(gate.AcknowledgePublished(gate.Evaluate(0), 0), Is.True);
        var failed = new[] { legacy.Fallback, legacy.BodyMapping, legacy.TrimMapping }[failedIndex];
        gate.ReportFailure(token, failed, 1);
        var hold = gate.Evaluate(1);
        Assert.That(hold.Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        Assert.That(hold.RetainedReplacement, Is.SameAs(token));
        Assert.That(gate.AcknowledgePublished(hold, 1), Is.False);
        Assert.That(gate.CanReleaseReplacement(token), Is.False);
        Ready(token, failed, 2);
        var fallback = gate.Evaluate(2);
        Assert.That(fallback.Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        Assert.That(gate.CanReleaseReplacement(token), Is.False);
        Assert.That(gate.AcknowledgePublished(fallback, 2), Is.True);
        Assert.That(gate.CanReleaseReplacement(token), Is.True);
    }

    [Test]
    public void MissingFallbackCanRecoverWhileSwitchingToOrdinaryLegacy()
    {
        var old = Begin();
        ReadyAll(old);
        gate.AcknowledgePublished(gate.Evaluate(0), 0);
        gate.ReportFailure(old, legacy.Fallback, 1);
        var ordinary = gate.Begin(new Gate.Request(source, Gate.Semantics.LegacyAdditive), 2, 100);
        Assert.That(gate.Evaluate(2).Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        Ready(ordinary, legacy.Fallback, 2);
        var fallback = gate.Evaluate(2);
        Assert.That(fallback.Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        Assert.That(gate.CanReleaseReplacement(old), Is.False);
        gate.AcknowledgePublished(fallback, 2);
        Assert.That(gate.Evaluate(2).Visibility, Is.EqualTo(Gate.Visibility.LegacyAdditive));
        Assert.That(gate.CanReleaseReplacement(old), Is.True);
    }

    [Test]
    public void OrdinaryLegacyIsAnUntouchedPassThroughWithoutReplacementEvidence()
    {
        var ordinaryGate = new Gate("ordinary");
        var ordinarySource = Source("ordinary", "v1", '1');
        var token = ordinaryGate.Begin(new Gate.Request(ordinarySource, Gate.Semantics.LegacyAdditive), 0, 10);
        var decision = ordinaryGate.Evaluate(999);
        Assert.That(decision.Visibility, Is.EqualTo(Gate.Visibility.LegacyAdditive));
        Assert.That(decision.RetainedReplacement, Is.Null);
        Assert.That(ordinaryGate.CanReleaseReplacement(token), Is.True);
        Assert.That(ordinaryGate.ReportValidatedReady(token, body, body.ContentHash, 999), Is.False);
        Assert.That(ordinaryGate.AcknowledgePublished(decision, 999), Is.True);
    }

    [Test]
    public void InvalidNewSourceCannotChangeExistingContractOrTurnItAdditive()
    {
        var token = Begin();
        ReadyAll(token);
        var active = gate.Evaluate(0);
        var other = Source("building-99", "v1", '1');
        Assert.Throws<ArgumentException>(() => gate.Begin(new Gate.Request(other, Gate.Semantics.LegacyAdditive), 1, 100));
        Assert.That(gate.Evaluate(1), Is.SameAs(active));
        Assert.That(gate.CanReleaseReplacement(token), Is.False);
    }

    [Test]
    public void RequiredIdentitiesAreImmutableCopiesAndDuplicateReadyDoesNotFillAnotherSlot()
    {
        var array = new[] { body, trim, profile };
        var immutable = new Gate.Request(source, Gate.Semantics.ReplaceLegacy, array);
        array[1] = body;
        var copy = immutable.RequiredPayloads;
        copy[1] = body;
        var token = gate.Begin(immutable, 0, 100);
        ReadyLegacy(token);
        Ready(token, body);
        Ready(token, body);
        Ready(token, profile);
        Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
        Assert.That(immutable.RequiredPayloads[1], Is.SameAs(trim));
        Ready(token, trim);
        Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.Replacement));
    }

    [Test]
    public void FallbackEvidenceIsReverifiedAfterRollbackAndOrdinaryLegacyReentry()
    {
        var old = Begin();
        ReadyAll(old);
        gate.AcknowledgePublished(gate.Evaluate(0), 0);
        gate.Cancel(old, 1);
        gate.AcknowledgePublished(gate.Evaluate(1), 1);
        var ordinary = gate.Begin(new Gate.Request(source, Gate.Semantics.LegacyAdditive), 2, 100);
        gate.ReportFailure(ordinary, legacy.Fallback, 2);
        Assert.That(gate.Evaluate(2).Visibility, Is.EqualTo(Gate.Visibility.LegacyAdditive));
        var next = gate.Begin(request, 3, 100);
        ReadyPayloads(next, 3);
        Assert.That(gate.Evaluate(3).Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        ReadyLegacy(next, 3);
        Assert.That(gate.Evaluate(3).Visibility, Is.EqualTo(Gate.Visibility.Replacement));
    }

    [Test]
    public void InvalidMeasuredFallbackHashCannotAuthorizeRollback()
    {
        var token = Begin();
        ReadyAll(token);
        gate.AcknowledgePublished(gate.Evaluate(0), 0);
        Assert.That(gate.ReportValidatedReady(token, legacy.Fallback, Hash('0'), 1), Is.False);
        Assert.That(gate.Evaluate(1).Visibility, Is.EqualTo(Gate.Visibility.HoldPublished));
        Assert.That(gate.CanReleaseReplacement(token), Is.False);
        Ready(token, legacy.Fallback, 2);
        Assert.That(gate.AcknowledgePublished(gate.Evaluate(2), 2), Is.True);
        Assert.That(gate.CanReleaseReplacement(token), Is.True);
    }

    [Test]
    public void StaleDecisionFromSameGenerationCannotAcknowledgeAfterReadinessMutation()
    {
        var token = Begin();
        ReadyAll(token);
        var old = gate.Evaluate(0);
        Ready(token, body);
        Assert.That(gate.AcknowledgePublished(old, 0), Is.False);
        var fresh = gate.Evaluate(0);
        Assert.That(fresh.Revision, Is.GreaterThan(old.Revision));
        Assert.That(gate.AcknowledgePublished(fresh, 0), Is.True);
    }

    [Test]
    public void StaleAndForeignAcknowledgmentsCannotAdvanceCurrentDeadline()
    {
        var old = Begin();
        ReadyLegacy(old);
        var oldDecision = gate.Evaluate(0);
        var other = new Gate(source.SourceId, legacy);
        other.Begin(request, 0, 100);
        var foreignDecision = other.Evaluate(0);
        var token = gate.Begin(request, 1, 100);
        Assert.That(gate.AcknowledgePublished(oldDecision, 999999), Is.False);
        Assert.That(gate.AcknowledgePublished(foreignDecision, 999999), Is.False);
        Assert.That(gate.Evaluate(1).Reason, Is.EqualTo(Gate.StopReason.None));
        ReadyLegacy(token, 1);
        var staleWithinGeneration = gate.Evaluate(1);
        Ready(token, body, 1);
        Assert.That(gate.AcknowledgePublished(staleWithinGeneration, 999999), Is.False);
        Assert.That(gate.Evaluate(1).Reason, Is.EqualTo(Gate.StopReason.None));
    }

    [Test]
    public void BackwardsClockFailsClosedRatherThanExtendingDeadline()
    {
        var token = gate.Begin(request, 10, 100);
        ReadyLegacy(token, 10);
        Assert.That(gate.ReportValidatedReady(token, body, body.ContentHash, 9), Is.False);
        Assert.That(gate.Evaluate(10).Reason, Is.EqualTo(Gate.StopReason.ClockReversed));
        Assert.That(gate.Evaluate(10).Visibility, Is.EqualTo(Gate.Visibility.KeepFallback));
    }

    [Test]
    public void BoundsAndIncompleteOrAmbiguousContractsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Gate.Request(source, Gate.Semantics.ReplaceLegacy,
            new Gate.PayloadIdentity[Gate.HardMaximumRequiredPayloads + 1]));
        Assert.Throws<ArgumentException>(() => new Gate.Request(source, Gate.Semantics.ReplaceLegacy, body));
        Assert.Throws<ArgumentException>(() => new Gate.Request(source, Gate.Semantics.ReplaceLegacy, body, profile, profile));
        Assert.Throws<ArgumentException>(() => new Gate.Request(source, Gate.Semantics.LegacyAdditive, body));
        Assert.Throws<ArgumentException>(() => new Gate.LegacyBinding(legacy.Fallback, legacy.TrimMapping, legacy.BodyMapping));
        Assert.Throws<ArgumentException>(() => new Gate("other", legacy));
        Assert.Throws<ArgumentException>(() => new Gate(source.SourceId).Begin(request, 0, 100));
        Assert.Throws<ArgumentException>(() => new Gate(source.SourceId, legacy, 2).Begin(request, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Begin(request, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Begin(request, 0, 30001));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Begin(request, long.MaxValue, 1));
        Assert.Throws<ArgumentException>(() => Source(" ", "v1", '1'));
        Assert.Throws<ArgumentException>(() => new Gate.SourceIdentity("valid", "v1", "not-a-digest"));
    }

    private Gate.Generation Begin() => gate.Begin(request, 0, 100);
    private void Ready(Gate.Generation token, Gate.PayloadIdentity item, long now = 0)
    { Assert.That(gate.ReportValidatedReady(token, item, item.ContentHash, now), Is.True); }
    private void ReadyLegacy(Gate.Generation token, long now = 0)
    { Ready(token, legacy.Fallback, now); Ready(token, legacy.BodyMapping, now); Ready(token, legacy.TrimMapping, now); }
    private void ReadyPayloads(Gate.Generation token, long now = 0)
    { Ready(token, body, now); Ready(token, trim, now); Ready(token, profile, now); }
    private void ReadyAll(Gate.Generation token, long now = 0)
    { ReadyLegacy(token, now); ReadyPayloads(token, now); }
    private void AssertStopped(Gate.StopReason reason)
    { Assert.That(gate.Evaluate(0).Reason, Is.EqualTo(reason)); Assert.That(gate.Evaluate(0).Visibility, Is.EqualTo(Gate.Visibility.KeepFallback)); }
    private static string Hash(char value) => new string(value, 64);
    private static Gate.SourceIdentity Source(string id, string version, char hash) => new Gate.SourceIdentity(id, version, Hash(hash));
    private static Gate.PayloadIdentity Payload(Gate.SourceIdentity src, string id, char hash, Gate.PayloadKind kind) =>
        new Gate.PayloadIdentity(src, id, Hash(hash), kind);
}
