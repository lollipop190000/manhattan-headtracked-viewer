using System;
using UnityEngine;

namespace WebShooter.Swing
{
    [Serializable]
    public sealed class FlowFeatures
    {
        [SerializeField] public bool preserveSteeringMomentum;
        [SerializeField] public bool preserveGlancingContact;
        [SerializeField] public bool preserveTransitionMomentum;
        [SerializeField] public bool resetLengthAtWrap;
        [SerializeField] public bool tensionOnlyRope;
        [SerializeField] public bool attachmentDrop;
        [SerializeField] public bool gentleReel;
        [SerializeField] public bool assistAnchor;
        [SerializeField] public bool releaseBoost;
        [SerializeField] public bool softSpeedLimit;
        [SerializeField] public bool fixedAttachmentDelay;
        [Range(.01f,.05f)] public float attachmentDelaySeconds=.04f;
        [Min(.01f)] public float excessSpeedDamping=2f;
        [Min(0)] public float releaseBoostSpeed=6f;
        [Range(0,1)] public float releasePhaseCosine=.65f;
        [Min(0)] public float releaseMinimumUpSpeed=3f;
        [Range(0,1)] public float anchorAssistStrength=.35f;
        [Range(1,25)] public float anchorConeDegrees=12f;
        [Range(4,24)] public int anchorSamples=12;
        [Range(.8f,1.3f)] public float initialLengthRatio=1.08f;
        [Range(0,2)] public float gentleReelSpeed=.6f;
        [Range(0,1)] public float yankExitSpeedRatio = .5f;
        [Min(0)] public float yankWallClearance = .65f;
        [Range(0,1)] public float yankStartBlend = .65f;
        [Min(0)] public float wallApproachSpeed = 5f;
        [Range(0,1)] public float wallApproachRatio = .55f;
        [Min(0)] public float steeringTurnDegrees = 65f;
        [Range(0,1)] public float airSteeringRatio = .35f;
        [Min(0)] public float airTargetSpeed = 10f;
        public void SetFlow(bool value)
        { preserveSteeringMomentum=preserveGlancingContact=preserveTransitionMomentum=resetLengthAtWrap=value; }
        public void SetPacing(bool value)
        { tensionOnlyRope=attachmentDrop=gentleReel=assistAnchor=releaseBoost=softSpeedLimit=fixedAttachmentDelay=value; }
    }
}
