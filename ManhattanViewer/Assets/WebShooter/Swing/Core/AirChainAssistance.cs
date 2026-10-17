using System;
using UnityEngine;
namespace WebShooter.Swing
{
    [Serializable]
    public sealed class AirChainAssistance
    {
        [SerializeField] public bool enabled;
        [SerializeField] public bool fastAirborneAttachment = true;
        [SerializeField] public bool airborneAimTolerance = true;
        [SerializeField] public bool avoidUnsafeCloseAnchors = true;
        [Range(0,.5f)] public float anchorReactionSeconds = .3f;
        [Range(0,12)] public float aimAcquisitionRadius = 12f;
        [SerializeField] public bool inputRedirect = true;
        [SerializeField] public bool respectSlackSteering = true;
        [SerializeField] public bool releaseRedirect = true;
        [Range(0,2)] public float releaseRedirectSeconds = 1.2f;
        [Range(0,1)] public float releaseRedirectRatio = .6f;
        [SerializeField] public bool cornerGuidance = true;
        [SerializeField] public bool wallApproachBrake = true;
        [Min(0)] public float wallApproachAcceleration = 180f;
        [SerializeField] public bool closeAnchorReelGuard = true;
        [Min(0)] public float closeAnchorDistance = 18f;
        [Min(0)] public float closeAnchorSpeed = 20f;
        [Range(.1f,1f)] public float previewSeconds = .8f;
        [Range(.1f,4f)] public float previewClearance = 2.5f;
        [Min(0)] public float cornerSpeed = 38f;
        [Min(0)] public float cornerDeceleration = 60f;
        [Range(.01f,.05f)] public float attachmentSeconds = .04f;
        [Range(0,2)] public float wallProximityGraceSeconds = 1.2f;
        [Range(0,.5f)] public float initialSlackSeconds = .35f;
        [Range(.1f,2f)] public float redirectSeconds = 1.1f;
        [Range(30,180)] public float redirectDegrees = 120f;
        [Min(0)] public float minimumRedirectSpeed = 12f;
    }
}
