using System;
using UnityEngine;

namespace WebShooter.Swing
{
    [Serializable]
    public sealed class FlowFeatures
    {
        [SerializeField] public bool preserveSteeringMomentum;
        [SerializeField] public bool preserveGlancingContact;
        [Min(0)] public float wallApproachSpeed = 5f;
        [Range(0,1)] public float wallApproachRatio = .55f;
        [Min(0)] public float steeringTurnDegrees = 65f;
        [Min(0)] public float airTargetSpeed = 10f;
    }
}
