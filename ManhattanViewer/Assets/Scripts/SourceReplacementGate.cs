using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Manhattan.Viewer
{
    /// <summary>
    /// Decision-only, single-threaded replacement transaction for one stable source ID.
    /// This does not load assets, inspect geometry/materials or change any Unity renderer.
    /// Its trusted adapter must validate exact body/trim ownership and optical compatibility,
    /// keep the fallback resident, and apply each decision atomically between frames.
    /// Readiness is evidence from that trusted adapter, not a material-capability override.
    /// </summary>
    public sealed class SourceReplacementGate
    {
        public const int HardMaximumRequiredPayloads = 4096;

        public enum Semantics { LegacyAdditive, ReplaceLegacy }
        public enum PayloadKind { Geometry, OpticalMaterialProfile, LegacyFallback, LegacyBodyMapping, LegacyTrimMapping }
        public enum Visibility { HoldPublished, KeepFallback, Replacement, LegacyAdditive }
        public enum StopReason { None, Cancelled, TimedOut, PayloadFailed, IdentityMismatch, ClockReversed }

        public sealed class SourceIdentity : IEquatable<SourceIdentity>
        {
            public string SourceId { get; }
            public string Version { get; }
            public string ContentHash { get; }
            public SourceIdentity(string sourceId, string version, string contentHash)
            {
                SourceId = Required(sourceId, nameof(sourceId));
                Version = Required(version, nameof(version));
                ContentHash = Hash(contentHash, nameof(contentHash));
            }
            public bool Equals(SourceIdentity other) => other != null && SourceId == other.SourceId &&
                Version == other.Version && ContentHash == other.ContentHash;
            public override bool Equals(object obj) => Equals(obj as SourceIdentity);
            public override int GetHashCode() => SourceId.GetHashCode() ^ Version.GetHashCode() ^ ContentHash.GetHashCode();
        }

        public sealed class PayloadIdentity : IEquatable<PayloadIdentity>
        {
            public SourceIdentity Source { get; }
            public string PayloadId { get; }
            public string ContentHash { get; }
            public PayloadKind Kind { get; }
            public PayloadIdentity(SourceIdentity source, string payloadId, string contentHash, PayloadKind kind)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                PayloadId = Required(payloadId, nameof(payloadId));
                ContentHash = Hash(contentHash, nameof(contentHash));
                if (!Enum.IsDefined(typeof(PayloadKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
                Kind = kind;
            }
            public bool Equals(PayloadIdentity other) => other != null && Source.Equals(other.Source) &&
                PayloadId == other.PayloadId && ContentHash == other.ContentHash && Kind == other.Kind;
            public override bool Equals(object obj) => Equals(obj as PayloadIdentity);
            public override int GetHashCode() => Source.GetHashCode() ^ PayloadId.GetHashCode() ^ ContentHash.GetHashCode() ^ (int)Kind;
        }

        /// <summary>
        /// Identities of verified legacy fallback and exact source-ID body/trim mapping artifacts.
        /// These identify an immutable legacy baseline, not the candidate source version.
        /// Changing this binding requires a new gate after the old transaction is rolled back.
        /// </summary>
        public sealed class LegacyBinding
        {
            internal readonly ReadOnlyCollection<PayloadIdentity> Items;
            public PayloadIdentity Fallback => Items[0];
            public PayloadIdentity BodyMapping => Items[1];
            public PayloadIdentity TrimMapping => Items[2];
            public LegacyBinding(PayloadIdentity fallback, PayloadIdentity bodyMapping, PayloadIdentity trimMapping)
            {
                if (fallback == null || bodyMapping == null || trimMapping == null)
                    throw new ArgumentNullException(nameof(fallback), "All three legacy identities are required.");
                if (fallback.Kind != PayloadKind.LegacyFallback || bodyMapping.Kind != PayloadKind.LegacyBodyMapping ||
                    trimMapping.Kind != PayloadKind.LegacyTrimMapping || !fallback.Source.Equals(bodyMapping.Source) ||
                    !fallback.Source.Equals(trimMapping.Source) || fallback.PayloadId == bodyMapping.PayloadId ||
                    fallback.PayloadId == trimMapping.PayloadId || bodyMapping.PayloadId == trimMapping.PayloadId)
                    throw new ArgumentException("Legacy fallback and distinct body/trim mappings must bind the same exact source.");
                Items = Array.AsReadOnly(new[] { fallback, bodyMapping, trimMapping });
            }
        }

        public sealed class Request
        {
            internal readonly ReadOnlyCollection<PayloadIdentity> Items;
            public SourceIdentity Source { get; }
            public Semantics Mode { get; }
            public PayloadIdentity[] RequiredPayloads
            {
                get { var copy = new PayloadIdentity[Items.Count]; Items.CopyTo(copy, 0); return copy; }
            }
            public Request(SourceIdentity source, Semantics mode, params PayloadIdentity[] requiredPayloads)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                if (!Enum.IsDefined(typeof(Semantics), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
                Mode = mode;
                if (requiredPayloads != null && requiredPayloads.Length > HardMaximumRequiredPayloads)
                    throw new ArgumentException("Required payload count exceeds the absolute bound.");
                Items = Array.AsReadOnly(requiredPayloads == null ? Array.Empty<PayloadIdentity>() : (PayloadIdentity[])requiredPayloads.Clone());
                if (mode == Semantics.LegacyAdditive)
                {
                    if (Items.Count != 0) throw new ArgumentException("Legacy additive is an untouched pass-through, not a replacement request.");
                    return;
                }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                int geometry = 0, profiles = 0;
                foreach (var item in Items)
                {
                    if (item == null || !source.Equals(item.Source) || !ids.Add(item.PayloadId))
                        throw new ArgumentException("Every required payload must have a distinct ID and the exact candidate source identity.");
                    if (item.Kind == PayloadKind.Geometry) geometry++;
                    else if (item.Kind == PayloadKind.OpticalMaterialProfile) profiles++;
                    else throw new ArgumentException("Legacy evidence belongs in the pinned legacy binding.");
                }
                if (geometry == 0 || profiles != 1)
                    throw new ArgumentException("Replacement requires geometry and exactly one immutable optical material profile.");
            }
        }

        public sealed class Generation
        {
            internal readonly SourceReplacementGate Owner;
            public long Number { get; }
            public Request Request { get; }
            internal Generation(SourceReplacementGate owner, long number, Request request)
            { Owner = owner; Number = number; Request = request; }
        }

        /// <summary>
        /// One whole visibility transaction, never independent old/new visibility flags.
        /// HoldPublished means no safe change is available; it does NOT assert fallback readiness.
        /// LegacyAdditive leaves ordinary legacy body/trim policy entirely untouched.
        /// The adapter must revalidate and apply/acknowledge without interleaving callbacks.
        /// </summary>
        public sealed class Decision
        {
            public Generation Generation { get; }
            public long Revision { get; }
            public Visibility Visibility { get; }
            public StopReason Reason { get; }
            public Generation RetainedReplacement { get; }
            internal Decision(Generation generation, long revision, Visibility visibility, StopReason reason, Generation retained)
            { Generation = generation; Revision = revision; Visibility = visibility; Reason = reason; RetainedReplacement = retained; }
        }

        private sealed class Session
        {
            public Generation Token;
            public bool[] Ready;
            public long Deadline;
            public bool ReachedReadiness;
            public StopReason Stop;
        }

        private readonly string sourceId;
        private readonly LegacyBinding legacy;
        private readonly int maximumPayloads;
        private readonly long maximumWaitMilliseconds;
        private readonly bool[] legacyReady = new bool[3];
        private Session current;
        // A replacement decision reserves its resources even before acknowledgment. If a caller
        // aborts after applying it, only an acknowledged fallback can release that reservation.
        private Generation replacementLease;
        private Decision decision;
        private long generation, revision, lastNow = -1;

        public SourceReplacementGate(string stableSourceId, LegacyBinding legacyBinding = null,
            int maximumRequiredPayloads = 64, long maximumWaitMilliseconds = 30000)
        {
            sourceId = Required(stableSourceId, nameof(stableSourceId));
            if (maximumRequiredPayloads < 2 || maximumRequiredPayloads > HardMaximumRequiredPayloads)
                throw new ArgumentOutOfRangeException(nameof(maximumRequiredPayloads));
            if (maximumWaitMilliseconds <= 0 || maximumWaitMilliseconds > 300000)
                throw new ArgumentOutOfRangeException(nameof(maximumWaitMilliseconds));
            if (legacyBinding != null && legacyBinding.Fallback.Source.SourceId != sourceId)
                throw new ArgumentException("Legacy binding belongs to another stable source ID.");
            legacy = legacyBinding;
            maximumPayloads = maximumRequiredPayloads;
            this.maximumWaitMilliseconds = maximumWaitMilliseconds;
        }

        /// <summary>Invalid requests leave the existing transaction intact. Times are monotonic milliseconds.</summary>
        public Generation Begin(Request request, long nowMilliseconds, long waitMilliseconds)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Source.SourceId != sourceId) throw new ArgumentException("Candidate belongs to another stable source ID.");
            if (request.Items.Count > maximumPayloads) throw new ArgumentException("Required payload count exceeds the configured bound.");
            if (request.Mode == Semantics.ReplaceLegacy && legacy == null)
                throw new ArgumentException("Replacement requires an immutable exact legacy binding.");
            if (nowMilliseconds < 0 || nowMilliseconds < lastNow) throw new ArgumentOutOfRangeException(nameof(nowMilliseconds));
            if (waitMilliseconds <= 0 || waitMilliseconds > maximumWaitMilliseconds || nowMilliseconds > long.MaxValue - waitMilliseconds)
                throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
            if (generation == long.MaxValue) throw new InvalidOperationException("Generation space exhausted; create a new gate after rollback.");
            var token = new Generation(this, ++generation, request);
            current = new Session { Token = token, Ready = new bool[request.Items.Count], Deadline = nowMilliseconds + waitMilliseconds };
            // Without a retained replacement there is no residency lease preserving old evidence.
            // Reentry must reverify the legacy baseline as well as candidate payloads.
            if (replacementLease == null) Array.Clear(legacyReady, 0, legacyReady.Length);
            lastNow = nowMilliseconds;
            InvalidateDecision();
            return token;
        }

        /// <summary>
        /// Called only after loading, byte-hash verification and semantic validation by the adapter.
        /// observed must identify the actual loaded artifact; verifiedContentHash is its measured
        /// SHA-256, not a copied manifest value. Hash equality here is not an optical/mesh validator.
        /// Stale/foreign generations are ignored, including their timestamps.
        /// Legacy readiness can recover after failure so a pinned replacement can safely roll back.
        /// </summary>
        public bool ReportValidatedReady(Generation token, PayloadIdentity observed, string verifiedContentHash, long nowMilliseconds)
        {
            if (!IsCurrent(token) || legacy == null) return false;
            Advance(nowMilliseconds);
            int legacyIndex = Find(legacy.Items, observed);
            int payloadIndex = Find(current.Token.Request.Items, observed);
            if (observed == null || (legacyIndex < 0 && payloadIndex < 0) ||
                !string.Equals(observed.ContentHash, verifiedContentHash, StringComparison.OrdinalIgnoreCase))
            {
                if (legacyIndex >= 0) legacyReady[legacyIndex] = false;
                Stop(StopReason.IdentityMismatch);
                InvalidateDecision();
                return false;
            }
            if (legacyIndex >= 0) legacyReady[legacyIndex] = true;
            else
            {
                if (current.Stop != StopReason.None) return false;
                current.Ready[payloadIndex] = true;
            }
            if (current.Stop == StopReason.None && All(legacyReady) && All(current.Ready)) current.ReachedReadiness = true;
            InvalidateDecision();
            return true;
        }

        /// <summary>Invalidate an exact resident artifact before releasing or changing it.</summary>
        public bool ReportFailure(Generation token, PayloadIdentity failed, long nowMilliseconds)
        {
            if (!IsCurrent(token) || legacy == null) return false;
            Advance(nowMilliseconds);
            int legacyIndex = Find(legacy.Items, failed);
            int payloadIndex = Find(current.Token.Request.Items, failed);
            if (legacyIndex >= 0) legacyReady[legacyIndex] = false;
            if (payloadIndex >= 0) current.Ready[payloadIndex] = false;
            Stop(legacyIndex >= 0 || payloadIndex >= 0 ? StopReason.PayloadFailed : StopReason.IdentityMismatch);
            InvalidateDecision();
            return true;
        }

        public bool Cancel(Generation token, long nowMilliseconds)
        {
            if (!IsCurrent(token)) return false;
            Advance(nowMilliseconds);
            Stop(StopReason.Cancelled);
            return true;
        }

        public Decision Evaluate(long nowMilliseconds)
        {
            if (current == null) throw new InvalidOperationException("Begin a source transaction first.");
            Advance(nowMilliseconds);
            Visibility visibility;
            bool legacyAvailable = legacy != null && All(legacyReady);
            bool differentLease = replacementLease != null && !ReferenceEquals(replacementLease, current.Token);
            if (current.Token.Request.Mode == Semantics.LegacyAdditive && replacementLease == null)
                visibility = Visibility.LegacyAdditive;
            else if (current.Token.Request.Mode == Semantics.ReplaceLegacy && current.Stop == StopReason.None &&
                current.ReachedReadiness && legacyAvailable && !differentLease)
                visibility = Visibility.Replacement;
            else visibility = legacyAvailable ? Visibility.KeepFallback : Visibility.HoldPublished;
            if (visibility == Visibility.Replacement && replacementLease == null) replacementLease = current.Token;
            if (decision == null || decision.Visibility != visibility || decision.Reason != current.Stop ||
                !ReferenceEquals(decision.RetainedReplacement, replacementLease))
                decision = new Decision(current.Token, ++revision, visibility, current.Stop, replacementLease);
            return decision;
        }

        /// <summary>
        /// Acknowledge ONLY after the same decision was atomically applied, with no intervening
        /// callbacks. A stale decision cannot commit. HoldPublished is not a publication.
        /// No replacement may be displayed independently of hiding its exact legacy body AND trim.
        /// A fallback publication must hide the replacement and restore both before acknowledgment.
        /// </summary>
        public bool AcknowledgePublished(Decision applied, long nowMilliseconds)
        {
            if (applied == null || !IsCurrent(applied.Generation) || !ReferenceEquals(applied, decision)) return false;
            if (!ReferenceEquals(applied, Evaluate(nowMilliseconds)) || applied.Visibility == Visibility.HoldPublished) return false;
            if (applied.Visibility == Visibility.KeepFallback)
            {
                replacementLease = null;
                InvalidateDecision();
            }
            return true;
        }

        /// <summary>False until any offered/active replacement has acknowledged fallback restoration.</summary>
        public bool CanReleaseReplacement(Generation token)
        {
            if (token == null || !ReferenceEquals(token.Owner, this)) return false;
            if (ReferenceEquals(token, replacementLease)) return false;
            return !IsCurrent(token) || token.Request.Mode == Semantics.LegacyAdditive || current.Stop != StopReason.None;
        }

        private bool IsCurrent(Generation token) => current != null && ReferenceEquals(current.Token, token) && ReferenceEquals(token.Owner, this);
        private static int Find(IList<PayloadIdentity> items, PayloadIdentity value)
        {
            if (value != null) for (int i = 0; i < items.Count; i++) if (items[i].Equals(value)) return i;
            return -1;
        }
        private static bool All(bool[] values)
        {
            foreach (bool value in values) if (!value) return false;
            return true;
        }
        private void Advance(long nowMilliseconds)
        {
            if (nowMilliseconds < 0 || nowMilliseconds < lastNow) { Stop(StopReason.ClockReversed); return; }
            lastNow = nowMilliseconds;
            if (current.Token.Request.Mode == Semantics.ReplaceLegacy && !current.ReachedReadiness && nowMilliseconds >= current.Deadline)
                Stop(StopReason.TimedOut);
        }
        private void Stop(StopReason reason)
        {
            if (current.Stop == StopReason.None) { current.Stop = reason; InvalidateDecision(); }
        }
        private void InvalidateDecision() { decision = null; }
        private static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) throw new ArgumentException("An exact nonblank identifier is required.", name);
            return value;
        }
        private static string Hash(string value, string name)
        {
            if (value == null || value.Length != 64) throw new ArgumentException("A SHA-256 hex digest is required.", name);
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    throw new ArgumentException("A SHA-256 hex digest is required.", name);
            return value.ToLowerInvariant();
        }
    }
}
